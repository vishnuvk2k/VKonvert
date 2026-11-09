using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ImageMagick;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace VKonvert.Engine
{
    public enum PageSizeMode { Fit, A4, Letter }
    public enum PageOrientation { Auto, Portrait, Landscape }
    public enum SplitMode { ExtractOne, PerRange, EveryPage }
    public enum PdfSqueeze { Light, Balanced, Strong }

    public sealed class ImagesToPdfOptions
    {
        public PageSizeMode Size = PageSizeMode.A4;
        public PageOrientation Orientation = PageOrientation.Auto;
        public double MarginPt = 20; // ~7 mm
        public string FileName = "Images.pdf";
        public string OutputFolder;
    }

    public static class Pdf
    {
        static readonly XSize A4 = new XSize(595.28, 841.89), Letter = new XSize(612, 792);

        static PdfDocument Open(string path, PdfDocumentOpenMode mode)
        {
            try
            {
                return PdfReader.Open(path, mode);
            }
            catch (PdfReaderException ex) when (ex.Message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidDataException("This PDF is locked with a password. Unlock it first, then try again.");
            }
            catch (Exception ex) when (!(ex is IOException) || ex is InvalidDataException)
            {
                throw new InvalidDataException("This file isn’t a PDF VKonvert can read (" + ex.Message.TrimEnd('.') + ").");
            }
        }

        public static int PageCount(string path)
        {
            using (var d = new Pdfium.Doc(path)) return d.PageCount;
        }

        static string Output(string folder, string firstInput, string fileName)
        {
            var dir = string.IsNullOrEmpty(folder) ? Path.GetDirectoryName(firstInput) : folder;
            if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) fileName += ".pdf";
            foreach (var c in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(c, '-');
            return Images.UniquePath(Path.Combine(dir, fileName));
        }

        static void SaveDoc(PdfDocument doc, string path)
        {
            doc.Options.CompressContentStreams = true;
            doc.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
            doc.Info.Creator = "VKonvert";
            doc.Save(path);
        }

        // ───────────── Images → PDF ─────────────

        public static string FromImages(IList<string> images, ImagesToPdfOptions o, Action<int> progress)
        {
            using (var doc = new PdfDocument())
            {
                for (int i = 0; i < images.Count; i++)
                {
                    byte[] jpeg; int pw, ph;
                    using (var img = Images.Load(images[i]))
                    {
                        img.AutoOrient();
                        if (img.HasAlpha) { img.ColorAlpha(MagickColors.White); img.Alpha(AlphaAction.Off); }
                        // Very large photos are capped so the PDF stays a sensible size (still ~300 dpi on A4).
                        if (Math.Max(img.Width, img.Height) > 3600) img.Resize(new MagickGeometry(3600, 3600));
                        img.Quality = 88;
                        img.Strip();
                        jpeg = img.ToByteArray(MagickFormat.Jpeg);
                        pw = (int)img.Width; ph = (int)img.Height;
                    }

                    var page = doc.AddPage();
                    XSize size;
                    var landscape = o.Orientation == PageOrientation.Landscape || (o.Orientation == PageOrientation.Auto && pw > ph);
                    if (o.Size == PageSizeMode.Fit)
                    {
                        // Page takes the picture's shape; the long side matches A4's long side.
                        var s = A4.Height / Math.Max(pw, ph);
                        size = new XSize(pw * s + 2 * o.MarginPt, ph * s + 2 * o.MarginPt);
                    }
                    else
                    {
                        var basis = o.Size == PageSizeMode.A4 ? A4 : Letter;
                        size = landscape ? new XSize(basis.Height, basis.Width) : basis;
                    }
                    page.Width = XUnit.FromPoint(size.Width);
                    page.Height = XUnit.FromPoint(size.Height);

                    using (var ms = new MemoryStream(jpeg, 0, jpeg.Length, false, true)) // PDFsharp reads the buffer directly
                    using (var ximg = XImage.FromStream(ms))
                    using (var gfx = XGraphics.FromPdfPage(page))
                    {
                        var box = new XRect(o.MarginPt, o.MarginPt, size.Width - 2 * o.MarginPt, size.Height - 2 * o.MarginPt);
                        var sc = Math.Min(box.Width / pw, box.Height / ph);
                        double w = pw * sc, h = ph * sc;
                        gfx.DrawImage(ximg, box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
                    }
                    progress?.Invoke(i);
                }
                var outPath = Output(o.OutputFolder, images[0], o.FileName);
                SaveDoc(doc, outPath);
                return outPath;
            }
        }

        // ───────────── Merge ─────────────

        public static string Merge(IList<string> pdfs, string fileName, bool bookmarks, string folder, Action<int> progress)
        {
            using (var outDoc = new PdfDocument())
            {
                for (int i = 0; i < pdfs.Count; i++)
                {
                    using (var src = Open(pdfs[i], PdfDocumentOpenMode.Import))
                    {
                        PdfPage first = null;
                        foreach (var page in src.Pages)
                        {
                            var added = outDoc.AddPage(page);
                            if (first == null) first = added;
                        }
                        if (bookmarks && first != null)
                            outDoc.Outlines.Add(Path.GetFileNameWithoutExtension(pdfs[i]), first, true);
                    }
                    progress?.Invoke(i);
                }
                var outPath = Output(folder, pdfs[0], fileName);
                SaveDoc(outDoc, outPath);
                return outPath;
            }
        }

        // ───────────── Split ─────────────

        /// <summary>Parses "1-3, 7, 10-" into 0-based page ranges. Throws with a clear message on bad input.</summary>
        public static List<int[]> ParseRanges(string text, int pageCount)
        {
            var result = new List<int[]>();
            foreach (var raw in (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var part = raw.Trim().Replace('–', '-').Replace('—', '-');
                if (part.Length == 0) continue;
                int a, b;
                var dash = part.IndexOf('-');
                if (dash < 0)
                {
                    if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out a)) throw Bad(part);
                    b = a;
                }
                else
                {
                    var left = part.Substring(0, dash).Trim();
                    var right = part.Substring(dash + 1).Trim();
                    a = left.Length == 0 ? 1 : int.TryParse(left, out var l) ? l : throw Bad(part);
                    b = right.Length == 0 ? pageCount : int.TryParse(right, out var r) ? r : throw Bad(part);
                }
                if (a < 1 || b < 1 || a > pageCount || b > pageCount)
                    throw new FormatException($"“{part}” is outside this PDF, which has {pageCount} page{(pageCount == 1 ? "" : "s")}.");
                if (b < a) { var t = a; a = b; b = t; }
                result.Add(Enumerable.Range(a - 1, b - a + 1).ToArray());
            }
            if (result.Count == 0) throw new FormatException("Type the pages you want, for example 1-3, 7.");
            return result;
        }

        static FormatException Bad(string part) => new FormatException($"“{part}” isn’t a page number or range. Use something like 1-3, 7.");

        public static List<string> Split(string pdf, SplitMode mode, string ranges, string folder, Action<int> progress)
        {
            var outputs = new List<string>();
            var name = Path.GetFileNameWithoutExtension(pdf);
            var dir = string.IsNullOrEmpty(folder) ? Path.GetDirectoryName(pdf) : folder;
            using (var src = Open(pdf, PdfDocumentOpenMode.Import))
            {
                var n = src.PageCount;
                List<int[]> groups;
                if (mode == SplitMode.EveryPage) groups = Enumerable.Range(0, n).Select(i => new[] { i }).ToList();
                else
                {
                    groups = ParseRanges(ranges, n);
                    if (mode == SplitMode.ExtractOne) groups = new List<int[]> { groups.SelectMany(g => g).ToArray() };
                }
                for (int gi = 0; gi < groups.Count; gi++)
                {
                    using (var outDoc = new PdfDocument())
                    {
                        foreach (var idx in groups[gi]) outDoc.AddPage(src.Pages[idx]);
                        string label;
                        if (mode == SplitMode.EveryPage) label = $"page {groups[gi][0] + 1}";
                        else if (mode == SplitMode.ExtractOne) label = "pages " + ranges.Replace(" ", "");
                        else label = groups[gi].Length == 1 ? $"page {groups[gi][0] + 1}" : $"pages {groups[gi][0] + 1}-{groups[gi].Last() + 1}";
                        var outPath = Output(dir, pdf, $"{name} ({label}).pdf");
                        SaveDoc(outDoc, outPath);
                        outputs.Add(outPath);
                    }
                    progress?.Invoke(gi * 100 / groups.Count);
                }
            }
            return outputs;
        }

        // ───────────── Rotate & reorder ─────────────

        /// <summary>Writes a copy with pages in the given order, each turned by the given degrees.</summary>
        public static string Rearrange(string pdf, IList<(int source, int rotate)> pages, string folder)
        {
            using (var src = Open(pdf, PdfDocumentOpenMode.Import))
            using (var outDoc = new PdfDocument())
            {
                foreach (var (source, rotate) in pages)
                {
                    var p = outDoc.AddPage(src.Pages[source]);
                    p.Rotate = (((p.Rotate + rotate) % 360) + 360) % 360;
                }
                var outPath = Output(folder, pdf, Path.GetFileNameWithoutExtension(pdf) + "-edited.pdf");
                SaveDoc(outDoc, outPath);
                return outPath;
            }
        }

        // ───────────── Protect ─────────────

        public static string Protect(string pdf, string password, string folder)
        {
            using (var doc = Open(pdf, PdfDocumentOpenMode.Modify))
            {
                doc.SecuritySettings.UserPassword = password;
                doc.SecuritySettings.OwnerPassword = password;
                doc.SecurityHandler.SetEncryptionToV5(); // AES-256
                var outPath = Output(folder, pdf, Path.GetFileNameWithoutExtension(pdf) + "-protected.pdf");
                SaveDoc(doc, outPath);
                return outPath;
            }
        }

        // ───────────── PDF → images ─────────────

        public static List<string> ToImages(string pdf, bool png, int dpi, string folder, Action<int> progress)
        {
            var name = Path.GetFileNameWithoutExtension(pdf);
            var dir = string.IsNullOrEmpty(folder) ? Path.GetDirectoryName(pdf) : folder;
            var outputs = new List<string>();
            using (var doc = new Pdfium.Doc(pdf))
            {
                var target = doc.PageCount > 1 ? Images.UniquePath(Path.Combine(dir, name + " pages")) : dir;
                Directory.CreateDirectory(target);
                var digits = doc.PageCount.ToString().Length;
                for (int i = 0; i < doc.PageCount; i++)
                {
                    using (var bmp = doc.Render(i, dpi, 0, true))
                    {
                        var file = Images.UniquePath(Path.Combine(target,
                            (doc.PageCount > 1 ? $"{name}-{(i + 1).ToString().PadLeft(digits, '0')}" : name) + (png ? ".png" : ".jpg")));
                        if (png) bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                        else
                        {
                            using (var ms = new MemoryStream())
                            {
                                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                                using (var img = new MagickImage(ms.ToArray())) { img.Quality = 90; img.Write(file, MagickFormat.Jpeg); }
                            }
                        }
                        outputs.Add(file);
                    }
                    progress?.Invoke((i + 1) * 100 / doc.PageCount);
                }
            }
            return outputs;
        }

        // ───────────── Compress ─────────────

        /// <summary>
        /// Re-saves the pictures inside a PDF smaller (fewer pixels, JPEG quality) and recompresses the rest.
        /// Text and drawings are kept as they are, so they stay sharp and searchable.
        /// </summary>
        public static string Compress(string pdf, PdfSqueeze level, string folder, Action<int> progress)
        {
            int maxSide, quality;
            switch (level)
            {
                case PdfSqueeze.Light: maxSide = 2400; quality = 80; break;
                case PdfSqueeze.Strong: maxSide = 1200; quality = 45; break;
                default: maxSide = 1700; quality = 65; break;
            }

            using (var doc = Open(pdf, PdfDocumentOpenMode.Modify))
            {
                var all = doc.Internals.GetAllObjects();
                // Alpha masks are left alone: blurring them would show as halos.
                var masks = new HashSet<PdfObject>();
                foreach (var o in all.OfType<PdfDictionary>())
                    if (o.Elements["/SMask"] is PdfReference r) masks.Add(r.Value);

                var images = all.OfType<PdfDictionary>()
                    .Where(d => d.Stream != null && d.Elements.GetName("/Subtype") == "/Image" && !masks.Contains(d))
                    .ToList();
                for (int i = 0; i < images.Count; i++)
                {
                    try { Squeeze(images[i], maxSide, quality); }
                    catch { /* an image we can't safely re-encode stays as it was */ }
                    progress?.Invoke((i + 1) * 100 / Math.Max(1, images.Count));
                }
                var outPath = Output(folder, pdf, Path.GetFileNameWithoutExtension(pdf) + "-compressed.pdf");
                SaveDoc(doc, outPath);
                // Nothing to squeeze (a text-only PDF): hand back the original rather than a bigger copy.
                if (new FileInfo(outPath).Length >= new FileInfo(pdf).Length) File.Copy(pdf, outPath, true);
                return outPath;
            }
        }

        static void Squeeze(PdfDictionary img, int maxSide, int quality)
        {
            var e = img.Elements;
            if (e.GetBoolean("/ImageMask") || e.ContainsKey("/Decode") || e.ContainsKey("/Mask")) return;
            if (e.GetInteger("/BitsPerComponent") != 8) return;
            var oldBytes = img.Stream.Value;
            if (oldBytes == null || oldBytes.Length < 20 * 1024) return; // small images aren't worth it

            var filters = FilterNames(e["/Filter"]);
            if (filters == null) return;
            var last = filters.Count > 0 ? filters[filters.Count - 1] : null;
            var outer = last == "/DCTDecode" ? filters.Take(filters.Count - 1).ToList() : filters;
            // Undo transport encodings (ASCII85, Flate…) to get at the JPEG or the raw pixels.
            if (e.ContainsKey("/DecodeParms") && outer.Any(f => f == "/FlateDecode" || f == "/LZWDecode")) return; // predictor rows; rare for photos
            var data = oldBytes;
            foreach (var f in outer)
            {
                if (f != "/ASCII85Decode" && f != "/ASCIIHexDecode" && f != "/FlateDecode" && f != "/LZWDecode") return;
                data = PdfSharp.Pdf.Filters.Filtering.Decode(data, f);
                if (data == null) return;
            }

            int w = e.GetInteger("/Width"), h = e.GetInteger("/Height");
            var comps = Components(e["/ColorSpace"]);
            MagickImage pic;
            if (last == "/DCTDecode")
            {
                pic = new MagickImage(data);
                if (pic.ColorSpace == ColorSpace.CMYK) { pic.Dispose(); return; }
                comps = pic.ChannelCount >= 3 ? 3 : 1;
            }
            else
            {
                if (comps != 1 && comps != 3) return;
                if (data.Length < w * h * comps) return;
                var settings = new PixelReadSettings((uint)w, (uint)h, StorageType.Char, comps == 3 ? "RGB" : "R");
                pic = new MagickImage();
                pic.ReadPixels(data, settings);
                if (comps == 1) pic.ColorType = ColorType.Grayscale;
            }

            using (pic)
            {
                if (Math.Max(pic.Width, pic.Height) > maxSide) pic.Resize(new MagickGeometry((uint)maxSide, (uint)maxSide));
                pic.Strip();
                pic.Quality = (uint)quality;
                if (comps == 1) pic.ColorType = ColorType.Grayscale;
                var jpeg = pic.ToByteArray(MagickFormat.Jpeg);
                if (jpeg.Length >= oldBytes.Length * 0.9) return; // not worth the quality loss

                img.Stream.Value = jpeg;
                e.SetName("/Filter", "/DCTDecode");
                e.Remove("/DecodeParms");
                e.SetInteger("/Width", (int)pic.Width);
                e.SetInteger("/Height", (int)pic.Height);
                e.SetInteger("/BitsPerComponent", 8);
                if (!(e["/ColorSpace"] is PdfReference) && !(e["/ColorSpace"] is PdfArray))
                    e.SetName("/ColorSpace", comps == 1 ? "/DeviceGray" : "/DeviceRGB");
                e.SetInteger("/Length", jpeg.Length);
            }
        }

        /// <summary>The image's filters in order, empty for none, or null when they can't be read.</summary>
        static List<string> FilterNames(PdfItem f)
        {
            if (f == null) return new List<string>();
            if (f is PdfReference r) f = r.Value;
            if (f is PdfName n) return new List<string> { n.Value };
            if (f is PdfArray a)
            {
                var list = new List<string>();
                foreach (var item in a.Elements)
                {
                    if (item is PdfName name) list.Add(name.Value);
                    else return null;
                }
                return list;
            }
            return null;
        }

        static int Components(PdfItem cs)
        {
            if (cs is PdfReference r) cs = r.Value;
            if (cs is PdfName n) return n.Value == "/DeviceRGB" ? 3 : n.Value == "/DeviceGray" ? 1 : 0;
            if (cs is PdfArray a && a.Elements.Count == 2 && a.Elements[0] is PdfName kind && kind.Value == "/ICCBased")
            {
                var stream = (a.Elements[1] as PdfReference)?.Value as PdfDictionary;
                var count = stream?.Elements.GetInteger("/N") ?? 0;
                return count == 3 || count == 1 ? count : 0;
            }
            return 0;
        }
    }
}
