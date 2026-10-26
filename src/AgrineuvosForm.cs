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
            try { File.WriteAllLines(MapFile, lines.ToArray(), new UTF8Encoding(false)); }
            catch (Exception ex) { Log.Write("Agrineuvos mapping: " + ex.Message); }
        }

        public string Get(Dictionary<string, string> d, string key) { string v; return d.TryGetValue(key ?? "", out v) ? v : ""; }
    }

    /// <summary>Choose saved runs and enter them into Agrineuvos as completed operations.</summary>
    class AgrineuvosForm : Form
    {
        readonly Settings s;
        readonly AnMapping map = AnMapping.Load();
        List<AnRun> runs = new List<AnRun>();
        ListView lv;
        TextBox tbLohko, tbItem, tbAmount;
        Label lblSel, lblStatus;
        CheckBox chkShowDone;
        RadioButton rbCovered, rbDriven;
        Button btnExport, btnClose;
        bool filling;
        readonly Timer poll = new Timer();
        Process worker;
        string jobFile;
        List<AnRun> sent;

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

        public AgrineuvosForm(Settings settings)
        {
            s = settings;
            Text = Lang.T("an.title");
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = true; MaximizeBox = true;
            TopMost = true;
            BackColor = Color.White;
            Font = Ui.F(10);
            ClientSize = new Size(1000, 640);
            MinimumSize = new Size(800, 520);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Label intro = Ui.Lbl(this, Lang.T("an.intro"), 12, 8, 976, 44, 9);
            intro.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            lv = new ListView();
            lv.View = View.Details; lv.CheckBoxes = true; lv.FullRowSelect = true; lv.HideSelection = false;
            lv.SetBounds(12, 56, 976, 330);
            lv.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lv.Columns.Add(Lang.T("an.col.date"), 120);
            lv.Columns.Add(Lang.T("an.col.field"), 130);
            lv.Columns.Add(Lang.T("an.col.product"), 190);
            lv.Columns.Add(Lang.T("an.col.area"), 70, HorizontalAlignment.Right);
            lv.Columns.Add(Lang.T("an.col.lohko"), 110);
            lv.Columns.Add(Lang.T("an.col.item"), 190);
            lv.Columns.Add(Lang.T("an.col.amount"), 70, HorizontalAlignment.Right);
            lv.Columns.Add(Lang.T("an.col.done"), 90);
            Controls.Add(lv);
            lv.SelectedIndexChanged += delegate { ShowSelected(); };

            int y = 394;
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
            g.SetBounds(12, y, 976, 130);
            g.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(g);
            lblSel = Ui.Lbl(g, "", 12, 22, 950, 22, 9);
            lblSel.ForeColor = Color.DimGray;
            Ui.Lbl(g, Lang.T("an.lohko") + ":", 12, 52, 300, 22, 9);
            tbLohko = new TextBox(); tbLohko.SetBounds(12, 76, 200, 28); g.Controls.Add(tbLohko);
            Ui.Lbl(g, Lang.T("an.item") + ":", 230, 52, 420, 22, 9);
            tbItem = new TextBox(); tbItem.SetBounds(230, 76, 420, 28); g.Controls.Add(tbItem);
            Ui.Lbl(g, Lang.T("an.amount") + ":", 668, 52, 290, 22, 9);
            tbAmount = new TextBox(); tbAmount.SetBounds(668, 76, 120, 28); g.Controls.Add(tbAmount);
            Button kb = Ui.Btn(g, "⌨", 800, 74, 44, 30, Ui.Grey, 12);
            kb.Click += delegate { Ui.Keyboard(); };
            Label hint = Ui.Lbl(g, Lang.T("an.mappingHint"), 12, 106, 950, 20, 8);
            hint.ForeColor = Color.DimGray;
            tbLohko.TextChanged += delegate { EditMapping(); };
            tbItem.TextChanged += delegate { EditMapping(); };
            tbAmount.TextChanged += delegate { EditMapping(); };

            y += 140;
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

            runs = AnRun.LoadAll(s.SaveDir);
            Fill();
            ShowSelected();
        }

        double AreaOf(AnRun r) { return map.UseCovered && r.CoveredHa > 0 ? r.CoveredHa : r.AreaHa; }

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
                it.SubItems.Add(AreaOf(r).ToString("0.00", Lang.Culture));
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
            }
        }

        AnRun Selected() { return lv.SelectedItems.Count > 0 ? (AnRun)lv.SelectedItems[0].Tag : null; }

        void ShowSelected()
        {
            AnRun r = Selected();
            filling = true;
            bool en = r != null;
            tbLohko.Enabled = tbItem.Enabled = tbAmount.Enabled = en;
            tbLohko.Text = en ? map.Get(map.Lohko, r.Field) : "";
            tbItem.Text = en ? map.Get(map.Item, r.ItemKey) : "";
            tbAmount.Text = en ? map.Get(map.Amount, r.ItemKey) : "";
            lblSel.Text = en ? Lang.T("an.selected", r.Field == "" ? "–" : r.Field, r.Title) : Lang.T("an.selectRow");
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
            // our windows must not cover Agrineuvos while it is being operated
            foreach (Form f in Application.OpenForms) if (f.Visible) f.WindowState = FormWindowState.Minimized;
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
            foreach (Form f in Application.OpenForms) f.WindowState = FormWindowState.Normal;
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
    }
}
