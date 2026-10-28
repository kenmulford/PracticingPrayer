using CommunityToolkit.Maui.Views;
using PrayerApp.Helpers;
using System.Text.RegularExpressions;

namespace PrayerApp.Views.Tags;

public partial class ColorPickerPopup : Popup
{
    private readonly PopupResultGate _resultGate = new();
    private static readonly Regex HexPattern = new(@"^#[0-9A-Fa-f]{6}$");

    // HSV state (H: 0-360, S: 0-1, V: 0-1)
    private float _hue = 120f;       // green default
    private float _saturation = 0.6f;
    private float _value = 0.5f;

    private bool _suppressHexUpdate;

    /// <summary>The selected hex string, or null if cancelled.</summary>
    public string? SelectedHex { get; private set; }

    public ColorPickerPopup()
    {
        InitializeComponent();

        SvPicker.Drawable = new SvDrawable(this);
        HueBar.Drawable = new HueBarDrawable(this);

        UpdateFromHsv(updateAccessibility: true);

        Opened += async (_, _) =>
        {
            await Task.Delay(100);
            HexEntry.SetSemanticFocus();
        };
    }

    // ── SV picker touch ─────────────────────────────────────────

    private void OnSvStartInteraction(object? sender, TouchEventArgs e)
        => HandleSvTouch(e.Touches.FirstOrDefault(), updateAccessibility: true);

    private void OnSvDragInteraction(object? sender, TouchEventArgs e)
        => HandleSvTouch(e.Touches.FirstOrDefault());

    private void HandleSvTouch(PointF? point, bool updateAccessibility = false)
    {
        if (point is null) return;
        var w = SvPicker.Width;
        var h = SvPicker.Height;
        if (w <= 0 || h <= 0) return;

        _saturation = Math.Clamp((float)(point.Value.X / w), 0f, 1f);
        _value = Math.Clamp(1f - (float)(point.Value.Y / h), 0f, 1f);

        UpdateFromHsv(updateAccessibility);
    }

    // ── Hue bar touch ───────────────────────────────────────────

    private void OnHueStartInteraction(object? sender, TouchEventArgs e)
        => HandleHueTouch(e.Touches.FirstOrDefault(), updateAccessibility: true);

    private void OnHueDragInteraction(object? sender, TouchEventArgs e)
        => HandleHueTouch(e.Touches.FirstOrDefault());

    private void HandleHueTouch(PointF? point, bool updateAccessibility = false)
    {
        if (point is null) return;
        var w = HueBar.Width;
        if (w <= 0) return;

        _hue = Math.Clamp((float)(point.Value.X / w) * 360f, 0f, 360f);

        UpdateFromHsv(updateAccessibility);
    }

    // ── Hex entry ───────────────────────────────────────────────

    private void OnHexTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressHexUpdate) return;

        var text = e.NewTextValue?.Trim() ?? string.Empty;
        if (text.Length > 0 && text[0] != '#')
            text = "#" + text;

        if (!HexPattern.IsMatch(text)) return;

        try
        {
            var color = Color.FromArgb(text);
            HsvFromColor(color, out _hue, out _saturation, out _value);
            UpdatePreview();
            SvPicker.Invalidate();
            HueBar.Invalidate();

            var name = GetApproximateColorName(_hue, _saturation, _value);
            SemanticProperties.SetDescription(PreviewBox.Parent as VisualElement ?? PreviewBox,
                $"Color preview: {name}, {text.ToUpperInvariant()}");
        }
        catch { /* ignore parse errors while typing */ }
    }

    // ── Buttons ─────────────────────────────────────────────────

    private async void OnCancel(object? sender, EventArgs e)
    {
        await _resultGate.CompleteAsync(() => this.TryCloseAsync(), () => SelectedHex = null);
    }

    private async void OnAccept(object? sender, EventArgs e)
    {
        await _resultGate.CompleteAsync(
            () => this.TryCloseAsync(),
            () => SelectedHex = ToHexString(ColorFromHsv(_hue, _saturation, _value)),
            () => SelectedHex = null);
    }

    // ── Update helpers ──────────────────────────────────────────

    private void UpdateFromHsv(bool updateAccessibility = false)
    {
        UpdatePreview();
        SvPicker.Invalidate();
        HueBar.Invalidate();

        // Update hex entry text
        var color = ColorFromHsv(_hue, _saturation, _value);
        var hex = ToHexString(color);
        _suppressHexUpdate = true;
        HexEntry.Text = hex;
        _suppressHexUpdate = false;

        // Only update accessible description on touch-start / hex entry, not every drag frame
        if (updateAccessibility)
        {
            var name = GetApproximateColorName(_hue, _saturation, _value);
            SemanticProperties.SetDescription(PreviewBox.Parent as VisualElement ?? PreviewBox,
                $"Color preview: {name}, {hex}");
        }
    }

    private void UpdatePreview()
    {
        var color = ColorFromHsv(_hue, _saturation, _value);
        PreviewBox.Color = color;
    }

    // ── HSV ↔ Color conversion ──────────────────────────────────

    internal static Color ColorFromHsv(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
        float m = v - c;

        float r, g, b;
        if (h < 60)       { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else              { r = c; g = 0; b = x; }

        return new Color(r + m, g + m, b + m);
    }

    internal static void HsvFromColor(Color color, out float h, out float s, out float v)
    {
        float r = color.Red, g = color.Green, b = color.Blue;
        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float delta = max - min;

        v = max;
        s = max == 0 ? 0 : delta / max;

        if (delta == 0)
        {
            h = 0;
        }
        else if (max == r)
        {
            h = 60f * (((g - b) / delta) % 6f);
        }
        else if (max == g)
        {
            h = 60f * (((b - r) / delta) + 2f);
        }
        else
        {
            h = 60f * (((r - g) / delta) + 4f);
        }

        if (h < 0) h += 360f;
    }

    private static string ToHexString(Color c)
    {
        int r = (int)(c.Red * 255);
        int g = (int)(c.Green * 255);
        int b = (int)(c.Blue * 255);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    // ── Accessible color name ─────────────────────────────────

    internal static string GetApproximateColorName(float h, float s, float v)
    {
        if (v < 0.15f) return "Black";
        if (s < 0.10f)
        {
            if (v > 0.85f) return "White";
            if (v > 0.5f) return "Light gray";
            return "Dark gray";
        }

        var prefix = v < 0.4f ? "Dark " : s < 0.25f && v > 0.8f ? "Pastel " : s < 0.5f ? "Light " : "";

        var name = h switch
        {
            < 15 => "Red",
            < 40 => "Orange",
            < 70 => "Yellow",
            < 160 => "Green",
            < 195 => "Teal",
            < 250 => "Blue",
            < 290 => "Purple",
            < 330 => "Pink",
            _ => "Red"
        };

        return prefix + name;
    }

    // ── Drawables ───────────────────────────────────────────────

    /// <summary>
    /// Draws the saturation (X) / value (Y) gradient area for the current hue,
    /// with a circle indicator at the selected position.
    /// </summary>
    private class SvDrawable : IDrawable
    {
        private readonly ColorPickerPopup _picker;
        public SvDrawable(ColorPickerPopup picker) => _picker = picker;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            float w = dirtyRect.Width;
            float h = dirtyRect.Height;
            if (w <= 0 || h <= 0) return;

            // Draw in vertical strips for smooth gradient
            int steps = (int)Math.Min(w, 80);
            float stripW = w / steps;

            for (int i = 0; i < steps; i++)
            {
                float sat = (float)i / (steps - 1);
                float x = i * stripW;

                // Top color: hue at this saturation, full value
                var topColor = ColorFromHsv(_picker._hue, sat, 1f);
                // Bottom: always black
                var bottomColor = Colors.Black;

                // Vertical gradient from topColor to black
                var paint = new LinearGradientPaint
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops = new PaintGradientStop[]
                    {
                        new(0f, topColor),
                        new(1f, bottomColor)
                    }
                };

                canvas.SetFillPaint(paint, new RectF(x, 0, stripW + 1, h));
                canvas.FillRectangle(x, 0, stripW + 1, h);
            }

            // White-to-transparent overlay on the left (value axis)
            // Already handled by the strip approach above

            // Round the corners with a clip
            var clipPath = new PathF();
            float radius = 8f;
            clipPath.AppendRoundedRectangle(dirtyRect, radius);

            // Draw indicator circle
            float cx = _picker._saturation * w;
            float cy = (1f - _picker._value) * h;
            float r = 8f;

            // Outer ring (white for contrast)
            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 3f;
            canvas.DrawCircle(cx, cy, r);

            // Inner ring (dark for contrast on light areas)
            canvas.StrokeColor = new Color(0, 0, 0, 0.3f);
            canvas.StrokeSize = 1f;
            canvas.DrawCircle(cx, cy, r + 1.5f);
        }
    }

    /// <summary>
    /// Draws the horizontal hue rainbow bar with a position indicator.
    /// </summary>
    private class HueBarDrawable : IDrawable
    {
        private readonly ColorPickerPopup _picker;
        public HueBarDrawable(ColorPickerPopup picker) => _picker = picker;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            float w = dirtyRect.Width;
            float h = dirtyRect.Height;
            if (w <= 0 || h <= 0) return;

            // Draw rainbow gradient in strips
            int steps = (int)Math.Min(w, 60);
            float stripW = w / steps;

            for (int i = 0; i < steps; i++)
            {
                float hue = (float)i / (steps - 1) * 360f;
                var color = ColorFromHsv(hue, 1f, 1f);
                canvas.FillColor = color;
                canvas.FillRectangle(i * stripW, 0, stripW + 1, h);
            }

            // Draw indicator
            float ix = (_picker._hue / 360f) * w;
            float indicatorR = h / 2f - 1f;

            // White circle indicator
            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 3f;
            canvas.DrawCircle(ix, h / 2f, indicatorR);

            canvas.StrokeColor = new Color(0, 0, 0, 0.3f);
            canvas.StrokeSize = 1f;
            canvas.DrawCircle(ix, h / 2f, indicatorR + 1.5f);
        }
    }
}
