using GpxToFusion.Geo;
using SkiaSharp;

namespace GpxToFusion.Map;

/// <summary>Draws map tiles for a <see cref="MapFrame"/>.</summary>
public static class MapComposer
{
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    /// <summary>
    /// Draws tiles into <paramref name="canvas"/> in frame-pixel coordinates (0..Width, 0..Height).
    /// The canvas may already be scaled (preview); <paramref name="outputScale"/> tells how many device pixels one frame pixel has.
    /// </summary>
    /// <summary>How many zoom levels up to look for a parent tile when the server has no tile.</summary>
    public const int MaxFallbackLevels = 6;

    /// <param name="getTile">Returns a tile or null (not loaded yet).</param>
    /// <param name="isNotFound">True if the tile does not exist on the server; a parent tile is upscaled instead.</param>
    /// <returns>Number of tiles that were missing.</returns>
    public static int DrawTiles(SKCanvas canvas, MapFrame frame, TileSource src, double outputScale,
        Func<int, int, int, SKImage?> getTile, Func<int, int, int, bool>? isNotFound = null)
    {
        int tz = frame.TileZoom(src.MaxZoom, outputScale);
        double s = Math.Pow(2, frame.Zoom - tz); // tile-zoom px -> frame px
        double tileSize = WebMercator.TileSize * s;
        int missing = 0;

        using var placeholder = new SKPaint { Color = new SKColor(0xDD, 0xDD, 0xDD) };
        using var paint = new SKPaint { IsAntialias = false };

        foreach (var (z, tx, ty) in frame.Tiles(tz))
        {
            // Integer-snapped edges (in device px) avoid hairline seams between tiles.
            float l = Snap(tx * tileSize - frame.Left, outputScale);
            float t = Snap(ty * tileSize - frame.Top, outputScale);
            float r = Snap((tx + 1) * tileSize - frame.Left, outputScale);
            float b = Snap((ty + 1) * tileSize - frame.Top, outputScale);
            var dest = new SKRect(l, t, r, b);

            var img = getTile(z, tx, ty);
            if (img != null)
            {
                canvas.DrawImage(img, new SKRect(0, 0, img.Width, img.Height), dest, Sampling, paint);
            }
            else if (isNotFound != null && isNotFound(z, tx, ty) && TryGetParent(z, tx, ty, getTile, isNotFound) is var (pimg, srcRect))
            {
                canvas.DrawImage(pimg, srcRect, dest, Sampling, paint);
            }
            else
            {
                missing++;
                canvas.DrawRect(dest, placeholder);
            }
        }
        return missing;
    }

    /// <summary>Finds the nearest available ancestor tile and the sub-rectangle of it that covers (z, x, y).</summary>
    private static (SKImage Image, SKRect Src)? TryGetParent(int z, int x, int y,
        Func<int, int, int, SKImage?> getTile, Func<int, int, int, bool> isNotFound)
    {
        for (int d = 1; d <= MaxFallbackLevels && z - d >= 0; d++)
        {
            int pz = z - d, px = x >> d, py = y >> d;
            var img = getTile(pz, px, py);
            if (img != null)
            {
                int n = 1 << d;
                float size = (float)img.Width / n;
                float sx = (x - (px << d)) * size, sy = (y - (py << d)) * size;
                return (img, new SKRect(sx, sy, sx + size, sy + size));
            }
            if (!isNotFound(pz, px, py)) return null; // parent still loading (or failed) – don't go further up
        }
        return null;
    }

    private static float Snap(double framePx, double outputScale) =>
        (float)(Math.Round(framePx * outputScale) / outputScale);

    public static void DrawDarken(SKCanvas canvas, MapFrame frame, int darkenPercent)
    {
        if (darkenPercent <= 0) return;
        byte a = (byte)Math.Clamp(darkenPercent * 255 / 100, 0, 204);
        using var p = new SKPaint { Color = new SKColor(0, 0, 0, a) };
        canvas.DrawRect(0, 0, frame.Width, frame.Height, p);
    }

    public static void DrawAttribution(SKCanvas canvas, MapFrame frame, string text)
    {
        float size = Math.Max(10f, Math.Min(frame.Width, frame.Height) / 1080f * 14f);
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), size);
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 200) };
        using var bg = new SKPaint { Color = new SKColor(255, 255, 255, 170) };
        float w = font.MeasureText(text);
        float pad = size * 0.4f;
        var rect = new SKRect(frame.Width - w - pad * 2, frame.Height - size - pad * 1.5f, frame.Width, frame.Height);
        canvas.DrawRect(rect, bg);
        canvas.DrawText(text, rect.Left + pad, frame.Height - pad, SKTextAlign.Left, font, paint);
    }

    /// <summary>Renders the full-resolution map.png (all tiles must be prefetched into <paramref name="cache"/>).</summary>
    public static SKBitmap RenderMap(MapFrame frame, TileSource src, TileCache cache, int darkenPercent, bool attribution)
        => RenderMap(frame, src, cache, darkenPercent, attribution, out _);

    public static SKBitmap RenderMap(MapFrame frame, TileSource src, TileCache cache, int darkenPercent, bool attribution,
        out int missingTiles)
    {
        var bmp = new SKBitmap(new SKImageInfo(frame.Width, frame.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        missingTiles = DrawTiles(canvas, frame, src, 1.0, (z, x, y) => cache.GetAsync(src, z, x, y, CancellationToken.None).GetAwaiter().GetResult(),
            (z, x, y) => cache.IsNotFound(src, z, x, y));
        DrawDarken(canvas, frame, darkenPercent);
        if (attribution) DrawAttribution(canvas, frame, src.Attribution);
        canvas.Flush();
        return bmp;
    }
}
