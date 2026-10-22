using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AgPath
{
    class SettingsForm : Form
    {
        readonly Settings s;
        RadioButton rbAog, rbOwn;
        NumericUpDown nudWidth, nudStep;
        CheckBox chkOnlyWork, chkTop;
        TextBox tbAog, tbSave, tbIp, tbKey, tbUrl, tbAttr;
        ComboBox cbLang, cbMap, cbFix, cbUnits;
        readonly List<string> langCodes = new List<string>();

        public SettingsForm(Settings settings)
        {
            s = settings;
            Text = Lang.T("settings.title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false; MaximizeBox = false;
            TopMost = true;
            BackColor = Color.White;
            Font = Ui.F(10);

            // Two columns so that everything fits on a low tablet screen without scrolling.
            const int L = 15, R = 625, W = 570;
            int y = 10;

            // ======== left column: general, width, recording ========
            Ui.Lbl(this, Lang.T("settings.language") + ":", L, y + 4, 200, 24, 10);
            cbLang = new ComboBox();
            cbLang.DropDownStyle = ComboBoxStyle.DropDownList;
            cbLang.SetBounds(L + 205, y, W - 205, 28);
            langCodes.Add("auto");
            cbLang.Items.Add(Lang.T("settings.languageAuto", Lang.AogLanguage()));
            foreach (KeyValuePair<string, string> kv in Lang.Available()) { langCodes.Add(kv.Key); cbLang.Items.Add(kv.Value + " (" + kv.Key + ")"); }
            int li = langCodes.IndexOf(s.Language);
            cbLang.SelectedIndex = li >= 0 ? li : 0;
            Controls.Add(cbLang);
            y += 36;

            Ui.Lbl(this, Lang.T("settings.units") + ":", L, y + 4, 200, 24, 10);
            cbUnits = new ComboBox();
            cbUnits.DropDownStyle = ComboBoxStyle.DropDownList;
            cbUnits.SetBounds(L + 205, y, W - 205, 28);
            cbUnits.Items.Add(Lang.T("settings.unitsAuto", Units.AogIsMetric(s.AogBaseDir) ? Lang.T("settings.unitsMetricShort") : Lang.T("settings.unitsImperialShort")));
            cbUnits.Items.Add(Lang.T("settings.unitsMetric"));
            cbUnits.Items.Add(Lang.T("settings.unitsImperial"));
            cbUnits.SelectedIndex = s.Units == "metric" ? 1 : s.Units == "imperial" ? 2 : 0;
            Controls.Add(cbUnits);
            y += 44;

            Ui.Lbl(this, Lang.T("settings.widthTitle"), L, y, W, 24, 11).Font = Ui.FB(11);
            y += 28;
            string prof = Settings.ActiveToolProfile();
            double aw = Settings.ReadAogToolWidth(s.AogBaseDir);
            rbAog = new RadioButton();
            rbAog.Text = Lang.T("settings.widthFromAog") + (prof != "" ? ": " + prof : "") +
                         (aw > 0 ? " (" + Units.Width(aw) + ")" : " (" + Lang.T("settings.notFound") + ")");
            rbAog.SetBounds(L + 15, y, W - 15, 26);
            Controls.Add(rbAog);
            y += 30;
            rbOwn = new RadioButton();
            rbOwn.Text = Lang.T("settings.widthOwn") + ", " + Units.WidthUnit + ":";
            rbOwn.SetBounds(L + 15, y, 165, 26);
            Controls.Add(rbOwn);
            nudWidth = new NumericUpDown();
            nudWidth.SetBounds(L + 185, y - 2, 90, 28);
            nudWidth.DecimalPlaces = 2; nudWidth.Increment = 0.05m; nudWidth.Minimum = 0.05m; nudWidth.Maximum = 400m;
            nudWidth.Value = (decimal)Math.Round(Units.WidthValue(s.ToolWidth > 0 ? s.ToolWidth : (aw > 0 ? aw : 3.0)), 2);
            Controls.Add(nudWidth);
            ComboBox cbProfiles = new ComboBox();
            cbProfiles.DropDownStyle = ComboBoxStyle.DropDownList;
            cbProfiles.SetBounds(L + 285, y - 2, W - 285, 28);
            cbProfiles.Items.Add(Lang.T("settings.fromProfile"));
            List<KeyValuePair<string, double>> profs = Settings.ListToolProfiles(s.AogBaseDir);
            foreach (KeyValuePair<string, double> kv in profs) cbProfiles.Items.Add(kv.Key + "  " + Units.Width(kv.Value));
            cbProfiles.SelectedIndex = 0;
            cbProfiles.SelectedIndexChanged += delegate
            {
                int i = cbProfiles.SelectedIndex - 1;
                if (i >= 0 && i < profs.Count) { nudWidth.Value = (decimal)Math.Round(Units.WidthValue(profs[i].Value), 2); rbOwn.Checked = true; }
            };
            Controls.Add(cbProfiles);
            if (s.ToolWidth > 0) rbOwn.Checked = true; else rbAog.Checked = true;
            y += 36;

            chkOnlyWork = new CheckBox();
            chkOnlyWork.Text = Lang.T("settings.onlySections");
            chkOnlyWork.Checked = s.OnlyWhenSectionsOn;
            chkOnlyWork.SetBounds(L, y, W, 26);
            Controls.Add(chkOnlyWork);
            y += 40;

            Ui.Lbl(this, Lang.T("settings.recordingTitle"), L, y, W, 24, 11).Font = Ui.FB(11);
            y += 28;
            Ui.Lbl(this, Lang.T("settings.minFix") + ":", L, y + 3, 260, 24, 10);
            cbFix = new ComboBox();
            cbFix.DropDownStyle = ComboBoxStyle.DropDownList;
            cbFix.SetBounds(L + 265, y, W - 265, 28);
            cbFix.Items.Add(Lang.T("settings.minFixAll"));
            cbFix.Items.Add(Lang.T("settings.minFixFloat"));
            cbFix.Items.Add(Lang.T("settings.minFixFix"));
            cbFix.SelectedIndex = s.MinFix == "float" ? 1 : s.MinFix == "fix" ? 2 : 0;
            Controls.Add(cbFix);
            y += 30;
            Label fixHint = Ui.Lbl(this, Lang.T("settings.minFixHint"), L + 265, y, W - 265, 34, 8);
            fixHint.ForeColor = Color.DimGray;
            y += 38;
            Ui.Lbl(this, Lang.T("settings.minStep") + ":", L, y + 3, 260, 24, 10);
            nudStep = new NumericUpDown();
            nudStep.SetBounds(L + 265, y, 80, 28);
            nudStep.DecimalPlaces = 2; nudStep.Increment = 0.05m; nudStep.Minimum = 0.05m; nudStep.Maximum = 10m;
            nudStep.Value = (decimal)s.MinStep;
            Controls.Add(nudStep);
            y += 36;
            chkTop = new CheckBox();
            chkTop.Text = Lang.T("settings.topMost");
            chkTop.Checked = s.TopMost;
            chkTop.SetBounds(L, y, W, 26);
            Controls.Add(chkTop);
            y += 34;
            int leftBottom = y;

            // ======== right column: map and folders ========
            y = 10;
            Ui.Lbl(this, Lang.T("settings.mapTitle"), R, y, W, 24, 11).Font = Ui.FB(11);
            y += 28;
            cbMap = new ComboBox();
            cbMap.DropDownStyle = ComboBoxStyle.DropDownList;
            cbMap.SetBounds(R, y, W, 28);
            cbMap.Items.Add(Lang.T("settings.mapOsm"));
            cbMap.Items.Add(Lang.T("settings.mapMml"));
            cbMap.Items.Add(Lang.T("settings.mapCustom"));
            cbMap.SelectedIndex = s.MapProvider == "mml" ? 1 : s.MapProvider == "custom" ? 2 : 0;
            Controls.Add(cbMap);
            y += 36;
            Ui.Lbl(this, Lang.T("settings.mmlKey") + ":", R, y + 3, 200, 24, 10);
            tbKey = new TextBox(); tbKey.SetBounds(R + 205, y, W - 205, 28); tbKey.Text = s.MmlApiKey; Controls.Add(tbKey);
            y += 34;
            Ui.Lbl(this, Lang.T("settings.customUrl") + ":", R, y + 3, 200, 24, 10);
            tbUrl = new TextBox(); tbUrl.SetBounds(R + 205, y, W - 205, 28); tbUrl.Text = s.CustomTileUrl; Controls.Add(tbUrl);
            y += 30;
            Label hint = Ui.Lbl(this, Lang.T("settings.customUrlHint"), R + 205, y, W - 205, 20, 8);
            hint.ForeColor = Color.DimGray;
            y += 24;
            Ui.Lbl(this, Lang.T("settings.customAttr") + ":", R, y + 3, 200, 24, 10);
            tbAttr = new TextBox(); tbAttr.SetBounds(R + 205, y, W - 205, 28); tbAttr.Text = s.CustomAttribution; Controls.Add(tbAttr);
            y += 44;
            cbMap.SelectedIndexChanged += delegate { UpdateMapFields(); };
            UpdateMapFields();

            Ui.Lbl(this, Lang.T("settings.foldersTitle"), R, y, W, 24, 11).Font = Ui.FB(11);
            y += 28;
            Ui.Lbl(this, Lang.T("settings.aogFolder") + ":", R, y, W, 22, 10);
            y += 24;
            tbAog = new TextBox(); tbAog.SetBounds(R, y, W - 100, 28); tbAog.Text = s.AogBaseDir; Controls.Add(tbAog);
            Button b1 = Ui.Btn(this, Lang.T("settings.browse"), R + W - 90, y - 2, 90, 30, Ui.Grey, 9);
            b1.Click += delegate { Browse(tbAog); };
            y += 36;
            Ui.Lbl(this, Lang.T("settings.saveFolder") + ":", R, y, W, 22, 10);
            y += 24;
            tbSave = new TextBox(); tbSave.SetBounds(R, y, W - 100, 28); tbSave.Text = s.SaveDir; Controls.Add(tbSave);
            Button b2 = Ui.Btn(this, Lang.T("settings.browse"), R + W - 90, y - 2, 90, 30, Ui.Grey, 9);
            b2.Click += delegate { Browse(tbSave); };
            y += 32;
            Label shareHint = Ui.Lbl(this, Lang.T("settings.saveFolderHint", Lang.T("map.sharedFile")), R, y, W, 34, 8);
            shareHint.ForeColor = Color.DimGray;
            y += 40;
            Ui.Lbl(this, Lang.T("settings.listenIp") + ":", R, y + 3, 400, 24, 9);
            tbIp = new TextBox(); tbIp.SetBounds(R + W - 165, y, 165, 28); tbIp.Text = s.ListenIp; Controls.Add(tbIp);
            y += 40;

            y = Math.Max(y, leftBottom) + 6;
            Button ok = Ui.Btn(this, "OK", R + W - 255, y, 120, 48, Ui.Green, 12);
            Button cancel = Ui.Btn(this, Lang.T("common.cancel"), R + W - 125, y, 125, 48, Ui.Grey, 11);
            ok.Click += delegate { Apply(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            ClientSize = new Size(R + W + 15, y + 60);
        }

        void UpdateMapFields()
        {
            tbKey.Enabled = cbMap.SelectedIndex == 1;
            tbUrl.Enabled = cbMap.SelectedIndex == 2;
            tbAttr.Enabled = cbMap.SelectedIndex == 2;
        }

        void Browse(TextBox tb)
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                if (Directory.Exists(tb.Text)) d.SelectedPath = tb.Text;
                if (d.ShowDialog(this) == DialogResult.OK) tb.Text = d.SelectedPath;
            }
        }

        void Apply()
        {
            s.Language = langCodes[Math.Max(0, cbLang.SelectedIndex)];
            s.ToolWidth = rbOwn.Checked ? Units.WidthToMeters((double)nudWidth.Value) : 0;
            s.Units = cbUnits.SelectedIndex == 1 ? "metric" : cbUnits.SelectedIndex == 2 ? "imperial" : "auto";
            s.OnlyWhenSectionsOn = chkOnlyWork.Checked;
            s.TopMost = chkTop.Checked;
            s.MinStep = (double)nudStep.Value;
            s.MinFix = cbFix.SelectedIndex == 1 ? "float" : cbFix.SelectedIndex == 2 ? "fix" : "all";
            s.MapProvider = cbMap.SelectedIndex == 1 ? "mml" : cbMap.SelectedIndex == 2 ? "custom" : "osm";
            s.MmlApiKey = tbKey.Text.Trim();
            s.CustomTileUrl = tbUrl.Text.Trim();
            s.CustomAttribution = tbAttr.Text.Trim();
            if (tbAog.Text.Trim() != "") s.AogBaseDir = tbAog.Text.Trim();
            if (tbSave.Text.Trim() != "") s.SaveDir = tbSave.Text.Trim();
            System.Net.IPAddress a;
            if (System.Net.IPAddress.TryParse(tbIp.Text.Trim(), out a)) s.ListenIp = tbIp.Text.Trim();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
