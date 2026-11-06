using System.Globalization;
using System.Xml;

namespace GpxToFusion.Gpx;

public static class GpxParser
{
    public static List<TrackPoint> Parse(string path)
    {
        using var stream = File.OpenRead(path);
        return Parse(stream);
    }

    /// <summary>Reads all trkpt (or rtept if there is no track). Namespace-agnostic, segments are concatenated.</summary>
    public static List<TrackPoint> Parse(Stream stream)
    {
        var trackPoints = new List<TrackPoint>();
        var routePoints = new List<TrackPoint>();
        var settings = new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore };
        using var reader = XmlReader.Create(stream, settings);

        reader.MoveToContent();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "trkpt" or "rtept")
            {
                bool isTrk = reader.LocalName == "trkpt";
                var pt = ReadPoint(reader); // leaves reader positioned after the element
                if (pt != null) (isTrk ? trackPoints : routePoints).Add(pt);
                continue;
            }
            reader.Read();
        }
        return trackPoints.Count > 0 ? trackPoints : routePoints;
    }

    private static TrackPoint? ReadPoint(XmlReader reader)
    {
        bool okLat = double.TryParse(reader.GetAttribute("lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out double lat);
        bool okLon = double.TryParse(reader.GetAttribute("lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out double lon);

        double? ele = null;
        DateTime? time = null;

        using (var sub = reader.ReadSubtree())
        {
            sub.Read(); // the trkpt element itself
            sub.Read();
            while (!sub.EOF)
            {
                // ReadElementContentAsString already advances to the next node, so don't Read() after it.
                if (sub.NodeType == XmlNodeType.Element && sub.Depth == 1 && sub.LocalName == "ele")
                {
                    if (double.TryParse(sub.ReadElementContentAsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double e))
                        ele = e;
                }
                else if (sub.NodeType == XmlNodeType.Element && sub.Depth == 1 && sub.LocalName == "time")
                {
                    if (DateTime.TryParse(sub.ReadElementContentAsString(), CultureInfo.InvariantCulture,
                            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t))
                        time = t;
                }
                else
                {
                    sub.Read();
                }
            }
        }
        reader.Read(); // move past the end of trkpt

        return okLat && okLon ? new TrackPoint(lat, lon, ele, time) : null;
    }
}
