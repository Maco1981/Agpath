// AgPath - records driven paths alongside AgOpenGPS.
// Copyright 2026 AgPath contributors. Licensed under the Apache License 2.0, see LICENSE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AgPath
{
    /// <summary>A saved run, read back from its GeoJSON file.</summary>
    class AnRun
    {
        public string File = "", Id = "";
        public string Name = "", Product = "", Variety = "", Field = "", Notes = "";
        public DateTime Start, End;
        public double AreaHa, CoveredHa, DistanceM, WidthM;

        public string ItemKey { get { return Product.Trim() != "" ? "p:" + Product.Trim() + "|" + Variety.Trim() : "n:" + Name.Trim(); } }
        public string Title
        {
            get
            {
                string pv = (Product + " " + Variety).Trim();
                if (pv == "") return Name;
                return Name != "" && Name != pv ? pv + " (" + Name + ")" : pv;
            }
        }

        static double D(Dictionary<string, object> p, string k)
        {
            object o;
            if (!p.TryGetValue(k, out o) || o == null) return 0;
            try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return 0; }
        }
        static string S(Dictionary<string, object> p, string k)
        {
            object o;
            return p.TryGetValue(k, out o) && o != null ? o.ToString() : "";
        }

        public static List<AnRun> LoadAll(string saveDir)
        {
            List<AnRun> list = new List<AnRun>();
            if (!Directory.Exists(saveDir)) return list;
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            foreach (string f in Directory.GetFiles(saveDir, "*.geojson", SearchOption.AllDirectories))
            {
                try
                {
                    string txt = System.IO.File.ReadAllText(f, Encoding.UTF8);
                    // only the properties are needed; they are written before the coordinates
                    int a = txt.IndexOf("\"properties\":{");
                    int b = txt.IndexOf(",\"point_times_s\"", a < 0 ? 0 : a);
                    if (a < 0 || b < 0) continue;
                    Dictionary<string, object> p = js.Deserialize<Dictionary<string, object>>(txt.Substring(a + 13, b - a - 13) + "}");
                    AnRun r = new AnRun();
                    r.File = f;
                    r.Id = f.Substring(saveDir.Length).TrimStart('\\', '/');
                    r.Name = S(p, "name"); r.Product = S(p, "product"); r.Variety = S(p, "variety");
                    r.Field = S(p, "field"); r.Notes = S(p, "notes");
                    DateTime t;
                    if (DateTime.TryParse(S(p, "start"), CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) r.Start = t;
                    if (DateTime.TryParse(S(p, "end"), CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) r.End = t;
                    if (r.Start == DateTime.MinValue) continue;
                    r.AreaHa = D(p, "area_ha"); r.CoveredHa = D(p, "covered_area_ha");
                    r.DistanceM = D(p, "distance_m"); r.WidthM = D(p, "width_m");
                    list.Add(r);
                }
                catch (Exception ex) { Log.Write("Agrineuvos: " + f + ": " + ex.Message); }
            }
            list.Sort(delegate(AnRun x, AnRun y) { return y.Start.CompareTo(x.Start); });
            return list;
        }
    }

    /// <summary>Remembers which Agrineuvos kasvulohko and tarvike belong to which AgOpenGPS field and product.</summary>
    class AnMapping
    {
        public Dictionary<string, string> Lohko = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Item = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Amount = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Exported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> AreaOverride = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public bool UseCovered = true;

        static string MapFile { get { return Path.Combine(Program.AppDir, "agrineuvos.txt"); } }

        public static AnMapping Load()
        {
            AnMapping m = new AnMapping();
            try
            {
                if (!File.Exists(MapFile)) return m;
                foreach (string line in File.ReadAllLines(MapFile, Encoding.UTF8))
                {
                    string[] p = line.Split('\t');
                    if (p.Length >= 3 && p[0] == "F") m.Lohko[p[1]] = p[2];
                    else if (p.Length >= 3 && p[0] == "I") { m.Item[p[1]] = p[2]; if (p.Length >= 4) m.Amount[p[1]] = p[3]; }
                    else if (p.Length >= 3 && p[0] == "X") m.Exported[p[1]] = p[2];
                    else if (p.Length >= 3 && p[0] == "H")
                    {
                        double h;
                        if (double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out h) && h > 0) m.AreaOverride[p[1]] = h;
                    }
                    else if (p.Length >= 2 && p[0] == "A") m.UseCovered = p[1] != "driven";
                }
            }
            catch (Exception ex) { Log.Write("Agrineuvos mapping: " + ex.Message); }
            return m;
        }

        static string C(string s) { return (s ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim(); }

        public void Save()
        {
            List<string> lines = new List<string>();
            lines.Add("# AgPath -> Agrineuvos: F = field -> kasvulohko, I = product -> tarvike, X = exported runs");
            lines.Add("A\t" + (UseCovered ? "covered" : "driven"));
            foreach (KeyValuePair<string, string> kv in Lohko) if (C(kv.Value) != "") lines.Add("F\t" + C(kv.Key) + "\t" + C(kv.Value));
            foreach (KeyValuePair<string, string> kv in Item)
            {
                string a; Amount.TryGetValue(kv.Key, out a);
                if (C(kv.Value) != "") lines.Add("I\t" + C(kv.Key) + "\t" + C(kv.Value) + "\t" + C(a));
            }
            foreach (KeyValuePair<string, string> kv in Exported) lines.Add("X\t" + C(kv.Key) + "\t" + C(kv.Value));
            foreach (KeyValuePair<string, double> kv in AreaOverride) lines.Add("H\t" + C(kv.Key) + "\t" + kv.Value.ToString("0.####", CultureInfo.InvariantCulture));
            try { File.WriteAllLines(MapFile, lines.ToArray(), new UTF8Encoding(false)); }
            catch (Exception ex) { Log.Write("Agrineuvos mapping: " + ex.Message); }
        }

        public string Get(Dictionary<string, string> d, string key) { string v; return d.TryGetValue(key ?? "", out v) ? v : ""; }
    }

    /// <summary>Choose saved runs and enter them into Agrineuvos as completed operations.</summary>
    class AgrineuvosForm : Form
    {
        readonly KSettings s;
        TextBox tbFolder;
        readonly AnMapping map = AnMapping.Load();
        List<AnRun> runs = new List<AnRun>();
        ListView lv;
        TextBox tbLohko, tbItem, tbAmount, tbArea;
        Label lblSel, lblStatus;
        CheckBox chkShowDone;
        RadioButton rbCovered, rbDriven;
        Button btnExport, btnClose;
        bool filling;
        readonly Timer poll = new Timer();
        Process worker;
        string jobFile;
        List<AnRun> sent;
        readonly Dictionary<Form, bool> topMost = new Dictionary<Form, bool>();

        public static string ExePath()
        {
            foreach (string pf in new string[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles") })
            {
                if (string.IsNullOrEmpty(pf)) continue;
                string f = Path.Combine(pf, @"Suonentieto\Agrineuvos\AgrineuvosClient.exe");
                if (File.Exists(f)) return f;
            }
            return null;
        }

        public static bool Available() { return ExePath() != null || Process.GetProcessesByName("AgrineuvosClient").Length > 0; }

        public AgrineuvosForm(KSettings settings)
        {
            s = settings;
            Text = Lang.T("an.title") + "  " + Program.Version;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = true; MaximizeBox = true;
            BackColor = Color.White;
            Font = Ui.F(10);
            ClientSize = new Size(1000, 700);
            MinimumSize = new Size(800, 520);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Label intro = Ui.Lbl(this, Lang.T("an.intro"), 12, 8, 976, 44, 9);
            intro.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // folder with the runs saved by AgPath (e.g. a shared OneDrive folder)
            Ui.Lbl(this, Lang.T("an.folder") + ":", 12, 60, 150, 24, 10);
            tbFolder = new TextBox(); tbFolder.ReadOnly = true; tbFolder.SetBounds(162, 57, 560, 28);
            tbFolder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(tbFolder);
            Button bFolder = Ui.Btn(this, Lang.T("an.chooseFolder"), 732, 55, 130, 32, Ui.Grey, 9);
            bFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            bFolder.Click += delegate { ChooseFolder(); };
            Button bReload = Ui.Btn(this, Lang.T("an.reload"), 868, 55, 120, 32, Ui.Grey, 9);
            bReload.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            bReload.Click += delegate { Reload(); };

            lv = new ListView();
            lv.View = View.Details; lv.CheckBoxes = true; lv.FullRowSelect = true; lv.HideSelection = false;
            lv.SetBounds(12, 96, 976, 330);
            lv.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lv.Columns.Add(Lang.T("an.col.date"), 150);
            lv.Columns.Add(Lang.T("an.col.field"), 130);
            lv.Columns.Add(Lang.T("an.col.product"), 170);
            lv.Columns.Add(Lang.T("an.col.area"), 70, HorizontalAlignment.Right);
            lv.Columns.Add(Lang.T("an.col.lohko"), 110);
            lv.Columns.Add(Lang.T("an.col.item"), 190);
            lv.Columns.Add(Lang.T("an.col.amount"), 70, HorizontalAlignment.Right);
            lv.Columns.Add(Lang.T("an.col.done"), 90);
            Controls.Add(lv);
            lv.SelectedIndexChanged += delegate { ShowSelected(); };

            int y = 434;
            chkShowDone = new CheckBox();
            chkShowDone.Text = Lang.T("an.showDone");
            chkShowDone.SetBounds(12, y, 400, 24);
            chkShowDone.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            chkShowDone.CheckedChanged += delegate { Fill(); };
            Controls.Add(chkShowDone);

            rbCovered = new RadioButton(); rbCovered.Text = Lang.T("an.areaCovered");
            rbCovered.SetBounds(430, y, 260, 24); rbCovered.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            rbDriven = new RadioButton(); rbDriven.Text = Lang.T("an.areaDriven");
            rbDriven.SetBounds(700, y, 288, 24); rbDriven.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(rbCovered); Controls.Add(rbDriven);
            if (map.UseCovered) rbCovered.Checked = true; else rbDriven.Checked = true;
            rbCovered.CheckedChanged += delegate { map.UseCovered = rbCovered.Checked; map.Save(); Fill(); };

            y += 32;
            GroupBox g = new GroupBox();
            g.Text = Lang.T("an.mapping");
            g.SetBounds(12, y, 976, 144);
            g.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(g);
            lblSel = Ui.Lbl(g, "", 12, 22, 950, 22, 9);
            lblSel.ForeColor = Color.DimGray;
            // labels above the boxes, each exactly as wide as its box (two lines allowed)
            int[] xs = { 12, 182, 542, 712 }, ws = { 160, 350, 160, 160 };
            string[] keys = { "an.lohko", "an.item", "an.amount", "an.areaThis" };
            for (int i = 0; i < 4; i++)
            {
                Label l = Ui.Lbl(g, Lang.T(keys[i]), xs[i], 46, ws[i], 38, 9);
                l.TextAlign = ContentAlignment.BottomLeft;
            }
            tbLohko = new TextBox(); tbLohko.SetBounds(xs[0], 86, ws[0], 28); g.Controls.Add(tbLohko);
            tbItem = new TextBox(); tbItem.SetBounds(xs[1], 86, ws[1], 28); g.Controls.Add(tbItem);
            tbAmount = new TextBox(); tbAmount.SetBounds(xs[2], 86, ws[2], 28); g.Controls.Add(tbAmount);
            tbArea = new TextBox(); tbArea.SetBounds(xs[3], 86, ws[3], 28); g.Controls.Add(tbArea);
            Button kb = Ui.Btn(g, "⌨", 884, 84, 50, 30, Ui.Grey, 12);
            kb.Click += delegate { Ui.Keyboard(); };
            Label hint = Ui.Lbl(g, Lang.T("an.mappingHint"), 12, 118, 950, 20, 8);
            hint.ForeColor = Color.DimGray;
            tbLohko.TextChanged += delegate { EditMapping(); };
            tbItem.TextChanged += delegate { EditMapping(); };
            tbAmount.TextChanged += delegate { EditMapping(); };
            tbArea.TextChanged += delegate { EditArea(); };

            y += 154;
            lblStatus = Ui.Lbl(this, "", 12, y + 6, 560, 44, 9);
            lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            btnExport = Ui.Btn(this, Lang.T("an.export"), 588, y, 260, 50, Ui.Green, 11);
            btnExport.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnExport.Click += delegate { Export(); };
            btnClose = Ui.Btn(this, Lang.T("common.close"), 858, y, 130, 50, Ui.Grey, 11);
            btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnClose.Click += delegate { Close(); };

            poll.Interval = 500;
            poll.Tick += delegate { CheckWorker(); };
            FormClosing += delegate(object o, FormClosingEventArgs e) { if (worker != null && !worker.HasExited) e.Cancel = true; };
            FormClosed += delegate { poll.Stop(); };

            Shown += delegate { if (s.DataDir == "" || !Directory.Exists(s.DataDir)) ChooseFolder(); };
            Reload();
            ShowSelected();
        }

        void ChooseFolder()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = Lang.T("an.folderHelp");
                if (Directory.Exists(s.DataDir)) d.SelectedPath = s.DataDir;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                s.DataDir = d.SelectedPath;
                s.Save();
            }
            Reload();
        }

        void Reload()
        {
            tbFolder.Text = s.DataDir;
            runs = Directory.Exists(s.DataDir) ? AnRun.LoadAll(s.DataDir) : new List<AnRun>();
            Fill();
            ShowSelected();
            lblStatus.ForeColor = Ui.Dark;
            lblStatus.Text = Directory.Exists(s.DataDir) ? Lang.T("an.loaded", runs.Count) : Lang.T("an.noFolder");
        }

        double MeasuredArea(AnRun r) { return map.UseCovered && r.CoveredHa > 0 ? r.CoveredHa : r.AreaHa; }

        double AreaOf(AnRun r)
        {
            double h;
            return map.AreaOverride.TryGetValue(r.Id, out h) ? h : MeasuredArea(r);
        }

        void EditArea()
        {
            if (filling) return;
            AnRun r = Selected();
            if (r == null) return;
            string t = tbArea.Text.Trim().Replace(',', '.');
            double h;
            if (t == "") map.AreaOverride.Remove(r.Id);
            else if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out h) && h > 0 && h < 10000) map.AreaOverride[r.Id] = h;
            else { tbArea.BackColor = Color.MistyRose; return; }
            tbArea.BackColor = SystemColors.Window;
            map.Save();
            RefreshRows();
        }

        void Fill()
        {
            filling = true;
            HashSet<string> wasChecked = new HashSet<string>();
            foreach (ListViewItem it in lv.Items) if (it.Checked) wasChecked.Add(((AnRun)it.Tag).Id);
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (AnRun r in runs)
            {
                string done = map.Get(map.Exported, r.Id);
                if (done != "" && !chkShowDone.Checked) continue;
                ListViewItem it = new ListViewItem(r.Start.ToString("d", Lang.Culture) + " " + r.Start.ToString("HH:mm"));
                it.SubItems.Add(r.Field);
                it.SubItems.Add(r.Title);
                it.SubItems.Add(AreaOf(r).ToString("0.00", Lang.Culture) + (map.AreaOverride.ContainsKey(r.Id) ? " *" : ""));
                it.SubItems.Add(map.Get(map.Lohko, r.Field));
                it.SubItems.Add(map.Get(map.Item, r.ItemKey));
                it.SubItems.Add(map.Get(map.Amount, r.ItemKey));
                it.SubItems.Add(done);
                it.Tag = r;
                it.Checked = wasChecked.Contains(r.Id);
                if (done != "") it.ForeColor = Color.Gray;
                lv.Items.Add(it);
            }
            lv.EndUpdate();
            filling = false;
            if (lv.Items.Count == 0) lblStatus.Text = Lang.T("an.noRuns");
        }

        void RefreshRows()
        {
            foreach (ListViewItem it in lv.Items)
            {
                AnRun r = (AnRun)it.Tag;
                it.SubItems[4].Text = map.Get(map.Lohko, r.Field);
                it.SubItems[5].Text = map.Get(map.Item, r.ItemKey);
                it.SubItems[6].Text = map.Get(map.Amount, r.ItemKey);
                it.SubItems[3].Text = AreaOf(r).ToString("0.00", Lang.Culture) + (map.AreaOverride.ContainsKey(r.Id) ? " *" : "");
            }
        }

        AnRun Selected() { return lv.SelectedItems.Count > 0 ? (AnRun)lv.SelectedItems[0].Tag : null; }

        void ShowSelected()
        {
            AnRun r = Selected();
            filling = true;
            bool en = r != null;
            tbLohko.Enabled = tbItem.Enabled = tbAmount.Enabled = tbArea.Enabled = en;
            double ov;
            tbArea.Text = en && map.AreaOverride.TryGetValue(r.Id, out ov) ? ov.ToString("0.##", Lang.Culture) : "";
            tbArea.BackColor = SystemColors.Window;
            tbLohko.Text = en ? map.Get(map.Lohko, r.Field) : "";
            tbItem.Text = en ? map.Get(map.Item, r.ItemKey) : "";
            tbAmount.Text = en ? map.Get(map.Amount, r.ItemKey) : "";
            lblSel.Text = en ? Lang.T("an.selected", r.Field == "" ? "–" : r.Field, r.Title) + "  " +
                               Lang.T("an.measured", MeasuredArea(r).ToString("0.00", Lang.Culture)) : Lang.T("an.selectRow");
            filling = false;
        }

        void EditMapping()
        {
            if (filling) return;
            AnRun r = Selected();
            if (r == null) return;
            if (r.Field != "") map.Lohko[r.Field] = tbLohko.Text.Trim();
            map.Item[r.ItemKey] = tbItem.Text.Trim();
            map.Amount[r.ItemKey] = tbAmount.Text.Trim();
            map.Save();
            RefreshRows();
        }

        string InfoText(AnRun r)
        {
            StringBuilder sb = new StringBuilder("AgPath: ");
            sb.Append(r.Title).Append(", ").Append(r.Start.ToString("HH:mm")).Append("–").Append(r.End.ToString("HH:mm"));
            sb.Append(", ").Append((r.DistanceM / 1000.0).ToString("0.00", Lang.Culture)).Append(" km");
            if (r.WidthM > 0) sb.Append(", ").Append(Lang.T("an.widthShort")).Append(" ").Append(r.WidthM.ToString("0.##", Lang.Culture)).Append(" m");
            sb.Append(", ").Append(map.UseCovered ? Lang.T("an.areaCoveredShort") : Lang.T("an.areaDrivenShort"));
            if (r.Notes.Trim() != "") sb.Append(". ").Append(r.Notes.Trim().Replace("\r", " ").Replace("\n", " "));
            return sb.ToString();
        }

        void Export()
        {
            List<AnRun> chosen = new List<AnRun>();
            foreach (ListViewItem it in lv.Items) if (it.Checked) chosen.Add((AnRun)it.Tag);
            if (chosen.Count == 0) { MessageBox.Show(this, Lang.T("an.noneChecked"), Text); return; }
            foreach (AnRun r in chosen)
            {
                if (map.Get(map.Lohko, r.Field) == "" || map.Get(map.Item, r.ItemKey) == "")
                {
                    MessageBox.Show(this, Lang.T("an.missingMapping", r.Start.ToString("d", Lang.Culture), r.Title), Text,
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            if (Process.GetProcessesByName("AgrineuvosClient").Length == 0)
            {
                MessageBox.Show(this, Lang.T("an.err.notRunning"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(this, Lang.T("an.confirm", chosen.Count), Text, MessageBoxButtons.OKCancel,
                                MessageBoxIcon.Information) != DialogResult.OK) return;

            List<string> lines = new List<string>();
            foreach (AnRun r in chosen)
            {
                AnJob j = new AnJob();
                j.Id = r.Id;
                j.Lohko = map.Get(map.Lohko, r.Field);
                j.Item = map.Get(map.Item, r.ItemKey);
                j.Amount = map.Get(map.Amount, r.ItemKey);
                j.Date = r.Start.Date;
                j.AreaHa = AreaOf(r);
                j.Info = InfoText(r);
                lines.Add(j.ToLine());
            }
            jobFile = Path.Combine(Program.AppDir, "agrineuvos_job.txt");
            File.WriteAllLines(jobFile, lines.ToArray(), new UTF8Encoding(false));
            try { File.Delete(jobFile + ".result"); } catch { }
            sent = chosen;

            btnExport.Enabled = btnClose.Enabled = false;
            lblStatus.ForeColor = Ui.Dark;
            lblStatus.Text = Lang.T("an.running");
            // our windows must not cover Agrineuvos while it is being operated. Minimizing is not possible
            // (it would end this dialog), so drop "always on top" and move them behind other windows.
            topMost.Clear();
            foreach (Form f in Application.OpenForms)
            {
                topMost[f] = f.TopMost;
                f.TopMost = false;
                SetWindowPos(f.Handle, (IntPtr)1, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // HWND_BOTTOM, no size/move/activate
            }
            try { AllowSetForegroundWindow(-1); } catch { }
            ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "--agrineuvos \"" + jobFile + "\"");
            psi.UseShellExecute = false;
            worker = Process.Start(psi);
            poll.Start();
        }

        void CheckWorker()
        {
            if (worker == null || !worker.HasExited) return;
            poll.Stop();
            worker = null;
            if (IsDisposed) return;
            foreach (KeyValuePair<Form, bool> kv in topMost) if (!kv.Key.IsDisposed) kv.Key.TopMost = kv.Value;
            Activate();
            btnExport.Enabled = btnClose.Enabled = true;

            int ok = 0;
            List<string> errors = new List<string>();
            string rf = jobFile + ".result";
            string[] res = File.Exists(rf) ? File.ReadAllLines(rf, Encoding.UTF8) : new string[0];
            foreach (string line in res)
            {
                string[] p = line.Split('\t');
                if (p.Length < 2) continue;
                if (p[0] == "OK")
                {
                    ok++;
                    map.Exported[p[1]] = DateTime.Now.ToString("d", Lang.Culture);
                }
                else
                {
                    string what = p[1];
                    foreach (AnRun r in sent) if (r.Id == p[1]) what = r.Start.ToString("d", Lang.Culture) + " " + r.Title;
                    errors.Add((what != "" ? what + ": " : "") + (p.Length > 2 ? p[2] : ""));
                }
            }
            map.Save();
            Fill();
            string msg = Lang.T("an.done", ok, sent.Count);
            if (errors.Count > 0) msg += "\n\n" + string.Join("\n", errors.ToArray());
            lblStatus.ForeColor = errors.Count > 0 ? Ui.Red : Ui.Green;
            lblStatus.Text = Lang.T("an.done", ok, sent.Count);
            MessageBox.Show(this, msg + "\n\n" + Lang.T("an.checkInAgrineuvos"), Text, MessageBoxButtons.OK,
                            errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
