using System.Text;
using GpxToFusion.Export;
using GpxToFusion.Geo;
using GpxToFusion.Gpx;
using GpxToFusion.Map;

namespace GpxToFusion.Tests;

public class CoreTests
{
    private static readonly string SampleGpx = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "Afternoon_Walk.gpx"));

    private const string MiniGpx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <gpx version="1.1" xmlns="http://www.topografix.com/GPX/1/1" xmlns:gpxtpx="http://www.garmin.com/xmlschemas/TrackPointExtension/v1">
         <trk><trkseg>
          <trkpt lat="48.1" lon="17.1"><ele>140.5</ele><time>2026-03-16T13:30:22Z</time>
           <extensions><gpxtpx:TrackPointExtension><gpxtpx:hr>93</gpxtpx:hr></gpxtpx:TrackPointExtension></extensions></trkpt>
          <trkpt lat="48.1" lon="17.2"><time>2026-03-16T13:31:22Z</time></trkpt>
         </trkseg><trkseg>
          <trkpt lat="48.2" lon="17.2"/>
         </trkseg></trk>
        </gpx>
        """;

    [Fact]
    public void Parser_ReadsPointsAcrossSegments()
    {
        var pts = GpxParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(MiniGpx)));
        Assert.Equal(3, pts.Count);
        Assert.Equal(140.5, pts[0].Ele);
        Assert.Equal(new DateTime(2026, 3, 16, 13, 30, 22, DateTimeKind.Utc), pts[0].Time);
        Assert.Null(pts[1].Ele);
        Assert.Equal(17.2, pts[2].Lon);
    }

    [Fact]
    public void Parser_ReadsSampleFile()
    {
        Assert.True(File.Exists(SampleGpx), SampleGpx);
        var pts = GpxParser.Parse(SampleGpx);
        Assert.Equal(10150, pts.Count);
        Assert.Equal(24.0978080, pts[0].Lat, 7);
        Assert.Equal(32.8952460, pts[0].Lon, 7);
        Assert.Equal(new DateTime(2026, 3, 16, 13, 30, 22, DateTimeKind.Utc), pts[0].Time);
        var track = new Track(pts);
        Assert.True(track.HasTime);
        Assert.InRange(track.TotalSeconds, 2 * 3600, 4 * 3600);
    }

    [Fact]
    public void Haversine_OneDegreeLatitude()
    {
        Assert.Equal(111195, Track.Haversine(0, 0, 1, 0), 0);
    }

    [Fact]
    public void Track_SubKeepsRangeAndResetsCumulatives()
    {
        var pts = GpxParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(MiniGpx)));
        var t = new Track(pts.Take(2).ToList()).Sub(1, 1);
        Assert.Equal(1, t.Count);
        Assert.Equal(0, t.TotalDistance);
    }

    [Fact]
    public void WebMercator_RoundTripAndKnownValues()
    {
        var (x, y) = WebMercator.ToWorld(0, 0, 0);
        Assert.Equal(128, x, 6);
        Assert.Equal(128, y, 6);
        var (wx, wy) = WebMercator.ToWorld(24.0978, 32.8952, 15.3);
        var (lat, lon) = WebMercator.FromWorld(wx, wy, 15.3);
        Assert.Equal(24.0978, lat, 8);
        Assert.Equal(32.8952, lon, 8);
    }

    [Fact]
    public void FitZoom_TrackFitsInsidePaddedFrame()
    {
        var track = new Track(GpxParser.Parse(SampleGpx));
        var s = new ExportSettings { Width = 1920, Height = 1080, PaddingPercent = 10 };
        var frame = MapFrame.Fit(track, s);
        foreach (var p in track.Points)
        {
            var (px, py) = frame.ToPixel(p.Lat, p.Lon);
            Assert.InRange(px, 1920 * 0.1 - 0.5, 1920 * 0.9 + 0.5);
            Assert.InRange(py, 1080 * 0.1 - 0.5, 1080 * 0.9 + 0.5);
        }
    }

    [Fact]
    public void MapFrame_TilesCoverFrame()
    {
        var f = new MapFrame(1000, 1000, 3, 512, 256);
        var tiles = f.Tiles(3);
        // Left=744..1256 -> tiles x 2..4, Top=872..1128 -> y 3..4
        Assert.Equal(6, tiles.Count);
        Assert.Contains((3, 2, 3), tiles);
        Assert.Contains((3, 4, 4), tiles);
    }

    [Fact]
    public void Simplifier_RemovesCollinearPoints()
    {
        var pts = Enumerable.Range(0, 100).Select(i => ((double)i, 0.0)).ToList();
        pts.Add((99, 50));
        var keep = PolylineSimplifier.Simplify(pts, 0.5);
        Assert.Equal([0, 99, 100], keep);
    }

    [Fact]
    public void FusionCoords_CornersAndCenter()
    {
        Assert.Equal((0.0, 0.0), FusionCoords.ToPolyline(960, 540, 1920, 1080));
        var (x, y) = FusionCoords.ToPolyline(0, 0, 1920, 1080);
        Assert.Equal(-0.5, x, 9);
        Assert.Equal(0.5, y, 9);
        (x, y) = FusionCoords.ToPolyline(1920, 1080, 1920, 1080);
        Assert.Equal(0.5, x, 9);
        Assert.Equal(-0.5, y, 9);
    }

    [Fact]
    public void LengthFractionAtTime_IsMonotonicFrom0To1()
    {
        var track = new Track(GpxParser.Parse(SampleGpx)).Sub(100, 3000);
        var s = new ExportSettings { Width = 1920, Height = 1080 };
        var geo = TrackGeometry.Build(track, MapFrame.Fit(track, s), 0.5, 0.5);
        double prev = -1;
        for (double t = 0; t <= track.TotalSeconds; t += 10)
        {
            double f = geo.LengthFractionAtTime(t);
            Assert.InRange(f, 0, 1);
            Assert.True(f >= prev - 1e-12);
            prev = f;
        }
        Assert.Equal(0, geo.LengthFractionAtTime(0));
        Assert.Equal(1, geo.LengthFractionAtTime(track.TotalSeconds));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    public void FusionSetting_IsWellFormed(bool keyframes, bool byTime, bool fitClip)
    {
        var prev = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("sk-SK"); // decimal comma culture
        try
        {
            var track = new Track(GpxParser.Parse(SampleGpx));
            var s = new ExportSettings { AddKeyframes = keyframes, KeyframesByTime = byTime, KeyframeFrames = 300, KeyframesFitClip = fitClip };
            string text = Exporter.BuildSetting(track, s, @"D:\out dir\map.png");

            Assert.Equal(text.Count(c => c == '{'), text.Count(c => c == '}'));
            foreach (var node in new[] { "MapLoader = Loader", "TrackPath = PolylineMask", "TrackColor = Background", "MergeTrack = Merge", "MergeCalib = Merge" })
                Assert.Contains(node, text);
            Assert.Contains(@"Filename = ""D:\\out dir\\map.png""", text);
            Assert.DoesNotMatch(@"\d,\d", text); // no decimal commas
            Assert.Equal(keyframes && !fitClip, text.Contains("TrackPathWriteLength = BezierSpline"));
            Assert.Equal(keyframes && fitClip, text.Contains("comp.RenderEnd"));
            Assert.Equal(keyframes && fitClip, text.Contains("AnimStartDelay = Input { Value = 0, }"));
            Assert.Equal(keyframes && fitClip, text.Contains("UserControls = ordered()"));
            Assert.Equal(text.Count(c => c == '('), text.Count(c => c == ')'));
            if (keyframes && !fitClip)
            {
                Assert.Contains("[0] = { 0,", text);
                Assert.Contains("[300] = { 1,", text);
            }
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
        }
    }

    [Fact]
    public void FusionSetting_MediaInVariant_HasNoLoader()
    {
        var track = new Track(GpxParser.Parse(SampleGpx));
        var s = new ExportSettings { MapSource = FusionMapSource.MediaIn };
        string text = Exporter.BuildSetting(track, s, @"D:\x\map.png");
        Assert.DoesNotContain("Loader {", text);
        Assert.Contains(@"Background = Input { SourceOp = ""MediaIn1""", text);
        Assert.Equal(text.Count(c => c == '{'), text.Count(c => c == '}'));
    }

    [Fact]
    public void FusionSetting_Marker_FollowsWriteLength()
    {
        string png = Path.Combine(Path.GetTempPath(), $"g2f_marker_{Guid.NewGuid():N}.png");
        using (var bmp = new SkiaSharp.SKBitmap(200, 100))
        using (var img = SkiaSharp.SKImage.FromBitmap(bmp))
        using (var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
        using (var fs = File.Create(png))
            data.SaveTo(fs);
        try
        {
            var track = new Track(GpxParser.Parse(SampleGpx));
            var s = new ExportSettings { Resolution = ResolutionPreset.HD_1920x1080, MarkerImagePath = png, MarkerSize1080 = 50, AddKeyframes = true };
            string text = Exporter.BuildSetting(track, s, @"D:\x\map.png");

            Assert.Contains("MarkerLoader = Loader", text);
            Assert.Contains("MarkerPath = PolyPath", text);
            Assert.Contains("Displacement = Input { Value = 0, Expression = \"iif(MarkerPath.PathSync<0.5, TrackPath.WriteLength, ", text);
            Assert.Contains("PathSync = Input { Value = 1, }", text);
            Assert.Contains("INPID_InputControl = \"ComboControl\"", text);
            Assert.Contains("Center = Input { SourceOp = \"MarkerPath\", Source = \"Position\", }", text);
            Assert.Contains("Size = Input { Value = 0.25, }", text); // 50 px / 200 px
            Assert.Contains("Background = Input { SourceOp = \"MergeMarker\"", text); // MergeCalib sits after the marker
            Assert.Equal(text.Count(c => c == '{'), text.Count(c => c == '}'));

            s.MarkerImagePath = png + ".missing";
            Assert.Throws<FileNotFoundException>(() => Exporter.BuildSetting(track, s, @"D:\x\map.png"));
        }
        finally
        {
            File.Delete(png);
        }
    }

    [Fact]
    public void FusionSetting_Labels_AreTextPlusNodes()
    {
        var prev = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("sk-SK");
        try
        {
            var track = new Track(GpxParser.Parse(SampleGpx));
            var s = new ExportSettings { Resolution = ResolutionPreset.HD_1920x1080 };
            var frame = MapFrame.Fit(track, s);
            // Label exactly in the map center → Text+ Center { 0.5, 0.5 }.
            var (lat, lon) = Geo.WebMercator.FromWorld(frame.CenterX, frame.CenterY, frame.Zoom);
            s.Labels.Add(new MapLabel { Text = "Štart \"A\"\nriadok 2", Lat = lat, Lon = lon, FontSize1080 = 48, Align = LabelAlign.Left });
            s.Labels.Add(new MapLabel { Text = "Cieľ", Lat = lat, Lon = lon, OutlineWidth1080 = 0, Bold = false });

            string text = Exporter.BuildSetting(track, s, @"D:\x\map.png");
            Assert.Contains("Label1 = TextPlus", text);
            Assert.Contains("Label2 = TextPlus", text);
            Assert.Contains(@"StyledText = Input { Value = ""Štart \""A\""\nriadok 2"", }", text);
            Assert.Contains("Center = Input { Value = { 0.5, 0.5 }, }", text);
            Assert.Contains("Size = Input { Value = 0.025, }", text); // 48 px / 1920
            Assert.Contains("HorizontalLeftCenterRight = Input { Value = -1, }", text);
            Assert.Contains(@"Style = Input { Value = ""Regular"", }", text);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "Enabled2")); // only the first has an outline
            Assert.Contains(@"Background = Input { SourceOp = ""MergeLabel2""", text); // MergeCalib after the labels
            Assert.DoesNotMatch(@"\d,\d", text);
            Assert.Equal(text.Count(c => c == '{'), text.Count(c => c == '}'));

            s.LabelOutput = LabelTarget.MapPng;
            Assert.DoesNotContain("TextPlus", Exporter.BuildSetting(track, s, @"D:\x\map.png"));

            var clone = s.Clone();
            clone.Labels[0].Text = "zmenené";
            Assert.NotEqual("zmenené", s.Labels[0].Text); // deep copy for the export thread
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
        }
    }

    [Fact]
    public void ProjectFile_RoundTripsLabels()
    {
        string path = Path.Combine(Path.GetTempPath(), $"g2f_{Guid.NewGuid():N}.g2f.json");
        try
        {
            var p = new ProjectFile();
            p.Settings.Labels.Add(new MapLabel
            {
                Text = "Chata", Lat = 48.1, Lon = 17.2, TextColor = System.Drawing.Color.FromArgb(255, 200, 0),
                FontName = "Arial", Align = LabelAlign.Right,
            });
            p.Save(path);
            var l = ProjectFile.Load(path).Settings.Labels.Single();
            Assert.Equal(("Chata", 48.1, 17.2, "Arial", LabelAlign.Right, "#FFC800"),
                (l.Text, l.Lat, l.Lon, l.FontName, l.Align, l.TextColorHex));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Fit_UsesManualFrameAndItSurvivesProjectRoundTrip()
    {
        var track = new Track(GpxParser.Parse(SampleGpx));
        var s = new ExportSettings { Width = 1920, Height = 1080, ManualFrame = new FrameOverride { Lat = 48.15, Lon = 17.11, Zoom = 14.3 } };
        var frame = MapFrame.Fit(track, s);
        Assert.Equal(14.3, frame.Zoom, 9);
        var (cx, cy) = frame.ToPixel(48.15, 17.11);
        Assert.Equal(960, cx, 6);
        Assert.Equal(540, cy, 6);

        string path = Path.Combine(Path.GetTempPath(), $"g2f_{Guid.NewGuid():N}.g2f.json");
        try
        {
            new ProjectFile { Settings = s }.Save(path);
            var m = ProjectFile.Load(path).Settings.ManualFrame!;
            Assert.Equal((48.15, 17.11, 14.3), (m.Lat, m.Lon, m.Zoom));
            Assert.NotSame(s.ManualFrame, s.Clone().ManualFrame);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LabelRenderer_DrawsTextAtAnchor()
    {
        using var bmp = new SkiaSharp.SKBitmap(400, 200);
        using var canvas = new SkiaSharp.SKCanvas(bmp);
        canvas.Clear(SkiaSharp.SKColors.Transparent);
        var label = new MapLabel { Text = "TEST", FontSize1080 = 40, TextColor = System.Drawing.Color.Red };
        var r = LabelRenderer.DrawOne(canvas, label, 200, 100, 1);
        canvas.Flush();
        Assert.InRange(r.MidX, 195, 205); // centered horizontally on the anchor
        Assert.InRange(r.MidY, 90, 110);
        Assert.Contains(Enumerable.Range((int)r.Left, (int)r.Width), x => bmp.GetPixel(x, 100).Alpha > 0);
    }

    [Fact]
    public void Settings_ResolutionPresetSyncsSize()
    {
        var s = new ExportSettings { Resolution = ResolutionPreset.HD_1920x1080 };
        Assert.Equal((1920, 1080), (s.Width, s.Height));
        s.Width = 2000;
        Assert.Equal(ResolutionPreset.Custom, s.Resolution);
        s.Width = 1920;
        Assert.Equal(ResolutionPreset.HD_1920x1080, s.Resolution);
    }

    [Fact]
    public void TrackExtension_PrependsAndAppendsWithSyntheticTimes()
    {
        var t0 = new DateTime(2026, 3, 16, 12, 0, 0, DateTimeKind.Utc);
        var recorded = new List<TrackPoint>
        {
            new(48.0, 17.0, 150, t0),
            new(48.001, 17.0, 151, t0.AddSeconds(100)),
        };
        // Outward from the start = south here: ~111 m, then another ~111 m.
        var before = new List<GeoPoint> { new(47.999, 17.0), new(47.998, 17.0) };
        var after = new List<GeoPoint> { new(48.002, 17.0) };

        var pts = TrackExtension.Extend(recorded, before, after, speedKmh: 3.6); // 1 m/s
        Assert.Equal(5, pts.Count);
        Assert.Equal(47.998, pts[0].Lat); // farthest added point comes first
        Assert.Equal(47.999, pts[1].Lat);
        Assert.Equal(48.0, pts[2].Lat);
        Assert.Equal(48.002, pts[4].Lat);
        Assert.Equal(150, pts[0].Ele);
        Assert.Equal(151, pts[4].Ele);
        Assert.Equal(-111.2, (pts[1].Time!.Value - t0).TotalSeconds, 0);
        Assert.Equal(-222.4, (pts[0].Time!.Value - t0).TotalSeconds, 0);
        Assert.Equal(211.2, (pts[4].Time!.Value - t0).TotalSeconds, 0);

        var track = new Track(pts);
        Assert.True(track.HasTime);
        for (int i = 1; i < track.Count; i++) Assert.True(track.CumTime[i] > track.CumTime[i - 1]);
    }

    [Fact]
    public void TrackExtension_FromRouteGivesConstantSpeedTimes()
    {
        var route = new List<GeoPoint> { new(48.0, 17.0), new(48.001, 17.0), new(48.003, 17.0) };

        var track = new Track(TrackExtension.FromRoute(route, speedKmh: 3.6)); // 1 m/s

        Assert.True(track.HasTime);
        Assert.Equal(track.CumDist[1], track.CumTime[1], 2);
        Assert.Equal(track.CumDist[2], track.CumTime[2], 2);
        Assert.All(track.Points, p => Assert.Null(p.Ele));
        // Rebuilding while editing must not shift the times.
        Assert.Equal(track.Points[0].Time, TrackExtension.FromRoute(route, 3.6)[0].Time);
    }

    [Fact]
    public void TrackExtension_FromRouteUsesFallbackForAutoSpeed()
    {
        var route = new List<GeoPoint> { new(48.0, 17.0), new(48.001, 17.0) };

        var auto = new Track(TrackExtension.FromRoute(route, speedKmh: 0));

        Assert.Equal(TrackExtension.FallbackSpeedKmh, auto.TotalDistance / auto.TotalSeconds * 3.6, 2);
    }

    [Fact]
    public void TrackExtension_FromRouteHandlesTooFewPoints()
    {
        Assert.Empty(TrackExtension.FromRoute([], 5));
        Assert.Single(TrackExtension.FromRoute([new GeoPoint(48.0, 17.0)], 5));
    }

    [Fact]
    public void EveryMapStyle_HasItsOwnTileSource()
    {
        var keys = Enum.GetValues<MapStyle>().Select(style => new ExportSettings { Style = style }.TileSource.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void DrawnRoute_ExportsToFusionAndGpx()
    {
        var route = new List<GeoPoint> { new(48.10, 17.10), new(48.12, 17.14), new(48.11, 17.20) };
        var track = new Track(TrackExtension.FromRoute(route, 15));

        string setting = Exporter.BuildSetting(track, new ExportSettings(), @"C:\tmp\map.png");
        Assert.Contains("TrackPath", setting);

        using var stream = new MemoryStream();
        GpxWriter.Write(stream, track.Points, "route");
        string path = Path.Combine(Path.GetTempPath(), $"g2f_{Guid.NewGuid():N}.gpx");
        try
        {
            File.WriteAllBytes(path, stream.ToArray());
            var parsed = GpxParser.Parse(path);
            Assert.Equal(route.Select(p => (p.Lat, p.Lon)), parsed.Select(p => (p.Lat, p.Lon)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ProjectFile_RoundTripsDrawnRouteWithoutGpx()
    {
        string path = Path.Combine(Path.GetTempPath(), $"g2f_{Guid.NewGuid():N}.g2f.json");
        try
        {
            var s = new ExportSettings();
            s.RoutePoints.Add(new GeoPoint(48.1, 17.2));
            s.RoutePoints.Add(new GeoPoint(48.2, 17.3));

            new ProjectFile { Settings = s }.Save(path);
            var loaded = ProjectFile.Load(path);

            Assert.Null(loaded.GpxPath);
            Assert.Equal([(48.1, 17.2), (48.2, 17.3)], loaded.Settings.RoutePoints.Select(p => (p.Lat, p.Lon)));
            Assert.NotSame(s.RoutePoints[0], s.Clone().RoutePoints[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TrackExtension_AutoSpeedIgnoresPauses()
    {
        var t0 = new DateTime(2026, 3, 16, 12, 0, 0, DateTimeKind.Utc);
        var pts = new List<TrackPoint>
        {
            new(48.0, 17.0, null, t0),
            new(48.001, 17.0, null, t0.AddSeconds(100)),  // 111 m / 100 s = 4 km/h
            new(48.001, 17.0, null, t0.AddSeconds(1000)), // pause
        };
        Assert.Equal(4.0, TrackExtension.MovingSpeedKmh(pts), 1);
    }

    [Fact]
    public void GpxWriter_RoundTrips()
    {
        var t0 = new DateTime(2026, 3, 16, 12, 0, 0, DateTimeKind.Utc);
        var pts = new List<TrackPoint> { new(48.1234567, 17.7654321, 140.25, t0), new(48.2, 17.3, null, t0.AddSeconds(1.5)) };
        var prev = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("sk-SK");
        var ms = new MemoryStream();
        try { GpxWriter.Write(ms, pts, "test"); }
        finally { Thread.CurrentThread.CurrentCulture = prev; }

        string xml = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("<time>2026-03-16T12:00:00Z</time>", xml);
        ms.Position = 0;
        var back = GpxParser.Parse(ms);
        Assert.Equal(2, back.Count);
        Assert.Equal(48.1234567, back[0].Lat, 7);
        Assert.Equal(140.25, back[0].Ele);
        Assert.Equal(t0, back[0].Time);
        Assert.Null(back[1].Ele);
        Assert.Equal(t0.AddSeconds(1.5), back[1].Time);
    }

    [Fact]
    public void ExportSettings_CloneCopiesAddedPoints()
    {
        var s = new ExportSettings();
        s.ExtraStart.Add(new GeoPoint(1, 2));
        var c = s.Clone();
        c.ExtraStart[0].Lat = 5;
        Assert.Equal(1, s.ExtraStart[0].Lat);
    }

    [Fact]
    public void ArcSyncCurve_MapsPixelLengthToNormalizedLength()
    {
        // 1920x1080: 960 px right, then 540 px down. Pixel length 1500, normalized length 0.5 + 0.5.
        var pts = new List<(double X, double Y)> { (0, 0), (960, 0), (960, 540) };
        var curve = FusionSettingWriter.ArcSyncCurve(pts, 1920, 1080, FusionSettingWriter.PathSync.MaskPixelsPathNormalized);
        double At(double x)
        {
            for (int i = 1; i < curve.Count; i++)
                if (x <= curve[i].X)
                    return curve[i - 1].Y + (x - curve[i - 1].X) / (curve[i].X - curve[i - 1].X) * (curve[i].Y - curve[i - 1].Y);
            return 1;
        }
        Assert.Equal(0, At(0), 3);
        Assert.Equal(0.5, At(0.64), 2); // the corner
        Assert.Equal(0.25, At(0.32), 2);
        Assert.Equal(1, At(1), 3);

        string expr = FusionSettingWriter.DisplacementExpression(pts, 1920, 1080, "MarkerPath");
        Assert.Equal(expr.Count(c => c == '('), expr.Count(c => c == ')'));
    }
}
