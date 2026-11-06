using System.Text.Json;
using System.Text.Json.Serialization;

namespace GpxToFusion.Export;

/// <summary>Saved project (*.g2f.json): GPX path, crop and settings.</summary>
public sealed class ProjectFile
{
    public string? GpxPath { get; set; }
    public int CropStart { get; set; }
    public int CropEnd { get; set; }
    public string? LastExportFolder { get; set; }
    public ExportSettings Settings { get; set; } = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Json));

    public static ProjectFile Load(string path) =>
        JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(path), Json) ?? new ProjectFile();
}
