namespace GpxToFusion.Gpx;

public sealed record TrackPoint(double Lat, double Lon, double? Ele, DateTime? Time);
