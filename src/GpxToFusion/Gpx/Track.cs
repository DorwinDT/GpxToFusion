namespace GpxToFusion.Gpx;

public sealed class Track
{
    public IReadOnlyList<TrackPoint> Points { get; }
    /// <summary>Cumulative distance in meters from the first point.</summary>
    public double[] CumDist { get; }
    /// <summary>Cumulative time in seconds from the first point (index if times are missing).</summary>
    public double[] CumTime { get; }
    public bool HasTime { get; }

    public int Count => Points.Count;
    public double TotalDistance => Count > 0 ? CumDist[^1] : 0;
    public double TotalSeconds => Count > 0 ? CumTime[^1] : 0;

    public Track(IReadOnlyList<TrackPoint> points)
    {
        Points = points;
        CumDist = new double[points.Count];
        CumTime = new double[points.Count];
        HasTime = points.Count > 0 && points.All(p => p.Time.HasValue);
        for (int i = 1; i < points.Count; i++)
        {
            CumDist[i] = CumDist[i - 1] + Haversine(points[i - 1], points[i]);
            CumTime[i] = HasTime ? (points[i].Time!.Value - points[0].Time!.Value).TotalSeconds : i;
        }
    }

    public Track Sub(int start, int end)
    {
        start = Math.Clamp(start, 0, Math.Max(0, Count - 1));
        end = Math.Clamp(end, start, Math.Max(0, Count - 1));
        var list = new List<TrackPoint>(end - start + 1);
        for (int i = start; i <= end && i < Count; i++) list.Add(Points[i]);
        return new Track(list);
    }

    public (double MinLat, double MinLon, double MaxLat, double MaxLon) Bounds()
    {
        double minLat = double.MaxValue, minLon = double.MaxValue, maxLat = double.MinValue, maxLon = double.MinValue;
        foreach (var p in Points)
        {
            minLat = Math.Min(minLat, p.Lat); maxLat = Math.Max(maxLat, p.Lat);
            minLon = Math.Min(minLon, p.Lon); maxLon = Math.Max(maxLon, p.Lon);
        }
        return (minLat, minLon, maxLat, maxLon);
    }

    /// <summary>Speed in km/h around point i, averaged over ±window seconds.</summary>
    public double SpeedKmh(int i, double windowSec = 15)
    {
        if (Count < 2) return 0;
        int a = i, b = i;
        while (a > 0 && CumTime[i] - CumTime[a] < windowSec) a--;
        while (b < Count - 1 && CumTime[b] - CumTime[i] < windowSec) b++;
        double dt = CumTime[b] - CumTime[a];
        return dt <= 0 ? 0 : (CumDist[b] - CumDist[a]) / dt * 3.6;
    }

    public static double Haversine(TrackPoint a, TrackPoint b) => Haversine(a.Lat, a.Lon, b.Lat, b.Lon);

    public static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371008.8;
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLon = (lon2 - lon1) * Math.PI / 180;
        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}
