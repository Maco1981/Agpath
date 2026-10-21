using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AgPath
{
    /// <summary>Editing the product and variety lists.</summary>
    class ProductsForm : Form
    {
        readonly Products products;
        ListBox lbProducts, lbVarieties;
        TextBox tbProduct, tbVariety;
        Label lblVar;

        public ProductsForm(Products p)
        {
            products = p;
            Text = Lang.T("products.title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false; MaximizeBox = false;
            TopMost = true;
            BackColor = Color.White;
            ClientSize = new Size(640, 520);

            Ui.Lbl(this, Lang.T("products.products"), 15, 10, 300, 28, 13).Font = Ui.FB(13);
            lblVar = Ui.Lbl(this, Lang.T("products.varieties"), 330, 10, 300, 28, 13);
            lblVar.Font = Ui.FB(13);

            lbProducts = new ListBox();
            lbProducts.SetBounds(15, 42, 295, 300);
            lbProducts.Font = Ui.F(14);
            lbProducts.IntegralHeight = false;
            Controls.Add(lbProducts);

            lbVarieties = new ListBox();
            lbVarieties.SetBounds(330, 42, 295, 300);
            lbVarieties.Font = Ui.F(14);
            lbVarieties.IntegralHeight = false;
            Controls.Add(lbVarieties);

            tbProduct = new TextBox();
            tbProduct.SetBounds(15, 352, 200, 32);
            tbProduct.Font = Ui.F(14);
            Controls.Add(tbProduct);
            Button bAddP = Ui.Btn(this, Lang.T("common.add"), 220, 350, 90, 36, Ui.Green, 11);

            tbVariety = new TextBox();
            tbVariety.SetBounds(330, 352, 200, 32);
            tbVariety.Font = Ui.F(14);
            Controls.Add(tbVariety);
            Button bAddV = Ui.Btn(this, Lang.T("common.add"), 535, 350, 90, 36, Ui.Green, 11);

            Button bDelP = Ui.Btn(this, Lang.T("products.deleteProduct"), 15, 396, 295, 40, Ui.Grey, 11);
            Button bDelV = Ui.Btn(this, Lang.T("products.deleteVariety"), 330, 396, 295, 40, Ui.Grey, 11);
            bDelP.ForeColor = Ui.Red; bDelV.ForeColor = Ui.Red;

            Button bKey = Ui.Btn(this, "⌨", 15, 456, 70, 50, Ui.Grey, 18);
            Button bClose = Ui.Btn(this, Lang.T("common.done"), 475, 456, 150, 50, Ui.Green, 13);

            bAddP.Click += delegate { AddProduct(); };
            bAddV.Click += delegate { AddVariety(); };
            tbProduct.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { AddProduct(); e.SuppressKeyPress = true; } };
            tbVariety.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { AddVariety(); e.SuppressKeyPress = true; } };
            bDelP.Click += delegate { DeleteProduct(); };
            bDelV.Click += delegate { DeleteVariety(); };
            bKey.Click += delegate { Ui.Keyboard(); };
            bClose.Click += delegate { Close(); };
            lbProducts.SelectedIndexChanged += delegate { FillVarieties(); };

            FillProducts(null);
        }

        string SelectedProduct { get { return lbProducts.SelectedItem as string; } }

        void FillProducts(string select)
        {
            lbProducts.Items.Clear();
            foreach (string k in products.Items.Keys) lbProducts.Items.Add(k);
            if (select != null)
            {
                for (int i = 0; i < lbProducts.Items.Count; i++)
                    if (string.Equals((string)lbProducts.Items[i], select, StringComparison.CurrentCultureIgnoreCase))
                        lbProducts.SelectedIndex = i;
            }
            else if (lbProducts.Items.Count > 0) lbProducts.SelectedIndex = 0;
            FillVarieties();
        }

        void FillVarieties()
        {
            lbVarieties.Items.Clear();
            string p = SelectedProduct;
            lblVar.Text = p == null ? Lang.T("products.varieties") : Lang.T("products.varieties") + ": " + p;
            if (p == null) return;
            foreach (string v in products.Varieties(p)) lbVarieties.Items.Add(v);
        }

        void AddProduct()
        {
            string t = tbProduct.Text.Trim();
            if (t == "") return;
            products.Add(t, null);
            products.Save();
            tbProduct.Text = "";
            FillProducts(t);
            tbVariety.Focus();
        }

        void AddVariety()
        {
            string p = SelectedProduct;
            string v = tbVariety.Text.Trim();
            if (p == null)
            {
                Ui.Choose(this, Lang.T("products.selectFirst"), Lang.T("f.variety"), "OK");
                return;
            }
            if (v == "") return;
            products.Add(p, v);
            products.Save();
            tbVariety.Text = "";
            FillVarieties();
        }

        void DeleteProduct()
        {
            string p = SelectedProduct;
            if (p == null) return;
            if (Ui.Choose(this, Lang.T("products.confirmDelete", p),
                          Lang.T("common.delete"), Lang.T("common.delete"), Lang.T("common.cancel")) != 0) return;
            products.Items.Remove(p);
            products.Save();
            FillProducts(null);
        }

        void DeleteVariety()
        {
            string p = SelectedProduct;
            string v = lbVarieties.SelectedItem as string;
            if (p == null || v == null) return;
            List<string> list = products.Varieties(p);
            list.Remove(v);
            products.Save();
            FillVarieties();
        }
    }
}
