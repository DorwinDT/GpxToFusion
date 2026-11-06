namespace GpxToFusion.Gpx;

/// <summary>A point the user added on the map (before the tracker was switched on / after it was switched off).</summary>
public sealed class GeoPoint
{
    public double Lat { get; set; }
    public double Lon { get; set; }

    public GeoPoint() { }
    public GeoPoint(double lat, double lon) { Lat = lat; Lon = lon; }

    public GeoPoint Clone() => new(Lat, Lon);
}

/// <summary>
/// Builds the effective track: user-added points before the start and after the end of the recorded GPX.
/// Both chains are stored in "outward" order: index 0 is next to the recorded track, the last item is farthest from it.
/// Added points get synthetic times (constant speed) and the elevation of the nearest recorded point,
/// so the timeline and time-based keyframes keep working.
/// </summary>
public static class TrackExtension
{
    public static List<TrackPoint> Extend(IReadOnlyList<TrackPoint> recorded, IReadOnlyList<GeoPoint> before,
        IReadOnlyList<GeoPoint> after, double speedKmh)
    {
        var result = new List<TrackPoint>(recorded.Count + before.Count + after.Count);
        if (recorded.Count == 0) return result;
        double speed = speedKmh > 0 ? speedKmh : MovingSpeedKmh(recorded);
        if (speed <= 0.1) speed = 4.5;
        double mps = speed / 3.6;

        // Before: walk outward from the first recorded point, going back in time, then reverse.
        var first = recorded[0];
        var prepend = new List<TrackPoint>(before.Count);
        TrackPoint prev = first;
        foreach (var g in before)
        {
            double d = Track.Haversine(prev.Lat, prev.Lon, g.Lat, g.Lon);
            var p = new TrackPoint(g.Lat, g.Lon, first.Ele, prev.Time?.AddSeconds(-d / mps));
            prepend.Add(p);
            prev = p;
        }
        prepend.Reverse();
        result.AddRange(prepend);
        result.AddRange(recorded);

        var last = recorded[^1];
        prev = last;
        foreach (var g in after)
        {
            double d = Track.Haversine(prev.Lat, prev.Lon, g.Lat, g.Lon);
            var p = new TrackPoint(g.Lat, g.Lon, last.Ele, prev.Time?.AddSeconds(d / mps));
            result.Add(p);
            prev = p;
        }
        return result;
    }

    /// <summary>Average speed while moving (segments faster than 1 km/h), 0 when the track has no times.</summary>
    public static double MovingSpeedKmh(IReadOnlyList<TrackPoint> pts)
    {
        double dist = 0, secs = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            if (pts[i].Time is not { } t1 || pts[i - 1].Time is not { } t0) return 0;
            double dt = (t1 - t0).TotalSeconds;
            if (dt <= 0) continue;
            double d = Track.Haversine(pts[i - 1], pts[i]);
            if (d / dt * 3.6 < 1) continue;
            dist += d;
            secs += dt;
        }
        return secs > 0 ? dist / secs * 3.6 : 0;
    }
}
