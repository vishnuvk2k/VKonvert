using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VKonvert.UI
{
    /// <summary>The VKonvert colours for one mode. Values match the website, so app and site look the same.</summary>
    public sealed class Palette
    {
        public bool Dark;
        public Color Bg, Panel, Ink, Muted, Line, Red, RedDeep, RedSoft, Tint, Grid, Ok, OkBg, Warn, Shadow;
    }

    public enum ThemeMode { System, Light, Dark }

    public static class Theme
    {
        public static readonly Palette Light = new Palette
        {
            Dark = false,
            Bg = Hex("#F7F3F2"), Panel = Hex("#FFFFFF"), Ink = Hex("#1F1716"), Muted = Hex("#6E615F"),
            Line = Hex("#E8DDDA"), Red = Hex("#D9342B"), RedDeep = Hex("#8A1C12"), RedSoft = Hex("#EBA9A3"),
            Tint = Hex("#FBE3E0"), Grid = Color.FromArgb(18, 31, 23, 22), Ok = Hex("#1F7A4D"), OkBg = Hex("#E3F3EA"),
            Warn = Hex("#B0261D"), Shadow = Color.FromArgb(70, 217, 52, 43),
        };

        public static readonly Palette DarkPalette = new Palette
        {
            Dark = true,
            Bg = Hex("#120E0D"), Panel = Hex("#1C1615"), Ink = Hex("#F5EDEB"), Muted = Hex("#B3A4A1"),
            Line = Hex("#342A28"), Red = Hex("#E5483E"), RedDeep = Hex("#8A1C12"), RedSoft = Hex("#7A2E28"),
            Tint = Hex("#3A1A17"), Grid = Color.FromArgb(16, 255, 255, 255), Ok = Hex("#6FD39E"), OkBg = Hex("#16301F"),
            Warn = Hex("#FF8A80"), Shadow = Color.FromArgb(90, 229, 72, 62),
        };

        public static Palette P { get; private set; } = Light;
        public static ThemeMode Mode { get; private set; } = ThemeMode.System;
        public static event Action Changed;

        const string SettingsKey = @"Software\VKonvert";

        public static void Init()
        {
            Mode = ThemeMode.System;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                    if (Enum.TryParse(k?.GetValue("Theme") as string, out ThemeMode m)) Mode = m;
            }
            catch { }
            P = Resolve(Mode) ? DarkPalette : Light;
        }

        public static void SetMode(ThemeMode mode)
        {
            Mode = mode;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey)) k.SetValue("Theme", mode.ToString());
            }
            catch { }
            var p = Resolve(mode) ? DarkPalette : Light;
            if (p == P) return;
            P = p;
            Changed?.Invoke();
        }

        public static void Toggle() => SetMode(P.Dark ? ThemeMode.Light : ThemeMode.Dark);

        static bool Resolve(ThemeMode mode)
        {
            if (mode != ThemeMode.System) return mode == ThemeMode.Dark;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return false; }
        }

        public static Color Hex(string s)
        {
            var v = Convert.ToInt32(s.TrimStart('#'), 16);
            return Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
        }

        public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        // ───────────── Scale (the app is per-monitor unaware but system-DPI aware) ─────────────

        public static float Scale { get; private set; } = 1f;

        public static void InitScale()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;
        }

        public static int Dp(float v) => (int)Math.Round(v * Scale);

        // ───────────── Fonts ─────────────

        static PrivateFontCollection brandFonts;
        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        static FontFamily BrandFamily
        {
            get
            {
                if (brandFonts == null)
                {
                    brandFonts = new PrivateFontCollection();
                    try
                    {
                        using (var s = typeof(Theme).Assembly.GetManifestResourceStream("brand.ttf"))
                        using (var ms = new MemoryStream())
                        {
                            s.CopyTo(ms);
                            var bytes = ms.ToArray();
                            // GDI+ reads the font from this memory for as long as the app runs, so it is never freed.
                            var mem = Marshal.AllocCoTaskMem(bytes.Length);
                            Marshal.Copy(bytes, 0, mem, bytes.Length);
                            brandFonts.AddMemoryFont(mem, bytes.Length);
                        }
                    }
                    catch { }
                }
                return brandFonts.Families.Length > 0 ? brandFonts.Families[0] : null;
            }
        }

        /// <summary>Bricolage Grotesque ExtraBold, the logo and headline face. Draw it with Graphics.DrawString.</summary>
        public static Font Brand(float pt) => Cached("brand" + pt, () =>
        {
            var fam = BrandFamily;
            return fam != null ? new Font(fam, pt, FontStyle.Regular, GraphicsUnit.Point)
                               : new Font("Segoe UI Black", pt, FontStyle.Regular);
        });

        public static Font Ui(float pt, FontStyle style = FontStyle.Regular) =>
            Cached($"ui{pt}{style}", () => new Font("Segoe UI", pt, style));

        public static Font UiSemibold(float pt) => Cached("uisb" + pt, () =>
        {
            var f = new Font("Segoe UI Semibold", pt);
            return f.Name == "Segoe UI Semibold" ? f : new Font("Segoe UI", pt, FontStyle.Bold);
        });

        public static Font Mono(float pt, bool bold = false) => Cached($"mono{pt}{bold}", () =>
        {
            var f = new Font("Cascadia Mono", pt, bold ? FontStyle.Bold : FontStyle.Regular);
            return f.Name == "Cascadia Mono" ? f : new Font("Consolas", pt, bold ? FontStyle.Bold : FontStyle.Regular);
        });

        static Font Cached(string key, Func<Font> make)
        {
            if (!fonts.TryGetValue(key, out var f)) fonts[key] = f = make();
            return f;
        }

        // ───────────── Window chrome ─────────────

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Dark or light title bar to match the app (Windows 10 1809+; ignored elsewhere).</summary>
        public static void ApplyTitleBar(Form f)
        {
            if (!f.IsHandleCreated) return;
            int on = P.Dark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0)
                    DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
            }
            catch { }
        }
    }

    public static class Draw
    {
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, RectangleF r, float radius, Color c)
        {
            using (var p = Round(r, radius)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void Stroke(Graphics g, RectangleF r, float radius, Color c, float width = 1f, DashStyle dash = DashStyle.Solid)
        {
            var half = width / 2f;
            var rr = new RectangleF(r.X + half, r.Y + half, r.Width - width, r.Height - width);
            using (var p = Round(rr, radius)) using (var pen = new Pen(c, width) { DashStyle = dash }) g.DrawPath(pen, p);
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static void Text(Graphics g, string text, Font font, Rectangle r, Color c,
            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter)
        {
            TextRenderer.DrawText(g, text, font, r, c, flags | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        public static Size Measure(string text, Font font) =>
            TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

        /// <summary>The brand face isn't installed, so it goes through GDI+ rather than TextRenderer.</summary>
        public static void BrandText(Graphics g, string text, Font font, PointF at, Color c)
        {
            var old = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var b = new SolidBrush(c)) g.DrawString(text, font, b, at, StringFormat.GenericTypographic);
            g.TextRenderingHint = old;
        }

        public static SizeF BrandMeasure(Graphics g, string text, Font font) =>
            g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);

        /// <summary>The VKonvert mark: a V whose left arm is pixels and right arm is one clean stroke.</summary>
        public static void Mark(Graphics g, RectangleF r, bool withTile = true, Color? glyph = null)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 64f, r.Height / 64f);
            if (withTile) using (var tile = Round(new RectangleF(0, 0, 64, 64), 15)) using (var red = new SolidBrush(Theme.Hex("#D9342B"))) g.FillPath(red, tile);
            var c = glyph ?? Color.White;
            using (var b = new SolidBrush(c))
            {
                g.FillRectangle(b, 13.25f, 13.25f, 7.5f, 7.5f);
                g.FillRectangle(b, 17.95f, 22.25f, 7.5f, 7.5f);
                g.FillRectangle(b, 22.55f, 31.25f, 7.5f, 7.5f);
            }
            using (var pen = new Pen(c, 7.5f) { LineJoin = LineJoin.Miter, StartCap = LineCap.Flat, EndCap = LineCap.Flat })
                g.DrawLines(pen, new[] { new PointF(28.4f, 39.2f), new PointF(32f, 46f), new PointF(48.4f, 14.6f) });
            g.Restore(state);
        }

        /// <summary>"VKonvert" with VK in red.</summary>
        public static void Wordmark(Graphics g, PointF at, float pt)
        {
            var f = Theme.Brand(pt);
            var vk = BrandMeasure(g, "VK", f);
            BrandText(g, "VK", f, at, Theme.P.Red);
            BrandText(g, "onvert", f, new PointF(at.X + vk.Width, at.Y), Theme.P.Ink);
        }

        public static float WordmarkWidth(Graphics g, float pt) => BrandMeasure(g, "VKonvert", Theme.Brand(pt)).Width;

        /// <summary>Faint dot grid, the "pixels" background behind the converter.</summary>
        public static void Dots(Graphics g, Rectangle r, int step)
        {
            var key = step + ":" + Theme.P.Grid.ToArgb();
            if (dotKey != key)
            {
                dotTile?.Dispose();
                var bmp = new Bitmap(step, step);
                using (var tg = Graphics.FromImage(bmp))
                using (var b = new SolidBrush(Theme.P.Grid))
                {
                    tg.SmoothingMode = SmoothingMode.AntiAlias;
                    var s = Math.Max(1.6f, 2.4f * Theme.Scale);
                    tg.FillEllipse(b, step / 2f - s / 2, step / 2f - s / 2, s, s);
                }
                dotTile = new TextureBrush(bmp);
                dotKey = key;
            }
            g.FillRectangle(dotTile, r);
        }

        static TextureBrush dotTile;
        static string dotKey;
    }
}
