namespace GpxToFusion.Geo;

public static class WebMercator
{
    public const int TileSize = 256;
    public const double MaxLat = 85.05112878;

    /// <summary>World pixel coordinates at given (fractional) zoom.</summary>
    public static (double X, double Y) ToWorld(double lat, double lon, double zoom)
    {
        lat = Math.Clamp(lat, -MaxLat, MaxLat);
        double scale = TileSize * Math.Pow(2, zoom);
        double x = (lon + 180.0) / 360.0 * scale;
        double sin = Math.Sin(lat * Math.PI / 180);
        double y = (0.5 - Math.Log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * scale;
        return (x, y);
    }

    public static (double Lat, double Lon) FromWorld(double x, double y, double zoom)
    {
        double scale = TileSize * Math.Pow(2, zoom);
        double lon = x / scale * 360.0 - 180.0;
        double n = Math.PI - 2 * Math.PI * y / scale;
        double lat = 180.0 / Math.PI * Math.Atan(Math.Sinh(n));
        return (lat, lon);
    }

    /// <summary>
    /// Fractional zoom so that the bbox fits into width x height, keeping paddingFraction of the size free on each side.
    /// </summary>
    public static double FitZoom(double minLat, double minLon, double maxLat, double maxLon, int width, int height,
        double paddingFraction, double maxZoom = 20)
    {
        var (x0, y0) = ToWorld(maxLat, minLon, 0);
        var (x1, y1) = ToWorld(minLat, maxLon, 0);
        double bw = Math.Max(Math.Abs(x1 - x0), 1e-9);
        double bh = Math.Max(Math.Abs(y1 - y0), 1e-9);
        double f = Math.Clamp(1 - 2 * paddingFraction, 0.05, 1);
        double z = Math.Log2(Math.Min(width * f / bw, height * f / bh));
        return Math.Clamp(z, 0, maxZoom);
    }
}
