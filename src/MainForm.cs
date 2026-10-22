using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AgPath
{
    class MainForm : Form
    {
        readonly Settings settings;
        Products products;
        readonly UdpReceiver rx = new UdpReceiver();
        Recorder rec = new Recorder();
        readonly FieldDetector fields = new FieldDetector();
        readonly Timer timer = new Timer();
        DateTime lastBackup = DateTime.MinValue, lastFieldCheck = DateTime.MinValue, msgUntil = DateTime.MinValue;
        string currentField = "";
        bool busy;

        Button btnRec, btnProducts, btnSettings, btnFolder, btnCompact, btnMap;
        Label lblGps, lblField, lblTime, lblDist, lblArea, lblWidth, lblMsg, lblCompact;

        public MainForm(Settings s)
        {
            settings = s;
            products = Products.Load();
            Text = "AgPath";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.White;
            StartPosition = FormStartPosition.Manual;
            TopMost = settings.TopMost;
            ShowInTaskbar = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            btnRec = Ui.Btn(this, "", 8, 8, 360, 70, Ui.Green, 15);
            btnRec.Click += delegate { ToggleRecording(); };

            lblCompact = Ui.Lbl(this, "", 8, 82, 360, 26, 11);
            lblCompact.TextAlign = ContentAlignment.MiddleCenter;

            lblGps = Ui.Lbl(this, "", 8, 84, 360, 24, 11);
            lblField = Ui.Lbl(this, "", 8, 108, 360, 24, 11);
            lblTime = Ui.Lbl(this, "", 8, 134, 360, 30, 15);
            lblTime.Font = Ui.FB(15);
            lblDist = Ui.Lbl(this, "", 8, 166, 360, 24, 11);
            lblArea = Ui.Lbl(this, "", 8, 190, 360, 24, 11);
            lblWidth = Ui.Lbl(this, "", 8, 214, 360, 22, 9);
            lblWidth.ForeColor = Color.DimGray;
            lblMsg = Ui.Lbl(this, "", 8, 236, 360, 36, 9);
            lblMsg.ForeColor = Ui.Green;

            btnProducts = Ui.Btn(this, Lang.T("main.products"), 8, 276, 68, 40, Ui.Grey, 8);
            btnMap = Ui.Btn(this, Lang.T("main.map"), 80, 276, 68, 40, Ui.Grey, 8);
            btnFolder = Ui.Btn(this, Lang.T("main.folder"), 152, 276, 68, 40, Ui.Grey, 8);
            btnSettings = Ui.Btn(this, Lang.T("main.settings"), 224, 276, 68, 40, Ui.Grey, 8);
            btnCompact = Ui.Btn(this, Lang.T("main.compact"), 296, 276, 68, 40, Ui.Grey, 8);
            foreach (Button b in new Button[] { btnProducts, btnMap, btnFolder, btnSettings, btnCompact })
            {
                b.Font = Ui.F(8);
                b.Padding = new Padding(0);
                b.TextAlign = ContentAlignment.MiddleCenter;
            }
            btnProducts.Click += delegate { using (ProductsForm f = new ProductsForm(products)) f.ShowDialog(this); };
            btnMap.Click += delegate { OpenMap(); };
            btnSettings.Click += delegate { OpenSettings(); };
            btnFolder.Click += delegate { OpenFolder(); };
            btnCompact.Click += delegate { settings.Compact = !settings.Compact; ApplyCompact(); settings.Save(); };
            lblCompact.Click += delegate { settings.Compact = false; ApplyCompact(); settings.Save(); };

            ApplyCompact();
            PlaceWindow();
            UpdateRecButton();

            Load += OnLoadForm;
            FormClosing += OnClosingForm;
            Move += delegate
            {
                if (WindowState == FormWindowState.Normal) { settings.WinX = Left; settings.WinY = Top; }
            };

            timer.Interval = 200;
            timer.Tick += delegate { OnTick(); };
        }

        void PlaceWindow()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int x = settings.WinX, y = settings.WinY;
            bool visible = false;
            foreach (Screen s in Screen.AllScreens)
                if (s.WorkingArea.Contains(x + 20, y + 20)) visible = true;
            if (x < 0 || y < 0 || !visible) { x = wa.Right - Width - 10; y = wa.Top + 80; }
            Location = new Point(x, y);
        }

        void ApplyCompact()
        {
            bool c = settings.Compact;
            lblCompact.Visible = c;
            foreach (Control ctl in new Control[] { lblGps, lblField, lblTime, lblDist, lblArea, lblWidth, lblMsg,
                                                    btnProducts, btnSettings, btnFolder, btnCompact, btnMap })
                ctl.Visible = !c;
            ClientSize = c ? new Size(376, 112) : new Size(376, 324);
        }

        void OnLoadForm(object sender, EventArgs e)
        {
            fields.Load(settings.AogBaseDir);
            MapData.Write(settings, fields); // keeps the shareable map in the save folder up to date
            rx.Start(settings.ListenIp);
            if (rx.Error != "") ShowMsg(rx.Error, true);
            timer.Start();
            CheckBackup();
        }

        void CheckBackup()
        {
            Recorder old = Recorder.ReadBackup();
            if (old == null) { Recorder.DeleteBackup(); return; }
            string txt = Lang.T("backup.text", old.StartTime.ToString("g", Lang.Culture),
                                Units.Dist(old.Distance), old.Points.Count);
            int c = Ui.Choose(this, txt, Lang.T("backup.title"), Lang.T("backup.continue"), Lang.T("backup.saveNow"), Lang.T("backup.discard"));
            if (c == 0)
            {
                rec = old;
                rec.MinStep = settings.MinStep;
                rec.Resume();
                UpdateRecButton();
            }
            else if (c == 1)
            {
                rec = old;
                ShowSaveDialog();
            }
            else if (c == 2)
            {
                if (Ui.Choose(this, Lang.T("backup.confirmDiscard"), Lang.T("backup.discard"), Lang.T("common.yesDiscard"), Lang.T("common.no")) == 0)
                    Recorder.DeleteBackup();
            }
            // closed without a choice: the backup is kept for the next start
        }

        void OnClosingForm(object sender, FormClosingEventArgs e)
        {
            if (rec.Active)
            {
                int c = Ui.Choose(this, Lang.T("close.recording"), "AgPath", Lang.T("close.stopAndSave"), Lang.T("close.dontClose"));
                if (c != 0) { e.Cancel = true; return; }
                DrainFixes();
                rec.Stop();
                rec.WriteBackup();
                ShowSaveDialog();
                if (rec.Active) { e.Cancel = true; return; }
            }
            timer.Stop();
            rx.Stop();
            settings.Save();
        }

        void ToggleRecording()
        {
            if (busy) return;
            if (!rec.Active)
            {
                rec = new Recorder();
                rec.Width = settings.EffectiveWidth();
                rec.OnlyWork = settings.OnlyWhenSectionsOn;
                rec.MinStep = settings.MinStep;
                rec.MinFix = settings.MinFix;
                rec.Start();
                lastBackup = DateTime.Now;
                if (!rx.HasPosition) ShowMsg(Lang.T("main.startedNoGps"), true);
                else ShowMsg(Lang.T("main.started", rec.StartTime.ToString("HH:mm:ss")), false);
            }
            else
            {
                DrainFixes();
                rec.Stop();
                rec.WriteBackup();
                ShowSaveDialog();
            }
            UpdateRecButton();
        }

        void ShowSaveDialog()
        {
            busy = true;
            try
            {
                using (SaveForm f = new SaveForm(rec, products, fields, settings))
                {
                    f.ShowDialog(this);
                    if (f.Result == SaveForm.Outcome.Resume)
                    {
                        rec.Resume();
                        ShowMsg(Lang.T("main.resumed"), false);
                    }
                    else if (f.Result == SaveForm.Outcome.Saved)
                    {
                        Recorder.DeleteBackup();
                        ShowMsg(Lang.T("main.saved", Path.GetFileName(f.SavedPath)), false);
                        if (Exporter.LastWarning != "") ShowMsg(Exporter.LastWarning, true);
                        MapData.Write(settings, fields);
                    }
                    else if (f.Result == SaveForm.Outcome.Discarded)
                    {
                        Recorder.DeleteBackup();
                        ShowMsg(Lang.T("main.discarded"), true);
                    }
                    // Outcome.None: dialog closed -> the backup is kept and offered at next start
                }
            }
            finally { busy = false; }
            UpdateRecButton();
        }

        void UpdateRecButton()
        {
            if (rec.Active)
            {
                btnRec.Text = "■  " + Lang.T("main.stop");
                btnRec.BackColor = Ui.Red;
            }
            else
            {
                btnRec.Text = "●  " + Lang.T("main.start");
                btnRec.BackColor = Ui.Green;
            }
        }

        void DrainFixes()
        {
            Fix f;
            bool? sec = rx.SectionsOn;
            bool work = sec.HasValue ? sec.Value : true; // unknown section state -> counted
            bool good = settings.AcceptsFix(rx.FixQuality);
            while (rx.Fixes.TryDequeue(out f))
            {
                TrackSpeed(f);
                if (!rec.Active) continue;
                if (good) rec.AddFix(f.Lat, f.Lon, work, f.Time);
                else rec.SkipFix();
            }
        }

        void ShowMsg(string text, bool warning)
        {
            lblMsg.Text = text;
            lblMsg.ForeColor = warning ? Ui.Red : Ui.Green;
            msgUntil = DateTime.Now.AddSeconds(12);
        }

        // ---- speed from the last seconds of fixes ----
        readonly System.Collections.Generic.Queue<Fix> recentFixes = new System.Collections.Generic.Queue<Fix>();
        double speedKmh = -1;

        void TrackSpeed(Fix f)
        {
            recentFixes.Enqueue(f);
            while (recentFixes.Count > 0 && (f.Time - recentFixes.Peek().Time).TotalSeconds > 2.5) recentFixes.Dequeue();
        }

        void UpdateSpeed()
        {
            if (!rx.HasPosition || recentFixes.Count < 2) { speedKmh = -1; recentFixes.Clear(); return; }
            Fix a = recentFixes.Peek();
            Fix b = null;
            foreach (Fix x in recentFixes) b = x;
            double dt = (b.Time - a.Time).TotalSeconds;
            if (dt < 0.5) return;
            double k = Math.Cos(a.Lat * Math.PI / 180.0);
            double dx = (b.Lon - a.Lon) * Math.PI / 180.0 * 6371008.8 * k;
            double dy = (b.Lat - a.Lat) * Math.PI / 180.0 * 6371008.8;
            speedKmh = Math.Sqrt(dx * dx + dy * dy) / dt * 3.6;
        }

        void OnTick()
        {
            DrainFixes();
            UpdateSpeed();
            DateTime now = DateTime.Now;

            string src;
            Color col;
            if (rx.AogAlive) { src = Lang.T("gps.fromAog"); col = Ui.Green; }
            else if (rx.AgioAlive) { src = Lang.T("gps.fromAgio"); col = Ui.Green; }
            else { src = Lang.T("gps.none"); col = Ui.Red; }
            string q = UdpReceiver.FixQualityText(rx.FixQuality);
            if (q != "" && rx.HasPosition) src += "  (" + q + ")";
            bool? sec = rx.SectionsOn;
            if (sec.HasValue) src += "  • " + (sec.Value ? Lang.T("gps.sectionsOn") : Lang.T("gps.sectionsOff"));
            if (rx.HasPosition && !settings.AcceptsFix(rx.FixQuality))
            {
                src += "  – " + Lang.T("gps.notRecorded");
                col = Ui.Red;
            }
            lblGps.Text = src;
            lblGps.ForeColor = col;

            if ((now - lastFieldCheck).TotalSeconds > 2)
            {
                lastFieldCheck = now;
                double lat, lon;
                if (rx.LastPosition(out lat, out lon)) currentField = fields.FieldAt(lat, lon);
            }
            lblField.Text = Lang.T("f.field") + ": " + (currentField != "" ? currentField : "–");

            if (rec.Active || rec.Points.Count > 0)
            {
                lblTime.Text = (rec.Active ? "● " : "") + Exporter.FormatDuration(rec.Elapsed) +
                               "   (" + Lang.T("main.startedAt", rec.StartTime.ToString("HH:mm")) + ")";
                lblTime.ForeColor = rec.Active ? Ui.Red : Ui.Dark;
                lblDist.Text = Lang.T("f.distance") + ": " + Units.Dist(rec.Distance) +
                               (speedKmh >= 0 ? "     " + Lang.T("f.speed") + ": " + Units.Speed(speedKmh) : "");
                lblArea.Text = Lang.T("f.area") + ": " + Units.Area(rec.DrivenArea) + "   (" +
                               Lang.T("main.coveredShort") + " " + Units.Area(rec.Cover.AreaM2) + ")";
            }
            else
            {
                lblTime.Text = "0:00:00";
                lblTime.ForeColor = Ui.Dark;
                lblDist.Text = Lang.T("f.distance") + ": –";
                lblArea.Text = Lang.T("f.area") + ": –";
            }
            double w = rec.Active ? rec.Width : settings.EffectiveWidth();
            string prof = settings.ToolWidth > 0 ? Lang.T("main.ownWidth") : Settings.ActiveToolProfile();
            lblWidth.Text = Lang.T("f.width") + " " + Units.Width(w) + (prof != "" ? " (" + prof + ")" : "") +
                            (settings.OnlyWhenSectionsOn ? ", " + Lang.T("main.onlySectionsShort") : "");

            lblCompact.Text = rec.Active
                ? "● " + Exporter.FormatDuration(rec.Elapsed) + "   " + Units.Area(rec.DrivenArea) +
                  (rx.HasPosition ? "" : "   " + Lang.T("main.noGpsShort"))
                : (rx.HasPosition ? Lang.T("main.gpsOk") : Lang.T("gps.none")) + "  –  " + Lang.T("main.showMore");
            lblCompact.ForeColor = rx.HasPosition ? Ui.Dark : Ui.Red;

            if (now > msgUntil) lblMsg.Text = "";

            if (rec.Active && (now - lastBackup).TotalSeconds > 15)
            {
                lastBackup = now;
                rec.WriteBackup();
            }
        }

        void OpenSettings()
        {
            string oldLang = settings.Language;
            using (SettingsForm f = new SettingsForm(settings))
            {
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    settings.Save();
                    TopMost = settings.TopMost;
                    fields.Load(settings.AogBaseDir);
                    if (!rec.Active)
                    {
                        rec.Width = settings.EffectiveWidth();
                        rec.OnlyWork = settings.OnlyWhenSectionsOn;
                    }
                    rec.MinStep = settings.MinStep;
                    Units.Load(settings);
                    MapData.Write(settings, fields);
                    if (settings.Language != oldLang) ShowMsg(Lang.T("settings.restartForLanguage"), false);
                }
            }
        }

        void OpenMap()
        {
            try
            {
                string html = MapData.Write(settings, fields);
                if (html == null) { ShowMsg(Lang.T("main.mapMissing"), true); return; }
                Process.Start(html);
            }
            catch (Exception ex) { ShowMsg(ex.Message, true); }
        }

        void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(settings.SaveDir);
                Process.Start("explorer.exe", "\"" + settings.SaveDir + "\"");
            }
            catch (Exception ex) { ShowMsg(ex.Message, true); }
        }
    }
}
