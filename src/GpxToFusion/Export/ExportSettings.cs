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
    [Description("Satelit (Esri World Imagery)")] Satellite,
    [Description("CARTO tmavá")] CartoDark,
    [Description("CARTO svetlá")] CartoLight,
}

[TypeConverter(typeof(EnumDescriptionConverter<FusionMapSource>))]
public enum FusionMapSource
{
    [Description("Loader (map.png zo súboru)")] Loader,
    [Description("MediaIn1 (map.png na timeline)")] MediaIn,
}

[TypeConverter(typeof(EnumDescriptionConverter<ResolutionPreset>))]
public enum ResolutionPreset
{
    [Description("1920 × 1080 (HD)")] HD_1920x1080,
    [Description("3840 × 2160 (4K UHD)")] UHD_3840x2160,
    [Description("7680 × 4320 (8K)")] UHD8K_7680x4320,
    [Description("1080 × 1920 (vertikálne)")] Vertical_1080x1920,
    [Description("2160 × 3840 (vertikálne 4K)")] Vertical_2160x3840,
    [Description("1080 × 1080 (štvorec)")] Square_1080,
    [Description("Vlastné")] Custom,
}

/// <summary>All user-adjustable settings. Shown in a PropertyGrid and saved in the project file.</summary>
public sealed class ExportSettings
{
    private ResolutionPreset _preset = ResolutionPreset.UHD_3840x2160;
    private int _width = 3840;
    private int _height = 2160;

    // ---------------- Mapa ----------------

    [Category("1. Mapa"), DisplayName("Podklad"), Description("Zdroj mapových dlaždíc.")]
    public MapStyle Style { get; set; } = MapStyle.OpenStreetMap;

    [Category("1. Mapa"), DisplayName("Rozlíšenie"), Description("Rozlíšenie map.png. Pre neskorší zoom/posun v Resolve zvoľ väčšie (8K).")]
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

    [Category("1. Mapa"), DisplayName("Šírka (px)"), Description("Šírka map.png. Zmenou sa preset prepne na Custom.")]
    [RefreshProperties(RefreshProperties.All)]
    public int Width
    {
        get => _width;
        set { _width = Math.Clamp(value, 64, 16384); SyncPreset(); }
    }

    [Category("1. Mapa"), DisplayName("Výška (px)"), Description("Výška map.png. Zmenou sa preset prepne na Custom.")]
    [RefreshProperties(RefreshProperties.All)]
    public int Height
    {
        get => _height;
        set { _height = Math.Clamp(value, 64, 16384); SyncPreset(); }
    }

    [Category("1. Mapa"), DisplayName("Okraj (%)"), Description("Voľné miesto okolo trasy na každej strane.")]
    public double PaddingPercent { get; set; } = 10;

    [Category("1. Mapa"), DisplayName("Extra oddialenie (zoom úrovne)"),
     Description("0 = trasa vyplní záber. 1 = mapa pokryje 2× väčšie územie (na posun/zoom v Resolve). Môže byť desatinné.")]
    public double ExtraZoomOut { get; set; } = 0;

    [Category("1. Mapa"), DisplayName("Výrez"),
     Description("Automatický = podľa orezanej trasy, okraja a oddialenia. Ručný = v náhľade si mapu posunieš ťahaním a priblížiš kolieskom myši (Shift = jemne). Tlačidlo 'Auto výrez' vráti automatický.")]
    [JsonIgnore]
    public string ManualFrameInfo => ManualFrame == null ? "automatický" : $"ručný (zoom {ManualFrame.Zoom:0.00})";

    /// <summary>Frame set by panning/zooming the preview; null = fit to the cropped track.</summary>
    [Browsable(false)]
    public FrameOverride? ManualFrame { get; set; }

    [Category("1. Mapa"), DisplayName("Stmavenie mapy (%)"), Description("Čierny overlay cez mapu (0–80 %). Hodí sa najmä pri satelite.")]
    public int DarkenPercent { get; set; } = 0;

    [Category("1. Mapa"), DisplayName("Attribution text"), Description("Vpísať do map.png text o autorských právach k mapovým dátam.")]
    public bool DrawAttribution { get; set; } = true;

    // ---------------- Trasa ----------------

    [Category("2. Trasa"), DisplayName("Farba čiary")]
    [JsonIgnore]
    public Color LineColor { get; set; } = Color.FromArgb(0xFF, 0x5A, 0x1F);

    [Browsable(false)]
    public string LineColorHex
    {
        get => $"#{LineColor.R:X2}{LineColor.G:X2}{LineColor.B:X2}";
        set { try { LineColor = ColorTranslator.FromHtml(value); } catch { /* keep default */ } }
    }

    [Category("2. Trasa"), DisplayName("Hrúbka čiary (px pri 1080p)"), Description("Pri inom rozlíšení sa škáluje podľa výšky (4K → 2×).")]
    public double LineWidth1080 { get; set; } = 6;

    [Category("2. Trasa"), DisplayName("Zjednodušenie (px)"),
     Description("Tolerancia Douglas–Peucker v pixeloch map.png. Väčšie = menej bodov vo Fusion polyline.")]
    public double SimplifyTolerancePx { get; set; } = 0.5;

    [Category("2. Trasa"), DisplayName("Filter GPS šumu (m)"), Description("Vynechá body bližšie než X metrov k predchádzajúcemu (státie). 0 = vypnuté.")]
    public double MinPointDistanceM { get; set; } = 0.5;

    [Category("2. Trasa"), DisplayName("Obrázok na čele čiary"),
     Description("Obrázok (napr. vláčik), ktorý sa vo Fusion posúva po trase spolu s koncom kreslenej čiary. PNG s priehľadným pozadím. Prázdne = bez obrázka.")]
    [Editor(typeof(ImageFileNameEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public string MarkerImagePath { get; set; } = "";

    [Category("2. Trasa"), DisplayName("Veľkosť obrázka (px pri 1080p)"),
     Description("Dlhšia strana obrázka v pixeloch pri 1080p. Pri inom rozlíšení sa škáluje podľa výšky. Vo Fusion: MergeMarker → Size.")]
    public double MarkerSize1080 { get; set; } = 60;

    // ---------------- Doplnené body ----------------

    [Category("5. Doplnené body"), DisplayName("Rýchlosť doplnených úsekov (km/h)"),
     Description("Body doplnené na začiatok/koniec trasy nemajú čas z trackera. Dopočíta sa z tejto rýchlosti. 0 = priemerná rýchlosť v pohybe z GPX.")]
    public double ExtensionSpeedKmh { get; set; } = 0;

    [Category("5. Doplnené body"), DisplayName("Doplnený začiatok"),
     Description("Body pridáš cez 'Doplniť trasu' a klikaním do náhľadu. Ťahanie = posunúť, klik na úsek = vložiť bod, pravé tlačidlo = zmazať.")]
    [JsonIgnore]
    public string ExtraStartInfo => ExtraStart.Count == 0 ? "žiadny" : $"{ExtraStart.Count} b.";

    [Category("5. Doplnené body"), DisplayName("Doplnený koniec"), Description("Pozri 'Doplnený začiatok'.")]
    [JsonIgnore]
    public string ExtraEndInfo => ExtraEnd.Count == 0 ? "žiadny" : $"{ExtraEnd.Count} b.";

    /// <summary>Points added before the recorded start, index 0 = next to the first recorded point.</summary>
    [Browsable(false)]
    public List<GeoPoint> ExtraStart { get; set; } = [];

    /// <summary>Points added after the recorded end, index 0 = next to the last recorded point.</summary>
    [Browsable(false)]
    public List<GeoPoint> ExtraEnd { get; set; } = [];

    // ---------------- Popisy ----------------

    [Category("4. Popisy"), DisplayName("Výstup popisov"),
     Description("Fusion Text+ = každý popis je samostatný Text+ uzol, text, font aj polohu zmeníš v Resolve. Vpáliť do map.png = popisy sú súčasťou obrázka mapy, presne ako v náhľade.")]
    public LabelTarget LabelOutput { get; set; } = LabelTarget.FusionText;

    [Category("4. Popisy"), DisplayName("Počet popisov"),
     Description("Popis pridáš tlačidlom 'Pridať popis' a klikom do náhľadu. Dvojklik = upraviť, ťahanie = posunúť, pravé tlačidlo = menu.")]
    [JsonIgnore]
    public string LabelCountInfo => Labels.Count == 0 ? "žiadne" : Labels.Count.ToString();

    [Browsable(false)]
    public List<MapLabel> Labels { get; set; } = [];

    // ---------------- Fusion ----------------

    [Category("3. Fusion"), DisplayName("Zdroj mapy vo Fusion"),
     Description("Loader = .setting načíta map.png zo súboru. MediaIn1 = map.png si dáš na timeline, otvoríš na ňom Fusion a .setting sa napojí na MediaIn1 (najspoľahlivejšie).")]
    public FusionMapSource MapSource { get; set; } = FusionMapSource.Loader;

    [Category("3. Fusion"), DisplayName("Pridať keyframy Write Length"),
     Description("Vytvorí úvodnú animáciu kreslenia trasy (0 → 1). Ak vypnuté, trasa je celá a animuješ si ju sám.")]
    public bool AddKeyframes { get; set; } = false;

    [Category("3. Fusion"), DisplayName("Prispôsobiť dĺžke klipu"),
     Description("Animácia sa natiahne na celú dĺžku Fusion klipu (výraz s comp.RenderStart/RenderEnd namiesto keyframov). Skrátiš/predĺžiš klip na timeline → trasa sa kreslí rýchlejšie/pomalšie a vždy skončí na poslednom frame.")]
    public bool KeyframesFitClip { get; set; } = true;

    [Category("3. Fusion"), DisplayName("Dĺžka animácie (framy)"),
     Description("Použije sa len keď je vypnuté 'Prispôsobiť dĺžke klipu'.")]
    public int KeyframeFrames { get; set; } = 600;

    [Category("3. Fusion"), DisplayName("Oneskorenie začiatku (framy)"),
     Description("Koľko framov od začiatku klipu je trasa ešte skrytá. Pri 'Prispôsobiť dĺžke klipu' sa dá meniť aj vo Fusion: TrackPath → Inspector → Controls → Start Delay.")]
    public int AnimStartDelayFrames { get; set; } = 0;

    [Category("3. Fusion"), DisplayName("Rezerva na konci (framy)"),
     Description("Trasa dobehne toľko framov pred koncom klipu a potom ostane celá. Len pri 'Prispôsobiť dĺžke klipu'; vo Fusion: TrackPath → Controls → End Hold.")]
    public int AnimEndHoldFrames { get; set; } = 0;

    [Category("3. Fusion"), DisplayName("Časovať podľa GPX času"),
     Description("Trasa pribúda reálnym tempom (vrátane prestávok). Inak lineárne podľa dĺžky.")]
    public bool KeyframesByTime { get; set; } = true;

    [Category("3. Fusion"), DisplayName("Kalibračný rámik"),
     Description("Pridá vypnutý (PassThrough) uzol s rámikom 10 px od okrajov mapy – na overenie, že polyline sedí na mape.")]
    public bool AddCalibrationFrame { get; set; } = true;

    // ---------------- helpers ----------------

    [Browsable(false), JsonIgnore]
    public TileSource TileSource => Style switch
    {
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
