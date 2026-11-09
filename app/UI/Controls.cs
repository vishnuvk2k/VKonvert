using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace VKonvert.UI
{
    /// <summary>Base for VKonvert's painted controls: double-buffered and repainted when the theme changes.</summary>
    public class VControl : Control
    {
        protected bool Hover, Pressed;

        public VControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Theme.Changed += OnThemeChanged;
        }

        protected virtual void OnThemeChanged() => Invalidate();

        protected override void Dispose(bool disposing)
        {
            Theme.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }

        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected void FocusRing(Graphics g, RectangleF r, float radius)
        {
            if (!Focused || !ShowFocusCues) return;
            Draw.Stroke(g, RectangleF.Inflate(r, -1, -1), radius, Theme.P.Red, Math.Max(2, Theme.Dp(2)));
        }
    }

    /// <summary>A clickable VControl: Enter/Space click it, and it's a tab stop.</summary>
    public class VButton : VControl
    {
        public VButton()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
    }

    public enum PillKind { Primary, Secondary, Nav, Ghost }

    public sealed class PillButton : VButton
    {
        PillKind kind;
        bool selected;
        public PillKind Kind { get => kind; set { kind = value; Invalidate(); } }
        public bool Selected { get => selected; set { selected = value; Invalidate(); } }
        public Font TextFont { get; set; }
        public float Radius { get; set; } = -1; // -1 = fully rounded
        /// <summary>Draws a small down-chevron after the text (for buttons that open a menu).</summary>
        public bool Chevron { get; set; }
        int ChevronW => Chevron ? Theme.Dp(16) : 0;

        public PillButton(string text, PillKind kind = PillKind.Secondary)
        {
            Text = text;
            this.kind = kind;
            TextFont = kind == PillKind.Primary ? Theme.UiSemibold(11.5f) : Theme.UiSemibold(9.75f);
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        public int PreferredWidth(int padding) => Draw.Measure(Text, TextFont).Width + padding * 2 + ChevronW;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var p = Theme.P;
            var r = new RectangleF(0, 0, Width, Height);
            var radius = Radius < 0 ? Height / 2f : Radius;
            Color bg, fg, border = Color.Empty;
            switch (kind)
            {
                case PillKind.Primary:
                    bg = !Enabled ? p.Line : Pressed ? p.RedDeep : Hover ? Theme.Mix(p.Red, Color.Black, .12f) : p.Red;
                    fg = Enabled ? Color.White : p.Muted;
                    if (Enabled)
                    {
                        // Soft red glow under the main button.
                        for (int i = 3; i >= 1; i--)
                            Draw.Fill(g, new RectangleF(i * 1.5f, i * 2f, Width - i * 3f, Height - i * 1.2f), radius, Color.FromArgb(14, p.Red));
                        r = new RectangleF(0, 0, Width, Height - Theme.Dp(4));
                    }
                    break;
                case PillKind.Nav:
                    bg = selected ? p.Ink : Hover ? p.Line : Color.Transparent;
                    fg = selected ? p.Bg : Hover ? p.Ink : p.Muted;
                    break;
                case PillKind.Ghost:
                    bg = Hover ? p.Line : Color.Transparent;
                    fg = p.Ink;
                    break;
                default:
                    bg = selected ? p.Tint : Hover ? Theme.Mix(p.Panel, p.Line, .5f) : p.Panel;
                    fg = selected ? (p.Dark ? p.Warn : p.Warn) : Enabled ? p.Ink : p.Muted;
                    border = selected ? p.Red : p.Line;
                    break;
            }
            if (bg.A > 0) Draw.Fill(g, r, radius, bg);
            if (border != Color.Empty) Draw.Stroke(g, r, radius, border);
            var tr = Rectangle.Round(r);
            tr.Width -= ChevronW;
            if (Chevron) tr.X += Theme.Dp(2);
            Draw.Text(g, Text, TextFont, tr, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Chevron)
            {
                var tw = Draw.Measure(Text, TextFont).Width;
                var cx = tr.X + (tr.Width + tw) / 2f + Theme.Dp(8);
                var cy = r.Y + r.Height / 2f;
                var c = Theme.Dp(4);
                using (var pen = new Pen(fg, Math.Max(1.5f, 1.6f * Theme.Scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] { new PointF(cx - c, cy - c / 2f), new PointF(cx, cy + c / 2f), new PointF(cx + c, cy - c / 2f) });
            }
            FocusRing(g, r, radius);
        }
    }

    /// <summary>Round icon button (theme toggle, menu).</summary>
    public sealed class IconButton : VButton
    {
        public Action<Graphics, Rectangle> Icon;
        public IconButton(Action<Graphics, Rectangle> icon, string accessibleName)
        {
            Icon = icon;
            AccessibleName = accessibleName;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Draw.Fill(g, r, Height / 2f, Hover ? Theme.P.Line : Theme.P.Bg);
            Draw.Stroke(g, r, Height / 2f, Theme.P.Line);
            var s = Theme.Dp(18);
            Icon?.Invoke(g, new Rectangle((Width - s) / 2, (Height - s) / 2, s, s));
            FocusRing(g, r, Height / 2f);
        }

        public static void Sun(Graphics g, Rectangle r)
        {
            using (var pen = new Pen(Theme.Hex("#F0C26B"), Math.Max(1.5f, 2 * Theme.Scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, rad = r.Width * .22f;
                g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
                for (int i = 0; i < 8; i++)
                {
                    var a = i * Math.PI / 4;
                    float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
                    g.DrawLine(pen, cx + c * r.Width * .36f, cy + s * r.Width * .36f, cx + c * r.Width * .48f, cy + s * r.Width * .48f);
                }
            }
        }

        public static void Moon(Graphics g, Rectangle r)
        {
            // Crescent outline worked out point by point: the part of the big disc outside the offset disc.
            float R = r.Width * .4f, cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            float r2 = R * .82f, cx2 = cx + R * .5f, cy2 = cy - R * .38f;
            var pts = new List<PointF>();
            for (int i = 0; i <= 96; i++)
            {
                var a = i * Math.PI * 2 / 96;
                var p = new PointF(cx + (float)Math.Cos(a) * R, cy + (float)Math.Sin(a) * R);
                if (Dist(p, cx2, cy2) >= r2) pts.Add(p);
            }
            // Start the outer run just after the gap so the polygon doesn't jump across.
            int gap = 0;
            for (int i = 1; i < pts.Count; i++) if (Dist2(pts[i], pts[i - 1]) > R * .2f) { gap = i; break; }
            var outer = pts.Skip(gap).Concat(pts.Take(gap)).ToList();
            var inner = new List<PointF>();
            for (int i = 0; i <= 96; i++)
            {
                var a = i * Math.PI * 2 / 96;
                var p = new PointF(cx2 + (float)Math.Cos(a) * r2, cy2 + (float)Math.Sin(a) * r2);
                if (Dist(p, cx, cy) <= R) inner.Add(p);
            }
            gap = 0;
            for (int i = 1; i < inner.Count; i++) if (Dist2(inner[i], inner[i - 1]) > R * .2f) { gap = i; break; }
            inner = inner.Skip(gap).Concat(inner.Take(gap)).ToList();
            // Walk the inner arc back from the outer run's end.
            if (inner.Count > 0 && Dist2(inner[0], outer[outer.Count - 1]) < Dist2(inner[inner.Count - 1], outer[outer.Count - 1])) { }
            else inner.Reverse();
            var poly = outer.Concat(inner).ToArray();
            if (poly.Length < 3) return;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Theme.P.Ink)) g.FillPolygon(b, poly);
            g.SmoothingMode = old;
        }

        static float Dist(PointF p, float x, float y) => (float)Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
        static float Dist2(PointF a, PointF b) => Dist(a, b.X, b.Y);

        public static void Dots(Graphics g, Rectangle r)
        {
            using (var b = new SolidBrush(Theme.P.Ink))
            {
                var d = Math.Max(3f, 3.4f * Theme.Scale);
                for (int i = -1; i <= 1; i++)
                    g.FillEllipse(b, r.X + r.Width / 2f + i * d * 1.9f - d / 2, r.Y + r.Height / 2f - d / 2, d, d);
            }
        }
    }

    /// <summary>A format "keycap": the picked one is red with a darker red edge underneath.</summary>
    public sealed class KeyButton : VButton
    {
        bool selected;
        public bool Selected { get => selected; set { selected = value; Invalidate(); } }
        public object Tag2;

        public KeyButton(string text) { Text = text; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var p = Theme.P;
            var depth = Pressed ? Theme.Dp(1) : Theme.Dp(4);
            var radius = Theme.Dp(10);
            var face = new RectangleF(0, Pressed ? Theme.Dp(3) : 0, Width, Height - Theme.Dp(4));
            var edge = new RectangleF(0, depth, Width, Height - depth);
            Draw.Fill(g, edge, radius, selected ? p.RedDeep : p.Line);
            Draw.Fill(g, face, radius, selected ? p.Red : Hover ? Theme.Mix(p.Panel, p.Line, .35f) : p.Panel);
            if (!selected) Draw.Stroke(g, face, radius, p.Line);
            var f = Theme.Mono(10.5f, true);
            Draw.Text(g, Text, f, Rectangle.Round(face), selected ? Color.White : Enabled ? p.Ink : p.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            FocusRing(g, face, radius);
        }
    }

    /// <summary>A row/grid of keys where exactly one is picked.</summary>
    public sealed class KeyGroup : Control
    {
        public readonly List<KeyButton> Keys = new List<KeyButton>();
        public int Columns { get; set; }
        public int KeyHeight { get; set; } = Theme.Dp(46);
        public int Gap { get; set; } = Theme.Dp(10);
        public event Action<int> Picked;
        int selectedIndex;

        public KeyGroup(IEnumerable<string> labels, int columns, int selected = 0)
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Columns = Math.Max(1, columns);
            foreach (var l in labels)
            {
                var k = new KeyButton(l);
                var i = Keys.Count;
                k.Click += (s, e) => Select(i, true);
                Keys.Add(k);
                Controls.Add(k);
            }
            selectedIndex = selected;
            Keys[selected].Selected = true;
            Height = PreferredHeight;
        }

        public int SelectedIndex => selectedIndex;
        public string SelectedText => Keys[selectedIndex].Text;

        public void Select(int i, bool raise)
        {
            selectedIndex = i;
            for (int j = 0; j < Keys.Count; j++) Keys[j].Selected = j == i;
            if (raise) Picked?.Invoke(i);
        }

        public int Rows => (Keys.Count + Columns - 1) / Columns;
        public int PreferredHeight => Rows * KeyHeight + (Rows - 1) * Gap;

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (Columns < 1) return;
            var w = (Width - (Columns - 1) * Gap) / (float)Columns;
            for (int i = 0; i < Keys.Count; i++)
            {
                int c = i % Columns, r = i / Columns;
                Keys[i].Bounds = new Rectangle((int)(c * (w + Gap)), r * (KeyHeight + Gap), (int)w, KeyHeight);
            }
            base.OnLayout(e);
        }
    }

    /// <summary>Quality as a bar of ten pixel blocks. Click or drag to set it; arrow keys work too.</summary>
    public sealed class PixelBar : VButton
    {
        int value = 85;
        public int Min = 10, Max = 100;
        public event Action ValueChanged;

        public int Value
        {
            get => value;
            set { var v = Math.Max(Min, Math.Min(Max, value)); if (v == this.value) return; this.value = v; Invalidate(); ValueChanged?.Invoke(); }
        }

        public PixelBar() { Height = Theme.Dp(24); AccessibleRole = AccessibleRole.Slider; AccessibleName = "Quality"; }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); SetFrom(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) SetFrom(e.X); }
        void SetFrom(int x) => Value = (int)Math.Round(Math.Max(0, Math.Min(1, x / (double)Width)) * 100 / 5.0) * 5;

        protected override bool IsInputKey(Keys k) => k == System.Windows.Forms.Keys.Left || k == System.Windows.Forms.Keys.Right || base.IsInputKey(k);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == System.Windows.Forms.Keys.Left) Value -= 5;
            if (e.KeyCode == System.Windows.Forms.Keys.Right) Value += 5;
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var p = Theme.P;
            var gap = Theme.Dp(5);
            var w = (Width - gap * 9) / 10f;
            var filled = value / 10f; // 85 -> 8.5 blocks
            for (int i = 0; i < 10; i++)
            {
                var r = new RectangleF(i * (w + gap), 0, w, Height);
                Draw.Fill(g, r, Theme.Dp(3), p.Line);
                var part = Math.Max(0, Math.Min(1, filled - i));
                if (part > 0) Draw.Fill(g, r, Theme.Dp(3), part >= 1 ? p.Red : Color.FromArgb((int)(255 * (0.3f + part * 0.5f)), p.Red));
            }
            FocusRing(g, new RectangleF(-2, -2, Width + 4, Height + 4), Theme.Dp(4));
        }
    }

    /// <summary>Colour dot for "see-through parts" (white, black, custom).</summary>
    public sealed class Swatch : VButton
    {
        bool selected;
        public Color Color;
        public bool Rainbow;
        public bool Selected { get => selected; set { selected = value; Invalidate(); } }

        public Swatch(Color c, string name) { Color = c; AccessibleName = name; Size = new Size(Theme.Dp(30), Theme.Dp(30)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var r = new RectangleF(Theme.Dp(3), Theme.Dp(3), Width - Theme.Dp(6), Height - Theme.Dp(6));
            if (Rainbow)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(r);
                    using (var b = new PathGradientBrush(path) { CenterColor = Color.White, SurroundColors = new[] { Theme.Hex("#D9342B"), Theme.Hex("#F0C26B"), Theme.Hex("#1F7A4D"), Theme.Hex("#3C6FD8") } })
                        g.FillPath(b, path);
                }
                using (var b = new SolidBrush(Color)) g.FillEllipse(b, RectangleF.Inflate(r, -r.Width * .28f, -r.Height * .28f));
            }
            else using (var b = new SolidBrush(Color)) g.FillEllipse(b, r);
            using (var pen = new Pen(Theme.P.Line)) g.DrawEllipse(pen, r);
            if (selected || (Focused && ShowFocusCues))
                using (var pen = new Pen(Theme.P.Red, Math.Max(2, Theme.Dp(2)))) g.DrawEllipse(pen, 1, 1, Width - 3, Height - 3);
        }
    }

    /// <summary>On/off switch with its label, like the installer's "I accept the license".</summary>
    public sealed class Switch : VButton
    {
        bool on;
        public event Action Toggled;
        public bool Checked { get => on; set { if (on == value) return; on = value; Invalidate(); Toggled?.Invoke(); } }

        public Switch(string text, bool on = false)
        {
            Text = text;
            this.on = on;
            Height = Theme.Dp(28);
            AccessibleRole = AccessibleRole.CheckButton;
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var p = Theme.P;
            int tw = Theme.Dp(40), th = Theme.Dp(22);
            var track = new RectangleF(0, (Height - th) / 2f, tw, th);
            Draw.Fill(g, track, th / 2f, on ? p.Red : p.Line);
            var k = th - Theme.Dp(6);
            using (var b = new SolidBrush(Color.White))
                g.FillEllipse(b, on ? track.Right - k - Theme.Dp(3) : track.X + Theme.Dp(3), track.Y + Theme.Dp(3), k, k);
            Draw.Text(g, Text, Theme.Ui(10f), new Rectangle(tw + Theme.Dp(10), 0, Width - tw - Theme.Dp(10), Height), p.Ink);
            FocusRing(g, track, th / 2f);
        }
    }

    public enum TextRole { Body, Muted, Title, Label, Red, Mono }

    /// <summary>Label coloured from the theme. Transparent, so the dot grid shows through.</summary>
    public sealed class TLabel : Label
    {
        TextRole role;
        public TLabel(string text, TextRole role = TextRole.Body, bool wrap = false)
        {
            Text = text;
            this.role = role;
            BackColor = Color.Transparent;
            UseMnemonic = false;
            AutoSize = !wrap;
            Font = role == TextRole.Title ? Theme.UiSemibold(10.5f)
                 : role == TextRole.Label ? Theme.Mono(8.25f, true)
                 : role == TextRole.Mono ? Theme.Mono(9f)
                 : Theme.Ui(9.75f);
            Recolor();
            Theme.Changed += Recolor;
        }

        void Recolor() => ForeColor = role == TextRole.Muted || role == TextRole.Label || role == TextRole.Mono ? Theme.P.Muted
                                    : role == TextRole.Red ? Theme.P.Red : Theme.P.Ink;

        protected override void Dispose(bool disposing) { Theme.Changed -= Recolor; base.Dispose(disposing); }

        /// <summary>Height needed to show all the text at this width.</summary>
        public int HeightFor(int width) =>
            TextRenderer.MeasureText(Text, Font, new Size(width, 10000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + Theme.Dp(2);
    }

    /// <summary>Rounded text box: a borderless TextBox inside a painted frame.</summary>
    public sealed class Field : VControl
    {
        public readonly TextBox Box;
        public string Suffix;

        public Field(string text = "", string accessibleName = null)
        {
            Height = Theme.Dp(40);
            Box = new TextBox { BorderStyle = BorderStyle.None, Text = text, Font = Theme.Ui(10.5f), AccessibleName = accessibleName };
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Recolor();
        }

        public override string Text { get => Box?.Text ?? ""; set { if (Box != null) Box.Text = value; } }

        protected override void OnThemeChanged() { Recolor(); base.OnThemeChanged(); }
        void Recolor() { Box.BackColor = Theme.P.Panel; Box.ForeColor = Theme.P.Ink; }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (Box == null) return; // layout can run from the constructor, before the box exists
            var sw = Suffix == null ? 0 : Draw.Measure(Suffix, Theme.UiSemibold(9.75f)).Width + Theme.Dp(12);
            Box.SetBounds(Theme.Dp(12), (Height - Box.PreferredHeight) / 2, Width - Theme.Dp(24) - sw, Box.PreferredHeight);
            base.OnLayout(e);
        }

        protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var r = new RectangleF(0, 0, Width, Height);
            Draw.Fill(g, r, Theme.Dp(10), Theme.P.Panel);
            Draw.Stroke(g, r, Theme.Dp(10), Box.Focused ? Theme.P.Red : Theme.P.Line, Box.Focused ? Math.Max(1.5f, Theme.Dp(2)) : 1);
            if (Suffix != null)
                Draw.Text(g, Suffix, Theme.UiSemibold(9.75f), new Rectangle(0, 0, Width - Theme.Dp(12), Height), Theme.P.Muted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Vertical stack: children take the full width, one under another, with a gap.</summary>
    public class Stack : Panel
    {
        public int Gap = Theme.Dp(16);
        public Padding Inner;
        public int MaxContentWidth = int.MaxValue;

        public Stack()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Transparent;
        }

        public int ContentHeight { get; private set; }

        /// <summary>This child takes whatever height is left (the page grid).</summary>
        public Control Fill;
        public bool Scrolls;
        int scrollY;
        public int ScrollY => scrollY;
        public int MaxScroll => Scrolls && Fill == null ? Math.Max(0, ContentHeight - Height) : 0;
        public event Action Scrolled;

        public void Wheel(int delta) => ScrollTo(scrollY - delta / 120 * Theme.Dp(70));

        public void ScrollTo(int y)
        {
            var v = Math.Max(0, Math.Min(MaxScroll, y));
            if (v == scrollY) return;
            scrollY = v;
            PerformLayout();
            Invalidate(true);
            Scrolled?.Invoke();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            var avail = Width - Inner.Horizontal;
            var w = Math.Min(avail, MaxContentWidth);
            var x = Inner.Left + (avail - w) / 2;
            var y = Inner.Top - (Scrolls ? scrollY : 0);
            var kids = Controls.Cast<Control>().Where(c => c.Visible).OrderBy(c => c.TabIndex).ToList();
            var fixedH = kids.Where(c => c != Fill).Sum(c => HeightOf(c, w)) + Math.Max(0, kids.Count - 1) * Gap + Inner.Vertical;
            foreach (var c in kids)
            {
                var h = c == Fill ? Math.Max(Theme.Dp(160), Height - fixedH) : HeightOf(c, w);
                c.SetBounds(x, y, c is TLabel tl && tl.AutoSize ? Math.Min(c.PreferredSize.Width, w) : w, h);
                y += h + Gap;
            }
            ContentHeight = y - Gap + Inner.Bottom + (Scrolls ? scrollY : 0);
            if (Scrolls && scrollY > MaxScroll) { scrollY = MaxScroll; BeginInvoke(new Action(() => PerformLayout())); }
            base.OnLayout(e);
            Scrolled?.Invoke();
        }

        static int HeightOf(Control c, int w) =>
            c is TLabel l && !l.AutoSize ? l.HeightFor(w) : c is Stack s && s.AutoHeight ? s.MeasureHeight(w) : c.Height;



        public bool AutoHeight;

        public int MeasureHeight(int width)
        {
            var w = Math.Min(width - Inner.Horizontal, MaxContentWidth);
            var h = Inner.Vertical;
            var n = 0;
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                h += c is TLabel l && !l.AutoSize ? l.HeightFor(w) : c is Stack s && s.AutoHeight ? s.MeasureHeight(w) : c.Height;
                n++;
            }
            return h + Math.Max(0, n - 1) * Gap;
        }

        public T Add<T>(T c) where T : Control
        {
            c.TabIndex = Controls.Count;
            Controls.Add(c);
            return c;
        }
    }

    /// <summary>A rounded panel holding a stack (the quality box, notes).</summary>
    public sealed class Card : Stack
    {
        public Card()
        {
            Inner = new Padding(Theme.Dp(18), Theme.Dp(16), Theme.Dp(18), Theme.Dp(16));
            Gap = Theme.Dp(10);
            AutoHeight = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Theme.Changed += Invalidate;
        }

        protected override void Dispose(bool disposing) { Theme.Changed -= Invalidate; base.Dispose(disposing); }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            var g = e.Graphics;
            Draw.Smooth(g);
            var r = new RectangleF(0, 0, Width, Height);
            Draw.Fill(g, r, Theme.Dp(14), Theme.P.Bg);
            Draw.Stroke(g, r, Theme.Dp(14), Theme.P.Line);
        }
    }

    /// <summary>Horizontal row of controls (left to right, with optional right-aligned ones).</summary>
    public sealed class Row : Panel
    {
        public int Gap = Theme.Dp(10);
        /// <summary>This control takes the space left between the left and right groups.</summary>
        public Control Stretch;
        readonly HashSet<Control> right = new HashSet<Control>();

        public Row(int height)
        {
            Height = height;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public T Add<T>(T c, bool alignRight = false) where T : Control
        {
            Controls.Add(c);
            if (alignRight) right.Add(c);
            return c;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int x = 0, rx = Width;
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                var w = c is TLabel l && l.AutoSize ? l.PreferredWidth : c.Width;
                var y = (Height - c.Height) / 2;
                if (right.Contains(c)) { rx -= w; c.SetBounds(rx, y, w, c.Height); rx -= Gap; }
                else if (c == Stretch) { }
                else { c.SetBounds(x, y, w, c.Height); x += w + Gap; }
            }
            foreach (Control c in Controls)
                if (c.Visible && c == Stretch) c.SetBounds(x, (Height - c.Height) / 2, Math.Max(0, rx - x), c.Height);
            base.OnLayout(e);
        }
    }

    /// <summary>The big "HEIC → JPG" display at the top of the converter.</summary>
    public sealed class BigDisplay : VControl
    {
        public string From = "", To = "", FromNote = "", ToNote = "";
        public string FromLabel = "FROM", ToLabel = "TO";

        public BigDisplay() { Height = Theme.Dp(170); }

        public void Set(string from, string to, string fromNote = "", string toNote = "")
        {
            From = from; To = to; FromNote = fromNote ?? ""; ToNote = toNote ?? "";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var p = Theme.P;
            var label = Theme.Mono(8.25f, true);
            var note = Theme.Mono(9f);
            var labelH = Theme.Dp(18);
            var noteH = Theme.Dp(20);

            // Biggest size where "FROM → TO" fits the width.
            var trailW = Theme.Dp(84);
            var gap = Theme.Dp(22);
            float pt = 60f;
            SizeF fs, ts;
            while (true)
            {
                var f = Theme.Brand(pt);
                fs = Draw.BrandMeasure(g, From, f);
                ts = Draw.BrandMeasure(g, To, f);
                if (fs.Width + ts.Width + trailW + gap * 2 <= Width - Theme.Dp(8) || pt <= 22) break;
                pt -= 2;
            }
            var font = Theme.Brand(pt);
            var bigH = fs.Height;
            var total = fs.Width + ts.Width + trailW + gap * 2;
            var x = (Width - total) / 2f;
            var top = Math.Max(0, (Height - (labelH + bigH + noteH)) / 2f);

            void Column(float cx, float w, string lab, Color labColor, string big, Color bigColor, string n)
            {
                Draw.Text(g, lab, label, new Rectangle((int)(cx - Theme.Dp(80)), (int)top, (int)w + Theme.Dp(160), labelH), labColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
                Draw.BrandText(g, big, font, new PointF(cx, top + labelH), bigColor);
                Draw.Text(g, n, note, new Rectangle((int)(cx - Theme.Dp(80)), (int)(top + labelH + bigH + Theme.Dp(2)), (int)w + Theme.Dp(160), noteH), p.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
            }

            Column(x, fs.Width, FromLabel, p.Muted, From, p.Ink, FromNote);
            var tx = x + fs.Width + gap + trailW + gap;
            Column(tx, ts.Width, ToLabel, p.Red, To, p.Red, ToNote);

            // The pixel trail from the logo, then an arrow.
            var midY = top + labelH + bigH * 0.55f;
            var sq = Theme.Dp(12);
            var sx = x + fs.Width + gap;
            float[] alpha = { .35f, .6f, .85f };
            for (int i = 0; i < 3; i++)
                using (var b = new SolidBrush(Color.FromArgb((int)(255 * alpha[i]), p.Red)))
                    g.FillRectangle(b, sx + i * (sq + Theme.Dp(6)), midY - sq / 2f, sq, sq);
            var ax = sx + 3 * (sq + Theme.Dp(6));
            using (var pen = new Pen(p.Red, Theme.Dp(4)) { LineJoin = LineJoin.Miter })
            {
                g.DrawLine(pen, ax, midY, ax + Theme.Dp(22), midY);
                g.DrawLines(pen, new[] { new PointF(ax + Theme.Dp(14), midY - Theme.Dp(8)), new PointF(ax + Theme.Dp(22), midY), new PointF(ax + Theme.Dp(14), midY + Theme.Dp(8)) });
            }
        }
    }

    /// <summary>Slim themed scrollbar for the options column (the system one is always light grey).</summary>
    public sealed class ThinScroll : VControl
    {
        Stack target;
        int dragOffset = -1;

        public ThinScroll() { Width = Theme.Dp(12); }

        public void Bind(Stack s)
        {
            if (target != null) target.Scrolled -= Invalidate;
            target = s;
            if (s != null) s.Scrolled += Invalidate;
            Invalidate();
        }

        RectangleF Thumb()
        {
            if (target == null || target.MaxScroll <= 0) return RectangleF.Empty;
            var h = Math.Max(Theme.Dp(40), Height * Height / (float)(Height + target.MaxScroll));
            var y = (Height - h) * target.ScrollY / target.MaxScroll;
            return new RectangleF(Theme.Dp(3), y, Theme.Dp(5), h);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var t = Thumb();
            if (t.IsEmpty) return;
            dragOffset = t.Contains(e.Location) ? (int)(e.Y - t.Y) : (int)(t.Height / 2);
            OnMouseMove(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragOffset < 0 || e.Button != MouseButtons.Left || target == null) return;
            var t = Thumb();
            var range = Height - t.Height;
            if (range > 0) target.ScrollTo((int)((e.Y - dragOffset) / range * target.MaxScroll));
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragOffset = -1; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Thumb();
            if (t.IsEmpty) return;
            Draw.Smooth(e.Graphics);
            Draw.Fill(e.Graphics, t, t.Width / 2, Hover || dragOffset >= 0 ? Theme.P.Muted : Theme.Mix(Theme.P.Line, Theme.P.Muted, .35f));
        }
    }

    /// <summary>Sends mouse-wheel turns to the scrollable thing under the pointer, not the focused control.</summary>
    public sealed class WheelRouter : IMessageFilter
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr WindowFromPoint(Point p);

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x020A) return false; // WM_MOUSEWHEEL
            var delta = (short)((long)m.WParam >> 16);
            var c = Control.FromChildHandle(WindowFromPoint(Cursor.Position));
            for (; c != null; c = c.Parent)
            {
                if (c is FileList fl) { fl.Wheel(delta); return true; }
                if (c is PageGrid pg) { pg.Wheel(delta); return true; }
                if (c is Stack st && st.Scrolls) { st.Wheel(delta); return true; }
                if (c is TextBoxBase || c is ComboBox) return false;
            }
            return false;
        }
    }

    /// <summary>The middle column background: panel colour with the dot grid.</summary>
    public sealed class DotPanel : Panel
    {
        public DotPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Theme.Changed += Invalidate;
        }

        protected override void Dispose(bool disposing) { Theme.Changed -= Invalidate; base.Dispose(disposing); }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var b = new SolidBrush(Theme.P.Panel)) e.Graphics.FillRectangle(b, ClientRectangle);
            Draw.Dots(e.Graphics, ClientRectangle, Theme.Dp(18));
            using (var pen = new Pen(Theme.P.Line))
            {
                e.Graphics.DrawLine(pen, 0, 0, 0, Height);
                e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
            }
        }
    }
}
