using System.Globalization;
using System.Text;
using GpxToFusion.Gpx;
using GpxToFusion.Map;
using SkiaSharp;

namespace GpxToFusion.Export;

public sealed record ExportResult(string Folder, string MapPng, string SettingFile, int PolylinePoints, int MissingTiles);

public static class Exporter
{
    public const string MapFileName = "map.png";
    public const string TrackFileName = "track.png";
    public const string SettingFileName = "GpxTrack.setting";
    public const string CsvFileName = "track_points.csv";

    public static FusionSettingWriter.Options FusionOptions(ExportSettings s, string mapPngPath, MapFrame frame)
    {
        string? marker = string.IsNullOrWhiteSpace(s.MarkerImagePath) ? null : s.MarkerImagePath.Trim().Trim('"');
        double markerSize = 1;
        if (marker != null)
        {
            if (!File.Exists(marker))
                throw new FileNotFoundException($"Line head image does not exist:\n{marker}", marker);
            using var codec = SKCodec.Create(marker)
                ?? throw new InvalidDataException($"Line head image cannot be loaded:\n{marker}");
            // Merge keeps the foreground's pixel size, so Size = wanted px / image px.
            markerSize = s.MarkerSizePx / Math.Max(1, Math.Max(codec.Info.Width, codec.Info.Height));
        }

        return new(
            mapPngPath, s.Width, s.Height, s.LineColor, s.LineWidthPx,
            s.AddKeyframes, s.KeyframeFrames, s.KeyframesByTime, s.AddCalibrationFrame,
            s.MapSource == FusionMapSource.Loader, s.KeyframesFitClip,
            s.AnimStartDelayFrames, s.AnimEndHoldFrames,
            marker, markerSize,
            s.LabelOutput == LabelTarget.FusionText ? FusionLabels(s, frame) : null);
    }

    /// <summary>Labels converted to map.png pixels for Text+ nodes.</summary>
    public static List<FusionSettingWriter.Label> FusionLabels(ExportSettings s, MapFrame frame) =>
        s.Labels.Where(l => !string.IsNullOrWhiteSpace(l.Text)).Select(l =>
        {
            var (x, y) = frame.ToPixel(l.Lat, l.Lon);
            return new FusionSettingWriter.Label(l.Text, x, y, l.FontName, l.FontStyleName, l.FontSize1080 * s.LabelSizeScale,
                l.TextColor, l.OutlineWidth1080 * s.LabelSizeScale, l.OutlineColor, l.Align);
        }).ToList();

    /// <summary>Builds the .setting text only (no files) – used for "copy to clipboard".</summary>
    public static string BuildSetting(Track cropped, ExportSettings s, string mapPngPath)
    {
        var frame = MapFrame.Fit(cropped, s);
        var geo = TrackGeometry.Build(cropped, frame, s.MinPointDistanceM, s.SimplifyTolerancePx);
        return FusionSettingWriter.Write(geo, FusionOptions(s, mapPngPath, frame));
    }

    public static async Task<ExportResult> ExportAsync(Track cropped, ExportSettings s, TileCache cache, string folder,
        IProgress<string>? status, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        string mapPath = Path.Combine(folder, MapFileName);
        var frame = MapFrame.Fit(cropped, s);
        var fusionOptions = FusionOptions(s, mapPath, frame); // validates the marker image before the long tile download
        var src = s.TileSource;
        int tz = frame.TileZoom(src.MaxZoom);
        var tiles = frame.Tiles(tz);

        var tileProgress = new Progress<(int Done, int Total)>(p => status?.Report($"Downloading map tiles {p.Done}/{p.Total}…"));
        await cache.PrefetchWithFallbackAsync(src, tiles, tileProgress, ct).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        status?.Report("Composing map.png…");
        int missing;
        using (var bmp = MapComposer.RenderMap(frame, src, cache, s.DarkenPercent, s.DrawAttribution, out missing))
        {
            if (s.LabelOutput == LabelTarget.MapPng && s.Labels.Count > 0)
            {
                using var canvas = new SKCanvas(bmp);
                LabelRenderer.Draw(canvas, frame, s.Labels, s.LabelSizeScale);
                canvas.Flush();
            }
            SavePng(bmp, mapPath);
        }

        ct.ThrowIfCancellationRequested();
        status?.Report("Generujem trasu…");
        var geo = TrackGeometry.Build(cropped, frame, s.MinPointDistanceM, s.SimplifyTolerancePx);

        string settingPath = Path.Combine(folder, SettingFileName);
        string setting = FusionSettingWriter.Write(geo, fusionOptions);
        await File.WriteAllTextAsync(settingPath, setting, new UTF8Encoding(false), ct).ConfigureAwait(false);

        using (var trackBmp = RenderTrackOnly(geo, s))
            SavePng(trackBmp, Path.Combine(folder, TrackFileName));

        await File.WriteAllTextAsync(Path.Combine(folder, CsvFileName), BuildCsv(cropped, geo), new UTF8Encoding(false), ct)
            .ConfigureAwait(false);

        return new ExportResult(folder, mapPath, settingPath, geo.Simplified.Count, missing);
    }

    public static SKBitmap RenderTrackOnly(TrackGeometry geo, ExportSettings s)
    {
        var bmp = new SKBitmap(new SKImageInfo(s.Width, s.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var path = BuildPath(geo.Full);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)s.LineWidthPx,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = new SKColor(s.LineColor.R, s.LineColor.G, s.LineColor.B),
        };
        canvas.DrawPath(path, paint);
        canvas.Flush();
        return bmp;
    }

    public static SKPath BuildPath(IReadOnlyList<PathPoint> pts)
    {
        var builder = new SKPathBuilder();
        for (int i = 0; i < pts.Count; i++)
        {
            if (i == 0) builder.MoveTo((float)pts[i].X, (float)pts[i].Y);
            else builder.LineTo((float)pts[i].X, (float)pts[i].Y);
        }
        return builder.Detach();
    }

    private static string BuildCsv(Track cropped, TrackGeometry geo)
    {
        var inv = CultureInfo.InvariantCulture;
        var cum = TrackGeometry.CumulativeLength(geo.Simplified);
        double total = cum.Length > 0 ? cum[^1] : 0;
        var sb = new StringBuilder();
        sb.AppendLine("polyline_index;source_index;utc_time;elapsed;elapsed_sec;distance_km;x_px;y_px;write_length");
        for (int i = 0; i < geo.Simplified.Count; i++)
        {
            var p = geo.Simplified[i];
            var t = cropped.Points[p.SourceIndex].Time;
            sb.Append(i).Append(';')
              .Append(p.SourceIndex).Append(';')
              .Append(t?.ToString("yyyy-MM-ddTHH:mm:ssZ", inv) ?? "").Append(';')
              .Append(((int)TimeSpan.FromSeconds(p.TimeSec).TotalHours).ToString("00", inv))
              .Append(TimeSpan.FromSeconds(p.TimeSec).ToString(@"\:mm\:ss", inv)).Append(';')
              .Append(p.TimeSec.ToString("0.#", inv)).Append(';')
              .Append((p.DistM / 1000).ToString("0.000", inv)).Append(';')
              .Append(p.X.ToString("0.0", inv)).Append(';')
              .Append(p.Y.ToString("0.0", inv)).Append(';')
              .Append((total > 0 ? cum[i] / total : 1).ToString("0.000000", inv))
              .AppendLine();
        }
        return sb.ToString();
    }

    private static void SavePng(SKBitmap bmp, string path)
    {
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }
}
