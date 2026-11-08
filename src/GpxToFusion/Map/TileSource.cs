namespace GpxToFusion.Map;

public sealed record TileSource(string Key, string Name, string UrlTemplate, string[] Subdomains, int MaxZoom, string Attribution)
{
    public string GetUrl(int z, int x, int y)
    {
        string url = UrlTemplate.Replace("{z}", z.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString());
        if (Subdomains.Length > 0)
            url = url.Replace("{s}", Subdomains[Math.Abs(x + y) % Subdomains.Length]);
        return url;
    }

    public override string ToString() => Name;

    public static readonly TileSource OpenStreetMap = new("osm", "OpenStreetMap",
        "https://tile.openstreetmap.org/{z}/{x}/{y}.png", [], 19, "© OpenStreetMap contributors");

    public static readonly TileSource OpenStreetMapLatin = new("osm-latin", "OpenStreetMap – Latin labels",
        "https://tile.openstreetmap.de/{z}/{x}/{y}.png", [], 18, "© OpenStreetMap contributors, tiles: openstreetmap.de");

    // Tiles exist only up to z12 (checked in Egypt and Slovakia); the service answers 404 above that.
    public static readonly TileSource NatGeo = new("natgeo", "National Geographic – English labels",
        "https://server.arcgisonline.com/ArcGIS/rest/services/NatGeo_World_Map/MapServer/tile/{z}/{y}/{x}?blankTile=false", [], 12,
        "Tiles © Esri — National Geographic, Esri, DeLorme, NAVTEQ, UN-WCMC, USGS, NASA, ESA, METI, NRCAN, GEBCO, NOAA, iPC");

    public static readonly TileSource EsriImagery = new("esri-imagery", "Esri World Imagery (satellite)",
        "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}?blankTile=false", [], 19, // blankTile=false: 404 instead of a "Map data not yet available" image
        "Tiles © Esri — Source: Esri, Maxar, Earthstar Geographics");

    public static readonly TileSource CartoDark = new("carto-dark", "CARTO Dark",
        "https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}.png", ["a", "b", "c", "d"], 20,
        "© OpenStreetMap contributors © CARTO");

    public static readonly TileSource CartoLight = new("carto-light", "CARTO Light",
        "https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}.png", ["a", "b", "c", "d"], 20,
        "© OpenStreetMap contributors © CARTO");

    public static readonly TileSource[] All = [OpenStreetMap, OpenStreetMapLatin, NatGeo, EsriImagery, CartoDark, CartoLight];

    public static TileSource ByKey(string? key) => All.FirstOrDefault(s => s.Key == key) ?? OpenStreetMap;
}
