using GpxToFusion.Gpx;
using GpxToFusion.Map;

namespace GpxToFusion.Export;

public readonly record struct PathPoint(double X, double Y, double TimeSec, double DistM, int SourceIndex);

/// <summary>The cropped track projected into map.png pixels and simplified.</summary>
public sealed class TrackGeometry
{
    /// <summary>All points after the noise filter (pixel coords).</summary>
    public IReadOnlyList<PathPoint> Full { get; }
    /// <summary>Simplified points (subset of <see cref="Full"/>), used for the Fusion polyline.</summary>
    public IReadOnlyList<PathPoint> Simplified { get; }

    private TrackGeometry(List<PathPoint> full, List<PathPoint> simplified)
    {
        Full = full;
        Simplified = simplified;
    }

    public static TrackGeometry Build(Track cropped, MapFrame frame, double minPointDistanceM, double simplifyTolerancePx)
    {
        var full = new List<PathPoint>(cropped.Count);
        TrackPoint? last = null;
        for (int i = 0; i < cropped.Count; i++)
        {
            var p = cropped.Points[i];
            bool isLast = i == cropped.Count - 1;
            if (last != null && !isLast && minPointDistanceM > 0 && Track.Haversine(last, p) < minPointDistanceM)
                continue;
            var (x, y) = frame.ToPixel(p.Lat, p.Lon);
            full.Add(new PathPoint(x, y, cropped.CumTime[i], cropped.CumDist[i], i));
            last = p;
        }

        var idx = PolylineSimplifier.Simplify(full.Select(p => (p.X, p.Y)).ToList(), simplifyTolerancePx);
        var simplified = idx.Select(i => full[i]).ToList();
        return new TrackGeometry(full, simplified);
    }

    /// <summary>Cumulative 2D length (px) along <paramref name="pts"/>.</summary>
    public static double[] CumulativeLength(IReadOnlyList<PathPoint> pts)
    {
        var c = new double[pts.Count];
        for (int i = 1; i < pts.Count; i++)
            c[i] = c[i - 1] + Math.Sqrt(Math.Pow(pts[i].X - pts[i - 1].X, 2) + Math.Pow(pts[i].Y - pts[i - 1].Y, 2));
        return c;
    }

    /// <summary>
    /// Fraction (0..1) of the simplified polyline length that is drawn at GPX time <paramref name="timeSec"/>
    /// (relative to the cropped start).
    /// </summary>
    public double LengthFractionAtTime(double timeSec, double[]? cumLength = null)
    {
        var pts = Simplified;
        if (pts.Count < 2) return 1;
        cumLength ??= CumulativeLength(pts);
        double total = cumLength[^1];
        if (total <= 0) return 1;
        if (timeSec <= pts[0].TimeSec) return 0;
        if (timeSec >= pts[^1].TimeSec) return 1;

        int lo = 0, hi = pts.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (pts[mid].TimeSec <= timeSec) lo = mid; else hi = mid;
        }
        double dt = pts[hi].TimeSec - pts[lo].TimeSec;
        double f = dt <= 0 ? 1 : (timeSec - pts[lo].TimeSec) / dt;
        return (cumLength[lo] + f * (cumLength[hi] - cumLength[lo])) / total;
    }
}
