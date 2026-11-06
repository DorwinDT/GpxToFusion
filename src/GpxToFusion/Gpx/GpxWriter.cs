using System.Globalization;
using System.Text;
using System.Xml;

namespace GpxToFusion.Gpx;

/// <summary>Writes a minimal GPX 1.1 file (one trk / one trkseg with lat, lon, ele, time).</summary>
public static class GpxWriter
{
    private const string Ns = "http://www.topografix.com/GPX/1/1";

    public static void Write(string path, IReadOnlyList<TrackPoint> points, string? name = null)
    {
        using var stream = File.Create(path);
        Write(stream, points, name);
    }

    public static void Write(Stream stream, IReadOnlyList<TrackPoint> points, string? name = null)
    {
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using var w = XmlWriter.Create(stream, settings);
        var inv = CultureInfo.InvariantCulture;
        w.WriteStartDocument();
        w.WriteStartElement("gpx", Ns);
        w.WriteAttributeString("version", "1.1");
        w.WriteAttributeString("creator", "GpxToFusion");
        w.WriteStartElement("trk", Ns);
        if (!string.IsNullOrEmpty(name)) w.WriteElementString("name", Ns, name);
        w.WriteStartElement("trkseg", Ns);
        foreach (var p in points)
        {
            w.WriteStartElement("trkpt", Ns);
            w.WriteAttributeString("lat", p.Lat.ToString("0.#######", inv));
            w.WriteAttributeString("lon", p.Lon.ToString("0.#######", inv));
            if (p.Ele is { } e) w.WriteElementString("ele", Ns, e.ToString("0.##", inv));
            if (p.Time is { } t) w.WriteElementString("time", Ns, t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFF'Z'", inv));
            w.WriteEndElement();
        }
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndDocument();
    }
}
