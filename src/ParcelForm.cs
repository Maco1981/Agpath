// AgPath - records driven paths alongside AgOpenGPS.
// Copyright 2026 AgPath contributors. Licensed under the Apache License 2.0, see LICENSE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace AgPath
{
    /// <summary>Find basic parcels (peruslohkot) from the open register and create AgOpenGPS fields from them.</summary>
    class ParcelForm : Form
    {
        /// <summary>Set by the main window: returns the current GPS position, if any.</summary>
        public static Func<double[]> CurrentPosition;

        readonly Settings s;
        readonly FieldDetector fields = new FieldDetector();
        ComboBox cbCenter, cbRadius;
        TextBox tbIds, tbName;
        ListView lv;
        Label lblStatus, lblSource;
        Button btnSearch, btnMap, btnCreate;
        List<Parcel> parcels = new List<Parcel>();
        readonly Dictionary<string, string> names = new Dictionary<string, string>();
        int year;
        bool filling;
        double[] searchCenter;

        public ParcelForm(Settings settings)
        {
            s = settings;
            fields.Load(s.AogBaseDir);
            Text = Lang.T("pi.title");
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            BackColor = Color.White;
            Font = Ui.F(10);
            ClientSize = new Size(1000, 640);
            MinimumSize = new Size(820, 520);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Label intro = Ui.Lbl(this, Lang.T("pi.intro"), 12, 8, 976, 44, 9);
            intro.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            Ui.Lbl(this, Lang.T("pi.around") + ":", 12, 60, 90, 24, 10);
            cbCenter = new ComboBox(); cbCenter.DropDownStyle = ComboBoxStyle.DropDownList;
            cbCenter.SetBounds(104, 57, 300, 28);
            cbCenter.Items.Add(Lang.T("pi.tractor"));
            foreach (string f in fields.FieldNames) if (fields.Center(f) != null) cbCenter.Items.Add(f);
            cbCenter.SelectedIndex = 0;
            Controls.Add(cbCenter);
            Ui.TouchCombo(cbCenter);
            cbRadius = new ComboBox(); cbRadius.DropDownStyle = ComboBoxStyle.DropDownList;
            cbRadius.SetBounds(414, 57, 110, 28);
            foreach (string r in new string[] { "0,5 km", "1 km", "2 km", "5 km" }) cbRadius.Items.Add(r);
            cbRadius.SelectedIndex = 1;
            Controls.Add(cbRadius);
            Ui.Lbl(this, Lang.T("pi.orIds") + ":", 540, 60, 150, 24, 10);
            tbIds = new TextBox(); tbIds.SetBounds(690, 57, 160, 28); Controls.Add(tbIds);
            tbIds.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnSearch = Ui.Btn(this, Lang.T("pi.search"), 860, 54, 128, 34, Ui.Green, 10);
            btnSearch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSearch.Click += delegate { Search(); };

            lv = new ListView();
            lv.View = View.Details; lv.CheckBoxes = true; lv.FullRowSelect = true; lv.HideSelection = false;
            lv.SetBounds(12, 96, 976, 330);
            lv.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lv.Columns.Add(Lang.T("pi.col.id"), 130);
            lv.Columns.Add(Lang.T("pi.col.area"), 70, HorizontalAlignment.Right);
            lv.Columns.Add(Lang.T("pi.col.crop"), 190);
            lv.Columns.Add(Lang.T("pi.col.dist"), 80, HorizontalAlignment.Right);
            lv.Columns.Add(Lang.T("pi.col.aog"), 170);
            lv.Columns.Add(Lang.T("pi.col.name"), 300);
            Controls.Add(lv);
            lv.SelectedIndexChanged += delegate { ShowSelected(); };
            lv.ItemChecked += delegate(object o, ItemCheckedEventArgs e)
            {
                if (filling) return;
                Parcel p = (Parcel)e.Item.Tag;
                if (e.Item.Checked && NameOf(p) == "") { names[p.Tunnus] = DefaultName(p); e.Item.SubItems[5].Text = names[p.Tunnus]; }
            };

            int y = 434;
            Label ln = Ui.Lbl(this, Lang.T("pi.name") + ":", 12, y + 4, 330, 24, 10);
            ln.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            tbName = new TextBox(); tbName.SetBounds(342, y, 320, 28); Controls.Add(tbName);
            tbName.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            tbName.TextChanged += delegate { EditName(); };
            Button kb = Ui.Btn(this, "⌨", 670, y - 1, 44, 30, Ui.Grey, 12);
            kb.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            kb.Click += delegate { Ui.Keyboard(); };
            Label hint = Ui.Lbl(this, Lang.T("pi.hint"), 12, y + 36, 976, 52, 8);
            hint.ForeColor = Color.DimGray;
            hint.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            y += 96;
            lblStatus = Ui.Lbl(this, "", 12, y, 430, 40, 9);
            lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            btnMap = Ui.Btn(this, Lang.T("pi.showMap"), 452, y, 150, 50, Ui.Grey, 10);
            btnMap.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnMap.Click += delegate { ShowMap(); };
            btnCreate = Ui.Btn(this, Lang.T("pi.create"), 612, y, 236, 50, Ui.Green, 10);
            btnCreate.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCreate.Click += delegate { Create(); };
            Button close = Ui.Btn(this, Lang.T("common.close"), 858, y, 130, 50, Ui.Grey, 11);
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.Click += delegate { Close(); };
            lblSource = Ui.Lbl(this, "", 12, y + 42, 430, 18, 8);
            lblSource.ForeColor = Color.DimGray;
            lblSource.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnMap.Enabled = btnCreate.Enabled = false;
            ShowSelected();
        }

        string NameOf(Parcel p) { string n; return names.TryGetValue(p.Tunnus, out n) ? n : ""; }
        static string DefaultName(Parcel p) { return p.TunnusPretty; }

        double[] Center()
        {
            if (cbCenter.SelectedIndex > 0) return fields.Center((string)cbCenter.SelectedItem);
            if (CurrentPosition == null) return null;
            return CurrentPosition();
        }

        void Search()
        {
            List<string> ids = new List<string>();
            foreach (Match m in Regex.Matches(tbIds.Text, @"\d[\d\- ]*\d"))
            {
                string d = Regex.Replace(m.Value, @"\D", "");
                if (d.Length == 10) ids.Add(d);
            }
            double[] c = ids.Count > 0 ? null : Center();
            if (ids.Count == 0 && c == null) { Status(Lang.T("pi.noCenter"), true); return; }
            double radius = new double[] { 500, 1000, 2000, 5000 }[Math.Max(0, cbRadius.SelectedIndex)];
            searchCenter = c;
            btnSearch.Enabled = btnMap.Enabled = btnCreate.Enabled = false;
            Status(Lang.T("pi.searching"), false);
            Cursor = Cursors.WaitCursor;
            Thread t = new Thread(delegate()
            {
                List<Parcel> found = null; string err = null; int yr = 0;
                try
                {
                    yr = ParcelImport.LatestYear();
                    double[] box = null;
                    if (c != null)
                    {
                        double e, n;
                        Tm35.ToTm(c[0], c[1], out e, out n);
                        box = new double[] { e - radius, n - radius, e + radius, n + radius };
                    }
                    found = ParcelImport.Fetch(yr, box, ids);
                }
                catch (Exception ex) { err = ex.Message; Log.Write("Parcel search: " + ex); }
                BeginInvoke((MethodInvoker)delegate { SearchDone(found, yr, err); });
            });
            t.IsBackground = true;
            t.Start();
        }

        void SearchDone(List<Parcel> found, int yr, string err)
        {
            Cursor = Cursors.Default;
            btnSearch.Enabled = true;
            if (err != null) { Status(Lang.T("pi.err.search", err), true); return; }
            year = yr;
            foreach (Parcel p in found)
            {
                p.Overlaps = fields.FieldAt(p.CLat, p.CLon);
                if (p.Overlaps == "")
                    foreach (double[] q in p.Ring)
                    {
                        // a parcel whose corner lies in an existing field is shown as overlapping too
                        string f = fields.FieldAt(q[0] + (p.CLat - q[0]) * 0.1, q[1] + (p.CLon - q[1]) * 0.1);
                        if (f != "") { p.Overlaps = f; break; }
                    }
                p.DistanceM = searchCenter == null ? 0 : Dist(searchCenter[0], searchCenter[1], p.CLat, p.CLon);
            }
            found.Sort(delegate(Parcel a, Parcel b) { return a.DistanceM.CompareTo(b.DistanceM); });
            parcels = found;
            Fill();
            lblSource.Text = Lang.T("pi.source", ParcelImport.Attribution, year);
            Status(Lang.T("pi.found", parcels.Count, year), false);
            btnMap.Enabled = btnCreate.Enabled = parcels.Count > 0;
        }

        static double Dist(double la1, double lo1, double la2, double lo2)
        {
            double k = Math.PI / 180;
            double x = (lo2 - lo1) * k * Math.Cos((la1 + la2) / 2 * k), y = (la2 - la1) * k;
            return Math.Sqrt(x * x + y * y) * 6371000;
        }

        void Fill()
        {
            filling = true;
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (Parcel p in parcels)
            {
                ListViewItem it = new ListViewItem(p.TunnusPretty);
                it.SubItems.Add(p.AreaHa.ToString("0.00", Lang.Culture));
                it.SubItems.Add(p.Crops);
                it.SubItems.Add(searchCenter == null ? "" : (p.DistanceM / 1000).ToString("0.0", Lang.Culture) + " km");
                it.SubItems.Add(p.Overlaps);
                it.SubItems.Add(NameOf(p));
                it.Tag = p;
                if (p.Overlaps != "") it.ForeColor = Color.Gray;
                lv.Items.Add(it);
            }
            lv.EndUpdate();
            filling = false;
        }

        Parcel Selected() { return lv.SelectedItems.Count > 0 ? (Parcel)lv.SelectedItems[0].Tag : null; }

        void ShowSelected()
        {
            Parcel p = Selected();
            filling = true;
            tbName.Enabled = p != null;
            tbName.Text = p == null ? "" : NameOf(p);
            tbName.BackColor = SystemColors.Window;
            filling = false;
        }

        void EditName()
        {
            if (filling || lv.SelectedItems.Count == 0) return;
            ListViewItem it = lv.SelectedItems[0];
            Parcel p = (Parcel)it.Tag;
            string n = tbName.Text.Trim();
            names[p.Tunnus] = n;
            it.SubItems[5].Text = n;
            tbName.BackColor = n == "" || ParcelImport.ValidName(n) ? SystemColors.Window : Color.MistyRose;
            if (n != "" && !it.Checked) { filling = true; it.Checked = true; filling = false; }
        }

        void Status(string text, bool error)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = error ? Ui.Red : Ui.Dark;
        }

        void ShowMap()
        {
            try
            {
                StringBuilder sb = new StringBuilder("window.PARCELS = {\n");
                sb.Append("map:{provider:").Append(Exporter.J(s.MapProvider)).Append(",mmlKey:").Append(Exporter.J(s.MmlApiKey))
                  .Append(",url:").Append(Exporter.J(s.CustomTileUrl)).Append(",attribution:").Append(Exporter.J(s.CustomAttribution)).Append("},\n");
                sb.Append("attribution:").Append(Exporter.J(ParcelImport.Attribution + " " + year)).Append(",\n");
                sb.Append("texts:{");
                string[] keys = { "ortho", "topo", "custom", "overlaps", "overlapsShort", "you", "info", "selected", "notSelected" };
                for (int i = 0; i < keys.Length; i++)
                    sb.Append(i > 0 ? "," : "").Append(keys[i]).Append(':').Append(Exporter.J(Lang.T("pi.map." + keys[i])));
                sb.Append("},\n");
                sb.Append("fields:").Append(fields.ToGeoJson()).Append(",\n");
                if (searchCenter != null)
                    sb.Append("pos:[").Append(searchCenter[0].ToString("0.0000000", CultureInfo.InvariantCulture)).Append(',')
                      .Append(searchCenter[1].ToString("0.0000000", CultureInfo.InvariantCulture)).Append("],\n");
                sb.Append("parcels:[\n");
                HashSet<string> checkedIds = new HashSet<string>();
                foreach (ListViewItem it in lv.Items) if (it.Checked) checkedIds.Add(((Parcel)it.Tag).Tunnus);
                bool first = true;
                foreach (Parcel p in parcels)
                {
                    if (!first) sb.Append(",\n");
                    first = false;
                    sb.Append("{pretty:").Append(Exporter.J(p.TunnusPretty)).Append(",name:").Append(Exporter.J(NameOf(p)))
                      .Append(",area:").Append(Exporter.J(p.AreaHa.ToString("0.00", Lang.Culture))).Append(",crops:").Append(Exporter.J(p.Crops))
                      .Append(",overlaps:").Append(Exporter.J(p.Overlaps)).Append(",selected:").Append(checkedIds.Contains(p.Tunnus) ? "true" : "false")
                      .Append(",ring:[");
                    for (int i = 0; i < p.Ring.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append('[').Append(p.Ring[i][0].ToString("0.0000000", CultureInfo.InvariantCulture)).Append(',')
                          .Append(p.Ring[i][1].ToString("0.0000000", CultureInfo.InvariantCulture)).Append(']');
                    }
                    sb.Append("]}");
                }
                sb.Append("]};\n");
                string dir = MapData.MapDir;
                File.WriteAllText(Path.Combine(dir, "parcels.js"), sb.ToString(), new UTF8Encoding(false));
                string html = Path.Combine(dir, "parcels.html");
                if (!File.Exists(html)) { Status(Lang.T("main.mapMissing"), true); return; }
                Process.Start(html);
            }
            catch (Exception ex) { Status(ex.Message, true); }
        }

        void Create()
        {
            List<KeyValuePair<Parcel, string>> todo = new List<KeyValuePair<Parcel, string>>();
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in fields.FieldNames) used.Add(f);
            string fieldsDir = Path.Combine(s.AogBaseDir, "Fields");
            foreach (ListViewItem it in lv.Items)
            {
                if (!it.Checked) continue;
                Parcel p = (Parcel)it.Tag;
                string n = NameOf(p);
                if (n == "") n = DefaultName(p);
                if (!ParcelImport.ValidName(n)) { Status(Lang.T("pi.err.badName", n), true); return; }
                if (used.Contains(n) || Directory.Exists(Path.Combine(fieldsDir, n))) { Status(Lang.T("pi.err.exists", n), true); return; }
                used.Add(n);
                todo.Add(new KeyValuePair<Parcel, string>(p, n));
            }
            if (todo.Count == 0) { Status(Lang.T("pi.noneChecked"), true); return; }
            if (!Directory.Exists(fieldsDir)) { Status(Lang.T("pi.err.noAog", fieldsDir), true); return; }
            if (MessageBox.Show(this, Lang.T("pi.confirm", todo.Count, fieldsDir), Text, MessageBoxButtons.OKCancel,
                                MessageBoxIcon.Information) != DialogResult.OK) return;
            int ok = 0;
            List<string> errors = new List<string>();
            foreach (KeyValuePair<Parcel, string> kv in todo)
            {
                try { ParcelImport.CreateField(s.AogBaseDir, kv.Value, kv.Key); ok++; }
                catch (Exception ex) { errors.Add(kv.Value + ": " + ex.Message); Log.Write("Create field: " + ex); }
            }
            fields.Load(s.AogBaseDir);
            foreach (Parcel p in parcels) p.Overlaps = fields.FieldAt(p.CLat, p.CLon);
            foreach (ListViewItem it in lv.Items) it.Checked = false;
            Fill();
            string msg = Lang.T("pi.created", ok);
            if (errors.Count > 0) msg += "\n" + string.Join("\n", errors.ToArray());
            Status(msg, errors.Count > 0);
            MessageBox.Show(this, msg + "\n\n" + Lang.T("pi.createdHint"), Text, MessageBoxButtons.OK,
                            errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
    }
}
