using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace AgPath
{
    static class Ui
    {
        public static readonly Color Green = Color.FromArgb(46, 160, 67);
        public static readonly Color Red = Color.FromArgb(207, 34, 46);
        public static readonly Color Grey = Color.FromArgb(225, 228, 232);
        public static readonly Color Dark = Color.FromArgb(36, 41, 47);

        public static Font F(float size) { return new Font("Segoe UI", size, FontStyle.Regular); }
        public static Font FB(float size) { return new Font("Segoe UI", size, FontStyle.Bold); }

        public static Button Btn(Control parent, string text, int x, int y, int w, int h, Color back, float fontSize)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = back;
            b.ForeColor = (back == Grey) ? Dark : Color.White;
            b.Font = FB(fontSize);
            b.UseVisualStyleBackColor = false;
            b.Cursor = Cursors.Hand;
            parent.Controls.Add(b);
            return b;
        }

        public static Label Lbl(Control parent, string text, int x, int y, int w, int h, float fontSize)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(x, y, w, h);
            l.Font = F(fontSize);
            l.AutoSize = false;
            parent.Controls.Add(l);
            return l;
        }

        public static void Keyboard()
        {
            try
            {
                string tabtip = @"C:\Program Files\Common Files\microsoft shared\ink\TabTip.exe";
                if (System.IO.File.Exists(tabtip)) Process.Start(tabtip);
                else Process.Start("osk.exe");
            }
            catch
            {
                try { Process.Start("osk.exe"); } catch { }
            }
        }

        /// <summary>Question dialog with custom buttons. Returns the index of the clicked button (or -1).</summary>
        public static int Choose(IWin32Window owner, string text, string title, params string[] buttons)
        {
            using (Form f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterScreen;
                f.MinimizeBox = false; f.MaximizeBox = false;
                f.TopMost = true;
                f.ShowInTaskbar = false;
                int bw = 150, gap = 10;
                int width = Math.Max(420, buttons.Length * (bw + gap) + gap + 10);
                f.ClientSize = new Size(width, 200);
                Label l = Lbl(f, text, 15, 15, width - 30, 110, 12);
                int result = -1;
                int x = width - buttons.Length * (bw + gap);
                for (int i = 0; i < buttons.Length; i++)
                {
                    int idx = i;
                    Button b = Btn(f, buttons[i], x + i * (bw + gap), 135, bw, 50, i == 0 ? Green : Grey, 11);
                    b.Click += delegate { result = idx; f.Close(); };
                }
                f.ShowDialog(owner);
                return result;
            }
        }
    }
}
