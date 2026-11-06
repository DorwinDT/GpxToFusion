using GpxToFusion.Export;
using SkiaSharp;

namespace GpxToFusion.Map;

/// <summary>
/// Draws map labels with Skia. Shared by the preview and by "bake into map.png", so both look the same.
/// </summary>
public static class LabelRenderer
{
    /// <param name="sizeScale">Label px at 1080p → canvas px (export: min(W,H)/1080; preview: that × view scale).</param>
    /// <returns>Bounding box of every label on the canvas (for hit-testing in the preview).</returns>
    public static List<(MapLabel Label, SKRect Bounds)> Draw(SKCanvas canvas, MapFrame frame, IEnumerable<MapLabel> labels, double sizeScale)
    {
        var result = new List<(MapLabel, SKRect)>();
        foreach (var label in labels)
        {
            var (x, y) = frame.ToPixel(label.Lat, label.Lon);
            result.Add((label, DrawOne(canvas, label, (float)x, (float)y, sizeScale)));
        }
        return result;
    }

    public static SKRect DrawOne(SKCanvas canvas, MapLabel label, float x, float y, double sizeScale)
    {
        float size = (float)Math.Max(1, label.FontSize1080 * sizeScale);
        float outline = (float)Math.Max(0, label.OutlineWidth1080 * sizeScale);
        using var typeface = SKTypeface.FromFamilyName(label.FontName,
            label.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal,
            label.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        using var font = new SKFont(typeface, size) { Subpixel = true, Edging = SKFontEdging.Antialias };
        var lines = SplitLines(label.Text);
        float lineH = font.Spacing;
        float ascent = -font.Metrics.Ascent;
        float top = y - lineH * lines.Length / 2; // text block centered vertically on the point (like Text+)

        var tc = label.TextColor;
        var oc = label.OutlineColor;
        using var fill = new SKPaint { IsAntialias = true, Color = new SKColor(tc.R, tc.G, tc.B) };
        using var stroke = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = outline * 2, StrokeJoin = SKStrokeJoin.Round,
            Color = new SKColor(oc.R, oc.G, oc.B),
        };

        var bounds = SKRect.Empty;
        for (int i = 0; i < lines.Length; i++)
        {
            float w = font.MeasureText(lines[i]);
            float lx = label.Align switch
            {
                LabelAlign.Left => x,
                LabelAlign.Right => x - w,
                _ => x - w / 2,
            };
            float baseline = top + i * lineH + ascent;
            if (outline > 0) canvas.DrawText(lines[i], lx, baseline, SKTextAlign.Left, font, stroke);
            canvas.DrawText(lines[i], lx, baseline, SKTextAlign.Left, font, fill);
            var r = new SKRect(lx - outline, top + i * lineH - outline, lx + w + outline, top + (i + 1) * lineH + outline);
            bounds = bounds.IsEmpty ? r : SKRect.Union(bounds, r);
        }
        return bounds;
    }

    public static string[] SplitLines(string? text) =>
        (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
}
