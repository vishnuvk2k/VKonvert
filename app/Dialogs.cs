using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using VKonvert.UI;

namespace VKonvert
{
    /// <summary>About VKonvert: who made it, the license, and the libraries inside.</summary>
    sealed class AboutDialog : Form
    {
        public static void ShowFor(IWin32Window owner)
        {
            using (var d = new AboutDialog()) d.ShowDialog(owner);
        }

        AboutDialog()
        {
            Text = "About VKonvert";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Theme.Dp(460), Theme.Dp(420));
            Font = Theme.Ui(9.75f);
            BackColor = Theme.P.Panel;

            var art = new Art { Bounds = new Rectangle(0, 0, ClientSize.Width, Theme.Dp(170)) };
            Controls.Add(art);

            var lines = new Stack { Bounds = new Rectangle(Theme.Dp(32), Theme.Dp(176), ClientSize.Width - Theme.Dp(64), Theme.Dp(160)), Gap = Theme.Dp(6) };
            lines.Add(new TLabel("Made by Vishnu VK", TextRole.Title));
            lines.Add(new TLabel("Made with Claude Code", TextRole.Body));
            lines.Add(new TLabel("Free to use · MIT License", TextRole.Body));
            lines.Add(new TLabel("Uses ImageMagick (Magick.NET), PDFsharp and PDFium. Their licenses are in THIRD-PARTY-NOTICES.txt.", TextRole.Muted, wrap: true));
            Controls.Add(lines);

            var license = new PillButton("View license") { Height = Theme.Dp(38), Radius = Theme.Dp(10) };
            license.Width = license.PreferredWidth(Theme.Dp(16));
            license.Location = new Point(Theme.Dp(32), ClientSize.Height - Theme.Dp(62));
            license.Click += (s, e) => ShowLicense();
            var site = new PillButton("vkonvert.com") { Height = Theme.Dp(38), Radius = Theme.Dp(10) };
            site.Width = site.PreferredWidth(Theme.Dp(16));
            site.Location = new Point(license.Right + Theme.Dp(8), license.Top);
            site.Click += (s, e) => { try { Process.Start("https://vkonvert.com"); } catch { } };
            var close = new PillButton("Close", PillKind.Primary) { Height = Theme.Dp(44) };
            close.Width = Theme.Dp(110);
            close.Location = new Point(ClientSize.Width - close.Width - Theme.Dp(32), ClientSize.Height - Theme.Dp(64));
            close.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { license, site, close });
            CancelButton = new Button(); // Esc closes
            ((Button)CancelButton).Click += (s, e) => Close();
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.ApplyTitleBar(this); }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessDialogKey(keyData);
        }

        void ShowLicense()
        {
            using (var f = new Form
            {
                Text = "VKonvert license", ClientSize = new Size(Theme.Dp(560), Theme.Dp(420)), StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false, MinimizeBox = false, BackColor = Theme.P.Panel,
            })
            {
                var box = new TextBox
                {
                    Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
                    Text = License.Text.Replace("\n", "\r\n"), Font = Theme.Mono(9.5f), BackColor = Theme.P.Panel, ForeColor = Theme.P.Ink,
                };
                f.Padding = new Padding(Theme.Dp(16));
                f.Controls.Add(box);
                f.Shown += (s, e) => { box.SelectionStart = 0; box.SelectionLength = 0; };
                f.HandleCreated += (s, e) => Theme.ApplyTitleBar(f);
                f.ShowDialog(this);
            }
        }

        sealed class Art : VControl
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Draw.Smooth(g);
                var p = Theme.P;
                using (var b = new SolidBrush(p.Bg)) g.FillRectangle(b, ClientRectangle);
                Draw.Dots(g, ClientRectangle, Theme.Dp(16));
                var s = Theme.Dp(72);
                Draw.Mark(g, new RectangleF(Theme.Dp(32), Theme.Dp(40), s, s));
                Draw.Wordmark(g, new PointF(Theme.Dp(32) + s + Theme.Dp(18), Theme.Dp(44)), 26f);
                Draw.Text(g, "Version " + ShellIntegration.Version + " · Convert, compress and combine", Theme.Ui(9.75f),
                    new Rectangle(Theme.Dp(32) + s + Theme.Dp(20), Theme.Dp(92), Width, Theme.Dp(22)), p.Muted);
                using (var pen = new Pen(p.Line)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            }
        }
    }

    static class License
    {
        public const string Text =
@"MIT License

Copyright (c) 2026 Vishnu VK

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.";
    }
}
