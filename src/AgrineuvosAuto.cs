// AgPath - records driven paths alongside AgOpenGPS.
// Copyright 2026 AgPath contributors. Licensed under the Apache License 2.0, see LICENSE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;

namespace AgPath
{
    /// <summary>One completed operation to be entered into Agrineuvos.</summary>
    class AnJob
    {
        public string Id = "";          // relative GeoJSON path, used to remember what was exported
        public string Lohko = "";       // Agrineuvos kasvulohko tunnus, e.g. "1-A"
        public string Item = "";        // tarvike (or tapahtuma) name as shown in Agrineuvos
        public string Amount = "";      // optional amount per ha, as typed
        public DateTime Date;
        public double AreaHa;
        public string Info = "";

        static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\t", " ").Replace("\r", "").Replace("\n", "\\n"); }
        static string Unesc(string s)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length) { i++; sb.Append(s[i] == 'n' ? '\n' : s[i]); }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        public string ToLine()
        {
            return Esc(Id) + "\t" + Esc(Lohko) + "\t" + Esc(Item) + "\t" + Esc(Amount) + "\t" +
                   Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\t" +
                   AreaHa.ToString("0.####", CultureInfo.InvariantCulture) + "\t" + Esc(Info);
        }

        public static AnJob FromLine(string line)
        {
            string[] p = line.Split('\t');
            if (p.Length < 7) return null;
            AnJob j = new AnJob();
            j.Id = Unesc(p[0]); j.Lohko = Unesc(p[1]); j.Item = Unesc(p[2]); j.Amount = Unesc(p[3]);
            j.Date = DateTime.ParseExact(p[4], "yyyy-MM-dd", CultureInfo.InvariantCulture);
            j.AreaHa = double.Parse(p[5], CultureInfo.InvariantCulture);
            j.Info = Unesc(p[6]);
            return j;
        }
    }

    class AnException : Exception { public AnException(string m) : base(m) { } }

    /// <summary>
    /// Enters operations into the Agrineuvos desktop client by operating its user interface the same way a
    /// person would (clicks and key presses), and checks what it sees at every step. It never touches the
    /// Agrineuvos database directly. Runs in its own DPI-aware process (AgPath.exe --agrineuvos job.txt).
    /// </summary>
    static class AgrineuvosAuto
    {
        // ------------------------------------------------------------ entry point (separate process)
        public static int RunJobFile(string jobFile)
        {
            try { SetProcessDPIAware(); } catch { }
            string resultFile = jobFile + ".result";
            string logFile = Path.Combine(Program.AppDir, "agrineuvos_log.txt");
            List<string> results = new List<string>();
            try
            {
                logW = new StreamWriter(logFile, true, new UTF8Encoding(false));
                logW.AutoFlush = true;
                L("==== " + DateTime.Now.ToString("s") + " AgPath " + Program.Version);
                List<AnJob> jobs = new List<AnJob>();
                foreach (string line in File.ReadAllLines(jobFile, Encoding.UTF8))
                {
                    AnJob j = AnJob.FromLine(line);
                    if (j != null) jobs.Add(j);
                }
                // UI Automation clients must run on an MTA thread (an STA thread can hang)
                Exception inner = null;
                Thread th = new Thread(delegate()
                {
                    try
                    {
                        Run(jobs, delegate(AnJob j, string err)
                        {
                            lock (results)
                            {
                                results.Add((err == null ? "OK" : "ERR") + "\t" + j.Id.Replace("\t", " ") + "\t" + (err ?? "").Replace("\t", " ").Replace("\n", " "));
                                File.WriteAllLines(resultFile, results.ToArray(), Encoding.UTF8);
                            }
                        });
                    }
                    catch (Exception ex) { inner = ex; }
                });
                th.SetApartmentState(ApartmentState.MTA);
                th.IsBackground = true;
                th.Start();
                // safety net: never keep operating the computer for long
                if (!th.Join(TimeSpan.FromSeconds(60 + 120 * jobs.Count)))
                {
                    L("TIMEOUT");
                    lock (results) results.Add("FATAL\t\t" + T("an.err.timeout"));
                    File.WriteAllLines(resultFile, results.ToArray(), Encoding.UTF8);
                    if (logW != null) logW.Dispose();
                    Environment.Exit(1);
                }
                if (inner != null) throw inner;
            }
            catch (Exception ex)
            {
                L("FATAL " + ex);
                results.Add("FATAL\t\t" + ex.Message.Replace("\t", " ").Replace("\n", " "));
            }
            try { File.WriteAllLines(resultFile, results.ToArray(), Encoding.UTF8); } catch { }
            if (logW != null) logW.Dispose();
            return 0;
        }

        static StreamWriter logW;
        static void L(string s) { try { if (logW != null) logW.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s); } catch { } }

        static uint pid;
        static IntPtr mainWnd;
        static double scale = 1.0;

        public delegate void JobDone(AnJob job, string error);

        static void Run(List<AnJob> jobs, JobDone done)
        {
            Attach();
            L("Reading season");
            int origSeason = CurrentSeason();
            L("Season now " + origSeason);
            bool fatal = false;
            foreach (AnJob j in jobs)
            {
                if (fatal) { done(j, T("an.err.stopped")); continue; }
                try
                {
                    L("-- " + j.Id + " lohko=" + j.Lohko + " item=" + j.Item + " date=" + j.Date.ToString("yyyy-MM-dd") + " ala=" + j.AreaHa);
                    DoOne(j);
                    L("OK");
                    done(j, null);
                }
                catch (Exception ex)
                {
                    L("ERROR " + ex);
                    done(j, ex is AnException ? ex.Message : T("an.err.unexpected") + " " + ex.Message);
                    // stop if Agrineuvos is in an unknown state (a dialog left open etc.)
                    if (!BackToMain()) fatal = true;
                }
            }
            try
            {
                ClearFieldSearch();
                if (origSeason > 0 && CurrentSeason() != origSeason) GoToSeason(origSeason);
            }
            catch (Exception ex) { L("cleanup: " + ex.Message); }
        }

        static string T(string key, params object[] a) { return Lang.T(key, a); }

        // ------------------------------------------------------------ Agrineuvos window
        static void Attach()
        {
            Process[] ps = Process.GetProcessesByName("AgrineuvosClient");
            if (ps.Length == 0) throw new AnException(T("an.err.notRunning"));
            foreach (Process p in ps)
            {
                IntPtr w = FindTop((uint)p.Id, "TACMainForm");
                if (w != IntPtr.Zero) { pid = (uint)p.Id; mainWnd = w; break; }
            }
            if (mainWnd == IntPtr.Zero) throw new AnException(T("an.err.noFarm"));
            if (!IsWindowEnabled(mainWnd)) throw new AnException(T("an.err.dialogOpen"));
            try { scale = GetDpiForWindow(mainWnd) / 96.0; } catch { scale = 1.0; }
            if (scale <= 0) scale = 1.0;
            L("Main window " + GetText(mainWnd) + " scale " + scale);
            if (IsIconic(mainWnd)) { ShowWindow(mainWnd, 9); Sleep(800); }
        }

        static bool BackToMain()
        {
            // close our own dialogs if one was left open; anything else is left for the user
            for (int i = 0; i < 3; i++)
            {
                if (IsWindowEnabled(mainWnd) && FindTop(pid, "TACTapahtumaSelectDialog") == IntPtr.Zero &&
                    FindTop(pid, "TACDialogTapahtumaEditFRM") == IntPtr.Zero) return true;
                foreach (string cls in new string[] { "TACDialogTapahtumaEditFRM", "TACTapahtumaSelectDialog" })
                {
                    IntPtr d = FindTop(pid, cls);
                    if (d == IntPtr.Zero) continue;
                    IntPtr cancel = FindChild(d, "TButton", "Peruuta");
                    if (cancel == IntPtr.Zero) cancel = FindChild(d, "TButton", "Cancel");
                    if (cancel != IntPtr.Zero) { Fore(d); ClickWnd(cancel); Sleep(1000); }
                }
            }
            return IsWindowEnabled(mainWnd);
        }

        // ------------------------------------------------------------ one operation
        static void DoOne(AnJob j)
        {
            if (j.Lohko.Trim() == "") throw new AnException(T("an.err.noLohko"));
            if (j.Item.Trim() == "") throw new AnException(T("an.err.noItem"));
            if (!IsWindowEnabled(mainWnd)) throw new AnException(T("an.err.dialogOpen"));

            GoToSeason(j.Date.Year);
            SelectField(j.Lohko.Trim());

            string lastCaption = "", rejected = null;
            for (int candidate = 0; candidate < 6; candidate++)
            {
                OpenAddDialog();
                IntPtr sel = WaitTop("TACTapahtumaSelectDialog", 10000);
                if (sel == IntPtr.Zero) throw new AnException(T("an.err.addDialog"));
                Sleep(500);
                bool found = PickItem(sel, j.Item.Trim(), candidate);
                if (!found)
                {
                    CancelDialog(sel);
                    if (candidate == 0) throw new AnException(T("an.err.itemNotFound", j.Item));
                    break;
                }
                IntPtr ed = WaitTop("TACDialogTapahtumaEditFRM", 10000);
                if (ed == IntPtr.Zero) throw new AnException(T("an.err.editDialog"));
                Sleep(600);
                string caption = EditCaption(ed);
                lastCaption = caption;
                L("Edit dialog: title=\"" + GetText(ed) + "\" caption=\"" + caption + "\"");
                string title = GetText(ed);
                bool lohkoOk = title.IndexOf("- " + j.Lohko.Trim() + "-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               caption.StartsWith(j.Lohko.Trim() + "-", StringComparison.OrdinalIgnoreCase);
                if (!lohkoOk)
                {
                    CancelDialog(ed);
                    throw new AnException(T("an.err.wrongLohko", j.Lohko, caption));
                }
                if (!CaptionHasItem(caption, j.Item.Trim()))
                {
                    L("Candidate " + candidate + " is a different item, trying the next one");
                    CancelDialog(ed);
                    Sleep(800);
                    if (caption == rejected) break; // end of the search results
                    rejected = caption;
                    continue;
                }
                FillAndSave(ed, j);
                return;
            }
            throw new AnException(T("an.err.itemNotFound", j.Item) + (lastCaption != "" ? " (" + lastCaption + ")" : ""));
        }

        static bool CaptionHasItem(string caption, string item)
        {
            // caption: "<tunnus>-<nimi>  <tarvike or tapahtuma>"
            int k = caption.IndexOf("  ");
            string rest = k >= 0 ? caption.Substring(k).Trim() : caption.Trim();
            return string.Equals(Norm(rest), Norm(item), StringComparison.OrdinalIgnoreCase);
        }

        static string Norm(string s) { return Regex.Replace(s ?? "", @"\s+", " ").Trim(); }

        static string EditCaption(IntPtr ed)
        {
            foreach (IntPtr h in Descendants(ed))
                if (Cls(h) == "TSTGroupBox" && IsWindowVisible(h))
                {
                    string t = GetText(h);
                    if (t.Trim() != "" && t.Trim() != "Info") return t.Trim();
                }
            return "";
        }

        // ------------------------------------------------------------ season (kasvukausi)
        static int CurrentSeason()
        {
            AutomationElement root = AutomationElement.FromHandle(mainWnd);
            AutomationElement bar = root.FindFirst(TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuBar));
            if (bar == null) bar = root;
            AutomationElementCollection items = bar.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));
            foreach (AutomationElement e in items)
            {
                string n = "";
                try { n = e.Current.Name; } catch { }
                Match m = Regex.Match(n, @"\((\d{4})\)");
                if (!m.Success) continue;
                int y = int.Parse(m.Groups[1].Value);
                // "Edellinen kasvukausi (2024)" / "Seuraava kasvukausi (2026)"
                if (n.IndexOf("Edellinen", StringComparison.OrdinalIgnoreCase) >= 0) return y + 1;
                if (n.IndexOf("Seuraava", StringComparison.OrdinalIgnoreCase) >= 0) return y - 1;
            }
            return 0;
        }

        static void GoToSeason(int year)
        {
            int cur = CurrentSeason();
            if (cur == 0) throw new AnException(T("an.err.season"));
            int guard = 0;
            while (cur != year)
            {
                if (++guard > 30) throw new AnException(T("an.err.seasonMissing", year));
                FocusMain();
                // Ctrl+Q = previous season, Ctrl+W = next season (Agrineuvos shortcuts)
                KeyCombo(VK_CONTROL, cur > year ? (ushort)'Q' : (ushort)'W');
                int before = cur;
                for (int i = 0; i < 40 && cur == before; i++) { Sleep(250); cur = CurrentSeason(); }
                if (cur == before) throw new AnException(T("an.err.seasonMissing", year));
                L("Season -> " + cur);
                if (!IsWindowEnabled(mainWnd)) throw new AnException(T("an.err.seasonMissing", year));
            }
        }

        // ------------------------------------------------------------ kasvulohko
        static IntPtr FieldTree()
        {
            IntPtr best = IntPtr.Zero; RECT br = new RECT();
            foreach (IntPtr h in Descendants(mainWnd))
            {
                if (Cls(h) != "TVirtualStringTree" || !IsWindowVisible(h)) continue;
                RECT r = Rect(h);
                if (best == IntPtr.Zero || r.L < br.L) { best = h; br = r; }
            }
            return best;
        }

        static IntPtr FieldSearch(IntPtr tree)
        {
            RECT tr = Rect(tree);
            IntPtr best = IntPtr.Zero; int bestDy = int.MaxValue;
            foreach (IntPtr h in Descendants(mainWnd))
            {
                if (Cls(h) != "TSTSearchBox" || !IsWindowVisible(h)) continue;
                RECT r = Rect(h);
                if (r.R < tr.L || r.L > tr.R || r.B > tr.T + 5) continue;
                int dy = tr.T - r.B;
                if (dy < bestDy) { bestDy = dy; best = h; }
            }
            return best;
        }

        static void SelectField(string lohko)
        {
            IntPtr tree = FieldTree();
            IntPtr search = tree == IntPtr.Zero ? IntPtr.Zero : FieldSearch(tree);
            if (search == IntPtr.Zero) throw new AnException(T("an.err.fieldList"));
            FocusMain();
            ClickWnd(search);
            ReplaceText(lohko);
            Sleep(1500);
            int[] cnt = FieldCount();
            L("Field search '" + lohko + "' -> " + (cnt == null ? "?" : cnt[0] + "/" + cnt[1]));
            if (cnt != null && cnt[1] == 0) throw new AnException(T("an.err.lohkoNotFound", lohko));
            if (cnt == null || cnt[1] > 1 || cnt[0] != 1)
            {
                // several matches (e.g. 1-A and 11-A): click the first row; the result is checked later
                RECT r = Rect(tree);
                ClickAt(r.L + (int)(60 * scale), r.T + (int)(29 * scale));
                Sleep(1200);
                cnt = FieldCount();
                if (cnt != null && cnt[0] != 1) throw new AnException(T("an.err.lohkoNotFound", lohko));
            }
        }

        static int[] FieldCount()
        {
            // status bar under the field list, e.g. "1/13    7,7 %"
            AutomationElement root = AutomationElement.FromHandle(mainWnd);
            AutomationElementCollection bars = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.StatusBar));
            foreach (AutomationElement b in bars)
            {
                foreach (AutomationElement c in b.FindAll(TreeScope.Children, Condition.TrueCondition))
                {
                    string v = "";
                    try { v = c.Current.Name; } catch { }
                    Match m = Regex.Match(v, @"(\d+)\s*/\s*(\d+)\s");
                    if (m.Success && v.IndexOf("ha") < 0) return new int[] { int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value) };
                }
            }
            return null;
        }

        static void ClearFieldSearch()
        {
            if (!IsWindowEnabled(mainWnd)) return;
            IntPtr tree = FieldTree();
            IntPtr search = tree == IntPtr.Zero ? IntPtr.Zero : FieldSearch(tree);
            if (search == IntPtr.Zero || GetText(search) == "") return;
            FocusMain();
            ClickWnd(search);
            ReplaceText("");
            Sleep(500);
        }

        // ------------------------------------------------------------ add dialog
        static void OpenAddDialog()
        {
            FocusMain();
            AutomationElement main = AutomationElement.FromHandle(mainWnd);
            AutomationElement tab = main.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem),
                new PropertyCondition(AutomationElement.NameProperty, "Toteutettu")));
            if (tab == null) throw new AnException(T("an.err.tab"));
            object sp;
            bool selected = tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out sp) && ((SelectionItemPattern)sp).Current.IsSelected;
            if (!selected)
            {
                ClickElement(tab);
                Sleep(1200);
            }
            // the operations list of the Toteutettu tab
            IntPtr grid = IntPtr.Zero;
            foreach (IntPtr h in Descendants(mainWnd))
            {
                if (Cls(h) != "TTabSheet" || GetText(h) != "Toteutettu" || !IsWindowVisible(h)) continue;
                foreach (IntPtr c in Descendants(h))
                    if (Cls(c) == "TVirtualStringTree" && IsWindowVisible(c)) { grid = c; break; }
            }
            if (grid == IntPtr.Zero) throw new AnException(T("an.err.tab"));
            RECT r = Rect(grid);
            RightClickAt(r.L + (r.R - r.L) / 2, r.T + (r.B - r.T) * 3 / 4);
            AutomationElement item = null;
            for (int i = 0; i < 20 && item == null; i++)
            {
                Sleep(200);
                item = FindMenuItem("Lisää tapahtuma");
            }
            if (item == null) { KeyPress(VK_ESCAPE); throw new AnException(T("an.err.menu")); }
            ClickElement(item);
        }

        static AutomationElement FindMenuItem(string name)
        {
            AutomationElementCollection menus = AutomationElement.RootElement.FindAll(TreeScope.Children, new AndCondition(
                new PropertyCondition(AutomationElement.ProcessIdProperty, (int)pid),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu)));
            foreach (AutomationElement m in menus)
            {
                AutomationElement it = m.FindFirst(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                    new PropertyCondition(AutomationElement.NameProperty, name)));
                if (it != null) return it;
            }
            return null;
        }

        /// <summary>Searches for the item and moves search result number <paramref name="index"/> to the
        /// selected list, then presses OK. Returns false if there is no such result.</summary>
        static bool PickItem(IntPtr dlg, string item, int index)
        {
            Fore(dlg);
            IntPtr showAll = FindChild(dlg, "TCheckBox", "Näytä kaikki");
            if (showAll != IntPtr.Zero && SendMessage(showAll, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == IntPtr.Zero)
            {
                ClickWnd(showAll);
                Sleep(800);
            }
            IntPtr search = IntPtr.Zero, source = IntPtr.Zero;
            RECT sr = new RECT();
            foreach (IntPtr h in Descendants(dlg))
            {
                if (!IsWindowVisible(h)) continue;
                string c = Cls(h);
                if (c == "TSTSearchBox" && search == IntPtr.Zero) search = h;
                if (c == "TVirtualStringTree")
                {
                    RECT r = Rect(h);
                    if (source == IntPtr.Zero || r.L < sr.L) { source = h; sr = r; }
                }
            }
            if (search == IntPtr.Zero || source == IntPtr.Zero) throw new AnException(T("an.err.addDialog"));
            ClickWnd(search);
            ReplaceText(item);
            Sleep(1500);
            // focus the result list without hitting a row (click the empty area at the bottom), then
            // Home = first result, Down = next ones
            ClickAt(sr.L + (sr.R - sr.L) / 3, sr.B - (int)(8 * scale));
            Sleep(300);
            KeyPress(VK_HOME);
            for (int i = 0; i < index; i++) { Sleep(150); KeyPress(VK_DOWN); }
            Sleep(300);
            // the "move selected" arrow is the upper of the two buttons between the lists
            IntPtr okBtn = FindChild(dlg, "TButton", "OK");
            IntPtr moveBtn = IntPtr.Zero; int topY = int.MaxValue;
            foreach (IntPtr h in Descendants(dlg))
            {
                if (Cls(h) != "TSTCommandButton" || !IsWindowVisible(h)) continue;
                RECT r = Rect(h);
                if (r.L < sr.R || r.R > sr.R + (int)(60 * scale)) continue;
                if (r.T < topY) { topY = r.T; moveBtn = h; }
            }
            if (moveBtn == IntPtr.Zero || okBtn == IntPtr.Zero) throw new AnException(T("an.err.addDialog"));
            ClickWnd(moveBtn);
            Sleep(800);
            if (!IsWindowEnabled(okBtn)) return false; // nothing was selected: no such result
            // make sure details are asked for the selected row (opens the edit dialog)
            ClickWnd(okBtn);
            return true;
        }

        static void CancelDialog(IntPtr dlg)
        {
            IntPtr cancel = FindChild(dlg, "TButton", "Peruuta");
            if (cancel == IntPtr.Zero) { Fore(dlg); KeyPress(VK_ESCAPE); }
            else { Fore(dlg); ClickWnd(cancel); }
            for (int i = 0; i < 30 && IsWindow(dlg) && IsWindowVisible(dlg); i++) Sleep(100);
        }

        // ------------------------------------------------------------ edit dialog
        static void FillAndSave(IntPtr ed, AnJob j)
        {
            Fore(ed);
            IntPtr group = IntPtr.Zero;
            foreach (IntPtr h in Descendants(ed))
                if (Cls(h) == "TSTGroupBox" && IsWindowVisible(h) && GetText(h).Trim() != "" && GetText(h).Trim() != "Info") { group = h; break; }
            if (group == IntPtr.Zero) throw new AnException(T("an.err.editDialog"));
            RECT gr = Rect(group);
            int mid = gr.L + (gr.R - gr.L) * 3 / 10;

            List<IntPtr> leftEdits = new List<IntPtr>();
            IntPtr dateEdit = IntPtr.Zero, memo = IntPtr.Zero, ok = FindChild(ed, "TButton", "OK");
            foreach (IntPtr h in Descendants(ed))
            {
                if (!IsWindowVisible(h)) continue;
                string c = Cls(h);
                RECT r = Rect(h);
                if (c == "TSTDBEdit" && r.L < mid && r.T > gr.T && r.B < gr.B) leftEdits.Add(h);
                else if (c == "TSTDBDateEdit") dateEdit = h;
                else if (c == "TSTDBMemo") memo = h;
            }
            leftEdits.Sort(delegate(IntPtr e1, IntPtr e2) { return Rect(e1).T.CompareTo(Rect(e2).T); });
            // order on screen: Tapahtumakerta, Ala ha, (Yksikkö), Määrä/ha, Määrä/ala
            if (leftEdits.Count < 3 || dateEdit == IntPtr.Zero || ok == IntPtr.Zero)
                throw new AnException(T("an.err.editDialog"));
            IntPtr area = leftEdits[1];
            IntPtr perHa = leftEdits[2];
            string areaTxt = GetText(area);
            char dec = areaTxt.IndexOf('.') >= 0 && areaTxt.IndexOf(',') < 0 ? '.' : ',';
            L("Area field before: '" + areaTxt + "'");
            double dummy;
            if (!double.TryParse(areaTxt.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out dummy))
                throw new AnException(T("an.err.editDialog"));

            // date
            ClickWnd(dateEdit);
            KeyPress(VK_HOME);
            TypeText(j.Date.ToString("ddMMyyyy", CultureInfo.InvariantCulture));
            KeyPress(VK_TAB);
            Sleep(300);
            // area
            string areaVal = j.AreaHa.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', dec);
            ClickWnd(area);
            ReplaceText(areaVal);
            KeyPress(VK_TAB);
            Sleep(300);
            // amount per ha (optional)
            string amount = (j.Amount ?? "").Trim().Replace('.', dec).Replace(',', dec);
            if (amount != "")
            {
                ClickWnd(perHa);
                ReplaceText(amount);
                KeyPress(VK_TAB);
                Sleep(300);
            }
            // info
            if (memo != IntPtr.Zero && j.Info.Trim() != "")
            {
                ClickWnd(memo);
                KeyCombo(VK_CONTROL, VK_END);
                if (GetText(memo).Trim() != "") KeyPress(VK_RETURN);
                TypeText(j.Info.Trim());
                Sleep(300);
            }

            // check what was entered before saving
            string d = Regex.Replace(GetText(dateEdit), @"\D", "");
            string a = GetText(area).Trim();
            L("Check: date='" + GetText(dateEdit) + "' area='" + a + "' perHa='" + GetText(perHa) + "'");
            if (d != j.Date.ToString("ddMMyyyy", CultureInfo.InvariantCulture) && d != j.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) &&
                d != j.Date.ToString("MMddyyyy", CultureInfo.InvariantCulture))
            {
                CancelDialog(ed);
                throw new AnException(T("an.err.check", "date", GetText(dateEdit)));
            }
            double got;
            if (!double.TryParse(a.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out got) || Math.Abs(got - Math.Round(j.AreaHa, 2)) > 0.006)
            {
                CancelDialog(ed);
                throw new AnException(T("an.err.check", "ala", a));
            }
            if (!IsForegroundOurs()) { CancelDialog(ed); throw new AnException(T("an.err.focus")); }

            ClickWnd(ok);
            for (int i = 0; i < 50 && IsWindow(ed) && IsWindowVisible(ed); i++) Sleep(100);
            if (IsWindow(ed) && IsWindowVisible(ed)) throw new AnException(T("an.err.notSaved"));
            Sleep(800);
            if (!IsWindowEnabled(mainWnd)) throw new AnException(T("an.err.message"));
        }

        // ------------------------------------------------------------ input helpers
        static void FocusMain() { Fore(mainWnd); }

        static void Fore(IntPtr w)
        {
            if (GetForegroundWindow() == w) return;
            // the ALT key trick allows SetForegroundWindow from a background process
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            SetForegroundWindow(w);
            for (int i = 0; i < 20 && GetForegroundWindow() != w; i++) Sleep(50);
            Sleep(150);
            if (!IsForegroundOurs()) throw new AnException(T("an.err.focus"));
        }

        static bool IsForegroundOurs()
        {
            IntPtr f = GetForegroundWindow();
            uint p;
            GetWindowThreadProcessId(f, out p);
            return p == pid;
        }

        static void ReplaceText(string text)
        {
            // never Ctrl+A: in Agrineuvos it selects all fields
            KeyPress(VK_END);
            KeyCombo(VK_SHIFT, VK_HOME);
            KeyPress(VK_DELETE);
            if (text != "") TypeText(text);
        }

        static void ClickWnd(IntPtr h)
        {
            RECT r = Rect(h);
            ClickAt((r.L + r.R) / 2, (r.T + r.B) / 2);
        }

        static void ClickElement(AutomationElement e)
        {
            System.Windows.Rect b = e.Current.BoundingRectangle;
            ClickAt((int)(b.X + b.Width / 2), (int)(b.Y + b.Height / 2));
        }

        static void ClickAt(int x, int y) { Mouse(x, y, 0x0002, 0x0004); }
        static void RightClickAt(int x, int y) { Mouse(x, y, 0x0008, 0x0010); }

        static void Mouse(int x, int y, uint down, uint up)
        {
            if (!IsForegroundOurs()) throw new AnException(T("an.err.focus"));
            // the click must land on an Agrineuvos window (not on a window covering it)
            IntPtr at = WindowFromPoint(new POINT { X = x, Y = y });
            uint p;
            GetWindowThreadProcessId(at, out p);
            if (p != pid) throw new AnException(T("an.err.covered"));
            int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
            int nx = (int)Math.Round((x - vx) * 65535.0 / Math.Max(1, vw - 1));
            int ny = (int)Math.Round((y - vy) * 65535.0 / Math.Max(1, vh - 1));
            const uint MOVE = 0x0001, ABS = 0x8000, VDESK = 0x4000;
            INPUT[] inp = new INPUT[3];
            inp[0] = MouseInput(nx, ny, MOVE | ABS | VDESK);
            inp[1] = MouseInput(nx, ny, down | ABS | VDESK);
            inp[2] = MouseInput(nx, ny, up | ABS | VDESK);
            SendInput(1, new INPUT[] { inp[0] }, Marshal.SizeOf(typeof(INPUT)));
            Sleep(60);
            SendInput(2, new INPUT[] { inp[1], inp[2] }, Marshal.SizeOf(typeof(INPUT)));
            Sleep(250);
        }

        static INPUT MouseInput(int x, int y, uint flags)
        {
            INPUT i = new INPUT();
            i.type = 0;
            i.u.mi.dx = x; i.u.mi.dy = y; i.u.mi.dwFlags = flags;
            return i;
        }

        static INPUT KeyInput(ushort vk, ushort scan, uint flags)
        {
            INPUT i = new INPUT();
            i.type = 1;
            i.u.ki.wVk = vk; i.u.ki.wScan = scan; i.u.ki.dwFlags = flags;
            return i;
        }

        const ushort VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_ESCAPE = 0x1B,
                     VK_END = 0x23, VK_HOME = 0x24, VK_DOWN = 0x28, VK_DELETE = 0x2E;

        static void KeyPress(ushort vk)
        {
            if (!IsForegroundOurs()) throw new AnException(T("an.err.focus"));
            uint ext = (vk >= 0x21 && vk <= 0x2E) ? 1u : 0u;
            SendInput(2, new INPUT[] { KeyInput(vk, 0, ext), KeyInput(vk, 0, ext | 2) }, Marshal.SizeOf(typeof(INPUT)));
            Sleep(60);
        }

        static void KeyCombo(ushort mod, ushort vk)
        {
            if (!IsForegroundOurs()) throw new AnException(T("an.err.focus"));
            uint ext = (vk >= 0x21 && vk <= 0x2E) ? 1u : 0u;
            SendInput(4, new INPUT[] { KeyInput(mod, 0, 0), KeyInput(vk, 0, ext), KeyInput(vk, 0, ext | 2), KeyInput(mod, 0, 2) },
                      Marshal.SizeOf(typeof(INPUT)));
            Sleep(80);
        }

        static void TypeText(string s)
        {
            foreach (char ch in s)
            {
                if (!IsForegroundOurs()) throw new AnException(T("an.err.focus"));
                if (ch == '\n') { KeyPress(VK_RETURN); continue; }
                if (ch == '\r') continue;
                SendInput(2, new INPUT[] { KeyInput(0, ch, 4), KeyInput(0, ch, 4 | 2) }, Marshal.SizeOf(typeof(INPUT)));
                Sleep(15);
            }
            Sleep(100);
        }

        static void Sleep(int ms) { Thread.Sleep(ms); }

        // ------------------------------------------------------------ window helpers
        static IntPtr WaitTop(string cls, int ms)
        {
            for (int t = 0; t < ms; t += 100)
            {
                IntPtr w = FindTop(pid, cls);
                if (w != IntPtr.Zero) return w;
                Sleep(100);
            }
            return IntPtr.Zero;
        }

        static IntPtr FindTop(uint processId, string cls)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint p;
                GetWindowThreadProcessId(h, out p);
                if (p == processId && IsWindowVisible(h) && Cls(h) == cls) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        static List<IntPtr> Descendants(IntPtr parent)
        {
            List<IntPtr> list = new List<IntPtr>();
            EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) { list.Add(h); return true; }, IntPtr.Zero);
            return list;
        }

        static IntPtr FindChild(IntPtr parent, string cls, string text)
        {
            foreach (IntPtr h in Descendants(parent))
                if (Cls(h) == cls && IsWindowVisible(h) && GetText(h).Replace("&", "") == text) return h;
            return IntPtr.Zero;
        }

        static string Cls(IntPtr h) { StringBuilder s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }

        static string GetText(IntPtr h)
        {
            IntPtr len;
            SendMessageTimeout(h, 0x000E, IntPtr.Zero, IntPtr.Zero, 2, 1000, out len); // WM_GETTEXTLENGTH
            int n = Math.Max(0, Math.Min(100000, len.ToInt32())) + 2;
            StringBuilder s = new StringBuilder(n);
            IntPtr r;
            SendMessageTimeout(h, 0x000D, (IntPtr)n, s, 2, 1000, out r); // WM_GETTEXT
            return s.ToString();
        }

        static RECT Rect(IntPtr h) { RECT r; GetWindowRect(h, out r); return r; }

        // ------------------------------------------------------------ P/Invoke
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Explicit)] struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public INPUTUNION u; }

        const uint BM_GETCHECK = 0x00F0;
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, StringBuilder l, uint flags, uint timeout, out IntPtr res);
        [DllImport("user32.dll")]
        static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr res);
    }
}
