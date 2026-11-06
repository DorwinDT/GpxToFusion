using GpxToFusion.Export;
using GpxToFusion.Gpx;
using GpxToFusion.Map;

namespace GpxToFusion.Tests;

/// <summary>Network test – runs only when GPX2FUSION_E2E_OUT is set to an output folder.</summary>
public class EndToEndTests
{
    [Fact]
    public async Task Export_WritesAllFiles()
    {
        string? outDir = Environment.GetEnvironmentVariable("GPX2FUSION_E2E_OUT");
        if (string.IsNullOrEmpty(outDir)) return;

        string gpx = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "Afternoon_Walk.gpx"));
        var track = new Track(GpxParser.Parse(gpx)).Sub(200, 9800);
        foreach (var style in new[] { MapStyle.OpenStreetMap, MapStyle.Satellite })
        {
            var s = new ExportSettings { Style = style, Resolution = ResolutionPreset.HD_1920x1080, AddKeyframes = true };
            string folder = Path.Combine(outDir, style.ToString());
            var result = await Exporter.ExportAsync(track, s, new TileCache(), folder, null, CancellationToken.None);
            Assert.Equal(0, result.MissingTiles);
            Assert.True(File.Exists(Path.Combine(folder, Exporter.MapFileName)));
            Assert.True(File.Exists(Path.Combine(folder, Exporter.SettingFileName)));
            Assert.True(File.Exists(Path.Combine(folder, Exporter.TrackFileName)));
            Assert.True(File.Exists(Path.Combine(folder, Exporter.CsvFileName)));
        }
    }
}
