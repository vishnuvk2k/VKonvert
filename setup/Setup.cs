using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using VKonvert.UI;
using Switch = VKonvert.UI.Switch;

namespace VKonvertSetup
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Init();
            Theme.InitScale();
            Application.Run(new SetupForm());
        }
    }

    /// <summary>
    /// The VKonvert installer: Welcome → License → Folder &amp; options → Install → Done.
    /// Installs for the current Windows account (no admin), then lets VKonvert.exe register itself.
    /// </summary>
    sealed class SetupForm : Form
    {
        const string Version = "2.0";
        static readonly string[] StepNames = { "Welcome", "License", "Folder", "Install", "Done" };

        int step;
        readonly Panel[] pages = new Panel[5];
        Header header;
        PillButton back, next, cancel;
        Switch accept, menu, startMenu, desktop, defaultApp, launch;
        Field folder;
        TLabel space, workText, doneText;
        SegmentBar progress;
        string installedExe;
        readonly string existing = ExistingFolder();

        static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VKonvert");

        static string ExistingFolder()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\VKonvert")) return k?.GetValue("InstallDir") as string;
        }

        public SetupForm()
        {
            Text = "VKonvert Setup";
            Font = Theme.Ui(9.75f);
            ClientSize = new Size(Theme.Dp(720), Theme.Dp(520));
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            header = new Header { Bounds = new Rectangle(0, 0, ClientSize.Width, Theme.Dp(150)) };
            header.ThemeClicked += () => Theme.Toggle();
            Controls.Add(header);

            var footer = new Footer { Bounds = new Rectangle(0, ClientSize.Height - Theme.Dp(72), ClientSize.Width, Theme.Dp(72)) };
            back = new PillButton("Back") { Height = Theme.Dp(40) };
            next = new PillButton("Next", PillKind.Primary) { Height = Theme.Dp(46) };
            cancel = new PillButton("Cancel", PillKind.Ghost) { Height = Theme.Dp(40) };
            back.Click += (s, e) => Go(step - 1);
            next.Click += async (s, e) => await Next();
            cancel.Click += (s, e) => Close();
            footer.Controls.AddRange(new Control[] { back, next, cancel });
            footer.Layout += (s, e) => LayoutFooter(footer);
            Controls.Add(footer);

            for (int i = 0; i < pages.Length; i++)
            {
                pages[i] = new Stack
                {
                    Bounds = new Rectangle(0, header.Bottom, ClientSize.Width, footer.Top - header.Bottom),
                    Inner = new Padding(Theme.Dp(32), Theme.Dp(8), Theme.Dp(32), Theme.Dp(8)), Gap = Theme.Dp(12), Visible = false,
                };
                Controls.Add(pages[i]);
            }
            BuildWelcome(pages[0] as Stack);
            BuildLicense(pages[1] as Stack);
            BuildFolder(pages[2] as Stack);
            BuildWork(pages[3] as Stack);
            BuildDone(pages[4] as Stack);

            Theme.Changed += ApplyTheme;
            ApplyTheme();
            Go(0);
        }

        void LayoutFooter(Control f)
        {
            next.Width = Math.Max(Theme.Dp(130), next.PreferredWidth(Theme.Dp(26)));
            next.Location = new Point(f.Width - next.Width - Theme.Dp(24), (f.Height - next.Height) / 2 + Theme.Dp(2));
            back.Width = back.PreferredWidth(Theme.Dp(20));
            back.Location = new Point(next.Left - back.Width - Theme.Dp(10), (f.Height - back.Height) / 2);
            cancel.Width = cancel.PreferredWidth(Theme.Dp(16));
            cancel.Location = new Point(Theme.Dp(24), (f.Height - cancel.Height) / 2);
        }

        // ───────────── Pages ─────────────

        void BuildWelcome(Stack s)
        {
            s.Add(new TLabel(existing != null ? "VKonvert is already on this PC. Setup will update it to version " + Version + "." : "Convert, compress and combine images and PDFs.", TextRole.Title, wrap: true) { Font = Theme.UiSemibold(13f) });
            s.Add(new TLabel("Free, works offline, and your files never leave your PC. Setup takes about ten seconds and doesn’t need admin rights.", TextRole.Muted, wrap: true));
            var card = s.Add(new Card());
            card.Add(new TLabel("What you get", TextRole.Title));
            card.Add(new TLabel("• 11 tools: convert, compress and resize pictures; make, merge, split, compress, lock and edit PDFs\n• Right-click any picture or PDF › VKonvert\n• Light and dark mode\n• Uninstall any time from Settings › Apps", TextRole.Body, wrap: true));
        }

        void BuildLicense(Stack s)
        {
            s.Add(new TLabel("Short version: use it for anything, free, forever.", TextRole.Muted));
            var box = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                Text = LicenseText().Replace("\r\n", "\n").Replace("\n", "\r\n"), Font = Theme.Mono(8.75f), Height = Theme.Dp(170), TabStop = false,
            };
            var frame = new BoxFrame(box) { Height = Theme.Dp(190) };
            s.Add(frame);
            accept = s.Add(new Switch("I accept the license"));
            accept.Toggled += UpdateButtons;
        }

        void BuildFolder(Stack s)
        {
            s.Add(new TLabel("Install folder", TextRole.Title));
            var row = s.Add(new Row(Theme.Dp(40)));
            folder = new Field(existing ?? DefaultFolder, "Install folder");
            folder.Box.Font = Theme.Mono(9f);
            var browse = new PillButton("Browse…") { Height = Theme.Dp(40), Radius = Theme.Dp(10) };
            browse.Width = browse.PreferredWidth(Theme.Dp(16));
            browse.Click += (x, y) => Browse();
            row.Add(browse, alignRight: true);
            row.Add(folder);
            row.Stretch = folder;
            space = s.Add(new TLabel("", TextRole.Mono));
            folder.Box.TextChanged += (x, y) => UpdateSpace();
            UpdateSpace();
            menu = s.Add(new Switch("Add “VKonvert” to the right-click menu", true));
            startMenu = s.Add(new Switch("Start menu shortcut", true));
            desktop = s.Add(new Switch("Desktop shortcut", true));
            defaultApp = s.Add(new Switch("Open pictures with VKonvert by default", false));
        }

        void BuildWork(Stack s)
        {
            workText = s.Add(new TLabel("Installing VKonvert…", TextRole.Title) { Font = Theme.UiSemibold(13f) });
            progress = s.Add(new SegmentBar { Height = Theme.Dp(22) });
            s.Add(new TLabel("This only takes a few seconds.", TextRole.Muted));
        }

        void BuildDone(Stack s)
        {
            s.Add(new TLabel("VKonvert is installed", TextRole.Title) { Font = Theme.UiSemibold(13f) });
            doneText = s.Add(new TLabel("", TextRole.Body, wrap: true));
            launch = s.Add(new Switch("Open VKonvert now", true));
        }

        // ───────────── Flow ─────────────

        void Go(int i)
        {
            step = Math.Max(0, Math.Min(4, i));
            for (int j = 0; j < pages.Length; j++) pages[j].Visible = j == step;
            header.Step = step;
            UpdateButtons();
            if (step == 1) accept.Focus();
            if (step == 2) folder.Box.Focus();
        }

        void UpdateButtons()
        {
            back.Visible = step == 1 || step == 2;
            cancel.Visible = step < 3;
            next.Visible = step != 3;
            next.Text = step == 0 ? "Next" : step == 1 ? "Next: choose folder" : step == 2 ? (existing != null ? "Update" : "Install") : "Finish";
            next.Enabled = step != 1 || accept.Checked;
            next.Parent?.PerformLayout();
            AcceptButton = null;
        }

        async Task Next()
        {
            if (step == 1 && !accept.Checked) return;
            if (step < 2) { Go(step + 1); return; }
            if (step == 2) { await Install(); return; }
            if (step == 4)
            {
                if (launch.Checked && installedExe != null) Process.Start(installedExe);
                Close();
            }
        }

        void Browse()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Choose a folder for VKonvert (a “VKonvert” folder is created inside it)." })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var chosen = dlg.SelectedPath;
                folder.Text = string.Equals(Path.GetFileName(chosen.TrimEnd('\\')), "VKonvert", StringComparison.OrdinalIgnoreCase)
                    ? chosen : Path.Combine(chosen, "VKonvert");
            }
        }

        void UpdateSpace()
        {
            var need = PayloadSize();
            string free = "";
            try
            {
                var root = Path.GetPathRoot(folder.Text.Trim());
                if (!string.IsNullOrEmpty(root))
                {
                    var d = new DriveInfo(root);
                    free = $" · {FormatSize(d.AvailableFreeSpace)} free on {root.TrimEnd('\\')}";
                }
            }
            catch { }
            space.Text = $"Needs {FormatSize(need)}{free}";
        }

        async Task Install()
        {
            var target = folder.Text.Trim().TrimEnd('\\');
            if (target.Length == 0 || !Path.IsPathRooted(target))
            {
                MessageBox.Show(this, "Please choose a full folder path, for example C:\\Users\\you\\Apps\\VKonvert.", Text);
                return;
            }
            Go(3);
            ControlBox = false;
            var args = $"{B(menu)} {B(startMenu)} {B(desktop)} {B(defaultApp)}";
            try
            {
                StopRunning();
                await Task.Run(() => Unpack(target));
                workText.Text = "Adding VKonvert to Windows…";
                var exe = Path.Combine(target, "VKonvert.exe");
                var code = await Task.Run(() =>
                {
                    using (var p = Process.Start(new ProcessStartInfo(exe, "--register " + args) { UseShellExecute = false }))
                    {
                        p.WaitForExit();
                        return p.ExitCode;
                    }
                });
                if (code != 0) throw new InvalidOperationException("VKonvert couldn’t add itself to Windows (code " + code + ").");
                progress.Value = 1;
                installedExe = exe;
                doneText.Text =
                    $"Installed in {target}\n\n" +
                    "• Find VKonvert in the Start menu" + (desktop.Checked ? " and on your desktop" : "") + ".\n" +
                    (menu.Checked ? "• Right-click any picture or PDF › VKonvert. On Windows 11, choose “Show more options” first.\n" : "") +
                    "• Uninstall any time from Settings › Apps › Installed apps.";
                (pages[4] as Stack).PerformLayout();
                Go(4);
            }
            catch (UnauthorizedAccessException)
            {
                Fail($"Setup can’t write to “{target}”.\n\nChoose a folder inside your user account, like the default one.");
            }
            catch (Exception ex)
            {
                Fail("Setup couldn’t install VKonvert.\n\n" + ex.Message);
            }
            finally
            {
                ControlBox = true;
            }
        }

        static string B(Switch s) => s.Checked ? "1" : "0";

        void Fail(string message)
        {
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Go(2);
        }

        // An open VKonvert (or an older Vpix) window would lock files while we overwrite them.
        static void StopRunning()
        {
            foreach (var name in new[] { "VKonvert", "Vpix", "VPixShift" })
                foreach (var p in Process.GetProcessesByName(name))
                    try { p.CloseMainWindow(); if (!p.WaitForExit(3000)) p.Kill(); } catch { }
        }

        void Unpack(string target)
        {
            Directory.CreateDirectory(target);
            using (var stream = typeof(SetupForm).Assembly.GetManifestResourceStream("payload.zip"))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var files = zip.Entries.Where(e => e.Name.Length > 0).ToList();
                for (int i = 0; i < files.Count; i++)
                {
                    files[i].ExtractToFile(Path.Combine(target, files[i].Name), true); // the payload is flat
                    var v = (i + 1) / (float)files.Count * 0.9f;
                    BeginInvoke(new Action(() => progress.Value = v));
                }
            }
        }

        static long PayloadSize()
        {
            try
            {
                using (var stream = typeof(SetupForm).Assembly.GetManifestResourceStream("payload.zip"))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                    return zip.Entries.Sum(e => e.Length);
            }
            catch { return 0; }
        }

        static string LicenseText()
        {
            using (var stream = typeof(SetupForm).Assembly.GetManifestResourceStream("payload.zip"))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            using (var r = new StreamReader(zip.GetEntry("LICENSE.txt").Open()))
                return r.ReadToEnd();
        }

        static string FormatSize(long b) =>
            b >= 1024L * 1024 * 1024 ? (b / 1024.0 / 1024 / 1024).ToString("0") + " GB" : (b / 1024.0 / 1024).ToString("0") + " MB";

        void ApplyTheme()
        {
            BackColor = Theme.P.Bg;
            foreach (var p in pages) p.BackColor = Theme.P.Bg;
            Theme.ApplyTitleBar(this);
            Invalidate(true);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.ApplyTitleBar(this); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (step == 3) { e.Cancel = true; return; } // don't stop half-way through copying
            base.OnFormClosing(e);
        }

        // ───────────── Pieces ─────────────

        /// <summary>Logo, "Install VKonvert", the five-step pixel bar, and the light/dark button.</summary>
        sealed class Header : VControl
        {
            int step;
            public int Step { get => step; set { step = value; Invalidate(); } }
            public event Action ThemeClicked;
            Rectangle themeRect;

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                if (themeRect.Contains(e.Location)) ThemeClicked?.Invoke();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                Cursor = themeRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Draw.Smooth(g);
                var p = Theme.P;
                using (var b = new SolidBrush(p.Bg)) g.FillRectangle(b, ClientRectangle);
                int x = Theme.Dp(32), y = Theme.Dp(24), s = Theme.Dp(54);
                Draw.Mark(g, new RectangleF(x, y, s, s));
                var f = Theme.Brand(21f);
                var install = "Install ";
                var iw = Draw.BrandMeasure(g, install, f).Width;
                Draw.BrandText(g, install, f, new PointF(x + s + Theme.Dp(16), y - Theme.Dp(2)), p.Ink);
                Draw.Wordmark(g, new PointF(x + s + Theme.Dp(16) + iw, y - Theme.Dp(2)), 21f);
                Draw.Text(g, $"Version {Version} · made by Vishnu VK · free to use", Theme.Ui(9.5f),
                    new Rectangle(x + s + Theme.Dp(17), y + Theme.Dp(34), Width, Theme.Dp(20)), p.Muted);

                // Theme toggle, top right.
                var label = p.Dark ? "Light mode" : "Dark mode";
                var lw = Draw.Measure(label, Theme.UiSemibold(8.5f)).Width + Theme.Dp(20);
                themeRect = new Rectangle(Width - lw - Theme.Dp(20), Theme.Dp(14), lw, Theme.Dp(26));
                Draw.Fill(g, themeRect, themeRect.Height / 2f, p.Panel);
                Draw.Stroke(g, themeRect, themeRect.Height / 2f, p.Line);
                Draw.Text(g, label, Theme.UiSemibold(8.5f), themeRect, p.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                // Five steps, each a row of six pixels.
                var top = Theme.Dp(100);
                var gap = Theme.Dp(10);
                var w = (Width - Theme.Dp(64) - gap * 4) / 5f;
                for (int i = 0; i < 5; i++)
                {
                    var sx = Theme.Dp(32) + i * (w + gap);
                    var c = i < step ? (p.Dark ? p.RedDeep : Theme.Hex("#F0B4AE")) : i == step ? p.Red : p.Line;
                    var pw = (w - 5 * Theme.Dp(3)) / 6f;
                    using (var b = new SolidBrush(c))
                        for (int k = 0; k < 6; k++) g.FillRectangle(b, sx + k * (pw + Theme.Dp(3)), top, pw, Theme.Dp(8));
                    Draw.Text(g, StepNames[i].ToUpperInvariant(), Theme.Mono(7.5f, true), new Rectangle((int)sx, top + Theme.Dp(14), (int)w, Theme.Dp(18)),
                        i <= step ? p.Ink : p.Muted, TextFormatFlags.Left | TextFormatFlags.Top);
                }
            }
        }

        sealed class Footer : VControl
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                using (var b = new SolidBrush(Theme.P.Panel)) e.Graphics.FillRectangle(b, ClientRectangle);
                using (var pen = new Pen(Theme.P.Line)) e.Graphics.DrawLine(pen, 0, 0, Width, 0);
            }
        }

        /// <summary>Progress as red pixel blocks, like the app's quality bar.</summary>
        sealed class SegmentBar : VControl
        {
            float value;
            public float Value { get => value; set { this.value = value; Invalidate(); } }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Draw.Smooth(g);
                const int n = 20;
                var gap = Theme.Dp(4);
                var w = (Width - gap * (n - 1)) / (float)n;
                for (int i = 0; i < n; i++)
                    Draw.Fill(g, new RectangleF(i * (w + gap), 0, w, Height), Theme.Dp(2), i < Math.Round(value * n) ? Theme.P.Red : Theme.P.Line);
            }
        }

        /// <summary>Rounded frame around the read-only license text.</summary>
        sealed class BoxFrame : VControl
        {
            readonly TextBox box;
            public BoxFrame(TextBox box)
            {
                this.box = box;
                Controls.Add(box);
                Recolor();
            }

            [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
            static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

            protected override void OnThemeChanged() { Recolor(); base.OnThemeChanged(); }

            void Recolor()
            {
                box.BackColor = Theme.P.Panel;
                box.ForeColor = Theme.P.Muted;
                // Dark scrollbar in dark mode (Windows 10 1809+; ignored elsewhere).
                try { if (box.IsHandleCreated) SetWindowTheme(box.Handle, Theme.P.Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
            }

            protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); box.HandleCreated += (s, a) => Recolor(); Recolor(); }

            protected override void OnLayout(LayoutEventArgs e)
            {
                box.SetBounds(Theme.Dp(14), Theme.Dp(10), Width - Theme.Dp(20), Height - Theme.Dp(20));
                base.OnLayout(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Draw.Smooth(e.Graphics);
                var r = new RectangleF(0, 0, Width, Height);
                Draw.Fill(e.Graphics, r, Theme.Dp(12), Theme.P.Panel);
                Draw.Stroke(e.Graphics, r, Theme.Dp(12), Theme.P.Line);
            }
        }
    }
}
