using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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
        ComboBox cbLang, cbMap, cbFix;
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

            int y = 10;
            // ---- language ----
            Ui.Lbl(this, Lang.T("settings.language") + ":", 15, y + 4, 200, 24, 10);
            cbLang = new ComboBox();
            cbLang.DropDownStyle = ComboBoxStyle.DropDownList;
            cbLang.SetBounds(220, y, 365, 28);
            langCodes.Add("auto");
            cbLang.Items.Add(Lang.T("settings.languageAuto", Lang.AogLanguage()));
            foreach (KeyValuePair<string, string> kv in Lang.Available()) { langCodes.Add(kv.Key); cbLang.Items.Add(kv.Value + " (" + kv.Key + ")"); }
            int li = langCodes.IndexOf(s.Language);
            cbLang.SelectedIndex = li >= 0 ? li : 0;
            Controls.Add(cbLang);
            y += 40;

            // ---- working width ----
            Ui.Lbl(this, Lang.T("settings.widthTitle"), 15, y, 570, 24, 11).Font = Ui.FB(11);
            y += 28;
            string prof = Settings.ActiveToolProfile();
            double aw = Settings.ReadAogToolWidth(s.AogBaseDir);
            rbAog = new RadioButton();
            rbAog.Text = Lang.T("settings.widthFromAog") + (prof != "" ? ": " + prof : "") +
                         (aw > 0 ? " (" + aw.ToString("0.##", Lang.Culture) + " m)" : " (" + Lang.T("settings.notFound") + ")");
            rbAog.SetBounds(30, y, 555, 26);
            Controls.Add(rbAog);
            y += 30;
            rbOwn = new RadioButton();
            rbOwn.Text = Lang.T("settings.widthOwn");
            rbOwn.SetBounds(30, y, 160, 26);
            Controls.Add(rbOwn);
            nudWidth = new NumericUpDown();
            nudWidth.SetBounds(195, y - 2, 90, 28);
            nudWidth.DecimalPlaces = 2; nudWidth.Increment = 0.05m; nudWidth.Minimum = 0.05m; nudWidth.Maximum = 100m;
            nudWidth.Value = (decimal)(s.ToolWidth > 0 ? s.ToolWidth : (aw > 0 ? aw : 3.0));
            Controls.Add(nudWidth);
            ComboBox cbProfiles = new ComboBox();
            cbProfiles.DropDownStyle = ComboBoxStyle.DropDownList;
            cbProfiles.SetBounds(295, y - 2, 290, 28);
            cbProfiles.Items.Add(Lang.T("settings.fromProfile"));
            List<KeyValuePair<string, double>> profs = Settings.ListToolProfiles(s.AogBaseDir);
            foreach (KeyValuePair<string, double> kv in profs) cbProfiles.Items.Add(kv.Key + "  " + kv.Value.ToString("0.##", Lang.Culture) + " m");
            cbProfiles.SelectedIndex = 0;
            cbProfiles.SelectedIndexChanged += delegate
            {
                int i = cbProfiles.SelectedIndex - 1;
                if (i >= 0 && i < profs.Count) { nudWidth.Value = (decimal)profs[i].Value; rbOwn.Checked = true; }
            };
            Controls.Add(cbProfiles);
            if (s.ToolWidth > 0) rbOwn.Checked = true; else rbAog.Checked = true;
            y += 36;

            chkOnlyWork = new CheckBox();
            chkOnlyWork.Text = Lang.T("settings.onlySections");
            chkOnlyWork.Checked = s.OnlyWhenSectionsOn;
            chkOnlyWork.SetBounds(15, y, 570, 26);
            Controls.Add(chkOnlyWork);
            y += 28;
            chkTop = new CheckBox();
            chkTop.Text = Lang.T("settings.topMost");
            chkTop.Checked = s.TopMost;
            chkTop.SetBounds(15, y, 570, 26);
            Controls.Add(chkTop);
            y += 32;
            Ui.Lbl(this, Lang.T("settings.minStep") + ":", 15, y + 3, 260, 24, 10);
            nudStep = new NumericUpDown();
            nudStep.SetBounds(280, y, 80, 28);
            nudStep.DecimalPlaces = 2; nudStep.Increment = 0.05m; nudStep.Minimum = 0.05m; nudStep.Maximum = 10m;
            nudStep.Value = (decimal)s.MinStep;
            Controls.Add(nudStep);
            y += 38;
            Ui.Lbl(this, Lang.T("settings.minFix") + ":", 15, y + 3, 260, 24, 10);
            cbFix = new ComboBox();
            cbFix.DropDownStyle = ComboBoxStyle.DropDownList;
            cbFix.SetBounds(280, y, 305, 28);
            cbFix.Items.Add(Lang.T("settings.minFixAll"));
            cbFix.Items.Add(Lang.T("settings.minFixFloat"));
            cbFix.Items.Add(Lang.T("settings.minFixFix"));
            cbFix.SelectedIndex = s.MinFix == "float" ? 1 : s.MinFix == "fix" ? 2 : 0;
            Controls.Add(cbFix);
            y += 30;
            Label fixHint = Ui.Lbl(this, Lang.T("settings.minFixHint"), 280, y, 305, 34, 8);
            fixHint.ForeColor = Color.DimGray;
            y += 40;

            // ---- map ----
            Ui.Lbl(this, Lang.T("settings.mapTitle"), 15, y, 570, 24, 11).Font = Ui.FB(11);
            y += 28;
            cbMap = new ComboBox();
            cbMap.DropDownStyle = ComboBoxStyle.DropDownList;
            cbMap.SetBounds(15, y, 570, 28);
            cbMap.Items.Add(Lang.T("settings.mapOsm"));
            cbMap.Items.Add(Lang.T("settings.mapMml"));
            cbMap.Items.Add(Lang.T("settings.mapCustom"));
            cbMap.SelectedIndex = s.MapProvider == "mml" ? 1 : s.MapProvider == "custom" ? 2 : 0;
            Controls.Add(cbMap);
            y += 36;
            Ui.Lbl(this, Lang.T("settings.mmlKey") + ":", 15, y + 3, 200, 24, 10);
            tbKey = new TextBox(); tbKey.SetBounds(220, y, 365, 28); tbKey.Text = s.MmlApiKey; Controls.Add(tbKey);
            y += 34;
            Ui.Lbl(this, Lang.T("settings.customUrl") + ":", 15, y + 3, 200, 24, 10);
            tbUrl = new TextBox(); tbUrl.SetBounds(220, y, 365, 28); tbUrl.Text = s.CustomTileUrl; Controls.Add(tbUrl);
            y += 30;
            Label hint = Ui.Lbl(this, Lang.T("settings.customUrlHint"), 220, y, 365, 20, 8);
            hint.ForeColor = Color.DimGray;
            y += 24;
            Ui.Lbl(this, Lang.T("settings.customAttr") + ":", 15, y + 3, 200, 24, 10);
            tbAttr = new TextBox(); tbAttr.SetBounds(220, y, 365, 28); tbAttr.Text = s.CustomAttribution; Controls.Add(tbAttr);
            y += 40;
            cbMap.SelectedIndexChanged += delegate { UpdateMapFields(); };
            UpdateMapFields();

            // ---- folders ----
            Ui.Lbl(this, Lang.T("settings.aogFolder") + ":", 15, y, 570, 22, 10);
            y += 24;
            tbAog = new TextBox(); tbAog.SetBounds(15, y, 470, 28); tbAog.Text = s.AogBaseDir; Controls.Add(tbAog);
            Button b1 = Ui.Btn(this, Lang.T("settings.browse"), 495, y - 2, 90, 30, Ui.Grey, 9);
            b1.Click += delegate { Browse(tbAog); };
            y += 36;
            Ui.Lbl(this, Lang.T("settings.saveFolder") + ":", 15, y, 570, 22, 10);
            y += 24;
            tbSave = new TextBox(); tbSave.SetBounds(15, y, 470, 28); tbSave.Text = s.SaveDir; Controls.Add(tbSave);
            Button b2 = Ui.Btn(this, Lang.T("settings.browse"), 495, y - 2, 90, 30, Ui.Grey, 9);
            b2.Click += delegate { Browse(tbSave); };
            y += 32;
            Label shareHint = Ui.Lbl(this, Lang.T("settings.saveFolderHint", Lang.T("map.sharedFile")), 15, y, 570, 34, 8);
            shareHint.ForeColor = Color.DimGray;
            y += 40;
            Ui.Lbl(this, Lang.T("settings.listenIp") + ":", 15, y + 3, 400, 24, 9);
            tbIp = new TextBox(); tbIp.SetBounds(420, y, 165, 28); tbIp.Text = s.ListenIp; Controls.Add(tbIp);
            y += 44;

            // Content scrolls if the screen is low; OK / Cancel stay visible at the bottom.
            Panel content = new Panel();
            content.AutoScroll = true;
            content.Dock = DockStyle.Fill;
            foreach (Control c in new List<Control>(Controls.Cast<Control>())) content.Controls.Add(c);
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 64;
            bottom.BackColor = Color.FromArgb(246, 248, 250);
            Button ok = Ui.Btn(bottom, "OK", 330, 8, 120, 48, Ui.Green, 12);
            Button cancel = Ui.Btn(bottom, Lang.T("common.cancel"), 460, 8, 125, 48, Ui.Grey, 11);
            ok.Click += delegate { Apply(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(content);
            Controls.Add(bottom);
            content.BringToFront(); // fill docks last, i.e. the space left above the button bar
            int h = y + 10 + bottom.Height;
            int maxH = Screen.PrimaryScreen.WorkingArea.Height - 60;
            ClientSize = new Size(600 + (h > maxH ? SystemInformation.VerticalScrollBarWidth : 0), Math.Min(h, maxH));
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
            s.ToolWidth = rbOwn.Checked ? (double)nudWidth.Value : 0;
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
