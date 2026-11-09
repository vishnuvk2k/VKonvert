using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VKonvert.Engine;
using VKonvert.UI;

namespace VKonvert
{
    /// <summary>
    /// The "conversion machine": files in on the left, the tool in the middle, finished files on the right.
    /// </summary>
    public sealed class MainForm : Form, IToolHost
    {
        readonly List<Tool> tools = ToolCatalog.All();
        readonly Dictionary<Tool, Stack> pages = new Dictionary<Tool, Stack>();
        readonly Dictionary<Tool, BigDisplay> displays = new Dictionary<Tool, BigDisplay>();
        readonly List<PillButton> saveToButtons = new List<PillButton>();
        readonly Dictionary<string, PillButton> navPills = new Dictionary<string, PillButton>();
        Tool tool;
        string outputFolder;
        bool busy;

        TopBar top;
        Panel inCol, outCol;
        DotPanel center;
        ThinScroll scrollBar;
        FileList imageList, pdfList, outList;
        TLabel inNote, outNote, tip;
        PillButton run, more, openFolder, clearIn, clearOut;
        IconButton themeButton, menuButton;
        ContextMenuStrip moreMenu, appMenu;
        long savedBytes;

        public MainForm(string[] files, string toolId = null)
        {
            Text = "VKonvert";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = Theme.Ui(9.75f);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(Theme.Dp(1180), Theme.Dp(720));
            Size = new Size(Theme.Dp(1300), Theme.Dp(820));
            KeyPreview = true;
            AllowDrop = true;

            BuildLayout();
            Theme.Changed += ApplyTheme;
            ApplyTheme();

            var initial = tools.FirstOrDefault(t => t.Id == toolId);
            if (initial == null && files.Length > 0 && files.All(Formats.IsPdf)) initial = tools.First(t => t.Id == (files.Length > 1 ? "merge" : "compresspdf"));
            SwitchTo(initial ?? tools[0]);
            AddFiles(files);
        }

        // ═════════════ Layout ═════════════

        void BuildLayout()
        {
            SuspendLayout();
            top = new TopBar { Dock = DockStyle.Top, Height = Theme.Dp(60) };

            foreach (var t in tools.Where(t => ToolCatalog.Pinned.Contains(t.Id)))
            {
                var pill = new PillButton(t.Title, PillKind.Nav) { Height = Theme.Dp(34) };
                pill.Width = pill.PreferredWidth(Theme.Dp(14));
                var me = t;
                pill.Click += (s, e) => SwitchTo(me);
                navPills[t.Id] = pill;
                top.Nav.Add(pill);
            }
            more = new PillButton("More", PillKind.Nav) { Height = Theme.Dp(34), Chevron = true };
            more.Width = more.PreferredWidth(Theme.Dp(14));
            moreMenu = new ContextMenuStrip { ShowImageMargin = false, Font = Theme.Ui(10f) };
            foreach (var t in tools.Where(t => !ToolCatalog.Pinned.Contains(t.Id)))
            {
                var me = t;
                moreMenu.Items.Add(t.Title.Replace("&", "&&"), null, (s, e) => SwitchTo(me));
            }
            more.Click += (s, e) => moreMenu.Show(more, new Point(0, more.Height + Theme.Dp(4)));
            top.Nav.Add(more);

            themeButton = new IconButton(null, "Switch light or dark mode") { Size = new Size(Theme.Dp(38), Theme.Dp(38)) };
            themeButton.Click += (s, e) => Theme.Toggle();
            menuButton = new IconButton(IconButton.Dots, "Menu") { Size = new Size(Theme.Dp(38), Theme.Dp(38)) };
            appMenu = BuildAppMenu();
            menuButton.Click += (s, e) => appMenu.Show(menuButton, new Point(menuButton.Width - appMenu.PreferredSize.Width, menuButton.Height + Theme.Dp(4)));
            top.AddButton(themeButton);
            top.AddButton(menuButton);

            // ── Bottom bar ──
            var bottom = new BottomBar { Dock = DockStyle.Bottom, Height = Theme.Dp(78) };
            tip = new TLabel("", TextRole.Muted) { AutoSize = false, AutoEllipsis = true };
            run = new PillButton("Convert", PillKind.Primary) { Height = Theme.Dp(54) };
            run.Click += (s, e) => Run();
            var hint = new TLabel("Ctrl + Enter", TextRole.Mono);
            bottom.Controls.Add(tip);
            bottom.Controls.Add(hint);
            bottom.Controls.Add(run);
            bottom.Layout += (s, e) =>
            {
                var w = Math.Max(run.PreferredWidth(Theme.Dp(30)), Theme.Dp(220));
                run.SetBounds(bottom.Width - w - Theme.Dp(22), (bottom.Height - run.Height) / 2 + Theme.Dp(2), w, run.Height);
                hint.Location = new Point(run.Left - hint.PreferredWidth - Theme.Dp(16), (bottom.Height - hint.PreferredHeight) / 2);
                tip.SetBounds(Theme.Dp(22), (bottom.Height - Theme.Dp(24)) / 2, Math.Max(0, hint.Left - Theme.Dp(40)), Theme.Dp(24));
            };

            // ── In column ──
            inCol = new Panel { Dock = DockStyle.Left, Width = Theme.Dp(300), Padding = new Padding(Theme.Dp(18), Theme.Dp(18), Theme.Dp(18), Theme.Dp(18)) };
            var inHead = new Row(Theme.Dp(28)) { Dock = DockStyle.Top };
            inHead.Add(new TLabel("IN", TextRole.Label));
            inNote = inHead.Add(new TLabel("", TextRole.Mono));
            clearIn = new PillButton("Clear", PillKind.Ghost) { Height = Theme.Dp(26) };
            clearIn.Width = clearIn.PreferredWidth(Theme.Dp(10));
            clearIn.Click += (s, e) => { if (!busy) { CurrentList.Items.Clear(); InputsChanged(); } };
            inHead.Add(clearIn, alignRight: true);
            imageList = MakeInputList();
            pdfList = MakeInputList();
            inCol.Controls.Add(imageList);
            inCol.Controls.Add(pdfList);
            inCol.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.Dp(10), BackColor = Color.Transparent });
            inCol.Controls.Add(inHead);

            // ── Out column ──
            outCol = new Panel { Dock = DockStyle.Right, Width = Theme.Dp(300), Padding = new Padding(Theme.Dp(18)) };
            var outHead = new Row(Theme.Dp(28)) { Dock = DockStyle.Top };
            outHead.Add(new TLabel("OUT", TextRole.Label));
            outNote = outHead.Add(new TLabel("", TextRole.Mono));
            clearOut = new PillButton("Clear", PillKind.Ghost) { Height = Theme.Dp(26) };
            clearOut.Width = clearOut.PreferredWidth(Theme.Dp(10));
            clearOut.Click += (s, e) => { outList.Items.Clear(); savedBytes = 0; UpdateOutNote(); outList.Refresh2(); };
            outHead.Add(clearOut, alignRight: true);
            outList = new FileList { IsInput = false, Dock = DockStyle.Fill };
            outList.Opened += f => OpenPath(f.Path);
            outList.ContextRequested += (f, p) => ItemMenu(f, false).Show(outList, p);
            openFolder = new PillButton("Open output folder") { Dock = DockStyle.Bottom, Height = Theme.Dp(40), Radius = Theme.Dp(10) };
            openFolder.Click += (s, e) =>
            {
                var last = outList.Items.FirstOrDefault();
                if (last != null) Process.Start("explorer.exe", $"/select,\"{last.Path}\"");
                else if (outputFolder != null) Process.Start("explorer.exe", $"\"{outputFolder}\"");
            };
            outCol.Controls.Add(outList);
            outCol.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.Dp(10), BackColor = Color.Transparent });
            outCol.Controls.Add(outHead);
            outCol.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = Theme.Dp(12), BackColor = Color.Transparent });
            outCol.Controls.Add(openFolder);

            center = new DotPanel { Dock = DockStyle.Fill };
            scrollBar = new ThinScroll { Dock = DockStyle.Right };
            center.Controls.Add(scrollBar);

            Controls.Add(center);
            Controls.Add(inCol);
            Controls.Add(outCol);
            Controls.Add(bottom);
            Controls.Add(top);
            ResumeLayout();
        }

        FileList MakeInputList()
        {
            var l = new FileList { Dock = DockStyle.Fill, Visible = false };
            l.AddClicked += BrowseForFiles;
            l.Changed += InputsChanged;
            l.Opened += f => OpenPath(f.Path);
            l.ContextRequested += (f, p) => ItemMenu(f, true).Show(l, p);
            return l;
        }

        ContextMenuStrip ItemMenu(FileItem f, bool input)
        {
            var m = new ContextMenuStrip { ShowImageMargin = false, Font = Theme.Ui(10f) };
            m.Items.Add("Open", null, (s, e) => OpenPath(f.Path));
            m.Items.Add("Show in folder", null, (s, e) => Process.Start("explorer.exe", $"/select,\"{f.Path}\""));
            if (input && !busy) m.Items.Add("Remove from list", null, (s, e) => { CurrentList.Items.Remove(f); InputsChanged(); });
            return m;
        }

        ContextMenuStrip BuildAppMenu()
        {
            var m = new ContextMenuStrip { ShowImageMargin = false, Font = Theme.Ui(10f) };
            var theme = new ToolStripMenuItem("Appearance");
            foreach (var (label, mode) in new[] { ("Light", ThemeMode.Light), ("Dark", ThemeMode.Dark), ("Match Windows", ThemeMode.System) })
            {
                var item = new ToolStripMenuItem(label) { Tag = mode };
                item.Click += (s, e) => Theme.SetMode(mode);
                theme.DropDownItems.Add(item);
            }
            theme.DropDownOpening += (s, e) => { foreach (ToolStripMenuItem i in theme.DropDownItems) i.Checked = (ThemeMode)i.Tag == Theme.Mode; };
            m.Items.Add(theme);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Website: vkonvert.com", null, (s, e) => OpenPath("https://vkonvert.com"));
            m.Items.Add("About VKonvert", null, (s, e) => AboutDialog.ShowFor(this));
            var uninstall = new ToolStripMenuItem("Uninstall VKonvert…");
            uninstall.Click += (s, e) => Program.Uninstall(this);
            m.Items.Add(uninstall);
            m.Opening += (s, e) => uninstall.Visible = ShellIntegration.IsInstalled;
            return m;
        }

        // ═════════════ Tools ═════════════

        FileList CurrentList => tool != null && tool.ForPdf ? pdfList : imageList;

        void SwitchTo(Tool t)
        {
            if (busy || t == tool) return;
            tool = t;
            foreach (var kv in navPills) kv.Value.Selected = kv.Key == t.Id;
            var inMore = !ToolCatalog.Pinned.Contains(t.Id);
            more.Selected = inMore;
            more.Text = inMore ? t.Title : "More";
            more.Width = more.PreferredWidth(Theme.Dp(14));
            top.PerformLayout();
            top.Nav.PerformLayout();
            top.Invalidate(true);

            if (!pages.TryGetValue(t, out var page))
            {
                page = new Stack
                {
                    Dock = DockStyle.Fill, Scrolls = true, MaxContentWidth = Theme.Dp(600),
                    Inner = new Padding(Theme.Dp(36), Theme.Dp(26), Theme.Dp(36), Theme.Dp(26)), Gap = Theme.Dp(18),
                };
                if (t.ShowDisplay)
                {
                    var d = page.Add(new BigDisplay());
                    displays[t] = d;
                }
                t.Build(page, this);
                pages[t] = page;
                center.Controls.Add(page);
                page.BringToFront();
            }
            foreach (var p in pages.Values) p.Visible = p == page;
            scrollBar.Bind(page);
            imageList.Visible = !t.ForPdf;
            pdfList.Visible = t.ForPdf;
            imageList.AllowReorder = pdfList.AllowReorder = t.Reorder;
            CurrentList.DropText = t.DropText;
            tip.Text = t.Tip;
            Text = "VKonvert · " + t.Title;
            t.InputsChanged(CurrentList.Items);
            OptionsChanged();
        }

        public void OptionsChanged()
        {
            if (tool == null) return;
            var files = CurrentList.Items;
            if (displays.TryGetValue(tool, out var d)) tool.UpdateDisplay(d, files);
            run.Text = tool.ActionText(files);
            run.Enabled = !busy && files.Count > 0;
            run.Parent?.PerformLayout();
            var size = files.Sum(f => f.Size);
            inNote.Text = files.Count == 0 ? "" : $"{files.Count} · {Images.FormatSize(size)}";
            inNote.Parent?.PerformLayout();
        }

        void InputsChanged()
        {
            CurrentList.Refresh2();
            tool.InputsChanged(CurrentList.Items);
            OptionsChanged();
        }

        // ═════════════ Save to ═════════════

        public string OutputFolder => outputFolder;

        public Control MakeSaveToRow()
        {
            var row = new Row(Theme.Dp(38));
            row.Add(new TLabel("Save to", TextRole.Title));
            var b = new PillButton(SaveToText) { Height = Theme.Dp(36), Radius = Theme.Dp(10), Chevron = true };
            b.Width = b.PreferredWidth(Theme.Dp(14));
            b.Click += (s, e) =>
            {
                var m = new ContextMenuStrip { ShowImageMargin = false, Font = Theme.Ui(10f) };
                m.Items.Add(new ToolStripMenuItem("Same folder as the original", null, (x, y) => SetOutput(null)) { Checked = outputFolder == null });
                m.Items.Add("Choose a folder…", null, (x, y) =>
                {
                    using (var dlg = new FolderBrowserDialog { Description = "Where should VKonvert save new files?", SelectedPath = outputFolder ?? "" })
                        if (dlg.ShowDialog(this) == DialogResult.OK) SetOutput(dlg.SelectedPath);
                });
                m.Show(b, new Point(0, b.Height + Theme.Dp(4)));
            };
            saveToButtons.Add(b);
            row.Add(b);
            return row;
        }

        string SaveToText => outputFolder == null ? "Same folder as the original" : Path.GetFileName(outputFolder.TrimEnd('\\'));

        void SetOutput(string folder)
        {
            outputFolder = folder;
            foreach (var b in saveToButtons)
            {
                b.Text = SaveToText;
                b.Width = b.PreferredWidth(Theme.Dp(14));
                b.Parent?.PerformLayout();
            }
        }

        // ═════════════ Files in ═════════════

        void BrowseForFiles()
        {
            var filter = tool.ForPdf ? "PDF files|*.pdf"
                : "Pictures|" + string.Join(";", Formats.ImageExtensions.Select(e => "*" + e)) + "|All files|*.*";
            using (var dlg = new OpenFileDialog { Multiselect = !tool.SingleInput, Filter = filter, Title = "Add files to VKonvert" })
                if (dlg.ShowDialog(this) == DialogResult.OK) AddFiles(dlg.FileNames);
        }

        public void AddFiles(IEnumerable<string> paths)
        {
            var all = new List<string>();
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                    all.AddRange(Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).Where(f => Formats.IsImage(f) || Formats.IsPdf(f)).OrderBy(f => f));
                else if (File.Exists(p)) all.Add(p);
            }
            if (all.Count == 0) return;
            var images = all.Where(Formats.IsImage).ToList();
            var pdfs = all.Where(Formats.IsPdf).ToList();

            // Dropped PDFs while on an image tool (or the other way round): go to a tool that fits.
            if (!busy && tool.ForPdf && images.Count > 0 && pdfs.Count == 0) SwitchTo(tools.First(t => t.Id == (images.Count > 1 ? "imagestopdf" : "convert")));
            else if (!busy && !tool.ForPdf && pdfs.Count > 0 && images.Count == 0) SwitchTo(tools.First(t => t.Id == (pdfs.Count > 1 ? "merge" : "compresspdf")));

            Add(imageList, images);
            Add(pdfList, pdfs);
            InputsChanged();
        }

        void Add(FileList list, List<string> paths)
        {
            foreach (var p in paths)
            {
                if (list.Items.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
                var item = new FileItem { Path = p, Badge = Formats.NameOf(p), Swatch = SwatchFor(p) };
                try { item.Size = new FileInfo(p).Length; } catch { }
                item.Note = Images.FormatSize(item.Size);
                list.Items.Add(item);
                LoadDetails(item, list);
            }
        }

        static Color SwatchFor(string p)
        {
            var palette = new[] { "#C9B49E", "#8FA9C2", "#D6C7A8", "#9C8A6E", "#A9B8A0", "#C2A3A0" };
            return Theme.Hex(palette[Math.Abs(p.GetHashCode()) % palette.Length]);
        }

        static readonly SemaphoreSlim thumbSlots = new SemaphoreSlim(2);

        void LoadDetails(FileItem item, FileList list)
        {
            Task.Run(async () =>
            {
                await thumbSlots.WaitAsync();
                try
                {
                    Image thumb = null;
                    string note = item.Note;
                    if (Formats.IsPdf(item.Path))
                    {
                        using (var doc = new Pdfium.Doc(item.Path))
                        {
                            item.Pages = doc.PageCount;
                            thumb = doc.Render(0, 0, Theme.Dp(96));
                        }
                        note = $"{item.Pages} page{(item.Pages == 1 ? "" : "s")} · {Images.FormatSize(item.Size)}";
                    }
                    else
                    {
                        var dim = Images.Dimensions(item.Path);
                        note = $"{dim.Width} × {dim.Height} · {Images.FormatSize(item.Size)}";
                        thumb = Images.Thumbnail(item.Path, Theme.Dp(96));
                    }
                    BeginInvoke(new Action(() => { item.Thumb = thumb; item.Note = note; list.Invalidate(); OptionsChanged(); }));
                }
                catch (Exception ex)
                {
                    var msg = Formats.IsPdf(item.Path) ? ex.Message : "Can’t read this picture";
                    try { BeginInvoke(new Action(() => { item.Note = msg; list.Invalidate(); })); } catch { }
                }
                finally { thumbSlots.Release(); }
            });
        }

        /// <summary>Files from a second launch (Explorer's "Open with" on more files, or a right-click tool).</summary>
        public void AddFilesFromOtherLaunch(string[] lines)
        {
            var toolLine = lines.FirstOrDefault(l => l.StartsWith("@tool:"));
            var files = lines.Where(l => !l.StartsWith("@")).ToArray();
            if (toolLine != null && !busy)
            {
                var t = tools.FirstOrDefault(x => x.Id == toolLine.Substring(6));
                if (t != null) SwitchTo(t);
            }
            AddFiles(files);
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) && !busy ? DragDropEffects.Copy : DragDropEffects.None;
            base.OnDragEnter(e);
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files);
            base.OnDragDrop(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Enter) { Run(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.O) { BrowseForFiles(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        // ═════════════ Run ═════════════

        async void Run()
        {
            if (busy) return;
            var files = CurrentList.Items.ToList();
            if (files.Count == 0) { BrowseForFiles(); return; }
            var problem = tool.Validate(files);
            if (problem != null)
            {
                tip.ForeColor = Theme.P.Warn;
                tip.Text = problem;
                return;
            }
            foreach (var f in files) { f.State = ItemState.Waiting; f.Error = null; f.Progress = 0; }
            SetBusy(true);
            tip.Text = "Working…";
            var t = tool;
            var list = CurrentList;
            var started = DateTime.Now;
            List<Output> outputs = null;
            try
            {
                outputs = await Task.Run(() => t.Run(files, this, (item, state, progress, error) =>
                {
                    BeginInvoke(new Action(() =>
                    {
                        item.State = state; item.Progress = progress; item.Error = error;
                        list.Invalidate();
                        var done = files.Count(x => x.State == ItemState.Done || x.State == ItemState.Failed);
                        if (files.Count > 1) tip.Text = $"Working… {done} of {files.Count}";
                    }));
                }));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Tool.Friendly(ex), "VKonvert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            SetBusy(false);
            if (outputs == null) return;
            ShowOutputs(outputs);

            var failed = files.Count(f => f.State == ItemState.Failed);
            var secs = (DateTime.Now - started).TotalSeconds;
            tip.ForeColor = failed > 0 ? Theme.P.Warn : Theme.P.Muted;
            tip.Text = failed > 0
                ? $"{failed} of {files.Count} couldn’t be done. Point at the red card to see why."
                : $"Done in {secs:0.#} s. Double-click a file on the right to open it.";
        }

        void SetBusy(bool on)
        {
            busy = on;
            run.Enabled = !on;
            if (on) run.Text = "Working…";
            foreach (var p in navPills.Values) p.Enabled = !on;
            more.Enabled = !on;
            clearIn.Enabled = !on;
            if (!on) OptionsChanged();
        }

        void ShowOutputs(List<Output> outputs)
        {
            var added = new List<FileItem>();
            foreach (var o in outputs)
            {
                if (!File.Exists(o.Path)) continue;
                var item = new FileItem { Path = o.Path, Badge = Formats.NameOf(o.Path), Swatch = SwatchFor(o.Path) };
                try { item.Size = new FileInfo(o.Path).Length; } catch { }
                item.Note = Images.FormatSize(item.Size);
                if (o.Source != null && o.Source.Size > 0)
                {
                    item.Result = Images.Change(o.Source.Size, item.Size);
                    item.ResultGood = item.Size <= o.Source.Size;
                    if (item.Size < o.Source.Size) savedBytes += o.Source.Size - item.Size;
                }
                added.Add(item);
                LoadDetails(item, outList);
            }
            outList.Items.InsertRange(0, added);
            outList.Refresh2();
            UpdateOutNote();
        }

        void UpdateOutNote()
        {
            outNote.Text = savedBytes > 0 ? "saved " + Images.FormatSize(savedBytes) : outList.Items.Count == 1 ? "1 file" : outList.Items.Count > 0 ? outList.Items.Count + " files" : "";
            outNote.ForeColor = savedBytes > 0 ? Theme.P.Ok : Theme.P.Muted;
            outNote.Parent?.PerformLayout();
        }

        static void OpenPath(string p)
        {
            try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "VKonvert"); }
        }

        // ═════════════ Theme ═════════════

        void ApplyTheme()
        {
            var p = Theme.P;
            BackColor = p.Bg;
            inCol.BackColor = outCol.BackColor = p.Bg;
            themeButton.Icon = p.Dark ? (Action<Graphics, Rectangle>)IconButton.Sun : IconButton.Moon;
            themeButton.AccessibleName = p.Dark ? "Switch to light mode" : "Switch to dark mode";
            UpdateOutNote();
            tip.ForeColor = p.Muted;
            Theme.ApplyTitleBar(this);
            Invalidate(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyTitleBar(this);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Theme.Changed -= ApplyTheme;
            base.OnFormClosed(e);
        }
    }

    /// <summary>Logo, tool pills and the round buttons along the top.</summary>
    sealed class TopBar : VControl
    {
        public readonly NavHost Nav = new NavHost();
        public readonly List<Control> Buttons = new List<Control>();

        public TopBar() { Controls.Add(Nav); }

        public void AddButton(Control c) { Buttons.Add(c); Controls.Add(c); }

        int LogoWidth => Theme.Dp(18) + Theme.Dp(28) + Theme.Dp(10) + Theme.Dp(110);

        protected override void OnLayout(LayoutEventArgs e)
        {
            var x = Width - Theme.Dp(16);
            for (int i = Buttons.Count - 1; i >= 0; i--)
            {
                x -= Buttons[i].Width;
                Buttons[i].Location = new Point(x, (Height - Buttons[i].Height) / 2);
                x -= Theme.Dp(8);
            }
            var navW = Nav.PreferredWidth;
            var room = x - LogoWidth - Theme.Dp(16);
            Nav.SetBounds(LogoWidth + Math.Max(Theme.Dp(8), (room - navW) / 2), (Height - Theme.Dp(44)) / 2, navW, Theme.Dp(44));
            base.OnLayout(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var p = Theme.P;
            using (var b = new SolidBrush(p.Panel)) g.FillRectangle(b, ClientRectangle);
            using (var pen = new Pen(p.Line)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            var s = Theme.Dp(28);
            Draw.Mark(g, new RectangleF(Theme.Dp(18), (Height - s) / 2f, s, s));
            var f = Theme.Brand(15f);
            var h = Draw.BrandMeasure(g, "VK", f).Height;
            Draw.Wordmark(g, new PointF(Theme.Dp(18) + s + Theme.Dp(10), (Height - h) / 2f), 15f);
        }
    }

    /// <summary>The capsule that holds the tool pills.</summary>
    sealed class NavHost : VControl
    {
        readonly List<Control> items = new List<Control>();
        int Pad => Theme.Dp(5);

        public void Add(Control c) { items.Add(c); Controls.Add(c); }

        public int PreferredWidth => items.Sum(c => c.Width) + Pad * 2 + Theme.Dp(2) * (items.Count - 1);

        protected override void OnLayout(LayoutEventArgs e)
        {
            var x = Pad;
            foreach (var c in items)
            {
                c.Location = new Point(x, (Height - c.Height) / 2);
                x += c.Width + Theme.Dp(2);
            }
            base.OnLayout(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var r = new RectangleF(.5f, .5f, Width - 1, Height - 1);
            Draw.Fill(g, r, Height / 2f, Theme.P.Bg);
            Draw.Stroke(g, r, Height / 2f, Theme.P.Line);
        }
    }

    sealed class BottomBar : VControl
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            using (var b = new SolidBrush(Theme.P.Panel)) e.Graphics.FillRectangle(b, ClientRectangle);
            using (var pen = new Pen(Theme.P.Line)) e.Graphics.DrawLine(pen, 0, 0, Width, 0);
        }
    }
}
