using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace VKonvert.UI
{
    public enum ItemState { Ready, Waiting, Working, Done, Failed }

    public sealed class FileItem
    {
        public string Path;
        public string Name => System.IO.Path.GetFileName(Path);
        public string Badge;
        public string Note;         // "3.1 MB", "4032 × 3024 · 3.1 MB"
        public string Result;       // "−61%"
        public bool ResultGood = true;
        public Image Thumb;
        public Color Swatch = Color.FromArgb(0xC9, 0xB4, 0x9E);
        public ItemState State;
        public float Progress;
        public string Error;
        public long Size;
        public int Pages;
    }

    /// <summary>
    /// The "In" and "Out" columns: file cards with a thumbnail, name, size and format badge.
    /// In "In" mode it ends with a dashed drop area, and cards can be dragged into a new order.
    /// </summary>
    public sealed class FileList : VControl
    {
        public readonly List<FileItem> Items = new List<FileItem>();
        public bool IsInput = true;
        public bool AllowReorder;
        public string DropText = "+ Add files or drop them here";
        public event Action AddClicked;
        public event Action Changed;
        public event Action<FileItem> Opened;
        public event Action<FileItem, Point> ContextRequested;

        int scroll, hoverIndex = -1, dragIndex = -1, dragTarget = -1;
        bool hoverRemove, hoverDrop;
        Point downAt;
        readonly ToolTip tip = new ToolTip();
        string tipText;

        int CardH => Theme.Dp(66);
        int Gap => Theme.Dp(8);
        int Step => CardH + Gap;
        int DropMinH => Theme.Dp(72);

        public FileList()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            AllowDrop = false;
        }

        int ContentHeight => Items.Count * Step + (IsInput ? DropMinH : 0);
        int MaxScroll => Math.Max(0, ContentHeight - Height);

        public void Refresh2() { scroll = Math.Min(scroll, MaxScroll); Invalidate(); }

        public void ScrollToEnd() { scroll = MaxScroll; Invalidate(); }

        public void Wheel(int delta)
        {
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll - delta / 120 * Step));
            Invalidate();
        }

        int IndexAt(Point p)
        {
            var y = p.Y + scroll;
            var i = y / Step;
            if (i < 0 || i >= Items.Count) return -1;
            return y - i * Step <= CardH ? i : -1;
        }

        Rectangle CardRect(int i) => new Rectangle(0, i * Step - scroll, Width - (MaxScroll > 0 ? Theme.Dp(8) : 0), CardH);
        Rectangle RemoveRect(Rectangle card) => new Rectangle(card.Right - Theme.Dp(34), card.Y + (card.Height - Theme.Dp(26)) / 2, Theme.Dp(26), Theme.Dp(26));

        Rectangle DropRect
        {
            get
            {
                var top = Items.Count * Step - scroll;
                return new Rectangle(0, top, Width - (MaxScroll > 0 ? Theme.Dp(8) : 0), Math.Max(DropMinH, Height - top));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragIndex >= 0 && e.Button == MouseButtons.Left)
            {
                if (Math.Abs(e.Y - downAt.Y) > Theme.Dp(6) || dragTarget >= 0)
                {
                    dragTarget = Math.Max(0, Math.Min(Items.Count - 1, (e.Y + scroll) / Step));
                    Cursor = Cursors.SizeNS;
                    Invalidate();
                }
                return;
            }
            var i = IndexAt(e.Location);
            var rem = i >= 0 && IsInput && RemoveRect(CardRect(i)).Contains(e.Location);
            var drop = IsInput && i < 0 && DropRect.Contains(e.Location);
            if (i != hoverIndex || rem != hoverRemove || drop != hoverDrop)
            {
                hoverIndex = i; hoverRemove = rem; hoverDrop = drop;
                Cursor = drop || rem || (i >= 0 && !IsInput) ? Cursors.Hand : AllowReorder && i >= 0 ? Cursors.SizeAll : Cursors.Default;
                Invalidate();
            }
            var t = i >= 0 ? (Items[i].Error ?? (IsInput ? Items[i].Path : Items[i].Path + "\nDouble-click to open")) : null;
            if (t != tipText) { tipText = t; tip.SetToolTip(this, t); }
        }

        protected override void OnMouseLeave(EventArgs e) { hoverIndex = -1; hoverRemove = hoverDrop = false; base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            downAt = e.Location;
            var i = IndexAt(e.Location);
            if (e.Button == MouseButtons.Right && i >= 0) { ContextRequested?.Invoke(Items[i], e.Location); return; }
            if (e.Button != MouseButtons.Left) return;
            if (i >= 0 && IsInput && RemoveRect(CardRect(i)).Contains(e.Location))
            {
                Items.RemoveAt(i);
                hoverIndex = -1;
                Refresh2();
                Changed?.Invoke();
                return;
            }
            if (i >= 0 && AllowReorder) dragIndex = i;
            if (i < 0 && IsInput && DropRect.Contains(e.Location)) AddClicked?.Invoke();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragIndex >= 0 && dragTarget >= 0 && dragTarget != dragIndex)
            {
                var item = Items[dragIndex];
                Items.RemoveAt(dragIndex);
                Items.Insert(dragTarget, item);
                Changed?.Invoke();
            }
            dragIndex = dragTarget = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            var i = IndexAt(PointToClient(MousePosition));
            if (i >= 0) Opened?.Invoke(Items[i]);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var p = Theme.P;
            var radius = Theme.Dp(12);
            for (int i = 0; i < Items.Count; i++)
            {
                var r = CardRect(i);
                if (r.Bottom < 0 || r.Top > Height) continue;
                var it = Items[i];
                var rf = new RectangleF(r.X + .5f, r.Y + .5f, r.Width - 1, r.Height - 1);
                Draw.Fill(g, rf, radius, i == dragTarget && dragIndex >= 0 ? p.Tint : p.Panel);
                var border = it.State == ItemState.Working ? p.Red : it.State == ItemState.Failed ? p.Warn : i == hoverIndex ? Theme.Mix(p.Line, p.Muted, .35f) : p.Line;
                Draw.Stroke(g, rf, radius, border);

                // Thumbnail (or a colour block until it's loaded).
                var th = new Rectangle(r.X + Theme.Dp(10), r.Y + Theme.Dp(10), Theme.Dp(46), Theme.Dp(46));
                using (var clip = Draw.Round(th, Theme.Dp(8)))
                {
                    var st = g.Save();
                    g.SetClip(clip);
                    if (it.Thumb != null)
                    {
                        using (var chk = new HatchBrush(HatchStyle.LargeCheckerBoard, Color.FromArgb(235, 235, 235), Color.White))
                            g.FillRectangle(chk, th);
                        var img = it.Thumb;
                        var sc = Math.Max(th.Width / (float)img.Width, th.Height / (float)img.Height);
                        var w = img.Width * sc; var h = img.Height * sc;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(img, th.X + (th.Width - w) / 2, th.Y + (th.Height - h) / 2, w, h);
                    }
                    else using (var b = new SolidBrush(it.Swatch)) g.FillRectangle(b, th);
                    g.Restore(st);
                }

                var textX = th.Right + Theme.Dp(12);
                var right = r.Right - Theme.Dp(10);
                var showRemove = IsInput && i == hoverIndex && it.State != ItemState.Working;
                var badgeText = it.Result ?? it.Badge;
                if (showRemove)
                {
                    var rr = RemoveRect(r);
                    Draw.Fill(g, rr, rr.Height / 2f, hoverRemove ? p.Tint : p.Bg);
                    using (var pen = new Pen(hoverRemove ? p.Red : p.Muted, Math.Max(1.5f, Theme.Dp(2))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        var c = Theme.Dp(5);
                        var cx = rr.X + rr.Width / 2; var cy = rr.Y + rr.Height / 2;
                        g.DrawLine(pen, cx - c, cy - c, cx + c, cy + c);
                        g.DrawLine(pen, cx + c, cy - c, cx - c, cy + c);
                    }
                    right = rr.X - Theme.Dp(6);
                }
                else if (!string.IsNullOrEmpty(badgeText))
                {
                    var bf = Theme.Mono(8.25f, true);
                    var bw = Draw.Measure(badgeText, bf).Width + Theme.Dp(12);
                    var br = new Rectangle(right - bw, r.Y + (r.Height - Theme.Dp(22)) / 2, bw, Theme.Dp(22));
                    var good = it.Result != null && it.ResultGood;
                    Draw.Fill(g, br, Theme.Dp(6), good ? p.OkBg : p.Bg);
                    Draw.Text(g, badgeText, bf, br, good ? p.Ok : p.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    right = br.X - Theme.Dp(8);
                }

                var nameR = new Rectangle(textX, r.Y + Theme.Dp(12), right - textX, Theme.Dp(20));
                Draw.Text(g, it.Name, Theme.UiSemibold(9.75f), nameR, p.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                var subR = new Rectangle(textX, r.Y + Theme.Dp(34), right - textX, Theme.Dp(18));
                if (it.State == ItemState.Working)
                {
                    var bar = new RectangleF(textX, subR.Y + Theme.Dp(6), subR.Width, Theme.Dp(5));
                    Draw.Fill(g, bar, bar.Height / 2, p.Line);
                    Draw.Fill(g, new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * it.Progress), bar.Height), bar.Height / 2, p.Red);
                }
                else
                {
                    var sub = it.State == ItemState.Failed ? "Couldn’t do this one · " + (it.Error ?? "") :
                              it.State == ItemState.Waiting ? "Waiting · " + it.Note :
                              it.State == ItemState.Done && IsInput ? "Done · " + it.Note : it.Note;
                    Draw.Text(g, sub ?? "", Theme.Mono(8.5f), subR, it.State == ItemState.Failed ? p.Warn : p.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                }
            }

            if (IsInput)
            {
                var d = DropRect;
                if (d.Top < Height)
                {
                    var df = new RectangleF(d.X + 1, d.Y + 1, d.Width - 2, d.Height - 2);
                    if (hoverDrop) Draw.Fill(g, df, Theme.Dp(12), p.Tint);
                    Draw.Stroke(g, df, Theme.Dp(12), hoverDrop ? p.Red : Items.Count == 0 ? p.RedSoft : p.Line, Math.Max(1.5f, Theme.Dp(1.5f)), DashStyle.Dash);
                    var text = Items.Count == 0 ? DropText : "+ Add more";
                    Draw.Text(g, text, Theme.UiSemibold(9.75f), Rectangle.Round(df), Items.Count == 0 || hoverDrop ? p.Red : p.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                }
            }
            else if (Items.Count == 0)
            {
                var r = new Rectangle(0, 0, Width, Theme.Dp(66));
                for (int k = 0; k < 2; k++)
                {
                    var rr = new RectangleF(1, k * (r.Height + Gap) + 1, Width - 2, r.Height - 2);
                    Draw.Stroke(g, rr, Theme.Dp(12), p.Line, Math.Max(1.5f, Theme.Dp(1.5f)), DashStyle.Dash);
                }
                Draw.Text(g, "Finished files show up here.", Theme.Ui(9f), new Rectangle(0, 2 * (r.Height + Gap), Width, Theme.Dp(24)), p.Muted);
            }

            if (MaxScroll > 0)
            {
                var track = Height;
                var thumbH = Math.Max(Theme.Dp(30), track * Height / (float)ContentHeight);
                var y = (track - thumbH) * scroll / (float)MaxScroll;
                Draw.Fill(g, new RectangleF(Width - Theme.Dp(4), y, Theme.Dp(4), thumbH), Theme.Dp(2), p.Line);
            }
        }
    }

    /// <summary>Page thumbnails for "Rotate & reorder": click to pick, then rotate, move or delete.</summary>
    public sealed class PageGrid : VControl
    {
        public sealed class Page { public int Source; public int Rotation; public Image Thumb; public bool Selected; }

        public readonly List<Page> Pages = new List<Page>();
        public event Action SelectionChanged;
        int scroll;
        int Cell => Theme.Dp(112);
        int CellH => Theme.Dp(150);
        int GapPx => Theme.Dp(14);

        public PageGrid() { SetStyle(ControlStyles.Selectable, true); TabStop = true; }

        int Cols => Math.Max(1, (Width + GapPx) / (Cell + GapPx));
        int ContentH => (Pages.Count + Cols - 1) / Cols * (CellH + GapPx);
        int MaxScroll => Math.Max(0, ContentH - Height);

        Rectangle CellRect(int i)
        {
            var cols = Cols;
            var used = cols * Cell + (cols - 1) * GapPx;
            var x0 = (Width - used) / 2;
            return new Rectangle(x0 + i % cols * (Cell + GapPx), i / cols * (CellH + GapPx) - scroll, Cell, CellH);
        }

        public void Wheel(int delta)
        {
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll - delta / 120 * (CellH / 2)));
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            for (int i = 0; i < Pages.Count; i++)
                if (CellRect(i).Contains(e.Location))
                {
                    if ((ModifierKeys & Keys.Control) == 0) foreach (var pg in Pages) if (pg != Pages[i]) pg.Selected = false;
                    Pages[i].Selected = !Pages[i].Selected || (ModifierKeys & Keys.Control) == 0;
                    Invalidate();
                    SelectionChanged?.Invoke();
                    return;
                }
        }

        public void ClampScroll() { scroll = Math.Min(scroll, MaxScroll); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Draw.Smooth(g);
            var p = Theme.P;
            if (Pages.Count == 0)
            {
                Draw.Text(g, "Add a PDF on the left to see its pages here.", Theme.Ui(10f), ClientRectangle, p.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            for (int i = 0; i < Pages.Count; i++)
            {
                var r = CellRect(i);
                if (r.Bottom < 0 || r.Top > Height) continue;
                var pg = Pages[i];
                var box = new Rectangle(r.X, r.Y, r.Width, r.Height - Theme.Dp(24));
                if (pg.Selected) Draw.Fill(g, RectangleF.Inflate(box, Theme.Dp(4), Theme.Dp(4)), Theme.Dp(10), p.Tint);
                if (pg.Thumb != null)
                {
                    var img = pg.Thumb;
                    var rot = ((pg.Rotation % 360) + 360) % 360;
                    float iw = rot % 180 == 0 ? img.Width : img.Height, ih = rot % 180 == 0 ? img.Height : img.Width;
                    var sc = Math.Min((box.Width - Theme.Dp(8)) / iw, (box.Height - Theme.Dp(8)) / ih);
                    float w = iw * sc, h = ih * sc;
                    var cx = box.X + box.Width / 2f; var cy = box.Y + box.Height / 2f;
                    using (var b = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillRectangle(b, cx - w / 2 + 2, cy - h / 2 + 3, w, h);
                    var st = g.Save();
                    g.TranslateTransform(cx, cy);
                    g.RotateTransform(rot);
                    float dw = rot % 180 == 0 ? w : h, dh = rot % 180 == 0 ? h : w;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(img, -dw / 2, -dh / 2, dw, dh);
                    g.Restore(st);
                    if (pg.Selected) using (var pen = new Pen(p.Red, Math.Max(2, Theme.Dp(2)))) g.DrawRectangle(pen, cx - w / 2, cy - h / 2, w, h);
                }
                else Draw.Fill(g, RectangleF.Inflate(box, -Theme.Dp(10), -Theme.Dp(4)), 4, p.Line);
                var label = (i + 1).ToString() + (pg.Rotation % 360 != 0 ? $" · turned {((pg.Rotation % 360) + 360) % 360}°" : "");
                Draw.Text(g, label, Theme.Mono(8.5f, pg.Selected), new Rectangle(r.X, box.Bottom + Theme.Dp(4), r.Width, Theme.Dp(18)),
                    pg.Selected ? p.Red : p.Muted, TextFormatFlags.HorizontalCenter);
            }
        }
    }
}
