// Dark theme and owner-drawn controls. Colours and shapes follow miguel.valenciasanchez.com:
// near-black surfaces, lime accent, pill buttons, and the Outfit font when it's installed.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Theme
{
    static Color C(int rgb) => Color.FromArgb(255, rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255);
    public static readonly Color Window = C(0x050608), Sidebar = C(0x090B0F), Group = C(0x0D1016), Separator = C(0x1C2029),
        Fill = C(0x151922), FillHover = C(0x1D222D), Raised = C(0x272D3A), Text = C(0xF4F4F2), Secondary = C(0xA0A8B8),
        Tertiary = C(0x667085), Accent = C(0xB6F23A), AccentHover = C(0xC8F76A), AccentDeep = C(0x8CC21C), OnAccent = C(0x0B1405),
        Red = C(0xFF6B6B), Orange = C(0xFF9A5A), Yellow = C(0xFFCC00), Pink = C(0xFF7FA6), Cyan = C(0x4FD3FF), Mint = C(0x7EE0C3), Violet = C(0xC4A3FF);
    public static readonly Color Line = Color.FromArgb(26, 244, 244, 242), LineStrong = Color.FromArgb(61, 244, 244, 242);

    /// How an LED colour should look on screen: lit colours get a touch of white so they read as light, off is dark gray.
    public static Color Led(Color c) =>
        IsOff(c) ? Color.FromArgb(46, 51, 62) : Color.FromArgb(c.R + (255 - c.R) * 22 / 100, c.G + (255 - c.G) * 22 / 100, c.B + (255 - c.B) * 22 / 100);
    public static bool IsOff(Color c) => c.R + c.G + c.B < 15;

    static readonly HashSet<string> fonts = new HashSet<string>(new InstalledFontCollection().Families.Select(f => f.Name));
    static Font F(float size, params (string family, FontStyle style)[] options)
    {
        foreach (var (family, style) in options)
            if (fonts.Contains(family)) try { return new Font(family, size, style); } catch (ArgumentException) { }
        return new Font("Segoe UI", size, options.Last().style);
    }
    const FontStyle R = FontStyle.Regular, B = FontStyle.Bold;
    public static readonly Font
        Body = F(10f, ("Outfit", R), ("Segoe UI Variable Text", R)),
        Small = F(8.75f, ("Outfit", R), ("Segoe UI Variable Text", R)),
        Strong = F(10f, ("Outfit SemiBold", R), ("Outfit", B), ("Segoe UI Variable Text Semibold", R)),
        Section = F(11f, ("Outfit SemiBold", R), ("Outfit", B), ("Segoe UI Variable Display Semib", R)),
        Big = F(13f, ("Outfit", B), ("Segoe UI Variable Display", B), ("Segoe UI Variable Display Semib", R)),
        Title = F(22f, ("Outfit ExtraBold", R), ("Outfit", B), ("Segoe UI Variable Display", B), ("Segoe UI Variable Display Semib", R)),
        Mono = new Font("Consolas", 9f);
    public static readonly string IconFamily = fonts.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hwnd, string app, string id);
    public static void DarkWindow(IntPtr hwnd)
    {
        int on = 1; DwmSetWindowAttribute(hwnd, 20, ref on, 4);
        int caption = 0x080605, text = 0xF2F4F4; DwmSetWindowAttribute(hwnd, 35, ref caption, 4); DwmSetWindowAttribute(hwnd, 36, ref text, 4);   // COLORREF is BGR
    }
    public static void RoundCorners(IntPtr hwnd) { int round = 2; DwmSetWindowAttribute(hwnd, 33, ref round, 4); }
    public static T DarkScrollbars<T>(T c) where T : Control
    {
        c.HandleCreated += (s, e) => SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
        return c;
    }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
    public static void FillRound(Graphics g, Color c, RectangleF r, float radius) { using (var p = Round(r, radius)) using (var b = new SolidBrush(c)) g.FillPath(b, p); }
    public static void StrokeRound(Graphics g, Color c, RectangleF r, float radius, float width = 1) { using (var p = Round(r, radius)) using (var pen = new Pen(c, width)) g.DrawPath(pen, p); }
    public static void Glow(Graphics g, Color c, RectangleF r, float radius, int strength = 50)
    {
        for (int i = 4; i >= 1; i--)
        {
            var s = r; s.Inflate(i * 1.5f - 3, i * 1.5f - 3); s.Offset(0, 3 + i);
            FillRound(g, Color.FromArgb(strength / (i + 1), c), s, radius);
        }
    }
    public static void Glyph(Graphics g, string glyph, float size, Color c, RectangleF r)
    {
        using (var f = new Font(IconFamily, size)) TextRenderer.DrawText(g, glyph, f, Rectangle.Round(r), c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    /// Soft nebula glow, like the portfolio background: (centre x, centre y, radius x, radius y) as fractions of the area.
    public static void Nebula(Graphics g, Rectangle area, params (float x, float y, float rx, float ry, int rgb, int alpha)[] clouds)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var (x, y, rx, ry, rgb, alpha) in clouds)
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(area.X + area.Width * (x - rx), area.Y + area.Height * (y - ry), area.Width * rx * 2, area.Height * ry * 2);
                using (var b = new PathGradientBrush(path) { CenterColor = Color.FromArgb(alpha, C(rgb)), SurroundColors = new[] { Color.FromArgb(0, C(rgb)) } }) g.FillPath(b, path);
            }
    }

    /// Faint stars. Same seed, same sky on every repaint.
    public static void Stars(Graphics g, Rectangle area, int seed, int stars)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rnd = new Random(seed);
        for (int i = 0; i < stars; i++)
        {
            float x = area.X + (float)rnd.NextDouble() * area.Width, y = area.Y + (float)rnd.NextDouble() * area.Height, d = rnd.Next(10) == 0 ? 2.2f : 1.3f;
            using (var b = new SolidBrush(Color.FromArgb(rnd.Next(30, 120), 236, 232, 210))) g.FillEllipse(b, x, y, d, d);
        }
    }

    public static Label Lbl(string t, Font f = null, Color? c = null, int maxWidth = 0)
    {
        var l = new Label { Text = t, AutoSize = true, Font = f ?? Body, ForeColor = c ?? Theme.Text, BackColor = Color.Transparent, Margin = new Padding(0), UseMnemonic = false };
        if (maxWidth > 0) l.MaximumSize = new Size(maxWidth, 0);
        return l;
    }
    public static FlowLayoutPanel Row(params Control[] cs)
    {
        var r = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty, Padding = Padding.Empty };
        foreach (var c in cs) { c.Margin = new Padding(8, 0, 0, 0); r.Controls.Add(c); }
        return r;
    }
}

/// Panel with the starry background (the sidebar).
class SkyPanel : Panel
{
    public SkyPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Theme.Nebula(e.Graphics, ClientRectangle, (0.1f, 0.1f, 0.9f, 0.3f, 0x127080, 40), (0.9f, 0.9f, 0.9f, 0.35f, 0x2B3FA8, 46), (0.95f, 0.45f, 0.5f, 0.2f, 0x6A33A0, 26));
        Theme.Stars(e.Graphics, ClientRectangle, 7, ClientSize.Width * ClientSize.Height / 2600);
    }
}

/// Base for painted controls: double-buffered, focusable, transparent over its parent.
abstract class Painted : Control
{
    protected bool hover, pressed;
    protected Painted()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent; Font = Theme.Body; ForeColor = Theme.Text; Cursor = Cursors.Default; TabStop = true; Margin = Padding.Empty;
    }
    protected Graphics G(PaintEventArgs e) { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit; return e.Graphics; }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
    protected void FocusRing(Graphics g, RectangleF r, float radius)
    {
        if (!Focused || !ShowFocusCues) return;
        r.Inflate(2, 2);
        Theme.StrokeRound(g, Color.FromArgb(160, Theme.Accent), r, radius + 2, 2f);
    }
}

/// Card with rows: label (and an optional line of help) on the left, a control on the right.
class Group : Panel
{
    class RowInfo { public Label Label, Sub; public Control Right, Full; public int Top, Height; }
    readonly List<RowInfo> rows = new List<RowInfo>();
    readonly Label header, footer;
    const int Pad = 18, MinRow = 48;
    int boxTop, boxHeight;

    public Group(string header = null, string footer = null, int width = 680)
    {
        DoubleBuffered = true; BackColor = Theme.Window; Width = width; Margin = new Padding(0, 0, 0, 24);
        if (header != null) { this.header = Theme.Lbl(header, Theme.Section); Controls.Add(this.header); }
        if (footer != null) { this.footer = Theme.Lbl(footer, Theme.Small, Theme.Secondary, width - 2 * Pad); Controls.Add(this.footer); }
    }

    public Group Add(string label, Control right = null, string sub = null)
    {
        var r = new RowInfo { Label = Theme.Lbl(label), Right = right };
        if (sub != null) r.Sub = Theme.Lbl(sub, Theme.Small, Theme.Secondary, Width - 2 * Pad - (right?.Width ?? 0) - 24);
        Controls.Add(r.Label); if (r.Sub != null) Controls.Add(r.Sub);
        if (right != null) { Controls.Add(right); right.SizeChanged += (s, e) => Relayout(); }   // values that grow stay right-aligned
        rows.Add(r); Relayout(); return this;
    }

    public Group AddFull(Control full)
    {
        rows.Add(new RowInfo { Full = full }); Controls.Add(full); Relayout(); return this;
    }

    public void SetFooter(string t) { if (footer != null) { footer.Text = t; Relayout(); } }

    public void Relayout()
    {
        int y = 0;
        if (header != null) { header.Location = new Point(4, 0); y = header.PreferredHeight + 10; }
        boxTop = y;
        foreach (var r in rows)
        {
            r.Top = y;
            if (r.Full != null)
            {
                r.Height = r.Full.Height == 0 ? 0 : r.Full.Height + 24;
                r.Full.Location = new Point(Math.Max(0, (Width - r.Full.Width) / 2), y + 12);
            }
            else
            {
                int textH = r.Label.PreferredHeight + (r.Sub != null ? r.Sub.PreferredHeight + 2 : 0);
                r.Height = Math.Max(MinRow, Math.Max(textH + 22, (r.Right?.Height ?? 0) + 18));
                r.Label.Location = new Point(Pad, y + (r.Height - textH) / 2);
                if (r.Sub != null) r.Sub.Location = new Point(Pad, r.Label.Bottom + 2);
                if (r.Right != null) r.Right.Location = new Point(Width - Pad - r.Right.Width, y + (r.Height - r.Right.Height) / 2);
            }
            y += r.Height;
        }
        boxHeight = y - boxTop;
        if (footer != null) { footer.Location = new Point(4, y + 8); y = footer.Bottom; }
        Height = y + 2;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        if (rows.Count == 0) return;
        var box = new RectangleF(0.5f, boxTop + 0.5f, Width - 2, boxHeight - 1);
        Theme.FillRound(g, Theme.Group, box, 14);
        Theme.StrokeRound(g, Theme.Line, box, 14);
        using (var p = new Pen(Theme.Separator))
            for (int i = 1; i < rows.Count; i++) if (rows[i].Height > 0) g.DrawLine(p, Pad, rows[i].Top, Width - Pad, rows[i].Top);
    }
}

/// Pill-shaped segmented control; the chosen segment is lime, like the filters on the portfolio.
class Segmented : Painted
{
    string[] items; Color?[] dots; int sel = -1, hot = -1, marked = -1;
    public event EventHandler SelectedIndexChanged;
    public Segmented(params string[] items) { this.items = items; Height = 32; Cursor = Cursors.Hand; Fit(); }
    public string[] Items { get => items; set { items = value; Fit(); Invalidate(); } }
    public Color?[] Dots { get => dots; set { dots = value; Fit(); Invalidate(); } }
    /// Gets a small underline (the profile the mouse is using).
    public int Marked { get => marked; set { marked = value; Invalidate(); } }
    public int SelectedIndex { get => sel; set { if (sel == value) return; sel = value; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); } }
    int W(int i) => TextRenderer.MeasureText(items[i], Theme.Strong).Width + 26 + (dots != null ? 15 : 0);
    void Fit() => Width = Enumerable.Range(0, items.Length).Sum(W) + 6;
    Rectangle Seg(int i) => new Rectangle(3 + Enumerable.Range(0, i).Sum(W), 3, W(i), Height - 6);
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = G(e); var all = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.FillRound(g, Theme.Fill, all, all.Height / 2);
        Theme.StrokeRound(g, Theme.Line, all, all.Height / 2);
        for (int i = 0; i < items.Length; i++)
        {
            var r = Seg(i); bool on = i == sel;
            if (on) Theme.FillRound(g, Enabled ? Theme.Accent : Theme.Raised, r, r.Height / 2f);
            else if (i == hot && Enabled) Theme.FillRound(g, Theme.FillHover, r, r.Height / 2f);
            var fg = !Enabled ? Theme.Tertiary : on ? Theme.OnAccent : i == hot ? Theme.Text : Theme.Secondary;
            int x = r.X + 13;
            if (dots != null && dots[i].HasValue)
            {
                var c = Theme.Led(dots[i].Value);
                using (var b = new SolidBrush(c)) g.FillEllipse(b, x, r.Y + r.Height / 2 - 4.5f, 9, 9);
                if (on) using (var p = new Pen(Color.FromArgb(90, Theme.OnAccent))) g.DrawEllipse(p, x, r.Y + r.Height / 2 - 4.5f, 9, 9);
                x += 15;
            }
            var tr = new Rectangle(x, r.Y, r.Right - x - 11, r.Height);
            TextRenderer.DrawText(g, items[i], on ? Theme.Strong : Theme.Body, tr, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            if (i == marked) Theme.FillRound(g, on ? Theme.OnAccent : Theme.Accent, new RectangleF(tr.X + tr.Width / 2f - 7, r.Bottom - 5, 14, 2.5f), 1.25f);
        }
        FocusRing(g, all, all.Height / 2);
    }
    int Hit(Point p) { for (int i = 0; i < items.Length; i++) if (Seg(i).Contains(p)) return i; return -1; }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = Hit(e.Location); if (h != hot) { hot = h; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = -1; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); int h = Hit(e.Location); if (h >= 0) SelectedIndex = h; }
    protected override bool IsInputKey(Keys k) => k == Keys.Left || k == Keys.Right || base.IsInputKey(k);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left && sel > 0) SelectedIndex = sel - 1;
        if (e.KeyCode == Keys.Right && sel < items.Length - 1) SelectedIndex = sel + 1;
    }
}

class Switch : Painted
{
    bool on;
    public event EventHandler CheckedChanged;
    public Switch() { Size = new Size(46, 26); Cursor = Cursors.Hand; }
    public bool Checked { get => on; set { if (on == value) return; on = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); } }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = G(e); var track = new RectangleF(1.5f, 1.5f, 42, 22);
        Theme.FillRound(g, on ? (Enabled ? Theme.Accent : Theme.AccentDeep) : Theme.Raised, track, 11);
        if (!on) Theme.StrokeRound(g, Theme.Line, track, 11);
        var knob = new RectangleF(on ? 24.5f : 5.5f, 5.5f, 14, 14);
        using (var b = new SolidBrush(on ? Theme.OnAccent : Enabled ? Theme.Text : Theme.Tertiary)) g.FillEllipse(b, knob);
        FocusRing(g, track, 11);
    }
    protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); Checked = !Checked; }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) Checked = !Checked; }
}

class Slider : Painted
{
    readonly int min, max, step; int value;
    public event EventHandler ValueChanged;
    public Slider(int min, int max, int step = 1, int width = 220) { this.min = min; this.max = max; this.step = step; value = min; Size = new Size(width, 24); Cursor = Cursors.Hand; }
    public int Value
    {
        get => value;
        set { int v = Math.Max(min, Math.Min(max, value)); if (v == this.value) return; this.value = v; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); }
    }
    float X(int v) => 10 + (Width - 20) * (v - min) / (float)(max - min);
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = G(e); float cy = Height / 2f, x = X(value);
        Theme.FillRound(g, Theme.Raised, new RectangleF(10, cy - 2, Width - 20, 4), 2);
        Theme.FillRound(g, Enabled ? Theme.Accent : Theme.Tertiary, new RectangleF(10, cy - 2, Math.Max(4, x - 10), 4), 2);
        using (var b = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) g.FillEllipse(b, x - 8, cy - 7, 16, 16);
        using (var b = new SolidBrush(pressed ? Theme.Accent : Theme.Text)) g.FillEllipse(b, x - 8, cy - 8, 16, 16);
        if (Focused && ShowFocusCues) using (var p = new Pen(Color.FromArgb(160, Theme.Accent), 2f)) g.DrawEllipse(p, x - 10.5f, cy - 10.5f, 21, 21);
    }
    void SetFrom(int px) => Value = min + (int)Math.Round((px - 10) / (float)(Width - 20) * (max - min) / step) * step;
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); SetFrom(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) SetFrom(e.X); }
    protected override bool IsInputKey(Keys k) => k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value -= step;
        else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value += step;
        else if (e.KeyCode == Keys.Home) Value = min;
        else if (e.KeyCode == Keys.End) Value = max;
    }
}

/// Pill buttons: Primary is lime with a glow, Secondary is the outlined "ghost" button, Plain is lime text.
class Btn : Painted
{
    public enum Kind { Primary, Secondary, Plain }
    readonly Kind kind;
    public Btn(string text, Kind kind = Kind.Secondary)
    {
        this.kind = kind; Text = text; Font = kind == Kind.Plain ? Theme.Body : Theme.Strong; Cursor = Cursors.Hand;
        Size = new Size(TextRenderer.MeasureText(text, Font).Width + (kind == Kind.Plain ? 8 : 36), kind == Kind.Primary ? 36 : 32);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = G(e);
        var r = kind == Kind.Primary ? new RectangleF(0.5f, 0.5f, Width - 1, Height - 5) : new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        float rad = r.Height / 2;
        Color fg;
        if (kind == Kind.Primary)
        {
            if (Enabled) Theme.Glow(g, Theme.Accent, r, rad, hover ? 90 : 60);
            Theme.FillRound(g, !Enabled ? Theme.Fill : pressed ? Theme.AccentDeep : hover ? Theme.AccentHover : Theme.Accent, r, rad);
            fg = Enabled ? Theme.OnAccent : Theme.Tertiary;
        }
        else if (kind == Kind.Secondary)
        {
            Theme.FillRound(g, Color.FromArgb(pressed ? 30 : hover && Enabled ? 18 : 8, 244, 244, 242), r, rad);
            Theme.StrokeRound(g, hover && Enabled ? Color.FromArgb(110, 244, 244, 242) : Theme.LineStrong, r, rad);
            fg = Enabled ? Theme.Text : Theme.Tertiary;
        }
        else fg = !Enabled ? Theme.Tertiary : pressed ? Theme.AccentDeep : hover ? Theme.AccentHover : Theme.Accent;
        TextRenderer.DrawText(g, Text, Font, Rectangle.Round(r), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        FocusRing(g, r, rad);
    }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); }
}

/// Shows the current choice and opens a menu on click.
class PopupButton : Painted
{
    public Action<ToolStripItemCollection> Build;
    public event Action<object> Picked;
    public PopupButton(int width = 200) { Size = new Size(width, 30); Cursor = Cursors.Hand; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = G(e); var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.FillRound(g, hover && Enabled ? Theme.FillHover : Theme.Fill, r, 10);
        Theme.StrokeRound(g, hover && Enabled ? Theme.LineStrong : Theme.Line, r, 10);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(12, 0, Width - 36, Height), Enabled ? Theme.Text : Theme.Tertiary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        using (var p = new Pen(Enabled ? Theme.Secondary : Theme.Tertiary, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            float cx = Width - 17, cy = Height / 2f;
            g.DrawLines(p, new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
        }
        FocusRing(g, r, 10);
    }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); Open(); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Down) Open(); }
    void Open()
    {
        if (!Enabled || Build == null) return;
        var menu = Menus.Create();
        Build(menu.Items);
        Menus.Wire(menu.Items, tag => Picked?.Invoke(tag));
        menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);
        menu.Show(this, new Point(0, Height + 4));
    }
}

/// Dark rounded menus (popup buttons, tray icon).
static class Menus
{
    static readonly Color Back = Theme.Fill, Border = Color.FromArgb(42, 47, 58);
    class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Back;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => Theme.Accent;
        public override Color SeparatorDark => Theme.Separator;
        public override Color SeparatorLight => Theme.Separator;
    }
    class Renderer : ToolStripProfessionalRenderer
    {
        public Renderer() : base(new Colors()) { RoundedEdges = false; }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(e.Graphics, Theme.Accent, new RectangleF(5, 1, e.Item.Width - 10, e.Item.Height - 2), 7);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Theme.Tertiary : e.Item.Selected ? Theme.OnAccent : Theme.Text;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) =>
            Theme.Glyph(e.Graphics, "", 9, e.Item.Selected ? Theme.OnAccent : Theme.Accent, new RectangleF(e.ImageRectangle.X, 0, e.ImageRectangle.Width, e.Item.Height));
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = e.Item.Selected ? Theme.OnAccent : Theme.Secondary; base.OnRenderArrow(e); }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var p = new Pen(Theme.Separator)) e.Graphics.DrawLine(p, 10, e.Item.Height / 2, e.Item.Width - 10, e.Item.Height / 2);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var p = new Pen(Border)) e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }
    }
    public static readonly ToolStripRenderer Look = new Renderer();

    public static ContextMenuStrip Create()
    {
        var m = new ContextMenuStrip { Renderer = Look, Font = Theme.Body, ShowImageMargin = false, ShowCheckMargin = true, Padding = new Padding(0, 5, 0, 5) };
        Style(m);
        return m;
    }
    static void Style(ToolStripDropDown d)
    {
        d.HandleCreated += (s, e) => { Theme.RoundCorners(d.Handle); Theme.DarkWindow(d.Handle); };
        d.Opened += (s, e) => Theme.RoundCorners(d.Handle);
    }

    public static ToolStripMenuItem Item(ToolStripItemCollection items, string text, object tag = null, bool check = false)
    {
        var i = new ToolStripMenuItem(text) { Tag = tag, Checked = check, Padding = new Padding(2, 4, 2, 4) };
        items.Add(i); return i;
    }
    public static ToolStripMenuItem Sub(ToolStripItemCollection items, string text, bool check = false)
    {
        var i = Item(items, text, null, check);
        var dd = (ToolStripDropDownMenu)i.DropDown;
        dd.Renderer = Look; dd.ShowImageMargin = false; dd.ShowCheckMargin = true; dd.Padding = new Padding(0, 5, 0, 5); dd.Font = Theme.Body;
        Style(dd);
        return i;
    }
    public static void Header(ToolStripItemCollection items, string text) =>
        items.Add(new ToolStripMenuItem(text) { Enabled = false, Font = Theme.Small, Padding = new Padding(2, 4, 2, 0) });
    public static void Line(ToolStripItemCollection items) => items.Add(new ToolStripSeparator());

    /// Every item with a Tag reports it when clicked (also inside submenus).
    public static void Wire(ToolStripItemCollection items, Action<object> picked)
    {
        foreach (ToolStripItem it in items)
            if (it is ToolStripMenuItem mi)
            {
                if (mi.HasDropDownItems) Wire(mi.DropDownItems, picked);
                else if (mi.Tag != null) mi.Click += (s, e) => picked(mi.Tag);
            }
    }
}

/// Rounded number field (no spinner arrows; arrow keys step).
class NumberField : Panel
{
    readonly TextBox box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Fill, ForeColor = Theme.Text, Font = Theme.Body, TextAlign = HorizontalAlignment.Right };
    readonly int min, max, step; int value;
    public event EventHandler ValueChanged;
    public NumberField(int min, int max, int step, int width = 76)
    {
        this.min = min; this.max = max; this.step = step;
        DoubleBuffered = true; BackColor = Theme.Group; Size = new Size(width, 30);
        box.Width = width - 22; box.Location = new Point(10, (Height - box.PreferredHeight) / 2 + 1);
        Controls.Add(box);
        box.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Up) { Set(value + step); e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { Set(value - step); e.Handled = true; }
        };
        box.Leave += (s, e) => Commit();
        box.GotFocus += (s, e) => Invalidate(); box.LostFocus += (s, e) => Invalidate();
    }
    public int Value { get => value; set { this.value = value; if (!box.Focused) box.Text = value.ToString(); } }
    void Commit() { if (int.TryParse(box.Text.Trim(), out int v)) Set(v); else box.Text = value.ToString(); }
    void Set(int v)
    {
        v = Math.Max(min, Math.Min(max, (int)Math.Round(v / (double)step) * step));
        box.Text = v.ToString();
        if (v != value) { value = v; ValueChanged?.Invoke(this, EventArgs.Empty); }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.FillRound(e.Graphics, Theme.Fill, r, 10);
        Theme.StrokeRound(e.Graphics, box.Focused ? Theme.Accent : Theme.Line, r, 10, box.Focused ? 1.5f : 1f);
    }
}

/// Shows a key combination (or several, for a macro) as key caps; click to record a new one.
class KeyCaps : Painted
{
    List<(int mods, Keys key)> steps = new List<(int, Keys)>();
    bool editable, recording;
    public event Action<int, Keys> Recorded;
    public KeyCaps(int width = 200) { Size = new Size(width, 28); }
    public void Set(IEnumerable<(int, Keys)> s, bool canEdit)
    {
        steps = s.ToList(); editable = canEdit; Cursor = canEdit ? Cursors.Hand : Cursors.Default; TabStop = canEdit; recording = false; Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = G(e); var all = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        if (recording)
        {
            Theme.FillRound(g, Theme.Fill, all, 8);
            Theme.StrokeRound(g, Theme.Accent, all, 8, 1.5f);
            TextRenderer.DrawText(g, "Press the keys", Theme.Body, Rectangle.Round(all), Theme.Secondary, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            return;
        }
        if (steps.Count == 0 || steps.All(s => s.key == Keys.None))
        {
            if (editable) TextRenderer.DrawText(g, "Record shortcut", Theme.Body, new Rectangle(0, 0, Width - 2, Height), hover ? Theme.AccentHover : Theme.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            return;
        }
        const string Then = "\n";   // marks the gap between two steps
        var caps = new List<string>();
        for (int i = 0; i < steps.Count; i++) { if (i > 0) caps.Add(Then); caps.AddRange(KeyNames.Caps(steps[i].mods, steps[i].key)); }
        var widths = caps.Select(c => c == Then ? 10f : TextRenderer.MeasureText(c, Theme.Small).Width + 12f).ToList();
        float x = Math.Max(0, Width - (widths.Sum() + 4 * (caps.Count - 1)));
        for (int i = 0; i < caps.Count && x + widths[i] <= Width + 1; i++)
        {
            var r = new RectangleF(x, 3, widths[i], Height - 6);
            if (caps[i] == Then)
                using (var p = new Pen(Theme.Tertiary, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(p, new[] { new PointF(r.X + 3.5f, r.Y + r.Height / 2 - 3.5f), new PointF(r.X + 7, r.Y + r.Height / 2), new PointF(r.X + 3.5f, r.Y + r.Height / 2 + 3.5f) });
            else
            {
                Theme.FillRound(g, hover && editable ? Theme.Raised : Theme.Fill, r, 6);
                Theme.StrokeRound(g, Theme.Line, r, 6);
                TextRenderer.DrawText(g, caps[i], Theme.Small, Rectangle.Round(r), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            x += widths[i] + 4;
        }
    }
    public void StartRecording() { Focus(); recording = true; Invalidate(); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); if (editable) StartRecording(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); recording = false; Invalidate(); }
    protected override bool IsInputKey(Keys k) => recording || base.IsInputKey(k);
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!recording) return base.ProcessCmdKey(ref msg, keyData);
        var key = keyData & Keys.KeyCode;
        if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin) return true;
        if (key == Keys.Escape && (keyData & Keys.Modifiers) == 0) { recording = false; Invalidate(); return true; }
        int mods = ((keyData & Keys.Control) != 0 ? 1 : 0) | ((keyData & Keys.Shift) != 0 ? 2 : 0) | ((keyData & Keys.Alt) != 0 ? 4 : 0) | (KeyNames.WinDown() ? 8 : 0);
        recording = false; Invalidate();
        Recorded?.Invoke(mods, key);
        return true;
    }
}

class NavItem : Painted
{
    readonly string glyph; readonly Color tint; bool selected;
    public NavItem(string text, string glyph, Color tint) { Text = text; this.glyph = glyph; this.tint = tint; Size = new Size(212, 38); Cursor = Cursors.Hand; TabStop = false; }
    public bool Selected { get => selected; set { selected = value; Invalidate(); } }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = G(e); var r = new RectangleF(0.5f, 1.5f, Width - 1, Height - 3);
        if (selected) Theme.FillRound(g, Theme.Accent, r, r.Height / 2);
        else if (hover) Theme.FillRound(g, Color.FromArgb(14, 244, 244, 242), r, r.Height / 2);
        Theme.Glyph(g, glyph, 11f, selected ? Theme.OnAccent : tint, new RectangleF(10, 0, 28, Height));
        TextRenderer.DrawText(g, Text, selected ? Theme.Strong : Theme.Body, new Rectangle(44, 0, Width - 48, Height),
            selected ? Theme.OnAccent : hover ? Theme.Text : Theme.Secondary, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// A round colour swatch that opens the colour picker.
class ColorWell : Painted
{
    Color color = Color.Black;
    public ColorWell(int size = 28) { Size = new Size(size, size); Cursor = Cursors.Hand; }
    public Color Color { get => color; set { color = value; Invalidate(); } }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = G(e); float d = Math.Min(Width, Height) - 2;
        var outer = new RectangleF(1, 1, d, d); var inner = RectangleF.Inflate(outer, -3.5f, -3.5f);
        using (var p = new Pen(hover || Focused ? Theme.Accent : Theme.LineStrong, 1.5f)) g.DrawEllipse(p, RectangleF.Inflate(outer, -0.75f, -0.75f));
        using (var b = new SolidBrush(Theme.IsOff(color) ? Theme.Fill : color)) g.FillEllipse(b, inner);
        if (Theme.IsOff(color)) using (var p = new Pen(Theme.Tertiary, 1.5f)) g.DrawLine(p, inner.Left + inner.Width * 0.22f, inner.Bottom - inner.Height * 0.22f, inner.Right - inner.Width * 0.22f, inner.Top + inner.Height * 0.22f);
    }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); }
}

/// Top view of the Kone AIMO, traced from a product photo; the shapes below are in that photo's pixels (283 x 500).
/// Lights: 0 wheel, 1-4 left strip and 5-8 right strip (front to back), 9 and 10 the big side panels.
/// Hover a light to find it on the real mouse, click it to change its colour.
class MouseView : Painted
{
    public Color[] Leds = Enumerable.Repeat(Color.Black, 11).ToArray();
    public event Action<int> LedClicked, LedHovered;
    int hot = -1;
    const float Zoom = 0.9f, OffX = 10, OffY = 2;   // photo pixels -> control; the main body (x 150) sits in the middle
    public MouseView() { Size = new Size(290, 460); TabStop = false; }

    static PointF[] P(params float[] xy) => Enumerable.Range(0, xy.Length / 2).Select(i => new PointF(xy[2 * i], xy[2 * i + 1])).ToArray();
    static readonly PointF[] Body = P(150, 74, 183, 75, 217, 80, 233, 90, 245, 103, 253, 133, 258, 167, 260, 200, 262, 233, 260, 267, 258, 300, 260, 333,
        267, 360, 268, 383, 265, 410, 257, 433, 240, 453, 217, 467, 183, 475, 150, 477, 117, 475, 83, 467, 63, 450, 47, 430, 37, 407, 30, 383,
        28, 360, 27, 344, 19, 336, 14, 322, 13, 305, 15, 285, 20, 262, 26, 240, 33, 220, 43, 202, 47, 173, 50, 150, 55, 127, 62, 107, 72, 90, 90, 79, 117, 75);
    static readonly PointF[] Fin = P(44, 204, 57, 212, 59, 340, 28, 343, 17, 327, 14, 305, 17, 282, 22, 255, 31, 225);
    static readonly PointF[] LeftPanel = P(60, 219, 70, 224, 76, 262, 83, 310, 91, 352, 86, 362, 76, 348, 66, 318, 60, 280, 56, 232);
    static readonly PointF[] RightPanel = P(244, 212, 251, 224, 253, 262, 250, 300, 241, 330, 227, 354, 219, 356, 224, 312, 231, 268, 238, 226);
    static readonly PointF[] LeftStrip = P(88, 360, 81, 373, 83, 400, 87, 425, 92, 450);
    static readonly PointF[] RightStrip = P(220, 358, 228, 371, 225, 400, 220, 425, 216, 448);
    static readonly RectangleF Well = new RectangleF(134, 120, 42, 152), Wheel = new RectangleF(141, 130, 28, 68);

    public static string Describe(int led) =>
        led == 0 ? "Wheel" : led <= 4 ? $"Left strip, light {led} of 4 from the front" : led <= 8 ? $"Right strip, light {led - 4} of 4 from the front"
        : led == 9 ? "Left side panel" : "Right side panel";

    static float Dist(PointF a, PointF b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// The stretch of a polyline between fractions t0 and t1 of its length.
    static PointF[] Part(PointF[] line, float t0, float t1)
    {
        var len = new float[line.Length];
        for (int i = 1; i < line.Length; i++) len[i] = len[i - 1] + Dist(line[i - 1], line[i]);
        float total = len[line.Length - 1], a = t0 * total, b = t1 * total;
        PointF At(float d)
        {
            int i = 1; while (i < line.Length - 1 && len[i] < d) i++;
            float f = (d - len[i - 1]) / (len[i] - len[i - 1]);
            return new PointF(line[i - 1].X + (line[i].X - line[i - 1].X) * f, line[i - 1].Y + (line[i].Y - line[i - 1].Y) * f);
        }
        var pts = new List<PointF> { At(a) };
        for (int i = 1; i < line.Length - 1; i++) if (len[i] > a && len[i] < b) pts.Add(line[i]);
        pts.Add(At(b));
        return pts.ToArray();
    }
    static PointF[] Strip(int led) => led <= 4 ? Part(LeftStrip, (led - 1) / 4f + 0.015f, led / 4f - 0.015f) : Part(RightStrip, (led - 5) / 4f + 0.015f, (led - 4) / 4f - 0.015f);
    static GraphicsPath Closed(PointF[] pts, float tension) { var p = new GraphicsPath(); p.AddClosedCurve(pts, tension); return p; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = G(e);
        Theme.Nebula(g, ClientRectangle, (0.5f, 0.48f, 0.5f, 0.42f, 0x2B3FA8, 44), (0.3f, 0.25f, 0.3f, 0.2f, 0x127080, 30));
        Theme.Stars(g, ClientRectangle, 3, 34);
        g.TranslateTransform(OffX, OffY); g.ScaleTransform(Zoom, Zoom);

        using (var cable = new Pen(Color.FromArgb(28, 31, 38), 5) { StartCap = LineCap.Round }) g.DrawLine(cable, 150, 6, 150, 60);
        using (var body = Closed(Body, 0.45f))
        {
            using (var shadow = (GraphicsPath)body.Clone())
            {
                using (var m = new Matrix()) { m.Translate(0, 10); shadow.Transform(m); }
                using (var b = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.FillPath(b, shadow);
            }
            using (var b = new LinearGradientBrush(new RectangleF(13, 70, 256, 410), Color.FromArgb(52, 57, 68), Color.FromArgb(22, 25, 31), 80f)) g.FillPath(b, body);
            using (var shine = new GraphicsPath())
            {
                shine.AddEllipse(95, 90, 110, 300);
                using (var b = new PathGradientBrush(shine) { CenterColor = Color.FromArgb(22, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } }) g.FillPath(b, shine);
            }
            using (var p = new Pen(Color.FromArgb(40, 255, 255, 255), 1.2f)) g.DrawPath(p, body);
        }
        using (var strain = new GraphicsPath()) { strain.AddPolygon(P(143, 58, 157, 58, 162, 76, 138, 76)); using (var b = new SolidBrush(Color.FromArgb(24, 27, 33))) g.FillPath(b, strain); }

        // thumb fin with the two side buttons
        using (var fin = Closed(Fin, 0.3f))
        {
            using (var b = new SolidBrush(Color.FromArgb(30, 33, 40))) g.FillPath(b, fin);
            using (var p = new Pen(Color.FromArgb(14, 16, 20), 1.5f)) { g.DrawPath(p, fin); g.DrawLine(p, 16, 280, 58, 277); }
        }
        using (var p = new Pen(Color.FromArgb(16, 18, 23), 2f)) { g.DrawLine(p, 150, 78, 150, 120); g.DrawEllipse(p, 126, 402, 48, 46); }

        // wheel well: lit wheel, then the + and - buttons
        Theme.FillRound(g, Color.FromArgb(12, 14, 18), Well, 14);
        Light(g, 0, null);
        foreach (var (r, s) in new[] { (new RectangleF(139, 204, 32, 28), "+"), (new RectangleF(139, 237, 32, 29), "-") })
        {
            Theme.FillRound(g, Color.FromArgb(30, 34, 42), r, 6);
            using (var p = new Pen(Color.FromArgb(120, 160, 168, 184), 1.6f))
            {
                float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                g.DrawLine(p, cx - 4, cy, cx + 4, cy);
                if (s == "+") g.DrawLine(p, cx, cy - 4, cx, cy + 4);
            }
        }
        for (int i = 1; i < 11; i++) Light(g, i, null);
        if (hot >= 0) Light(g, hot, Color.White);

        g.ResetTransform();
        TextRenderer.DrawText(g, hot >= 0 ? Describe(hot) : "Hover a light to find it on your mouse", Theme.Small,
            new Rectangle(0, Height - 20, Width, 18), hot >= 0 ? Theme.Text : Theme.Secondary, TextFormatFlags.HorizontalCenter);
    }

    /// Draws light i, or just its outline in `ring` (the hover highlight).
    void Light(Graphics g, int i, Color? ring)
    {
        var c = Leds[i]; bool off = Theme.IsOff(c); var shown = Theme.Led(c);
        if (i == 0)
        {
            if (ring.HasValue) { Theme.StrokeRound(g, ring.Value, RectangleF.Inflate(Wheel, 3, 3), 13, 2.2f); return; }
            if (!off) for (int k = 3; k >= 1; k--) Theme.FillRound(g, Color.FromArgb(22, c), RectangleF.Inflate(Wheel, k * 4, k * 4), 10 + k * 4);
            Theme.FillRound(g, shown, Wheel, 10);
            using (var p = new Pen(Color.FromArgb(off ? 50 : 70, 0, 0, 0), 1.2f)) for (float y = Wheel.Y + 7; y < Wheel.Bottom - 4; y += 7) g.DrawLine(p, Wheel.X + 3, y, Wheel.Right - 3, y);
        }
        else if (i >= 9)
        {
            using (var panel = Closed(i == 9 ? LeftPanel : RightPanel, 0.15f))
            {
                if (ring.HasValue) { using (var p = new Pen(ring.Value, 2.2f) { LineJoin = LineJoin.Round }) g.DrawPath(p, panel); return; }
                if (!off) for (int k = 3; k >= 1; k--) using (var p = new Pen(Color.FromArgb(24, c), k * 7) { LineJoin = LineJoin.Round }) g.DrawPath(p, panel);
                var bounds = panel.GetBounds();
                using (var b = new LinearGradientBrush(bounds, off ? Theme.Fill : ControlPaint.Light(shown, 0.5f), off ? Color.FromArgb(20, 23, 30) : c, 90f)) g.FillPath(b, panel);
                using (var p = new Pen(off ? Theme.Line : Color.FromArgb(90, 255, 255, 255), 1f)) g.DrawPath(p, panel);
            }
        }
        else
        {
            var pts = Strip(i);
            if (ring.HasValue) { using (var p = new Pen(ring.Value, 9) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) g.DrawLines(p, pts); }
            else if (!off) for (int k = 3; k >= 1; k--) using (var p = new Pen(Color.FromArgb(26, c), 4 + k * 6) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) g.DrawLines(p, pts);
            using (var p = new Pen(shown, 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) g.DrawLines(p, pts);
        }
    }

    int Hit(Point at)
    {
        var p = new PointF((at.X - OffX) / Zoom, (at.Y - OffY) / Zoom);
        if (RectangleF.Inflate(Wheel, 4, 4).Contains(p)) return 0;
        for (int i = 9; i <= 10; i++)
            using (var panel = Closed(i == 9 ? LeftPanel : RightPanel, 0.15f))
            using (var pen = new Pen(Color.Black, 10))
                if (panel.IsVisible(p) || panel.IsOutlineVisible(p, pen)) return i;
        for (int i = 1; i <= 8; i++)
        {
            var pts = Strip(i);
            for (int k = 1; k < pts.Length; k++)
            {
                PointF a = pts[k - 1], b = pts[k];
                float dx = b.X - a.X, dy = b.Y - a.Y, t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy)));
                if (Dist(p, new PointF(a.X + t * dx, a.Y + t * dy)) <= 7) return i;
            }
        }
        return -1;
    }
    void SetHot(int h) { if (h == hot) return; hot = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); LedHovered?.Invoke(h); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); SetHot(Hit(e.Location)); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); SetHot(-1); }
    protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); int h = Hit(e.Location); if (h >= 0) LedClicked?.Invoke(h); }
}

/// Hue/saturation disc for the colour picker.
class ColorWheel : Painted
{
    static Bitmap disc;
    float hue, sat, val = 1;
    public event EventHandler Changed;
    public ColorWheel(int size = 220) { Size = new Size(size, size); Cursor = Cursors.Cross; TabStop = false; }
    public float Hue => hue; public float Sat => sat;
    public void Set(float h, float s, float v) { hue = h; sat = s; val = v; Invalidate(); }

    static Bitmap Disc(int size)
    {
        if (disc != null && disc.Width == size) return disc;
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        float r = size / 2f - 1, c = size / 2f;
        var data = bmp.LockBits(new Rectangle(0, 0, size, size), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
        var px = new int[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c, d = (float)Math.Sqrt(dx * dx + dy * dy);
                float a = Math.Max(0, Math.Min(1, r - d + 0.5f));
                if (a <= 0) continue;
                float h = (float)(Math.Atan2(-dy, dx) * 180 / Math.PI); if (h < 0) h += 360;
                var col = ColorPicker.Hsv(h, Math.Min(1, d / r), 1);
                px[y * size + x] = (int)(a * 255) << 24 | col.R << 16 | col.G << 8 | col.B;
            }
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
        return disc = bmp;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = G(e); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(Disc(Width), 0, 0, Width, Height);
        if (val < 1) using (var b = new SolidBrush(Color.FromArgb((int)((1 - val) * 255), 0, 0, 0))) g.FillEllipse(b, 0.5f, 0.5f, Width - 1, Height - 1);
        float rad = Width / 2f - 1, ang = (float)(hue * Math.PI / 180);
        float kx = Width / 2f + (float)Math.Cos(ang) * sat * rad, ky = Height / 2f - (float)Math.Sin(ang) * sat * rad;
        using (var b = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) g.FillEllipse(b, kx - 10, ky - 9, 20, 20);
        using (var b = new SolidBrush(ColorPicker.Hsv(hue, sat, val))) g.FillEllipse(b, kx - 8, ky - 8, 16, 16);
        using (var p = new Pen(Color.White, 3f)) g.DrawEllipse(p, kx - 8, ky - 8, 16, 16);
    }
    void Pick(Point p)
    {
        float dx = p.X - Width / 2f, dy = p.Y - Height / 2f, rad = Width / 2f - 1;
        hue = (float)(Math.Atan2(-dy, dx) * 180 / Math.PI); if (hue < 0) hue += 360;
        sat = Math.Min(1, (float)Math.Sqrt(dx * dx + dy * dy) / rad);
        Invalidate(); Changed?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Pick(e.Location); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) Pick(e.Location); }
}

/// Slider whose track shows a gradient (used for brightness in the colour picker).
class GradientSlider : Painted
{
    int value;
    public Color From = Color.Black, To = Color.White;
    public event EventHandler ValueChanged;
    public GradientSlider(int width = 220) { Size = new Size(width, 26); Cursor = Cursors.Hand; }
    public int Value { get => value; set { int v = Math.Max(0, Math.Min(100, value)); if (v == this.value) return; this.value = v; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); } }
    public void SetQuiet(int v) { value = Math.Max(0, Math.Min(100, v)); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = G(e); var track = new RectangleF(1, Height / 2f - 7, Width - 2, 14);
        using (var b = new LinearGradientBrush(track, From, To, 0f)) using (var p = Theme.Round(track, 7)) g.FillPath(b, p);
        Theme.StrokeRound(g, Theme.Line, track, 7);
        float x = 8 + (Width - 16) * value / 100f, cy = Height / 2f;
        using (var b = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) g.FillEllipse(b, x - 9, cy - 8, 18, 18);
        using (var b = new SolidBrush(Theme.Text)) g.FillEllipse(b, x - 9, cy - 9, 18, 18);
    }
    void SetFrom(int px) => Value = (int)Math.Round((px - 8) / (float)(Width - 16) * 100);
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); SetFrom(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) SetFrom(e.X); }
}
