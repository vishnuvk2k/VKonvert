using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using VKonvert.Engine;
using VKonvert.UI;

namespace VKonvert
{
    /// <summary>What a tool needs from the main window.</summary>
    public interface IToolHost
    {
        string OutputFolder { get; }
        Control MakeSaveToRow();
        void OptionsChanged();
    }

    /// <summary>One finished file, and the input it came from (for "−61%").</summary>
    public sealed class Output
    {
        public string Path;
        public FileItem Source;
        public Output(string path, FileItem source) { Path = path; Source = source; }
    }

    public delegate void Report(FileItem item, ItemState state, float progress = 0, string error = null);

    public abstract class Tool
    {
        public abstract string Id { get; }
        public abstract string Title { get; }
        public virtual bool ForPdf => false;
        public virtual bool Reorder => false;
        public virtual bool SingleInput => false;
        public virtual bool ShowDisplay => true;
        public virtual string Tip => "Tip: right-click any image in File Explorer › VKonvert";
        public virtual string DropText => ForPdf ? "+ Add PDFs or drop them here" : "+ Add images or drop them here";

        /// <summary>Puts the tool's options into the middle column (under the big display).</summary>
        public abstract void Build(Stack s, IToolHost host);
        public abstract void UpdateDisplay(BigDisplay d, IList<FileItem> files);
        public abstract string ActionText(IList<FileItem> files);
        /// <summary>A sentence telling the person what to fix, or null when ready.</summary>
        public virtual string Validate(IList<FileItem> files) => null;
        public abstract List<Output> Run(IList<FileItem> files, IToolHost host, Report report);
        public virtual void InputsChanged(IList<FileItem> files) { }

        protected static string Count(int n, string one, string many = null) => n + " " + (n == 1 ? one : many ?? one + "s");

        /// <summary>Runs <paramref name="work"/> for each file, marking each one done or failed.</summary>
        protected static List<Output> PerFile(IList<FileItem> files, Report report, Func<FileItem, IEnumerable<string>> work)
        {
            var outputs = new List<Output>();
            foreach (var f in files) report(f, ItemState.Waiting);
            foreach (var f in files)
            {
                report(f, ItemState.Working, 0.35f);
                try
                {
                    foreach (var o in work(f)) outputs.Add(new Output(o, f));
                    report(f, ItemState.Done, 1);
                }
                catch (Exception ex)
                {
                    report(f, ItemState.Failed, 0, Friendly(ex));
                }
            }
            return outputs;
        }

        /// <summary>One job over all files (merge, images → PDF).</summary>
        protected static List<Output> Together(IList<FileItem> files, Report report, Func<Action<int>, IEnumerable<string>> work)
        {
            foreach (var f in files) report(f, ItemState.Working, 0.1f);
            try
            {
                var outs = work(i => { if (i >= 0 && i < files.Count) report(files[i], ItemState.Done, 1); }).ToList();
                foreach (var f in files) report(f, ItemState.Done, 1);
                return outs.Select(o => new Output(o, null)).ToList();
            }
            catch (Exception ex)
            {
                foreach (var f in files) report(f, ItemState.Failed, 0, Friendly(ex));
                return new List<Output>();
            }
        }

        public static string Friendly(Exception ex)
        {
            Program.Log(ex);
            if (ex is UnauthorizedAccessException) return "Windows didn’t allow saving there. Choose another folder in “Save to”.";
            if (ex is IOException io && io.Message.Contains("used by another process")) return "The file is open in another app. Close it and try again.";
            if (ex is OutOfMemoryException) return "This file is too big to do in one go.";
            if (ex is ImageMagick.MagickException) return "This file looks damaged or isn’t a supported picture.";
            return ex.Message;
        }

        // ───────────── Shared building blocks ─────────────

        protected static TLabel Heading(string text) => new TLabel(text, TextRole.Title);
        protected static TLabel Note(string text) => new TLabel(text, TextRole.Muted, wrap: true);

        /// <summary>The pixel quality bar in a card, with its value and a plain-words hint.</summary>
        protected static Card QualityCard(int initial, Action<int> changed, out PixelBar bar)
        {
            var card = new Card();
            var head = new Row(Theme.Dp(24));
            head.Add(new TLabel("Quality", TextRole.Title));
            var value = head.Add(new TLabel(initial.ToString(), TextRole.Red) { Font = Theme.Mono(10f, true) });
            var hint = head.Add(new TLabel(QualityHint(initial), TextRole.Muted));
            var pb = new PixelBar { Value = initial };
            pb.ValueChanged += () =>
            {
                value.Text = pb.Value.ToString();
                hint.Text = QualityHint(pb.Value);
                head.PerformLayout();
                changed(pb.Value);
            };
            var scale = new Row(Theme.Dp(18));
            scale.Add(new TLabel("Smaller file", TextRole.Mono));
            scale.Add(new TLabel("Better quality", TextRole.Mono), alignRight: true);
            card.Add(head);
            card.Add(pb);
            card.Add(scale);
            bar = pb;
            return card;
        }

        static string QualityHint(int q) =>
            q >= 92 ? "Best quality, bigger files" :
            q >= 75 ? "Looks the same as the original" :
            q >= 55 ? "Good for sharing and websites" :
            q >= 35 ? "Small files, a little soft" : "Smallest files, visibly softer";

        protected static string Dominant(IList<FileItem> files, out string others)
        {
            others = "";
            if (files.Count == 0) return "Any";
            var groups = files.GroupBy(f => Formats.NameOf(f.Path)).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
            if (groups.Count > 1) others = "+ " + string.Join(", ", groups.Skip(1).Take(3));
            return groups[0];
        }

        protected static long TotalSize(IList<FileItem> files) => files.Sum(f => f.Size);
    }

    // ═════════════ Images ═════════════

    sealed class ConvertTool : Tool
    {
        public override string Id => "convert";
        public override string Title => "Convert";
        readonly ConvertOptions o = new ConvertOptions();
        Card quality;
        Row background;

        public void SetTarget(string name) { var t = Formats.TryGet(name); if (t != null) o.Target = t; if (keys != null) keys.Select(Array.IndexOf(Formats.Targets, o.Target), false); Refresh(); }
        KeyGroup keys;
        IToolHost host;

        public override void Build(Stack s, IToolHost host)
        {
            this.host = host;
            keys = s.Add(new KeyGroup(Formats.Targets.Select(t => t.Name), 5, Array.IndexOf(Formats.Targets, o.Target)));
            keys.Picked += i => { o.Target = Formats.Targets[i]; Refresh(); host.OptionsChanged(); };
            quality = s.Add(QualityCard(o.Quality, v => { o.Quality = v; host.OptionsChanged(); }, out _));

            background = s.Add(new Row(Theme.Dp(34)));
            background.Add(new TLabel("See-through parts become", TextRole.Title));
            var swatches = new[] { new Swatch(Color.White, "White") { Selected = true }, new Swatch(Color.Black, "Black"), new Swatch(Color.FromArgb(0xD9, 0x34, 0x2B), "Pick a colour") { Rainbow = true } };
            foreach (var sw in swatches)
            {
                var me = sw;
                sw.Click += (a, b) =>
                {
                    if (me.Rainbow)
                    {
                        using (var dlg = new ColorDialog { Color = o.Background, FullOpen = true })
                        {
                            if (dlg.ShowDialog(me.FindForm()) != DialogResult.OK) return;
                            me.Color = dlg.Color;
                        }
                    }
                    o.Background = me.Color;
                    foreach (var x in swatches) x.Selected = x == me;
                };
                background.Add(sw);
            }
            s.Add(host.MakeSaveToRow());
            Refresh();
        }

        void Refresh()
        {
            if (quality == null) return;
            quality.Visible = o.Target.HasQuality;
            background.Visible = !o.Target.KeepsTransparency;
            quality.Parent?.PerformLayout();
        }

        public override void UpdateDisplay(BigDisplay d, IList<FileItem> files)
        {
            var from = Dominant(files, out var others);
            d.Set(from, o.Target.Name, others, files.Count == 0 ? "" : o.Target.Name == "HEIC" ? "needs Windows HEIF codec" : "");
        }

        public override string ActionText(IList<FileItem> files) =>
            files.Count == 0 ? "Convert → " + o.Target.Name : $"Convert {Count(files.Count, "file")} → {o.Target.Name}";

        public override List<Output> Run(IList<FileItem> files, IToolHost host, Report report)
        {
            o.OutputFolder = host.OutputFolder;
            return PerFile(files, report, f => new[] { Images.Convert(f.Path, o) });
        }
    }

    sealed class CompressImageTool : Tool
    {
        public override string Id => "compress";
        public override string Title => "Compress";
        readonly CompressOptions o = new CompressOptions();
        static readonly long[] Sizes = { 50, 100, 200, 500, 1024 };
        Control sizeKeys, custom, qualityCard;
        Field customField;

        public override void Build(Stack s, IToolHost host)
        {
            var mode = s.Add(new KeyGroup(new[] { "Size limit", "Quality" }, 2));
            s.Add(Heading("Each picture under"));
            var keys = s.Add(new KeyGroup(new[] { "50 KB", "100 KB", "200 KB", "500 KB", "1 MB" }, 5, 3));
            sizeKeys = keys;
            customField = new Field("", "Custom size in KB") { Suffix = "KB" };
            custom = s.Add(new Row(Theme.Dp(40)));
            ((Row)custom).Add(new TLabel("Or type a size", TextRole.Muted));
            customField.Width = Theme.Dp(160);
            ((Row)custom).Add(customField);
            qualityCard = s.Add(QualityCard(o.Quality, v => { o.Quality = v; host.OptionsChanged(); }, out _));
            var strip = s.Add(new Switch("Also remove location and camera info"));
            strip.Toggled += () => o.RemoveInfo = strip.Checked;
            s.Add(host.MakeSaveToRow());
            s.Add(Note("PNG and GIF keep their see-through parts. Pictures that can’t get small enough by quality alone are also scaled down."));

            void Apply()
            {
                var bySize = o.Mode == CompressMode.TargetSize;
                foreach (Control c in s.Controls) if (c is TLabel l && l.Text == "Each picture under") l.Visible = bySize;
                sizeKeys.Visible = custom.Visible = bySize;
                qualityCard.Visible = !bySize;
                s.PerformLayout();
                host.OptionsChanged();
            }
            mode.Picked += i => { o.Mode = i == 0 ? CompressMode.TargetSize : CompressMode.Quality; Apply(); };
            keys.Picked += i => { o.TargetBytes = Sizes[i] * 1024; customField.Text = ""; host.OptionsChanged(); };
            customField.Box.TextChanged += (a, b) =>
            {
                if (long.TryParse(customField.Text.Trim(), out var kb) && kb > 0)
                {
                    o.TargetBytes = kb * 1024;
                    foreach (var k in keys.Keys) k.Selected = false;
                }
                host.OptionsChanged();
            };
            Apply();
        }

        string TargetText => o.Mode == CompressMode.Quality ? "Q " + o.Quality : Images.FormatSize(o.TargetBytes);

        public override void UpdateDisplay(BigDisplay d, IList<FileItem> files)
        {
            var total = TotalSize(files);
            d.Set(files.Count == 0 ? "Big" : Images.FormatSize(total), files.Count == 0 ? "Small" : TargetText,
                files.Count == 0 ? "" : Count(files.Count, "picture"), o.Mode == CompressMode.TargetSize && files.Count > 0 ? "max, each" : "");
        }

        public override string ActionText(IList<FileItem> files) => files.Count == 0 ? "Compress" : $"Compress {Count(files.Count, "picture")}";

        public override List<Output> Run(IList<FileItem> files, IToolHost host, Report report)
        {
            o.OutputFolder = host.OutputFolder;
            return PerFile(files, report, f => new[] { Images.Compress(f.Path, o) });
        }
    }

    sealed class ResizeTool : Tool
    {
        public override string Id => "resize";
        public override string Title => "Resize";
        readonly ResizeOptions o = new ResizeOptions();
        static readonly string[] Labels = { "1080 px", "1280 px", "1920 px", "3840 px", "75%", "50%", "Square 1080", "Passport" };
        KeyGroup keys;
        Field custom;

        void Pick(int i)
        {
            switch (i)
            {
                case 0: case 1: case 2: case 3:
                    o.Mode = ResizeMode.LongSide; o.LongSide = new[] { 1080, 1280, 1920, 3840 }[i]; o.Label = o.LongSide + "px"; break;
                case 4: case 5:
                    o.Mode = ResizeMode.Percent; o.Percent = i == 4 ? 75 : 50; o.Label = o.Percent + "pct"; break;
                case 6:
                    o.Mode = ResizeMode.Exact; o.Width = o.Height = 1080; o.Label = "square"; break;
                case 7:
                    // 35 × 45 mm at 300 dpi, the usual passport/visa photo.
                    o.Mode = ResizeMode.Exact; o.Width = 413; o.Height = 531; o.Label = "passport"; break;
            }
        }

        public override void Build(Stack s, IToolHost host)
        {
            Pick(2);
            keys = s.Add(new KeyGroup(Labels, 4, 2));
            keys.Picked += i => { Pick(i); custom.Text = ""; host.OptionsChanged(); };
            var row = s.Add(new Row(Theme.Dp(40)));
            row.Add(new TLabel("Or a long side of", TextRole.Muted));
            custom = new Field("", "Long side in pixels") { Suffix = "px", Width = Theme.Dp(140) };
            row.Add(custom);
            custom.Box.TextChanged += (a, b) =>
            {
                if (int.TryParse(custom.Text.Trim(), out var px) && px >= 16 && px <= 20000)
                {
                    o.Mode = ResizeMode.LongSide; o.LongSide = px; o.Label = px + "px";
                    foreach (var k in keys.Keys) k.Selected = false;
                }
                host.OptionsChanged();
            };
            var grow = s.Add(new Switch("Don’t make small pictures bigger", true));
            grow.Toggled += () => o.NoEnlarge = grow.Checked;
            s.Add(Note("Square and Passport crop from the centre to fill the exact size. Passport is 35 × 45 mm at 300 dpi."));
            s.Add(host.MakeSaveToRow());
        }

        public override void UpdateDisplay(BigDisplay d, IList<FileItem> files)
        {
            string to = o.Mode == ResizeMode.LongSide ? o.LongSide + " px" : o.Mode == ResizeMode.Percent ? o.Percent + "%" : $"{o.Width}×{o.Height}";
            var first = files.FirstOrDefault(f => f.Note != null && f.Note.Contains("×"));
            var from = first != null ? first.Note.Split('·')[0].Trim().Replace(" × ", "×") : files.Count == 0 ? "Any" : "…";
            d.Set(from, to, files.Count > 1 ? $"+ {files.Count - 1} more" : "", o.Mode == ResizeMode.LongSide ? "long side" : "");
        }

        public override string ActionText(IList<FileItem> files) => files.Count == 0 ? "Resize" : $"Resize {Count(files.Count, "picture")}";

        public override List<Output> Run(IList<FileItem> files, IToolHost host, Report report)
        {
            o.OutputFolder = host.OutputFolder;
            return PerFile(files, report, f => new[] { Images.Resize(f.Path, o) });
        }
    }

    sealed class RemoveInfoTool : Tool
    {
        public override string Id => "removeinfo";
        public override string Title => "Remove photo info";

        public override void Build(Stack s, IToolHost host)
        {
            var card = s.Add(new Card());
            card.Add(Heading("What gets removed"));
            card.Add(Note("Location (GPS), camera and phone model, date taken, editing software, and hidden thumbnails. The picture itself doesn’t change, and the original file stays as it is."));
            s.Add(host.MakeSaveToRow());
        }

        public override void UpdateDisplay(BigDisplay d, IList<FileItem> files) =>
            d.Set("Info", "Clean", "GPS · camera · date", files.Count == 0 ? "" : Count(files.Count, "picture"));

        public override string ActionText(IList<FileItem> files) => files.Count == 0 ? "Remove info" : $"Remove info from {Count(files.Count, "picture")}";

        public override List<Output> Run(IList<FileItem> files, IToolHost host, Report report) =>
            PerFile(files, report, f => new[] { Images.RemoveInfo(f.Path, host.OutputFolder) });
    }

    sealed class ImagesToPdfTool : Tool
    {
        public override string Id => "imagestopdf";
        public override string Title => "Images → PDF";
        public override bool Reorder => true;
        public override string Tip => "Drag the pictures on the left to change the page order.";
        readonly ImagesToPdfOptions o = new ImagesToPdfOptions();
        Field name;

        public override void Build(Stack s, IToolHost host)
        {
            s.Add(Heading("Page size"));
            var size = s.Add(new KeyGroup(new[] { "A4", "Letter", "Fit picture" }, 3));
            size.Picked += i => o.Size = new[] { PageSizeMode.A4, PageSizeMode.Letter, PageSizeMode.Fit }[i];
            s.Add(Heading("Page direction"));
            var orient = s.Add(new KeyGroup(new[] { "Auto", "Portrait", "Landscape" }, 3));
            orient.Picked += i => o.Orientation = (PageOrientation)i;
            s.Add(Heading("Margin"));
            var margin = s.Add(new KeyGroup(new[] { "None", "Small", "Large" }, 3, 1));
            margin.Picked += i => o.MarginPt = new[] { 0, 20, 48 }[i];
            s.Add(Heading("File name"));
            name = s.Add(new Field("Images.pdf", "File name"));
            s.Add(host.MakeSaveToRow());
        }

        public override void InputsChanged(IList<FileItem> files)
        {
            if (name != null && files.Count > 0 && (name.Text == "Images.pdf" || name.Tag as string == name.Text))
            {
                name.Text = Path.GetFileNameWithoutExtension(files[0].Path) + ".pdf";
                name.Tag = name.Text;
            }
        }

        public override void UpdateDisplay(BigDisplay d, IList<FileItem> files) =>
            d.Set(files.Count == 0 ? "JPG" : files.Count.ToString(), "PDF", files.Count == 0 ? "PNG, HEIC…" : files.Count == 1 ? "picture" : "pictures", files.Count == 0 ? "" : Count(files.Count, "page"));

        public override string ActionText(IList<FileItem> files) => files.Count == 0 ? "Make PDF" : $"Make 1 PDF from {Count(files.Count, "picture")}";

        public override string Validate(IList<FileItem> files) => string.IsNullOrWhiteSpace(name.Text) ? "Give the PDF a file name." : null;

        public override List<Output> Run(IList<FileItem> files, IToolHost host, Report report)
        {
            o.OutputFolder = host.OutputFolder;
            o.FileName = name.Text.Trim();
            return Together(files, report, done => new[] { Pdf.FromImages(files.Select(f => f.Path).ToList(), o, done) });
        }
    }

    // ═════════════ PDF ═════════════

    sealed class MergePdfTool : Tool
    {
        public override string Id => "merge";
        public override string Title => "Merge PDF";
        public override bool ForPdf => true;
        public override bool Reorder => true;
        public override string Tip => "Drag the PDFs on the left to change their order.";
        Field name;
        bool bookmarks = true;

        public override void Build(Stack s, IToolHost host)
        {
            s.Add(Heading("File name"));
            name = s.Add(new Field("Merged.pdf", "File name"));
            var bm = s.Add(new Switch("Add a bookmark for each file", true));
            bm.Toggled += () => bookmarks = bm.Checked;
            s.Add(host.MakeSaveToRow());
            s.Add(Note("Pages keep their own size. Locked PDFs need their password removed first."));
        }

        public override void UpdateDisplay(BigDisplay d, IList<FileItem> files)
        {
            var pages = files.Sum(f => f.Pages);
            d.Set(files.Count == 0 ? "2+" : files.Count.ToString(), "1 PDF", files.Count == 1 ? "PDF" : "PDFs", pages > 0 ? Count(pages, "page") : "");
        }

        public override string ActionText(IList<FileItem> files) => files.Count < 2 ? "Merge PDFs" : $"Merge {files.Count} PDFs into one";

        public override string Validate(IList<FileItem> files) =>
            files.Count < 2 ? "Add at least two PDFs to merge." : string.IsNullOrWhiteSpace(name.Text) ? "Give the PDF a file name." : null;

        public override List<Output> Run(IList<FileItem> files, IToolHost host, Report report) =>
            Together(files, report, done => new[] { Pdf.Merge(files.Select(f => f.Path).ToList(), name.Text.Trim(), bookmarks, host.OutputFolder, done) });
    }

    sealed class SplitPdfTool : Tool
    {
        public override string Id => "split";
        public override string Title => "Split PDF";
        public override bool ForPdf => true;
        SplitMode mode = SplitMode.ExtractOne;
        Field ranges;
        Control rangeRow, rangeNote;

        public override void Build(Stack s, IToolHost host)
        {
            var keys = s.Add(new KeyGroup(new[] { "Pick pages", "One file per range", "Every page" }, 3));
            rangeRow = s.Add(Heading("Pages"));
            ranges = s.Add(new Field("1-2", "Pages to take"));
            ranges.Box.TextChanged += (a, b) => host.OptionsChanged();
            rangeNote = s.Add(Note("Use commas and dashes: 1-3, 7, 10- means pages 1 to 3, page 7, and page 10 to the end."));
            keys.Picked += i =>
            {
                mode = (SplitMode)i;
                rangeRow.Visible = ranges.Visible = rangeNote.Visible = mode != SplitMode.EveryPage;
                s.PerformLayout();
                host.OptionsChanged();
            };
            s.Add(host.MakeSaveToRow());
        }

        int Outputs(IList<FileItem> files)
        {
            var pages = files.FirstOrDefault()?.Pages ?? 0;
            if (mode == SplitMode.EveryPage) return pages;
            if (mode == SplitMode.ExtractOne) return 1;
            try { return Pdf.ParseRanges(ranges.Text, Math.Max(1, pages)).Count; } catch { return 0; }
        }

        public override void UpdateDisplay(BigDisplay d, IList<FileItem> files)
        {
            var pages = files.FirstOrDefault()?.Pages ?? 0;
            var n = Outputs(files);
            d.Set(pages > 0 ? pages.ToString() : "1 PDF", n > 0 && files.Count > 0 ? n.ToString() : "Parts", pages > 0 ? "pages" : "", n > 0 && files.Count > 0 ? (n == 1 ? "PDF" : "PDFs") : "");
        }

        public override string ActionText(IList<FileItem> files) => mode == SplitMode.EveryPage ? "Split into single pages" : mode == SplitMode.PerRange ? "Split into files" : "Save these pages";

        public override string Validate(IList<FileItem> files)
        {
            if (mode == SplitMode.EveryPage) return null;
            try
            {
                foreach (var f in files) Pdf.ParseRanges(ranges.Text, f.Pages > 0 ? f.Pages : 99999);
                return null;
            }
            catch (FormatException ex)
            {
                var bad = files.FirstOrDefault(f => { try { Pdf.ParseRanges(ranges.Text, f.Pages > 0 ? f.Pages : 99999); return false; } catch { return true; } });
                return (files.Count > 1 && bad != null ? bad.Name + ": " : "") + ex.Message;
            }
        }

        public override List<Output> Run(IList<FileItem> files, IToolHost host, Report report)
        {
            var outs = PerFile(files, report, f => Pdf.Split(f.Path, mode, ranges.Text, host.OutputFolder, p => report(f, ItemState.Working, p / 100f)));
            foreach (var o in outs) o.Source = null; // parts aren't "smaller versions" of the original
            return outs;
        }
    }

    sealed class CompressPdfTool : Tool
    {
        public override string Id => "compresspdf";
        public override string Title => "Compress PDF";
        public override bool ForPdf => true;
        PdfSqueeze level = PdfSqueeze.Balanced;
        static readonly string[] Notes =
        {
            "Light: pictures stay sharp enough to print. Smallest change.",
            "Balanced: good for email and uploads. Pictures stay clear on screen.",
            "Strong: smallest file. Pictures get softer, text stays sharp.",
        };

        public override void Build(Stack s, IToolHost host)
        {
            var keys = s.Add(new KeyGroup(new[] { "Light", "Balanced", "Strong" }, 3, 1));
            var note = s.Add(Note(Notes[1]));
            keys.Picked += i => { level = (PdfSqueeze)i; note.Text = Notes[i]; s.PerformLayout(); host.OptionsChanged(); };
            var card = s.Add(new Card());
            card.Add(Heading("What to expect"));
            card.Add(Note("Scanned and photo-heavy PDFs often shrink by half or more. PDFs that are mostly text are already small and change less. Text always stays sharp and searchable."));
            s.Add(host.MakeSaveToRow());
        }

        public override void UpdateDisplay(BigDisplay d, IList<FileItem> files) =>
            d.Set(files.Count == 0 ? "Big" : Images.FormatSize(TotalSize(files)), "Small", files.Count == 0 ? "PDF" : Count(files.Count, "PDF"), level.ToString().ToLowerInvariant());

        public override string ActionText(IList<FileItem> files) => files.Count == 0 ? "Compress PDF" : $"Compress {Count(files.Count, "PDF")}";

        public override List<Output> Run(IList<FileItem> files, IToolHost host, Report report) =>
            PerFile(files, report, f => new[] { Pdf.Compress(f.Path, level, host.OutputFolder, p => report(f, ItemState.Working, p / 100f)) });
    }

    sealed class PdfToImagesTool : Tool
    {
        public override string Id => "pdftoimages";
        public override string Title => "PDF to images";
        public override bool ForPdf => true;
        bool png;
        int dpi = 150;

        public override void Build(Stack s, IToolHost host)
        {
            var fmt = s.Add(new KeyGroup(new[] { "JPG", "PNG" }, 2));
            fmt.Picked += i => { png = i == 1; host.OptionsChanged(); };
            s.Add(Heading("Sharpness"));
            var q = s.Add(new KeyGroup(new[] { "Screen · 96 dpi", "Standard · 150 dpi", "Print · 300 dpi" }, 3, 1));
            q.Picked += i => dpi = new[] { 96, 150, 300 }[i];
            s.Add(Note("Each page becomes one picture. PDFs with more than one page get their own folder."));
            s.Add(host.MakeSaveToRow());
        }

        public override void UpdateDisplay(BigDisplay d, IList<FileItem> files)
        {
            var pages = files.Sum(f => f.Pages);
            d.Set("PDF", png ? "PNG" : "JPG", files.Count == 0 ? "" : Count(files.Count, "file"), pages > 0 ? Count(pages, "picture") : "");
        }

        public override string ActionText(IList<FileItem> files) => $"Save pages as {(png ? "PNG" : "JPG")}";

        public override List<Output> Run(IList<FileItem> files, IToolHost host, Report report)
        {
            var outs = PerFile(files, report, f => Pdf.ToImages(f.Path, png, dpi, host.OutputFolder, p => report(f, ItemState.Working, p / 100f)));
            foreach (var o in outs) o.Source = null; // a page picture isn't a smaller copy of the PDF
            return outs;
        }
    }

    sealed class ProtectPdfTool : Tool
    {
        public override string Id => "protect";
        public override string Title => "Protect PDF";
        public override bool ForPdf => true;
        Field pass, confirm;

        public override void Build(Stack s, IToolHost host)
        {
            s.Add(Heading("Password"));
            pass = s.Add(new Field("", "Password"));
            pass.Box.UseSystemPasswordChar = true;
            s.Add(Heading("Type it again"));
            confirm = s.Add(new Field("", "Confirm password"));
            confirm.Box.UseSystemPasswordChar = true;
            var show = s.Add(new Switch("Show password"));
            show.Toggled += () => pass.Box.UseSystemPasswordChar = confirm.Box.UseSystemPasswordChar = !show.Checked;
            var card = s.Add(new Card());
            card.Add(Heading("Keep this password safe"));
            card.Add(Note("The PDF is locked with AES-256. Without the password nobody can open it, and there is no way to get it back."));
            s.Add(host.MakeSaveToRow());
        }

        public override void UpdateDisplay(BigDisplay d, IList<FileItem> files) =>
            d.Set("PDF", "Locked", files.Count == 0 ? "" : Count(files.Count, "file"), "AES-256");

        public override string ActionText(IList<FileItem> files) => files.Count <= 1 ? "Lock PDF" : $"Lock {files.Count} PDFs";

        public override string Validate(IList<FileItem> files)
        {
            if (pass.Text.Length == 0) return "Type a password.";
            if (pass.Text.Length < 4) return "Use at least 4 characters for the password.";
            if (pass.Text != confirm.Text) return "The two passwords don’t match.";
            return null;
        }

        public override List<Output> Run(IList<FileItem> files, IToolHost host, Report report)
        {
            var pw = pass.Text;
            return PerFile(files, report, f => new[] { Pdf.Protect(f.Path, pw, host.OutputFolder) });
        }
    }

    sealed class RotateTool : Tool
    {
        public override string Id => "rotate";
        public override string Title => "Rotate & reorder";
        public override bool ForPdf => true;
        public override bool SingleInput => true;
        public override bool ShowDisplay => false;
        public override string Tip => "Works on the first PDF in the list. Click a page to pick it; Ctrl+click picks more.";
        PageGrid grid;
        string loaded;
        PillButton[] actions;

        public override void Build(Stack s, IToolHost host)
        {
            var bar = s.Add(new Row(Theme.Dp(38)));
            PillButton B(string t, Action a)
            {
                var b = new PillButton(t) { Height = Theme.Dp(36) };
                b.Width = b.PreferredWidth(Theme.Dp(14));
                b.Click += (x, y) => { a(); grid.Invalidate(); host.OptionsChanged(); };
                return bar.Add(b);
            }
            actions = new[]
            {
                B("Turn left", () => Turn(-90)), B("Turn right", () => Turn(90)),
                B("← Earlier", () => Move(-1)), B("Later →", () => Move(1)), B("Delete", Delete),
            };
            var all = new PillButton("Select all", PillKind.Ghost) { Height = Theme.Dp(36) };
            all.Width = all.PreferredWidth(Theme.Dp(12));
            all.Click += (x, y) => { var on = !grid.Pages.All(p => p.Selected); foreach (var p in grid.Pages) p.Selected = on; grid.Invalidate(); };
            bar.Add(all, alignRight: true);
            grid = s.Add(new PageGrid());
            s.Fill = grid;
            s.Add(host.MakeSaveToRow());
        }

        IEnumerable<PageGrid.Page> Picked => grid.Pages.Where(p => p.Selected);
        void Turn(int deg) { foreach (var p in Picked) p.Rotation += deg; }

        void Move(int dir)
        {
            var list = grid.Pages;
            var idx = list.Select((p, i) => p.Selected ? i : -1).Where(i => i >= 0).ToList();
            if (dir > 0) idx.Reverse();
            foreach (var i in idx)
            {
                var j = i + dir;
                if (j < 0 || j >= list.Count || list[j].Selected) continue;
                var t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }

        void Delete()
        {
            if (grid.Pages.Count(p => !p.Selected) == 0) return; // a PDF needs at least one page
            grid.Pages.RemoveAll(p => p.Selected);
            grid.ClampScroll();
        }

        public override void InputsChanged(IList<FileItem> files)
        {
            var path = files.FirstOrDefault()?.Path;
            if (path == loaded) return;
            loaded = path;
            foreach (var p in grid.Pages) p.Thumb?.Dispose();
            grid.Pages.Clear();
            grid.Invalidate();
            if (path == null) return;
            var g = grid;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using (var doc = new Pdfium.Doc(path))
                    {
                        var pages = Enumerable.Range(0, doc.PageCount).Select(i => new PageGrid.Page { Source = i }).ToList();
                        g.BeginInvoke(new Action(() => { if (loaded == path) { g.Pages.AddRange(pages); g.Invalidate(); } }));
                        foreach (var pg in pages)
                        {
                            if (loaded != path) return;
                            var bmp = doc.Render(pg.Source, 0, Theme.Dp(200));
                            g.BeginInvoke(new Action(() => { pg.Thumb = bmp; g.Invalidate(); }));
                        }
                    }
                }
                catch { }
            });
        }

        public override void UpdateDisplay(BigDisplay d, IList<FileItem> files) { }

        public override string ActionText(IList<FileItem> files) => "Save the new PDF";

        public override string Validate(IList<FileItem> files) => grid.Pages.Count == 0 ? "Wait for the pages to load, or add a PDF." : null;

        public override List<Output> Run(IList<FileItem> files, IToolHost host, Report report)
        {
            var order = (List<(int, int)>)grid.Invoke(new Func<List<(int, int)>>(() => grid.Pages.Select(p => (p.Source, p.Rotation)).ToList()));
            var f = files[0];
            report(f, ItemState.Working, 0.4f);
            try
            {
                var o = Pdf.Rearrange(f.Path, order, host.OutputFolder);
                report(f, ItemState.Done, 1);
                return new List<Output> { new Output(o, null) };
            }
            catch (Exception ex) { report(f, ItemState.Failed, 0, Friendly(ex)); return new List<Output>(); }
        }
    }

    public static class ToolCatalog
    {
        public static List<Tool> All() => new List<Tool>
        {
            new ConvertTool(), new CompressImageTool(), new ResizeTool(), new ImagesToPdfTool(),
            new MergePdfTool(), new SplitPdfTool(), new CompressPdfTool(),
            new PdfToImagesTool(), new RotateTool(), new ProtectPdfTool(), new RemoveInfoTool(),
        };

        /// <summary>Shown as pills in the top bar; the rest are under "More".</summary>
        public static readonly string[] Pinned = { "convert", "compress", "resize", "imagestopdf", "merge", "split", "compresspdf" };
    }
}
