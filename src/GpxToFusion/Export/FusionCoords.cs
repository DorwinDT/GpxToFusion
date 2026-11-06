namespace GpxToFusion.Export;

/// <summary>
/// Conversion from image pixels (origin top-left, Y down) to Fusion coordinates.
/// If the polyline does not line up with the map in Resolve, this is the ONLY place to fix.
/// </summary>
public static class FusionCoords
{
    /// <summary>
    /// Fusion polyline (mask) point: origin in the image center, Y up, X normalized by image width and
    /// Y by image height (both range -0.5..0.5). Verified in DaVinci Resolve: scaling Y by H/W squashed the track.
    /// </summary>
    public static (double X, double Y) ToPolyline(double px, double py, int width, int height) =>
        (px / width - 0.5, 0.5 - py / height);

    /// <summary>Fusion "Center"-style point: 0..1 on both axes, origin bottom-left.</summary>
    public static (double X, double Y) ToNormalized(double px, double py, int width, int height) =>
        (px / width, 1.0 - py / height);
}
