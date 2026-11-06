using GpxToFusion.Export;
using GpxToFusion.Geo;
using GpxToFusion.Gpx;

namespace GpxToFusion.Map;

/// <summary>
/// The map viewport (= map.png): center in world pixels at <see cref="Zoom"/>, size in pixels.
/// Single source of truth for lat/lon → image pixel, used by the map image, preview and Fusion polyline.
/// </summary>
public sealed record MapFrame(double CenterX, double CenterY, double Zoom, int Width, int Height)
{
    public double Left => CenterX - Width / 2.0;
    public double Top => CenterY - Height / 2.0;

    public (double X, double Y) ToPixel(double lat, double lon)
    {
        var (wx, wy) = WebMercator.ToWorld(lat, lon, Zoom);
        return (wx - Left, wy - Top);
    }

    /// <summary>Export frame: the manual frame when set, otherwise fitted to the (cropped) track.</summary>
    public static MapFrame Fit(Track track, ExportSettings s)
    {
        int maxZoom = s.TileSource.MaxZoom;
        if (s.ManualFrame is { } m)
        {
            double z = Math.Clamp(m.Zoom, 0, maxZoom);
            var (cx, cy) = WebMercator.ToWorld(m.Lat, m.Lon, z);
            return new MapFrame(cx, cy, z, s.Width, s.Height);
        }
        if (track.Count == 0) return new MapFrame(0, 0, 0, s.Width, s.Height);
        var b = track.Bounds();
        double zoom = WebMercator.FitZoom(b.MinLat, b.MinLon, b.MaxLat, b.MaxLon, s.Width, s.Height, s.PaddingPercent / 100.0, maxZoom);
        zoom = Math.Max(0, zoom - Math.Max(0, s.ExtraZoomOut));
        var (x0, y0) = WebMercator.ToWorld(b.MaxLat, b.MinLon, zoom);
        var (x1, y1) = WebMercator.ToWorld(b.MinLat, b.MaxLon, zoom);
        return new MapFrame((x0 + x1) / 2, (y0 + y1) / 2, zoom, s.Width, s.Height);
    }

    /// <summary>Tile zoom used to render at a given output scale (1 = full map.png resolution).</summary>
    public int TileZoom(int maxZoom, double outputScale = 1.0)
    {
        double z = Zoom + Math.Log2(Math.Max(outputScale, 1e-6));
        return Math.Clamp((int)Math.Ceiling(z - 1e-9), 0, maxZoom);
    }

    /// <summary>All tiles covering this frame at tile zoom <paramref name="tileZoom"/>.</summary>
    public List<(int Z, int X, int Y)> Tiles(int tileZoom)
    {
        double s = Math.Pow(2, tileZoom - Zoom); // frame px -> tile-zoom px
        int n = 1 << tileZoom;
        int tx0 = (int)Math.Floor(Left * s / WebMercator.TileSize);
        int tx1 = (int)Math.Floor((Left + Width) * s / WebMercator.TileSize - 1e-9);
        int ty0 = Math.Max(0, (int)Math.Floor(Top * s / WebMercator.TileSize));
        int ty1 = Math.Min(n - 1, (int)Math.Floor((Top + Height) * s / WebMercator.TileSize - 1e-9));
        var list = new List<(int, int, int)>();
        for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++)
                list.Add((tileZoom, tx, ty));
        return list;
    }
}
