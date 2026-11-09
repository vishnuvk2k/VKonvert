using System;
using System.Drawing;
using System.IO;
using System.Linq;
using ImageMagick;

namespace VKonvert.Engine
{
    public sealed class TargetFormat
    {
        public string Name { get; }
        public string Extension { get; }
        public MagickFormat Format { get; }
        public bool KeepsTransparency { get; }
        public bool HasQuality { get; }

        public TargetFormat(string name, string ext, MagickFormat format, bool keepsTransparency, bool hasQuality)
        {
            Name = name; Extension = ext; Format = format;
            KeepsTransparency = keepsTransparency; HasQuality = hasQuality;
        }

        public override string ToString() => Name;
    }

    public static class Formats
    {
        public static readonly TargetFormat[] Targets =
        {
            new TargetFormat("JPG",  ".jpg",  MagickFormat.Jpeg, false, true),
            new TargetFormat("PNG",  ".png",  MagickFormat.Png,  true,  false),
            new TargetFormat("WebP", ".webp", MagickFormat.WebP, true,  true),
            new TargetFormat("AVIF", ".avif", MagickFormat.Avif, true,  true),
            new TargetFormat("HEIC", ".heic", MagickFormat.Heic, true,  false),
            new TargetFormat("PDF",  ".pdf",  MagickFormat.Pdf,  false, true),
            new TargetFormat("ICO",  ".ico",  MagickFormat.Ico,  true,  false),
            new TargetFormat("GIF",  ".gif",  MagickFormat.Gif,  true,  false),
            new TargetFormat("TIFF", ".tiff", MagickFormat.Tiff, true,  false),
            new TargetFormat("BMP",  ".bmp",  MagickFormat.Bmp3, false, false),
        };

        public static readonly string[] ImageExtensions =
        {
            ".png", ".jpg", ".jpeg", ".jfif", ".webp", ".bmp", ".gif", ".tif", ".tiff",
            ".ico", ".heic", ".heif", ".avif", ".tga", ".psd",
        };

        public static TargetFormat Get(string name) =>
            Targets.First(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        public static TargetFormat TryGet(string name) =>
            Targets.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
        public static bool IsPdf(string path) => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

        /// <summary>Short name of a file's format: "JPG", "PNG", "HEIC"…</summary>
        public static string NameOf(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".jpg": case ".jpeg": case ".jfif": return "JPG";
                case ".tif": case ".tiff": return "TIFF";
                case ".heif": return "HEIC";
                case ".webp": return "WebP";
                default: return Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            }
        }
    }

    public sealed class ConvertOptions
    {
        public TargetFormat Target = Formats.Get("JPG");
        public int Quality = 85;
        public Color Background = Color.White;
        public string OutputFolder;
    }

    public enum CompressMode { Quality, TargetSize }

    public sealed class CompressOptions
    {
        public CompressMode Mode = CompressMode.TargetSize;
        public int Quality = 70;
        public long TargetBytes = 500 * 1024;
        public bool RemoveInfo;
        public string OutputFolder;
    }

    public enum ResizeMode { LongSide, Percent, Exact }

    public sealed class ResizeOptions
    {
        public ResizeMode Mode = ResizeMode.LongSide;
        public int LongSide = 1920;
        public int Percent = 50;
        public int Width = 1080, Height = 1080; // Exact: crops to fill
        public bool NoEnlarge = true;
        public string Label = "1920px";
        public string OutputFolder;
    }

    public static class Images
    {
        // HEIC goes through Windows' codecs (see HeicWindows); null where those aren't available.
        public static Action<byte[], string> HeicWriter;
        public static Func<string, byte[]> HeicReader;

        public static MagickImage Load(string path)
        {
            try
            {
                return new MagickImage(path);
            }
            catch (MagickException) when (HeicReader != null && Formats.NameOf(path) == "HEIC")
            {
                return new MagickImage(HeicReader(path));
            }
        }

        public static Size Dimensions(string path)
        {
            try
            {
                var info = new MagickImageInfo(path);
                return new Size((int)info.Width, (int)info.Height);
            }
            catch
            {
                using (var img = Load(path)) return new Size((int)img.Width, (int)img.Height);
            }
        }

        /// <summary>Small preview for the file cards.</summary>
        public static Bitmap Thumbnail(string path, int maxSide)
        {
            using (var img = Load(path))
            {
                img.AutoOrient();
                img.Thumbnail(new MagickGeometry((uint)maxSide, (uint)maxSide));
                using (var ms = new MemoryStream(img.ToByteArray(MagickFormat.Png)))
                    return new Bitmap(ms);
            }
        }

        public static string Convert(string input, ConvertOptions o)
        {
            using (var img = Load(input))
            {
                img.AutoOrient();
                if (!o.Target.KeepsTransparency) Flatten(img, o.Background);
                if (o.Target.HasQuality) img.Quality = (uint)Clamp(o.Quality, 1, 100);
                if (o.Target.Format == MagickFormat.Ico) PrepareIcon(img);
                if (o.Target.Format == MagickFormat.Pdf) img.Settings.Compression = CompressionMethod.JPEG;
                if (o.Target.Format == MagickFormat.Tiff) img.Settings.Compression = CompressionMethod.LZW;

                var outPath = UniquePath(OutPath(input, o.OutputFolder, "", o.Target.Extension));
                Save(img, outPath, o.Target.Format);
                return outPath;
            }
        }

        /// <summary>Makes an image smaller, by quality or until it fits under a size. Keeps its format where that helps.</summary>
        public static string Compress(string input, CompressOptions o)
        {
            using (var img = Load(input))
            {
                img.AutoOrient();
                if (o.RemoveInfo) StripInfo(img);

                // Formats without useful compression become JPG (or PNG when they have see-through parts).
                var name = Formats.NameOf(input);
                MagickFormat fmt; string ext;
                switch (name)
                {
                    case "JPG": fmt = MagickFormat.Jpeg; ext = Path.GetExtension(input); break;
                    case "WebP": fmt = MagickFormat.WebP; ext = ".webp"; break;
                    case "AVIF": fmt = MagickFormat.Avif; ext = ".avif"; break;
                    case "PNG": fmt = MagickFormat.Png; ext = ".png"; break;
                    case "GIF": fmt = MagickFormat.Gif; ext = ".gif"; break;
                    default:
                        if (img.HasAlpha && !img.IsOpaque) { fmt = MagickFormat.Png; ext = ".png"; }
                        else { fmt = MagickFormat.Jpeg; ext = ".jpg"; }
                        break;
                }

                byte[] bytes = o.Mode == CompressMode.Quality ? EncodeQuality(img, fmt, o.Quality) : EncodeUnder(img, fmt, o.TargetBytes);
                var outPath = UniquePath(OutPath(input, o.OutputFolder, "-compressed", ext));
                // Already small (or already well compressed): never hand back a bigger file.
                var original = new FileInfo(input).Length;
                var sameFormat = string.Equals(Path.GetExtension(input), ext, StringComparison.OrdinalIgnoreCase);
                if (sameFormat && !o.RemoveInfo && bytes.LongLength >= original) File.Copy(input, outPath);
                else File.WriteAllBytes(outPath, bytes);
                return outPath;
            }
        }

        static bool Lossy(MagickFormat f) => f == MagickFormat.Jpeg || f == MagickFormat.WebP || f == MagickFormat.Avif;

        static byte[] EncodeQuality(MagickImage img, MagickFormat fmt, int quality)
        {
            using (var copy = (MagickImage)img.Clone())
            {
                if (Lossy(fmt))
                {
                    if (fmt == MagickFormat.Jpeg) Flatten(copy, Color.White);
                    copy.Quality = (uint)Clamp(quality, 5, 100);
                    if (fmt == MagickFormat.Jpeg) copy.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", quality < 90 ? "4:2:0" : "4:4:4");
                }
                else ReduceColors(copy, quality);
                copy.Strip(); // thumbnails and profiles are dead weight in a compressed copy
                return copy.ToByteArray(fmt);
            }
        }

        // PNG/GIF have no quality setting: fewer colours is what makes them smaller.
        static void ReduceColors(MagickImage img, int quality)
        {
            var colors = quality >= 90 ? 0 : quality >= 70 ? 256 : quality >= 40 ? 128 : 64;
            if (colors > 0) img.Quantize(new QuantizeSettings { Colors = (uint)colors, DitherMethod = DitherMethod.FloydSteinberg });
            img.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
        }

        /// <summary>Highest quality that fits under the size; if even the lowest doesn't, also shrink the pixels.</summary>
        static byte[] EncodeUnder(MagickImage img, MagickFormat fmt, long target)
        {
            using (var work = (MagickImage)img.Clone())
            {
                byte[] best = null;
                for (int round = 0; round < 8; round++)
                {
                    if (Lossy(fmt))
                    {
                        int lo = 10, hi = 92;
                        byte[] fit = null;
                        while (lo <= hi)
                        {
                            var mid = (lo + hi) / 2;
                            var b = EncodeQuality(work, fmt, mid);
                            if (b.LongLength <= target) { fit = b; lo = mid + 1; } else hi = mid - 1;
                        }
                        if (fit != null) return fit;
                        best = EncodeQuality(work, fmt, 10);
                    }
                    else
                    {
                        foreach (var q in new[] { 95, 75, 50, 30 })
                        {
                            var b = EncodeQuality(work, fmt, q);
                            if (b.LongLength <= target) return b;
                            best = b;
                        }
                    }
                    // Still too big: shrink the image in proportion and try again.
                    var ratio = Math.Sqrt(target / (double)best.LongLength) * 0.92;
                    ratio = Math.Max(0.35, Math.Min(0.9, ratio));
                    var w = Math.Max(16, (int)(work.Width * ratio));
                    if (w >= work.Width) break;
                    work.Resize(new MagickGeometry((uint)w, 0));
                }
                return best;
            }
        }

        public static string Resize(string input, ResizeOptions o)
        {
            using (var img = Load(input))
            {
                img.AutoOrient();
                switch (o.Mode)
                {
                    case ResizeMode.LongSide:
                        var side = Math.Max(img.Width, img.Height);
                        if (!(o.NoEnlarge && side <= o.LongSide))
                        {
                            var s = o.LongSide / (double)side;
                            img.Resize(new MagickGeometry((uint)Math.Max(1, Math.Round(img.Width * s)), (uint)Math.Max(1, Math.Round(img.Height * s))) { IgnoreAspectRatio = true });
                        }
                        break;
                    case ResizeMode.Percent:
                        img.Resize(new Percentage(o.Percent));
                        break;
                    case ResizeMode.Exact:
                        // Fill the exact size, cropping the overflow from the centre (like a passport or square post).
                        img.Resize(new MagickGeometry((uint)o.Width, (uint)o.Height) { FillArea = true });
                        img.Crop(new MagickGeometry((uint)o.Width, (uint)o.Height), Gravity.Center);
                        img.ResetPage();
                        break;
                }
                var fmt = SaveFormatFor(input, out var ext);
                if (Lossy(fmt)) img.Quality = 90;
                var outPath = UniquePath(OutPath(input, o.OutputFolder, "-" + o.Label, ext));
                Save(img, outPath, fmt);
                return outPath;
            }
        }

        /// <summary>Copy without location, camera and other hidden details.</summary>
        public static string RemoveInfo(string input, string outputFolder)
        {
            using (var img = Load(input))
            {
                var fmt = SaveFormatFor(input, out var ext);
                var q = img.Quality;
                img.AutoOrient(); // the rotation lives in the info we're removing, so apply it to the pixels first
                StripInfo(img);
                if (Lossy(fmt)) img.Quality = q > 0 ? q : 92;
                var outPath = UniquePath(OutPath(input, outputFolder, "-clean", ext));
                Save(img, outPath, fmt);
                return outPath;
            }
        }

        static void StripInfo(MagickImage img)
        {
            var icc = img.GetColorProfile(); // keep colours right
            img.Strip();
            if (icc != null) img.SetProfile(icc);
        }

        // Same format as the input where we can write it; HEIC/PSD/TGA become JPG or PNG.
        static MagickFormat SaveFormatFor(string input, out string ext)
        {
            var t = Formats.TryGet(Formats.NameOf(input));
            if (t != null && t.Format != MagickFormat.Heic && t.Format != MagickFormat.Pdf && t.Format != MagickFormat.Ico)
            {
                ext = Path.GetExtension(input);
                return t.Format;
            }
            ext = ".jpg";
            return MagickFormat.Jpeg;
        }

        static void Save(MagickImage img, string outPath, MagickFormat fmt)
        {
            if (fmt == MagickFormat.Jpeg) Flatten(img, Color.White);
            if (fmt == MagickFormat.Heic)
            {
                if (HeicWriter == null) throw new NotSupportedException("Saving HEIC only works on Windows.");
                HeicWriter(img.ToByteArray(MagickFormat.Png), outPath);
            }
            else img.Write(outPath, fmt);
        }

        static void Flatten(MagickImage img, Color bg)
        {
            if (!img.HasAlpha) return;
            img.ColorAlpha(new MagickColor(bg.R, bg.G, bg.B));
            img.Alpha(AlphaAction.Off);
        }

        static void PrepareIcon(MagickImage img)
        {
            // Icons must be square: pad with transparency rather than stretch.
            if (img.Width != img.Height)
            {
                var side = Math.Max(img.Width, img.Height);
                img.BackgroundColor = MagickColors.Transparent;
                img.Extent(side, side, Gravity.Center);
            }
            img.Settings.SetDefine(MagickFormat.Icon, "auto-resize", "256,64,48,32,16");
        }

        public static string OutPath(string input, string folder, string suffix, string ext)
        {
            var dir = string.IsNullOrEmpty(folder) ? Path.GetDirectoryName(input) : folder;
            return Path.Combine(dir, Path.GetFileNameWithoutExtension(input) + suffix + ext);
        }

        /// <summary>photo.jpg → photo (1).jpg → photo (2).jpg… so nothing is ever overwritten.</summary>
        public static string UniquePath(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path);
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            for (int i = 1; ; i++)
            {
                var p = Path.Combine(dir, $"{name} ({i}){ext}");
                if (!File.Exists(p) && !Directory.Exists(p)) return p;
            }
        }

        static int Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));

        public static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024 / 1024).ToString("0.0") + " GB";
            if (bytes >= 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
            if (bytes >= 1024) return (bytes / 1024) + " KB";
            return bytes + " B";
        }

        /// <summary>"−61%" when smaller, "+12%" when bigger.</summary>
        public static string Change(long before, long after)
        {
            if (before <= 0) return null;
            var pct = (int)Math.Round((after - before) * 100.0 / before);
            return pct < 0 ? "−" + (-pct) + "%" : pct == 0 ? "±0%" : "+" + pct + "%";
        }
    }
}
