using System.ComponentModel;
using System.Drawing;
using System.Text.Json.Serialization;
using GpxToFusion.Gpx;
using GpxToFusion.Map;
using GpxToFusion.UI;

namespace GpxToFusion.Export;

[TypeConverter(typeof(EnumDescriptionConverter<MapStyle>))]
public enum MapStyle
{
    [Description("OpenStreetMap")] OpenStreetMap,
    [Description("OpenStreetMap – Latin labels")] OpenStreetMapLatin,
    [Description("National Geographic – English labels (up to zoom 12)")] NatGeo,
    [Description("Satellite (Esri World Imagery)")] Satellite,
    [Description("CARTO dark")] CartoDark,
    [Description("CARTO light")] CartoLight,
}

[TypeConverter(typeof(EnumDescriptionConverter<FusionMapSource>))]
public enum FusionMapSource
{
    [Description("Loader (map.png from file)")] Loader,
    [Description("MediaIn1 (map.png on the timeline)")] MediaIn,
}

[TypeConverter(typeof(EnumDescriptionConverter<ResolutionPreset>))]
public enum ResolutionPreset
{
    [Description("1920 × 1080 (HD)")] HD_1920x1080,
    [Description("3840 × 2160 (4K UHD)")] UHD_3840x2160,
    [Description("7680 × 4320 (8K)")] UHD8K_7680x4320,
    [Description("1080 × 1920 (vertical)")] Vertical_1080x1920,
    [Description("2160 × 3840 (vertical 4K)")] Vertical_2160x3840,
    [Description("1080 × 1080 (square)")] Square_1080,
    [Description("Custom")] Custom,
}

/// <summary>All user-adjustable settings. Shown in a PropertyGrid and saved in the project file.</summary>
public sealed class ExportSettings
{
    private ResolutionPreset _preset = ResolutionPreset.UHD_3840x2160;
    private int _width = 3840;
    private int _height = 2160;

    // ---------------- Map ----------------

    [Category("1. Map"), DisplayName("Base map"), Description("Map tile source.")]
    public MapStyle Style { get; set; } = MapStyle.OpenStreetMap;

    [Category("1. Map"), DisplayName("Resolution"), Description("Resolution of map.png. Pick a larger one (8K) if you want to zoom/pan in Resolve later.")]
    [RefreshProperties(RefreshProperties.All)]
    public ResolutionPreset Resolution
    {
        get => _preset;
        set
        {
            _preset = value;
            var size = PresetSize(value);
            if (size != null) (_width, _height) = size.Value;
        }
    }

    [Category("1. Map"), DisplayName("Width (px)"), Description("Width of map.png. Changing it switches the preset to Custom.")]
    [RefreshProperties(RefreshProperties.All)]
    public int Width
    {
        get => _width;
        set { _width = Math.Clamp(value, 64, 16384); SyncPreset(); }
    }

    [Category("1. Map"), DisplayName("Height (px)"), Description("Height of map.png. Changing it switches the preset to Custom.")]
    [RefreshProperties(RefreshProperties.All)]
    public int Height
    {
        get => _height;
        set { _height = Math.Clamp(value, 64, 16384); SyncPreset(); }
    }

    [Category("1. Map"), DisplayName("Padding (%)"), Description("Free space around the track on each side.")]
    public double PaddingPercent { get; set; } = 10;

    [Category("1. Map"), DisplayName("Extra zoom-out (zoom levels)"),
     Description("0 = the track fills the frame. 1 = the map covers a 2× larger area (for panning/zooming in Resolve). Can be fractional.")]
    public double ExtraZoomOut { get; set; } = 0;

    [Category("1. Map"), DisplayName("Frame"),
     Description("Automatic = fitted to the cropped track, padding and zoom-out. Manual = pan the map in the preview by dragging and zoom with the mouse wheel (Shift = fine). The 'Auto frame' button restores automatic.")]
    [JsonIgnore]
    public string ManualFrameInfo => ManualFrame == null ? "automatic" : $"manual (zoom {ManualFrame.Zoom:0.00})";

    /// <summary>Frame set by panning/zooming the preview; null = fit to the cropped track.</summary>
    [Browsable(false)]
    public FrameOverride? ManualFrame { get; set; }

    [Category("1. Map"), DisplayName("Darken map (%)"), Description("Black overlay over the map (0–80 %). Useful mainly with satellite imagery.")]
    public int DarkenPercent { get; set; } = 0;

    [Category("1. Map"), DisplayName("Attribution text"), Description("Write the map data copyright notice into map.png.")]
    public bool DrawAttribution { get; set; } = true;

    // ---------------- Track ----------------

    [Category("2. Track"), DisplayName("Line color")]
    [JsonIgnore]
    public Color LineColor { get; set; } = Color.FromArgb(0xFF, 0x5A, 0x1F);

    [Browsable(false)]
    public string LineColorHex
    {
        get => $"#{LineColor.R:X2}{LineColor.G:X2}{LineColor.B:X2}";
        set { try { LineColor = ColorTranslator.FromHtml(value); } catch { /* keep default */ } }
    }

    [Category("2. Track"), DisplayName("Line width (px at 1080p)"), Description("Scaled by height at other resolutions (4K → 2×).")]
    public double LineWidth1080 { get; set; } = 6;

    [Category("2. Track"), DisplayName("Simplification (px)"),
     Description("Douglas–Peucker tolerance in map.png pixels. Larger = fewer points in the Fusion polyline = faster rendering in Resolve. Also set by the Track detail slider.")]
    public double SimplifyTolerancePx { get; set; } = 0.5;

    [Category("2. Track"), DisplayName("GPS noise filter (m)"), Description("Skips points closer than X meters to the previous one (standing still). 0 = off.")]
    public double MinPointDistanceM { get; set; } = 0.5;

    [Category("2. Track"), DisplayName("Image at line head"),
     Description("An image (e.g. a little train) that moves along the track in Fusion together with the end of the drawn line. PNG with a transparent background. Empty = no image.")]
    [Editor(typeof(ImageFileNameEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public string MarkerImagePath { get; set; } = "";

    [Category("2. Track"), DisplayName("Image size (px at 1080p)"),
     Description("Longer side of the image in pixels at 1080p. Scaled by height at other resolutions. In Fusion: MergeMarker → Size.")]
    public double MarkerSize1080 { get; set; } = 60;

    // ---------------- Added points ----------------

    [Category("5. Added points"), DisplayName("Speed of added segments (km/h)"),
     Description("Added points and drawn routes have no tracker time. It is computed from this speed. 0 = moving average speed from the GPX (4.5 km/h for a drawn route).")]
    public double ExtensionSpeedKmh { get; set; } = 0;

    [Category("5. Added points"), DisplayName("Added start"),
     Description("Add points via 'Extend track' and clicking in the preview. Drag = move, click on a segment = insert point, right button = delete.")]
    [JsonIgnore]
    public string ExtraStartInfo => ExtraStart.Count == 0 ? "none" : $"{ExtraStart.Count} pt.";

    [Category("5. Added points"), DisplayName("Added end"), Description("See 'Added start'.")]
    [JsonIgnore]
    public string ExtraEndInfo => ExtraEnd.Count == 0 ? "none" : $"{ExtraEnd.Count} pt.";

    /// <summary>Points added before the recorded start, index 0 = next to the first recorded point.</summary>
    [Browsable(false)]
    public List<GeoPoint> ExtraStart { get; set; } = [];

    /// <summary>Points added after the recorded end, index 0 = next to the last recorded point.</summary>
    [Browsable(false)]
    public List<GeoPoint> ExtraEnd { get; set; } = [];

    /// <summary>Route drawn on the map instead of opening a GPX, in travel order; empty for a GPX project.</summary>
    [Browsable(false)]
    public List<GeoPoint> RoutePoints { get; set; } = [];

    // ---------------- Labels ----------------

    [Category("4. Labels"), DisplayName("Label output"),
     Description("Fusion Text+ = each label is a separate Text+ node; change text, font and position in Resolve. Burn into map.png = labels are part of the map image, exactly as in the preview.")]
    public LabelTarget LabelOutput { get; set; } = LabelTarget.FusionText;

    [Category("4. Labels"), DisplayName("Label count"),
     Description("Add a label with the 'Add label' button and a click in the preview. Double-click = edit, drag = move, right button = menu.")]
    [JsonIgnore]
    public string LabelCountInfo => Labels.Count == 0 ? "none" : Labels.Count.ToString();

    [Browsable(false)]
    public List<MapLabel> Labels { get; set; } = [];

    // ---------------- Fusion ----------------

    [Category("3. Fusion"), DisplayName("Map source in Fusion"),
     Description("Loader = the .setting loads map.png from file. MediaIn1 = put map.png on the timeline, open Fusion on it and the .setting connects to MediaIn1 (most reliable).")]
    public FusionMapSource MapSource { get; set; } = FusionMapSource.Loader;

    [Category("3. Fusion"), DisplayName("Add Write Length keyframes"),
     Description("Creates an intro animation drawing the track (0 → 1). If off, the track is complete and you animate it yourself.")]
    public bool AddKeyframes { get; set; } = false;

    [Category("3. Fusion"), DisplayName("Fit to clip length"),
     Description("The animation stretches over the whole Fusion clip (an expression using comp.RenderStart/RenderEnd instead of keyframes). Shorten/lengthen the clip on the timeline → the track draws faster/slower and always finishes on the last frame.")]
    public bool KeyframesFitClip { get; set; } = true;

    [Category("3. Fusion"), DisplayName("Animation length (frames)"),
     Description("Used only when 'Fit to clip length' is off.")]
    public int KeyframeFrames { get; set; } = 600;

    [Category("3. Fusion"), DisplayName("Start delay (frames)"),
     Description("How many frames from the clip start the track stays hidden. With 'Fit to clip length' it can also be changed in Fusion: TrackPath → Inspector → Controls → Start Delay.")]
    public int AnimStartDelayFrames { get; set; } = 0;

    [Category("3. Fusion"), DisplayName("End hold (frames)"),
     Description("The track finishes this many frames before the clip end and then stays complete. Only with 'Fit to clip length'; in Fusion: TrackPath → Controls → End Hold.")]
    public int AnimEndHoldFrames { get; set; } = 0;

    [Category("3. Fusion"), DisplayName("Time by GPX time"),
     Description("The track grows at the real pace (including pauses). Otherwise linearly by length.")]
    public bool KeyframesByTime { get; set; } = true;

    [Category("3. Fusion"), DisplayName("Calibration frame"),
     Description("Adds a disabled (PassThrough) node with a rectangle 10 px inside the map edges – to verify that the polyline lines up with the map.")]
    public bool AddCalibrationFrame { get; set; } = true;

    // ---------------- helpers ----------------

    [Browsable(false), JsonIgnore]
    public TileSource TileSource => Style switch
    {
        MapStyle.OpenStreetMapLatin => TileSource.OpenStreetMapLatin,
        MapStyle.NatGeo => TileSource.NatGeo,
        MapStyle.Satellite => TileSource.EsriImagery,
        MapStyle.CartoDark => TileSource.CartoDark,
        MapStyle.CartoLight => TileSource.CartoLight,
        _ => TileSource.OpenStreetMap,
    };

    [Browsable(false), JsonIgnore]
    public double LineWidthPx => LineWidth1080 * Math.Min(Width, Height) / 1080.0;

    [Browsable(false), JsonIgnore]
    public double MarkerSizePx => MarkerSize1080 * Math.Min(Width, Height) / 1080.0;

    public static (int W, int H)? PresetSize(ResolutionPreset p) => p switch
    {
        ResolutionPreset.HD_1920x1080 => (1920, 1080),
        ResolutionPreset.UHD_3840x2160 => (3840, 2160),
        ResolutionPreset.UHD8K_7680x4320 => (7680, 4320),
        ResolutionPreset.Vertical_1080x1920 => (1080, 1920),
        ResolutionPreset.Vertical_2160x3840 => (2160, 3840),
        ResolutionPreset.Square_1080 => (1080, 1080),
        _ => null,
    };

    private void SyncPreset()
    {
        var match = Enum.GetValues<ResolutionPreset>().FirstOrDefault(p => PresetSize(p) == (_width, _height), ResolutionPreset.Custom);
        _preset = match;
    }

    [Browsable(false), JsonIgnore]
    public double LabelSizeScale => Math.Min(Width, Height) / 1080.0;

    public ExportSettings Clone()
    {
        var c = (ExportSettings)MemberwiseClone();
        c.Labels = Labels.Select(l => l.Clone()).ToList();
        c.ExtraStart = ExtraStart.Select(p => p.Clone()).ToList();
        c.ExtraEnd = ExtraEnd.Select(p => p.Clone()).ToList();
        c.RoutePoints = RoutePoints.Select(p => p.Clone()).ToList();
        c.ManualFrame = ManualFrame?.Clone();
        return c;
    }
}

/// <summary>Manually positioned export frame: center (lat/lon) and fractional zoom.</summary>
public sealed class FrameOverride
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double Zoom { get; set; }

    public FrameOverride Clone() => (FrameOverride)MemberwiseClone();
}
