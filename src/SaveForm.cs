using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace AgPath
{
    /// <summary>Save dialog shown when a recording is stopped.</summary>
    class SaveForm : Form
    {
        public enum Outcome { None, Saved, Resume, Discarded }
        public Outcome Result = Outcome.None;
        public string SavedPath = "";

        readonly Recorder rec;
        readonly Products products;
        readonly Settings settings;
        static CultureInfo Fi { get { return Lang.Culture; } }

        RadioButton rbProduct, rbName;
        ComboBox cbProduct, cbVariety, cbField;
        CheckBox chkAddToList, chkOnlyWork;
        TextBox tbName, tbNotes;
        NumericUpDown nudWidth;
        Label lblSummary;

        public SaveForm(Recorder r, Products p, FieldDetector fields, Settings s)
        {
            rec = r; products = p; settings = s;
            Text = Lang.T("save.title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false; MaximizeBox = false;
            TopMost = true;
            BackColor = Color.White;
            ClientSize = new Size(560, 575);
            Font = Ui.F(12);

            lblSummary = Ui.Lbl(this, "", 15, 10, 530, 80, 11);
            lblSummary.BackColor = Color.FromArgb(246, 248, 250);
            lblSummary.Padding = new Padding(6);

            int y = 100;
            rbProduct = new RadioButton();
            rbProduct.Text = Lang.T("save.chooseProduct");
            rbProduct.Font = Ui.FB(12);
            rbProduct.SetBounds(15, y, 400, 30);
            Controls.Add(rbProduct);
            y += 34;
            Ui.Lbl(this, Lang.T("f.product") + ":", 40, y + 4, 80, 28, 12);
            cbProduct = new ComboBox();
            cbProduct.SetBounds(125, y, 420, 32);
            cbProduct.DropDownStyle = ComboBoxStyle.DropDown;
            cbProduct.Font = Ui.F(14);
            cbProduct.MaxDropDownItems = 15;
            foreach (string prod in products.Items.Keys) cbProduct.Items.Add(prod);
            Controls.Add(cbProduct);
            y += 42;
            Ui.Lbl(this, Lang.T("f.variety") + ":", 40, y + 4, 80, 28, 12);
            cbVariety = new ComboBox();
            cbVariety.SetBounds(125, y, 420, 32);
            cbVariety.DropDownStyle = ComboBoxStyle.DropDown;
            cbVariety.Font = Ui.F(14);
            cbVariety.MaxDropDownItems = 15;
            Controls.Add(cbVariety);
            y += 42;
            chkAddToList = new CheckBox();
            chkAddToList.Text = Lang.T("save.addToList");
            chkAddToList.Checked = true;
            chkAddToList.Font = Ui.F(10);
            chkAddToList.SetBounds(125, y, 420, 26);
            Controls.Add(chkAddToList);
            y += 34;

            rbName = new RadioButton();
            rbName.Text = Lang.T("save.typeName");
            rbName.Font = Ui.FB(12);
            rbName.SetBounds(15, y, 400, 30);
            Controls.Add(rbName);
            y += 34;
            tbName = new TextBox();
            tbName.SetBounds(40, y, 505, 32);
            tbName.Font = Ui.F(14);
            Controls.Add(tbName);
            y += 50;

            Ui.Lbl(this, Lang.T("f.field") + ":", 15, y + 4, 110, 28, 12);
            cbField = new ComboBox();
            cbField.SetBounds(125, y, 420, 32);
            cbField.DropDownStyle = ComboBoxStyle.DropDown;
            cbField.Font = Ui.F(14);
            cbField.MaxDropDownItems = 15;
            foreach (string f in fields.FieldNames) cbField.Items.Add(f);
            cbField.Text = fields.FieldForPath(rec.Points);
            Controls.Add(cbField);
            Ui.TouchCombo(cbProduct);
            Ui.TouchCombo(cbVariety);
            Ui.TouchCombo(cbField);
            y += 44;

            Ui.Lbl(this, Lang.T("f.width") + " (" + Units.WidthUnit + "):", 15, y + 4, 110, 28, 12);
            nudWidth = new NumericUpDown();
            nudWidth.SetBounds(125, y, 110, 32);
            nudWidth.Font = Ui.F(14);
            nudWidth.DecimalPlaces = 2;
            nudWidth.Increment = 0.05m;
            nudWidth.Minimum = 0.05m;
            nudWidth.Maximum = 400m;
            nudWidth.Value = (decimal)Math.Max(0.05, Math.Min(400, Math.Round(Units.WidthValue(rec.Width), 2)));
            Controls.Add(nudWidth);
            chkOnlyWork = new CheckBox();
            chkOnlyWork.Text = Lang.T("save.onlySections");
            chkOnlyWork.Font = Ui.F(10);
            chkOnlyWork.Checked = rec.OnlyWork;
            chkOnlyWork.SetBounds(250, y + 4, 300, 26);
            Controls.Add(chkOnlyWork);
            y += 44;

            Ui.Lbl(this, Lang.T("f.notes") + ":", 15, y, 110, 28, 12);
            tbNotes = new TextBox();
            tbNotes.Multiline = true;
            tbNotes.SetBounds(125, y, 420, 60);
            tbNotes.Font = Ui.F(12);
            Controls.Add(tbNotes);
            y += 74;

            Button bSave = Ui.Btn(this, Lang.T("save.save"), 15, y, 180, 60, Ui.Green, 14);
            Button bResume = Ui.Btn(this, Lang.T("save.resume"), 205, y, 140, 60, Ui.Grey, 12);
            Button bDiscard = Ui.Btn(this, Lang.T("save.discard"), 355, y, 110, 60, Ui.Grey, 12);
            Button bKey = Ui.Btn(this, "⌨", 475, y, 70, 60, Ui.Grey, 18);
            bDiscard.ForeColor = Ui.Red;

            bSave.Click += delegate { DoSave(); };
            bResume.Click += delegate { Result = Outcome.Resume; Close(); };
            bDiscard.Click += delegate
            {
                if (Ui.Choose(this, Lang.T("save.confirmDiscard"), Lang.T("save.discard"), Lang.T("common.yesDiscard"), Lang.T("common.cancel")) == 0)
                {
                    Result = Outcome.Discarded;
                    Close();
                }
            };
            bKey.Click += delegate { Ui.Keyboard(); };

            rbProduct.CheckedChanged += delegate { UpdateEnabled(); };
            rbName.CheckedChanged += delegate { UpdateEnabled(); };
            cbProduct.TextChanged += delegate { FillVarieties(); };
            cbProduct.SelectedIndexChanged += delegate { FillVarieties(); };
            cbProduct.SelectionChangeCommitted += delegate
            {
                // after choosing a product, go straight to its varieties
                BeginInvoke((MethodInvoker)delegate
                {
                    FillVarieties();
                    cbVariety.Focus();
                    if (cbVariety.Items.Count > 0) cbVariety.DroppedDown = true;
                });
            };
            nudWidth.ValueChanged += delegate { Recalc(); };
            chkOnlyWork.CheckedChanged += delegate { Recalc(); };

            if (products.Items.Count > 0) rbProduct.Checked = true; else rbName.Checked = true;
            UpdateEnabled();
            UpdateSummary();
        }

        string lastVarProduct = null;
        void FillVarieties()
        {
            string p = cbProduct.Text.Trim();
            if (p == lastVarProduct) return;
            lastVarProduct = p;
            string keep = cbVariety.Text;
            cbVariety.Items.Clear();
            foreach (string v in products.Varieties(p)) cbVariety.Items.Add(v);
            cbVariety.Text = keep;
        }

        void UpdateEnabled()
        {
            bool prod = rbProduct.Checked;
            cbProduct.Enabled = prod;
            cbVariety.Enabled = prod;
            chkAddToList.Enabled = prod;
            tbName.Enabled = !prod;
            if (prod) cbProduct.Focus(); else tbName.Focus();
        }

        void Recalc()
        {
            rec.Width = Units.WidthToMeters((double)nudWidth.Value);
            rec.OnlyWork = chkOnlyWork.Checked;
            rec.Recompute();
            UpdateSummary();
        }

        void UpdateSummary()
        {
            TimeSpan dur = rec.EndTime - rec.StartTime;
            lblSummary.Text =
                rec.StartTime.ToString("D", Fi) + "   " + rec.StartTime.ToString("HH:mm:ss") +
                " – " + rec.EndTime.ToString("HH:mm:ss") + "   (" + Exporter.FormatDuration(dur) + ")\n" +
                Lang.T("f.distance") + " " + Units.Dist(rec.Distance) + ",  " +
                Lang.T("f.area") + " " + Units.Area(rec.DrivenArea) + ",  " +
                Lang.T("f.covered") + " " + Units.Area(rec.Cover.AreaM2) + "\n" +
                Lang.T("save.points", rec.Points.Count) + (rec.SegmentCount > 1 ? ", " + Lang.T("save.parts", rec.SegmentCount) : "");
        }

        void DoSave()
        {
            if (rec.Points.Count < 2)
            {
                if (Ui.Choose(this, Lang.T("save.noPoints"), Lang.T("save.title"), Lang.T("save.save"), Lang.T("common.cancel")) != 0)
                    return;
            }
            SaveInfo info = new SaveInfo();
            if (rbProduct.Checked)
            {
                info.Product = cbProduct.Text.Trim();
                info.Variety = cbVariety.Text.Trim();
                if (info.Product == "")
                {
                    Ui.Choose(this, Lang.T("save.needProduct"), Lang.T("save.title"), "OK");
                    return;
                }
                info.Name = (info.Product + " " + info.Variety).Trim();
                if (chkAddToList.Checked && products.Add(info.Product, info.Variety)) products.Save();
            }
            else
            {
                info.Name = tbName.Text.Trim();
            }
            info.Field = cbField.Text.Trim();
            info.Notes = tbNotes.Text.Trim();

            try
            {
                SavedPath = Exporter.Save(rec, info, settings.SaveDir);
                Result = Outcome.Saved;
                Close();
            }
            catch (Exception ex)
            {
                Log.Write("Save failed: " + ex);
                Ui.Choose(this, Lang.T("save.failed", ex.Message), Lang.T("app.error"), "OK");
            }
        }
    }
}
