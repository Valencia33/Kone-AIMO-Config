using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

/// Colour picker: hue/saturation wheel, brightness, LED-friendly swatches, recent colours, hex.
/// Every change is reported to `live` so the real mouse can show it while you pick.
class ColorPicker : Form
{
    static readonly Color[] Swatches =
    {
        Color.FromArgb(255, 0, 0), Color.FromArgb(255, 80, 0), Color.FromArgb(255, 170, 0), Color.FromArgb(255, 230, 0),
        Color.FromArgb(120, 255, 0), Color.FromArgb(0, 255, 0), Color.FromArgb(0, 255, 150), Color.FromArgb(0, 220, 255),
        Color.FromArgb(0, 90, 255), Color.FromArgb(0, 0, 255), Color.FromArgb(120, 0, 255), Color.FromArgb(200, 0, 255),
        Color.FromArgb(255, 0, 200), Color.FromArgb(255, 0, 80), Color.FromArgb(255, 210, 150), Color.FromArgb(255, 255, 255),
    };
    readonly ColorWheel wheel = new ColorWheel(224);
    readonly GradientSlider brightness = new GradientSlider(224);
    readonly ColorWell newWell = new ColorWell(58) { TabStop = false, Cursor = Cursors.Default }, oldWell = new ColorWell(58) { TabStop = false };
    readonly TextBox hex = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.FillHover, ForeColor = Theme.Text, Font = Theme.Body, Width = 96 };
    readonly Action<Color> live;
    readonly Color original;
    Color result;
    bool syncing;

    ColorPicker(string what, Color start, Action<Color> live)
    {
        this.live = live; original = start;
        Text = "Colour"; ClientSize = new Size(476, 470); BackColor = Theme.Group; ForeColor = Theme.Text; Font = Theme.Body;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent; KeyPreview = true;

        Add(Theme.Lbl(what, Theme.Big), 24, 18);
        Add(wheel, 24, 58);
        Add(Theme.Lbl("Brightness", Theme.Small, Theme.Secondary), 26, 294);
        Add(brightness, 24, 312);

        int x = 280;
        Add(Theme.Lbl("New", Theme.Small, Theme.Secondary), x + 4, 58); Add(newWell, x, 76);
        Add(Theme.Lbl("Current", Theme.Small, Theme.Secondary), x + 76, 58); Add(oldWell, x + 72, 76);
        oldWell.Color = start; oldWell.Click += (s, e) => SetColor(original);
        Add(Theme.Lbl("Hex", Theme.Small, Theme.Secondary), x + 4, 148);
        var hexBox = new Rounded { Size = new Size(124, 28) }; hex.Location = new Point(10, 6); hexBox.Controls.Add(hex);
        Add(hexBox, x, 166);
        hex.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { FromHex(); e.SuppressKeyPress = true; } };
        hex.Leave += (s, e) => FromHex();

        Add(Theme.Lbl("Colours", Theme.Small, Theme.Secondary), x + 4, 212);
        var grid = new FlowLayoutPanel { Size = new Size(176, 150), BackColor = Color.Transparent };
        foreach (var c in Swatches) grid.Controls.Add(Swatch(c));
        Add(grid, x, 230);

        var recent = AppConfig.RecentColors;
        if (recent.Count > 0)
        {
            Add(Theme.Lbl("Recent", Theme.Small, Theme.Secondary), 26, 352);
            var row = new FlowLayoutPanel { Size = new Size(240, 34), BackColor = Color.Transparent, WrapContents = false };
            foreach (var c in recent.Take(7)) row.Controls.Add(Swatch(c));
            Add(row, 24, 370);
        }

        var done = new Btn("Done", Btn.Kind.Primary); var cancel = new Btn("Cancel");
        Add(done, ClientSize.Width - done.Width - 24, ClientSize.Height - done.Height - 20);
        Add(cancel, done.Left - cancel.Width - 10, done.Top);
        done.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        wheel.Changed += (s, e) => FromControls();
        brightness.ValueChanged += (s, e) => FromControls();
        SetColor(start);
    }

    public static void Snapshot(string path)
    {
        using (var f = new ColorPicker("Left strip 2", Color.FromArgb(255, 80, 0), null) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000) })
        {
            f.Show(); Application.DoEvents();
            using (var bmp = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height)); bmp.Save(path); }
        }
    }

    void Add(Control c, int x, int y) { c.Location = new Point(x, y); Controls.Add(c); }
    ColorWell Swatch(Color c) { var w = new ColorWell(32) { Color = c, Margin = new Padding(0, 0, 4, 4) }; w.Click += (s, e) => SetColor(c); return w; }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkWindow(Handle); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        if (e.KeyCode == Keys.Enter && !hex.Focused) { DialogResult = DialogResult.OK; Close(); }
    }

    public static Color? Pick(IWin32Window owner, string what, Color start, Action<Color> live = null)
    {
        using (var f = new ColorPicker(what, start, live))
        {
            if (f.ShowDialog(owner) != DialogResult.OK) return null;
            AppConfig.AddRecentColor(f.result);
            return f.result;
        }
    }

    void SetColor(Color c)
    {
        var (h, s, v) = ToHsv(c);
        syncing = true;
        wheel.Set(h, s, Math.Max(v, 0.0001f)); brightness.SetQuiet((int)Math.Round(v * 100));
        syncing = false;
        Display(c);
    }

    void FromControls()
    {
        if (syncing) return;
        float v = brightness.Value / 100f;
        wheel.Set(wheel.Hue, wheel.Sat, v);
        Display(Hsv(wheel.Hue, wheel.Sat, v));
    }

    void Display(Color c)
    {
        result = c; newWell.Color = c;
        brightness.To = Hsv(wheel.Hue, wheel.Sat, 1); brightness.Invalidate();
        if (!hex.Focused) hex.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        live?.Invoke(c);
    }

    void FromHex()
    {
        var t = hex.Text.Trim().TrimStart('#');
        if (t.Length == 6 && int.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out int v)) SetColor(Color.FromArgb(v >> 16 & 255, v >> 8 & 255, v & 255));
        else hex.Text = $"#{result.R:X2}{result.G:X2}{result.B:X2}";
    }

    public static Color Hsv(float h, float s, float v)
    {
        double hh = (h % 360) / 60.0; int i = (int)Math.Floor(hh) % 6; double f = hh - Math.Floor(hh);
        double V = v, p = V * (1 - s), q = V * (1 - f * s), t = V * (1 - (1 - f) * s);
        var (r, g, b) = i == 0 ? (V, t, p) : i == 1 ? (q, V, p) : i == 2 ? (p, V, t) : i == 3 ? (p, q, V) : i == 4 ? (t, p, V) : (V, p, q);
        return Color.FromArgb((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
    }
    static (float h, float s, float v) ToHsv(Color c)
    {
        float max = Math.Max(c.R, Math.Max(c.G, c.B)) / 255f, min = Math.Min(c.R, Math.Min(c.G, c.B)) / 255f;
        return (c.GetHue(), max == 0 ? 0 : (max - min) / max, max);
    }

    /// Rounded dark background for the hex box.
    class Rounded : Panel
    {
        public Rounded() { DoubleBuffered = true; BackColor = Theme.Group; }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Theme.FillRound(e.Graphics, Theme.FillHover, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6); }
    }
}
