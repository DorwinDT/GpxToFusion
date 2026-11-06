using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using System.Drawing.Design;
using System.Drawing.Text;
using System.Text.Json.Serialization;
using GpxToFusion.UI;

namespace GpxToFusion.Export;

[TypeConverter(typeof(EnumDescriptionConverter<LabelAlign>))]
public enum LabelAlign
{
    [Description("Left (text starts at the point)")] Left,
    [Description("Center")] Center,
    [Description("Right (text ends at the point)")] Right,
}

[TypeConverter(typeof(EnumDescriptionConverter<LabelTarget>))]
public enum LabelTarget
{
    [Description("Fusion Text+ (editable in Resolve)")] FusionText,
    [Description("Burn into map.png")] MapPng,
}

/// <summary>
/// A text label on the map. Anchored to lat/lon, so it stays on the same place when the crop, padding or
/// resolution changes. Sizes are in px at 1080p and scale with the map like the line width.
/// </summary>
public sealed class MapLabel
{
    [Category("1. Text"), DisplayName("Text"), Description("Label text. Multiple lines: click the arrow on the right.")]
    [Editor(typeof(MultilineStringEditor), typeof(UITypeEditor))]
    public string Text { get; set; } = "Label";

    [Category("1. Text"), DisplayName("Alignment"), Description("How the text sits relative to the point you clicked.")]
    public LabelAlign Align { get; set; } = LabelAlign.Center;

    [Category("2. Font"), DisplayName("Font"), Description("Installed font. The same font must be available in Fusion.")]
    [TypeConverter(typeof(FontNameConverter))]
    public string FontName { get; set; } = "Segoe UI";

    [Category("2. Font"), DisplayName("Size (px at 1080p)"), Description("Scaled by height at other resolutions (4K → 2×).")]
    public double FontSize1080 { get; set; } = 40;

    [Category("2. Font"), DisplayName("Bold")]
    public bool Bold { get; set; } = true;

    [Category("2. Font"), DisplayName("Italic")]
    public bool Italic { get; set; }

    [Category("2. Font"), DisplayName("Text color")]
    [JsonIgnore]
    public Color TextColor { get; set; } = Color.White;

    [Browsable(false)]
    public string TextColorHex
    {
        get => ToHex(TextColor);
        set => TextColor = FromHex(value, TextColor);
    }

    [Category("3. Outline"), DisplayName("Outline width (px at 1080p)"), Description("0 = no outline. An outline helps readability on the map.")]
    public double OutlineWidth1080 { get; set; } = 3;

    [Category("3. Outline"), DisplayName("Outline color")]
    [JsonIgnore]
    public Color OutlineColor { get; set; } = Color.Black;

    [Browsable(false)]
    public string OutlineColorHex
    {
        get => ToHex(OutlineColor);
        set => OutlineColor = FromHex(value, OutlineColor);
    }

    [Category("4. Position"), DisplayName("Latitude"), Description("Can also be changed by dragging the label in the preview.")]
    public double Lat { get; set; }

    [Category("4. Position"), DisplayName("Longitude")]
    public double Lon { get; set; }

    [Browsable(false), JsonIgnore]
    public string FontStyleName => (Bold, Italic) switch
    {
        (true, true) => "Bold Italic",
        (true, false) => "Bold",
        (false, true) => "Italic",
        _ => "Regular",
    };

    public MapLabel Clone() => (MapLabel)MemberwiseClone();

    public void CopyFrom(MapLabel o)
    {
        Text = o.Text; Align = o.Align; FontName = o.FontName; FontSize1080 = o.FontSize1080; Bold = o.Bold; Italic = o.Italic;
        TextColor = o.TextColor; OutlineWidth1080 = o.OutlineWidth1080; OutlineColor = o.OutlineColor; Lat = o.Lat; Lon = o.Lon;
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color FromHex(string? hex, Color fallback)
    {
        try { return string.IsNullOrWhiteSpace(hex) ? fallback : ColorTranslator.FromHtml(hex); }
        catch { return fallback; }
    }
}

/// <summary>Drop-down with installed font families (free text still allowed).</summary>
public sealed class FontNameConverter : StringConverter
{
    private static string[]? _names;

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
    {
        if (_names == null)
        {
            using var fonts = new InstalledFontCollection();
            _names = fonts.Families.Select(f => f.Name).ToArray();
        }
        return new StandardValuesCollection(_names);
    }
}
