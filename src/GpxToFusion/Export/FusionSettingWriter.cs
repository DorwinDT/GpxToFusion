using System.Drawing;
using System.Globalization;
using System.Text;

namespace GpxToFusion.Export;

/// <summary>
/// Generates a Fusion .setting (Lua table) that can be pasted (Ctrl+V) into the Fusion page node editor:
///   MapLoader (map.png) ─ BG ─┐
///   TrackColor (masked by TrackPath polyline) ─ FG ─ MergeTrack ─ [MergeMarker] ─ [MergeCalib (pass-through)]
/// MergeMarker puts an image (MarkerLoader) on the head of the line: its Center follows MarkerPath, a PolyPath with
/// the same points as TrackPath whose Displacement is linked to TrackPath.WriteLength.
/// </summary>
public static class FusionSettingWriter
{
    public sealed record Options(
        string MapPngPath,
        int Width,
        int Height,
        Color LineColor,
        double LineWidthPx,
        bool AddKeyframes,
        int KeyframeFrames,
        bool KeyframesByTime,
        bool AddCalibrationFrame,
        bool UseLoader = true,
        bool FitToClip = false,
        int StartDelayFrames = 0,
        int EndHoldFrames = 0,
        string? MarkerImagePath = null,
        double MarkerSize = 1,
        IReadOnlyList<Label>? Labels = null);

    /// <summary>A Text+ label already converted to export pixels (X, Y = anchor in map.png px).</summary>
    public sealed record Label(string Text, double X, double Y, string Font, string Style, double FontPx,
        Color TextColor, double OutlinePx, Color OutlineColor, LabelAlign Align);

    /// <summary>User controls on TrackPath (Inspector → Controls) read by the WriteLength expression.</summary>
    internal const string StartDelayControl = "AnimStartDelay";
    internal const string EndHoldControl = "AnimEndHold";

    /// <summary>Hold frames for the still map image (Loader "Hold First/Last Frame"), so it is visible on every frame.</summary>
    private const int HoldFrames = 100000;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string F(double v) => v.ToString("0.######", Inv);

    public static string Write(TrackGeometry geo, Options o)
    {
        var sb = new StringBuilder();
        bool marker = !string.IsNullOrWhiteSpace(o.MarkerImagePath);
        var labels = o.Labels ?? [];
        string labelsOut = labels.Count > 0 ? $"MergeLabel{labels.Count}" : "MergeTrack";
        string trackOut = marker ? "MergeMarker" : labelsOut;
        string lastNode = o.AddCalibrationFrame ? "MergeCalib" : trackOut;
        int markerX = 330 + 110 * labels.Count;

        sb.AppendLine("{");
        sb.AppendLine("\tTools = ordered() {");

        // --- Map source: Loader with map.png, or MediaIn1 (map.png placed on the timeline) ---
        string mapNode = o.UseLoader ? "MapLoader" : "MediaIn1";
        if (o.UseLoader)
            AppendLoader(sb, "MapLoader", o.MapPngPath, (0, 0));

        // --- Polyline mask with the track ---
        AppendPolylineMask(sb, "TrackPath", geo.Simplified.Select(p => (p.X, p.Y)).ToList(), closed: false, o,
            writeLength: !o.AddKeyframes ? null
                : o.FitToClip ? WriteLengthSource.Expression(WriteLengthExpression(geo, o))
                : WriteLengthSource.Spline("TrackPathWriteLength"),
            pos: (0, 66));

        if (o.AddKeyframes && !o.FitToClip)
            AppendWriteLengthSpline(sb, geo, o);

        // --- Colored background masked by the polyline ---
        AppendBackground(sb, "TrackColor", "TrackPath", o.LineColor, o.Width, o.Height, (110, 66));

        // --- Merge over the map ---
        AppendMerge(sb, "MergeTrack", mapNode, "TrackColor", passThrough: false, (220, 0));

        // --- Text labels (Text+), each merged over the previous result ---
        string prev = "MergeTrack";
        for (int i = 0; i < labels.Count; i++)
        {
            int x = 330 + 110 * i;
            AppendTextPlus(sb, $"Label{i + 1}", labels[i], o, (x, -132));
            AppendMerge(sb, $"MergeLabel{i + 1}", prev, $"Label{i + 1}", passThrough: false, (x, 0));
            prev = $"MergeLabel{i + 1}";
        }

        // --- Image riding on the head of the line ---
        if (marker)
        {
            AppendLoader(sb, "MarkerLoader", o.MarkerImagePath!, (markerX, -66));
            AppendMarkerPath(sb, "MarkerPath", geo.Simplified.Select(p => (p.X, p.Y)).ToList(), o, (markerX - 110, -66));
            AppendMerge(sb, "MergeMarker", labelsOut, "MarkerLoader", passThrough: false, (markerX, 0),
                extraInputs:
                [
                    "Center = Input { SourceOp = \"MarkerPath\", Source = \"Position\", },",
                    $"Size = Input {{ Value = {F(o.MarkerSize)}, }},",
                ]);
        }

        if (o.AddCalibrationFrame)
        {
            double inset = 10;
            var rect = new List<(double, double)>
            {
                (inset, inset), (o.Width - inset, inset), (o.Width - inset, o.Height - inset), (inset, o.Height - inset),
            };
            AppendPolylineMask(sb, "CalibrationFrame", rect, closed: true, o with { LineWidthPx = Math.Max(4, o.LineWidthPx / 2) },
                writeLength: null, pos: (220, 132));
            AppendBackground(sb, "CalibColor", "CalibrationFrame", Color.Magenta, o.Width, o.Height, (330, 132));
            AppendMerge(sb, "MergeCalib", trackOut, "CalibColor", passThrough: true, (markerX + (marker ? 110 : 0), 0));
        }

        sb.AppendLine("\t},");
        sb.AppendLine($"\tActiveTool = \"{lastNode}\"");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendPolylineMask(StringBuilder sb, string name, IReadOnlyList<(double X, double Y)> pixelPts, bool closed,
        Options o, WriteLengthSource? writeLength, (int X, int Y) pos)
    {
        var pts = pixelPts.Select(p => FusionCoords.ToPolyline(p.X, p.Y, o.Width, o.Height)).ToList();
        double borderWidth = o.LineWidthPx / o.Width / 2; // Fusion border width is measured on each side of the line

        bool timingControls = writeLength?.Expr != null;

        sb.AppendLine($"\t\t{name} = PolylineMask {{");
        sb.AppendLine("\t\t\tDrawMode = \"ModifyOnly\",");
        sb.AppendLine("\t\t\tDrawMode2 = \"InsertAndModify\",");
        sb.AppendLine("\t\t\tInputs = {");
        if (timingControls)
        {
            sb.AppendLine($"\t\t\t\t{StartDelayControl} = Input {{ Value = {Math.Max(0, o.StartDelayFrames)}, }},");
            sb.AppendLine($"\t\t\t\t{EndHoldControl} = Input {{ Value = {Math.Max(0, o.EndHoldFrames)}, }},");
        }
        sb.AppendLine($"\t\t\t\tMaskWidth = Input {{ Value = {o.Width}, }},");
        sb.AppendLine($"\t\t\t\tMaskHeight = Input {{ Value = {o.Height}, }},");
        sb.AppendLine("\t\t\t\tPixelAspect = Input { Value = { 1, 1 }, },");
        sb.AppendLine("\t\t\t\tClippingMode = Input { Value = FuID { \"None\" }, },");
        sb.AppendLine("\t\t\t\tSolid = Input { Value = 0, },");
        sb.AppendLine($"\t\t\t\tBorderWidth = Input {{ Value = {F(borderWidth)}, }},");
        if (writeLength?.SplineOp is { } op)
            sb.AppendLine($"\t\t\t\tWriteLength = Input {{ SourceOp = \"{op}\", Source = \"Value\", }},");
        else if (writeLength?.Expr is { } expr)
            sb.AppendLine($"\t\t\t\tWriteLength = Input {{ Value = 0, Expression = \"{expr}\", }},");
        AppendPolylineInput(sb, "Polyline", pts, closed);
        sb.AppendLine("\t\t\t},");
        sb.AppendLine($"\t\t\tViewInfo = OperatorInfo {{ Pos = {{ {pos.X}, {pos.Y} }} }},");
        if (timingControls)
        {
            sb.AppendLine("\t\t\tUserControls = ordered() {");
            AppendFrameControl(sb, StartDelayControl, "Start Delay (frames)", last: false);
            AppendFrameControl(sb, EndHoldControl, "End Hold (frames)", last: true);
            sb.AppendLine("\t\t\t},");
        }
        sb.AppendLine("\t\t},");
    }

    /// <summary>Polyline input with linear segments (mask: "Polyline", path: "PolyLine").</summary>
    private static void AppendPolylineInput(StringBuilder sb, string inputName, IReadOnlyList<(double X, double Y)> pts, bool closed)
    {
        sb.AppendLine($"\t\t\t\t{inputName} = Input {{");
        sb.AppendLine("\t\t\t\t\tValue = Polyline {");
        if (closed) sb.AppendLine("\t\t\t\t\t\tClosed = true,");
        sb.AppendLine("\t\t\t\t\t\tPoints = {");
        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            var p = pts[i];
            // Linear segments: handles point 1/3 of the way to the neighbours (what Fusion writes itself).
            var prev = i > 0 ? pts[i - 1] : (closed ? pts[n - 1] : p);
            var next = i < n - 1 ? pts[i + 1] : (closed ? pts[0] : p);
            double lx = (prev.X - p.X) / 3, ly = (prev.Y - p.Y) / 3;
            double rx = (next.X - p.X) / 3, ry = (next.Y - p.Y) / 3;
            sb.Append("\t\t\t\t\t\t\t{ Linear = true, ");
            sb.Append($"X = {F(p.X)}, Y = {F(p.Y)}, ");
            sb.Append($"LX = {F(lx)}, LY = {F(ly)}, RX = {F(rx)}, RY = {F(ry)} }}");
            sb.AppendLine(i < n - 1 ? "," : "");
        }
        sb.AppendLine("\t\t\t\t\t\t}");
        sb.AppendLine("\t\t\t\t\t},");
        sb.AppendLine("\t\t\t\t},");
    }

    /// <summary>
    /// How MarkerPath.Displacement is derived from TrackPath.WriteLength. Both are "fraction of the path length", but
    /// verified in Resolve: with Displacement = WriteLength the marker sits on the path and matches at both ends,
    /// yet drifts behind the line head in between, i.e. the two measure length differently. The mask knows its
    /// MaskWidth/Height, so it most likely measures in pixels; the path in normalized X/W, Y/H units.
    /// The other options stay selectable in Resolve (MarkerPath → Controls → Path Sync) in case that guess is wrong.
    /// </summary>
    internal enum PathSync
    {
        Off = 0,
        MaskPixelsPathNormalized = 1,
        MaskNormalizedPathPixels = 2,
        MaskPixelsPathPerPoint = 3,
    }

    private static readonly string[] PathSyncLabels =
    [
        "Off (Displacement = WriteLength)",
        "Mask px / Path normalized",
        "Mask normalized / Path px",
        "Mask px / Path per point",
    ];

    internal const string PathSyncControl = "PathSync";

    /// <summary>Max. breakpoints of each WriteLength → Displacement curve.</summary>
    private const int MaxSyncCurvePoints = 30;

    internal static string DisplacementExpression(IReadOnlyList<(double X, double Y)> pixelPts, int w, int h, string self)
    {
        const string wl = "TrackPath.WriteLength";
        string sel = $"{self}.{PathSyncControl}";
        string f1 = Piecewise(ArcSyncCurve(pixelPts, w, h, PathSync.MaskPixelsPathNormalized), wl);
        string f2 = Piecewise(ArcSyncCurve(pixelPts, w, h, PathSync.MaskNormalizedPathPixels), wl);
        string f3 = Piecewise(ArcSyncCurve(pixelPts, w, h, PathSync.MaskPixelsPathPerPoint), wl);
        return $"iif({sel}<0.5, {wl}, iif({sel}<1.5, {f1}, iif({sel}<2.5, {f2}, {f3})))";
    }

    /// <summary>(WriteLength, Displacement) breakpoints: same point on the polyline, measured in the mask's and the path's metric.</summary>
    internal static List<(double X, double Y)> ArcSyncCurve(IReadOnlyList<(double X, double Y)> px, int w, int h, PathSync mode)
    {
        int n = px.Count;
        if (n < 2 || mode == PathSync.Off) return [(0, 0), (1, 1)];
        double[] Cum(Func<int, double> seg)
        {
            var c = new double[n];
            for (int i = 1; i < n; i++) c[i] = c[i - 1] + seg(i);
            return c;
        }
        double Px(int i) => Math.Sqrt(Math.Pow(px[i].X - px[i - 1].X, 2) + Math.Pow(px[i].Y - px[i - 1].Y, 2));
        double Norm(int i) => Math.Sqrt(Math.Pow((px[i].X - px[i - 1].X) / w, 2) + Math.Pow((px[i].Y - px[i - 1].Y) / h, 2));
        var pxCum = Cum(Px);
        var normCum = Cum(Norm);
        var idxCum = Cum(_ => 1);
        var (src, dst) = mode switch
        {
            PathSync.MaskNormalizedPathPixels => (normCum, pxCum),
            PathSync.MaskPixelsPathPerPoint => (pxCum, idxCum),
            _ => (pxCum, normCum),
        };
        if (!(src[^1] > 0) || !(dst[^1] > 0)) return [(0, 0), (1, 1)];

        const int samples = 600;
        var pts = new List<(double X, double Y)>(samples + 1);
        int seg = 1;
        for (int k = 0; k <= samples; k++)
        {
            double u = (double)k / samples;
            double s = u * src[^1];
            while (seg < n - 1 && src[seg] < s) seg++;
            double len = src[seg] - src[seg - 1];
            double t = len > 0 ? Math.Clamp((s - src[seg - 1]) / len, 0, 1) : 1;
            pts.Add((u, (dst[seg - 1] + t * (dst[seg] - dst[seg - 1])) / dst[^1]));
        }
        pts[0] = (0, 0);
        pts[^1] = (1, 1);

        List<int> kept;
        double tol = 0.0005;
        do { kept = PolylineSimplifier.Simplify(pts, tol); tol *= 1.5; } while (kept.Count > MaxSyncCurvePoints);
        return kept.Select(i => pts[i]).ToList();
    }

    /// <summary>Piecewise-linear function of <paramref name="variable"/> as nested iif(), clamped to 0..1.</summary>
    internal static string Piecewise(IReadOnlyList<(double X, double Y)> curve, string variable)
    {
        if (curve.Count < 2) return $"min(1, max(0, {variable}))";
        var e = new StringBuilder();
        int segs = curve.Count - 1;
        for (int i = 1; i <= segs; i++)
        {
            var (u0, y0) = curve[i - 1];
            var (u1, y1) = curve[i];
            double b = u1 > u0 ? (y1 - y0) / (u1 - u0) : 0;
            double a = y0 - b * u0;
            string lin = $"{F(a)}+{F(b)}*{variable}";
            if (i < segs) e.Append($"iif({variable}<{F(u1)}, {lin}, ");
            else e.Append(lin);
        }
        e.Append(')', segs - 1);
        return $"min(1, max(0, {e}))";
    }

    /// <summary>
    /// Motion path for the marker, with exactly the same points as TrackPath. Center {0.5, 0.5} turns the
    /// mask-style points (-0.5..0.5) into Merge Center coordinates (0..1). Displacement: see <see cref="PathSync"/>.
    /// </summary>
    private static void AppendMarkerPath(StringBuilder sb, string name, IReadOnlyList<(double X, double Y)> pixelPts, Options o,
        (int X, int Y) pos)
    {
        var pts = pixelPts.Select(p => FusionCoords.ToPolyline(p.X, p.Y, o.Width, o.Height)).ToList();
        sb.AppendLine($"\t\t{name} = PolyPath {{");
        sb.AppendLine("\t\t\tDrawMode = \"InsertAndModify\",");
        sb.AppendLine("\t\t\tCtrlWZoom = false,");
        sb.AppendLine("\t\t\tInputs = {");
        sb.AppendLine($"\t\t\t\t{PathSyncControl} = Input {{ Value = {(int)PathSync.MaskPixelsPathNormalized}, }},");
        sb.AppendLine("\t\t\t\tCenter = Input { Value = { 0.5, 0.5 }, },");
        sb.AppendLine($"\t\t\t\tDisplacement = Input {{ Value = 0, Expression = \"{DisplacementExpression(pixelPts, o.Width, o.Height, name)}\", }},");
        AppendPolylineInput(sb, "PolyLine", pts, closed: false);
        sb.AppendLine("\t\t\t},");
        sb.AppendLine($"\t\t\tViewInfo = OperatorInfo {{ Pos = {{ {pos.X}, {pos.Y} }} }},");
        sb.AppendLine("\t\t\tUserControls = ordered() {");
        sb.AppendLine($"\t\t\t\t{PathSyncControl} = {{");
        sb.AppendLine("\t\t\t\t\tLINKS_Name = \"Path Sync\",");
        sb.AppendLine("\t\t\t\t\tLINKID_DataType = \"Number\",");
        sb.AppendLine("\t\t\t\t\tINPID_InputControl = \"ComboControl\",");
        sb.AppendLine("\t\t\t\t\tINP_Integer = true,");
        sb.AppendLine($"\t\t\t\t\tINP_Default = {(int)PathSync.MaskPixelsPathNormalized},");
        foreach (var label in PathSyncLabels) sb.AppendLine($"\t\t\t\t\t{{ CCS_AddString = \"{label}\", }},");
        sb.AppendLine("\t\t\t\t\tCC_LabelPosition = \"Vertical\",");
        sb.AppendLine("\t\t\t\t\tICS_ControlPage = \"Controls\",");
        sb.AppendLine("\t\t\t\t}");
        sb.AppendLine("\t\t\t},");
        sb.AppendLine("\t\t},");
    }

    /// <summary>
    /// Text+ generator with the map's size. Center is 0..1 with Y up; the text block is centered vertically on it and
    /// HorizontalLeftCenterRight anchors its left edge / center / right edge (like the preview).
    /// Unverified in Resolve: Size = font px / image width, and outline Thickness2 = outline px / font px.
    /// </summary>
    private static void AppendTextPlus(StringBuilder sb, string name, Label l, Options o, (int X, int Y) pos)
    {
        var (cx, cy) = FusionCoords.ToNormalized(l.X, l.Y, o.Width, o.Height);
        int anchor = l.Align switch { LabelAlign.Left => -1, LabelAlign.Right => 1, _ => 0 };
        sb.AppendLine($"\t\t{name} = TextPlus {{");
        sb.AppendLine("\t\t\tInputs = {");
        sb.AppendLine($"\t\t\t\tWidth = Input {{ Value = {o.Width}, }},");
        sb.AppendLine($"\t\t\t\tHeight = Input {{ Value = {o.Height}, }},");
        sb.AppendLine("\t\t\t\tUseFrameFormatSettings = Input { Value = 0, },");
        sb.AppendLine($"\t\t\t\tCenter = Input {{ Value = {{ {F(cx)}, {F(cy)} }}, }},");
        sb.AppendLine($"\t\t\t\tStyledText = Input {{ Value = \"{LuaString(l.Text)}\", }},");
        sb.AppendLine($"\t\t\t\tFont = Input {{ Value = \"{LuaString(l.Font)}\", }},");
        sb.AppendLine($"\t\t\t\tStyle = Input {{ Value = \"{LuaString(l.Style)}\", }},");
        sb.AppendLine($"\t\t\t\tSize = Input {{ Value = {F(l.FontPx / o.Width)}, }},");
        sb.AppendLine($"\t\t\t\tHorizontalLeftCenterRight = Input {{ Value = {anchor}, }},");
        sb.AppendLine($"\t\t\t\tRed1 = Input {{ Value = {F(l.TextColor.R / 255.0)}, }},");
        sb.AppendLine($"\t\t\t\tGreen1 = Input {{ Value = {F(l.TextColor.G / 255.0)}, }},");
        sb.AppendLine($"\t\t\t\tBlue1 = Input {{ Value = {F(l.TextColor.B / 255.0)}, }},");
        if (l.OutlinePx > 0)
        {
            sb.AppendLine("\t\t\t\tEnabled2 = Input { Value = 1, },");
            sb.AppendLine($"\t\t\t\tThickness2 = Input {{ Value = {F(l.OutlinePx / Math.Max(1, l.FontPx))}, }},");
            sb.AppendLine($"\t\t\t\tRed2 = Input {{ Value = {F(l.OutlineColor.R / 255.0)}, }},");
            sb.AppendLine($"\t\t\t\tGreen2 = Input {{ Value = {F(l.OutlineColor.G / 255.0)}, }},");
            sb.AppendLine($"\t\t\t\tBlue2 = Input {{ Value = {F(l.OutlineColor.B / 255.0)}, }},");
        }
        sb.AppendLine("\t\t\t},");
        sb.AppendLine($"\t\t\tViewInfo = OperatorInfo {{ Pos = {{ {pos.X}, {pos.Y} }} }},");
        sb.AppendLine("\t\t},");
    }

    /// <summary>Escapes text for a Lua double-quoted string.</summary>
    internal static string LuaString(string s) => s
        .Replace("\\", "\\\\").Replace("\"", "\\\"")
        .Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n");

    /// <summary>Loader of a still image, held on every frame.</summary>
    private static void AppendLoader(StringBuilder sb, string name, string file, (int X, int Y) pos)
    {
        string path = Path.GetFullPath(file).Replace("\\", "\\\\").Replace("\"", "\\\"");
        string? format = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".png" => "PNGFormat",
            ".jpg" or ".jpeg" => "JpegFormat",
            ".tif" or ".tiff" => "TiffFormat",
            ".bmp" => "BMPFormat",
            ".tga" => "TargaFormat",
            _ => null,
        };
        sb.AppendLine($"\t\t{name} = Loader {{");
        sb.AppendLine("\t\t\tClips = {");
        sb.AppendLine("\t\t\t\tClip {");
        sb.AppendLine("\t\t\t\t\tID = \"Clip1\",");
        sb.AppendLine($"\t\t\t\t\tFilename = \"{path}\",");
        if (format != null) sb.AppendLine($"\t\t\t\t\tFormatID = \"{format}\",");
        sb.AppendLine("\t\t\t\t\tStartFrame = -1,");
        sb.AppendLine("\t\t\t\t\tLengthSetManually = true,");
        sb.AppendLine("\t\t\t\t\tTrimIn = 0,");
        sb.AppendLine("\t\t\t\t\tTrimOut = 0,");
        sb.AppendLine($"\t\t\t\t\tExtendFirst = {HoldFrames},");
        sb.AppendLine($"\t\t\t\t\tExtendLast = {HoldFrames},");
        sb.AppendLine("\t\t\t\t\tLoop = 0,");
        sb.AppendLine("\t\t\t\t\tAspectMode = 0,");
        sb.AppendLine("\t\t\t\t\tDepth = 0,");
        sb.AppendLine("\t\t\t\t\tTimeCode = 0,");
        sb.AppendLine("\t\t\t\t\tGlobalStart = 0,");
        sb.AppendLine("\t\t\t\t\tGlobalEnd = 0");
        sb.AppendLine("\t\t\t\t}");
        sb.AppendLine("\t\t\t},");
        sb.AppendLine("\t\t\tInputs = {");
        sb.AppendLine($"\t\t\t\tHoldFirstFrame = Input {{ Value = {HoldFrames}, }},");
        sb.AppendLine($"\t\t\t\tHoldLastFrame = Input {{ Value = {HoldFrames}, }},");
        sb.AppendLine("\t\t\t},");
        sb.AppendLine($"\t\t\tViewInfo = OperatorInfo {{ Pos = {{ {pos.X}, {pos.Y} }} }},");
        sb.AppendLine("\t\t},");
    }

    private static void AppendFrameControl(StringBuilder sb, string id, string label, bool last)
    {
        sb.AppendLine($"\t\t\t\t{id} = {{");
        sb.AppendLine($"\t\t\t\t\tLINKS_Name = \"{label}\",");
        sb.AppendLine("\t\t\t\t\tLINKID_DataType = \"Number\",");
        sb.AppendLine("\t\t\t\t\tINPID_InputControl = \"SliderControl\",");
        sb.AppendLine("\t\t\t\t\tINP_Integer = true,");
        sb.AppendLine("\t\t\t\t\tINP_Default = 0,");
        sb.AppendLine("\t\t\t\t\tINP_MinAllowed = 0,");
        sb.AppendLine("\t\t\t\t\tINP_MinScale = 0,");
        sb.AppendLine("\t\t\t\t\tINP_MaxScale = 250,");
        sb.AppendLine("\t\t\t\t\tICS_ControlPage = \"Controls\",");
        sb.AppendLine(last ? "\t\t\t\t}" : "\t\t\t\t},");
    }

    private sealed record WriteLengthSource(string? SplineOp, string? Expr)
    {
        public static WriteLengthSource Spline(string op) => new(op, null);
        public static WriteLengthSource Expression(string expr) => new(null, expr);
    }

    /// <summary>Normalized progress 0..1 over the comp render range (= the Fusion clip length in Resolve).
    /// Starts after TrackPath.AnimStartDelay frames and reaches 1 TrackPath.AnimEndHold frames before the end.</summary>
    internal const string ProgressExpr =
        "((time-comp.RenderStart-TrackPath." + StartDelayControl + ")/max(1, comp.RenderEnd-comp.RenderStart-TrackPath." + StartDelayControl + "-TrackPath." + EndHoldControl + "))";

    /// <summary>Max. breakpoints of the time-based WriteLength curve (keeps the expression short).</summary>
    private const int MaxCurvePoints = 40;

    /// <summary>
    /// Fusion simple expression for WriteLength that stretches the animation over the whole clip:
    /// linear progress, or (KeyframesByTime) a piecewise-linear GPX time → length curve made of nested iif().
    /// </summary>
    internal static string WriteLengthExpression(TrackGeometry geo, Options o)
    {
        string p = ProgressExpr;
        var curve = o.KeyframesByTime ? TimeCurve(geo) : null;
        if (curve == null || curve.Count < 2)
            return $"min(1, max(0, {p}))";

        return Piecewise(curve, p);
    }

    /// <summary>(normalized time, length fraction) breakpoints, simplified with Douglas–Peucker.</summary>
    internal static List<(double X, double Y)>? TimeCurve(TrackGeometry geo)
    {
        if (geo.Simplified.Count < 2) return null;
        double t0 = geo.Simplified[0].TimeSec, t1 = geo.Simplified[^1].TimeSec;
        if (!(t1 > t0)) return null;
        var cum = TrackGeometry.CumulativeLength(geo.Simplified);
        const int samples = 400;
        var pts = new List<(double X, double Y)>(samples + 1);
        for (int k = 0; k <= samples; k++)
        {
            double u = (double)k / samples;
            pts.Add((u, geo.LengthFractionAtTime(t0 + (t1 - t0) * u, cum)));
        }
        pts[0] = (0, 0);
        pts[^1] = (1, 1);

        List<int> kept;
        double tol = 0.001;
        do { kept = PolylineSimplifier.Simplify(pts, tol); tol *= 1.5; } while (kept.Count > MaxCurvePoints);
        return kept.Select(i => pts[i]).ToList();
    }

    private static void AppendWriteLengthSpline(StringBuilder sb, TrackGeometry geo, Options o)
    {
        int frames = Math.Max(1, o.KeyframeFrames);
        var keys = new List<(int Frame, double Value)>();
        if (o.KeyframesByTime && geo.Simplified.Count >= 2)
        {
            var cum = TrackGeometry.CumulativeLength(geo.Simplified);
            double t0 = geo.Simplified[0].TimeSec, t1 = geo.Simplified[^1].TimeSec;
            const int step = 10;
            for (int f = 0; f <= frames; f += step)
                keys.Add((f, geo.LengthFractionAtTime(t0 + (t1 - t0) * f / frames, cum)));
            if (keys[^1].Frame != frames) keys.Add((frames, 1));
            keys[0] = (0, 0);
            keys[^1] = (frames, 1);
        }
        else
        {
            keys.Add((0, 0));
            keys.Add((frames, 1));
        }

        sb.AppendLine("\t\tTrackPathWriteLength = BezierSpline {");
        sb.AppendLine("\t\t\tSplineColor = { Red = 236, Green = 236, Blue = 236 },");
        sb.AppendLine("\t\t\tNameSet = true,");
        sb.AppendLine("\t\t\tKeyFrames = {");
        for (int i = 0; i < keys.Count; i++)
        {
            sb.Append($"\t\t\t\t[{keys[i].Frame + Math.Max(0, o.StartDelayFrames)}] = {{ {F(keys[i].Value)}, Flags = {{ Linear = true }} }}");
            sb.AppendLine(i < keys.Count - 1 ? "," : "");
        }
        sb.AppendLine("\t\t\t}");
        sb.AppendLine("\t\t},");
    }

    private static void AppendBackground(StringBuilder sb, string name, string maskNode, Color c, int w, int h, (int X, int Y) pos)
    {
        sb.AppendLine($"\t\t{name} = Background {{");
        sb.AppendLine("\t\t\tInputs = {");
        sb.AppendLine($"\t\t\t\tWidth = Input {{ Value = {w}, }},");
        sb.AppendLine($"\t\t\t\tHeight = Input {{ Value = {h}, }},");
        sb.AppendLine($"\t\t\t\tTopLeftRed = Input {{ Value = {F(c.R / 255.0)}, }},");
        sb.AppendLine($"\t\t\t\tTopLeftGreen = Input {{ Value = {F(c.G / 255.0)}, }},");
        sb.AppendLine($"\t\t\t\tTopLeftBlue = Input {{ Value = {F(c.B / 255.0)}, }},");
        sb.AppendLine($"\t\t\t\tEffectMask = Input {{ SourceOp = \"{maskNode}\", Source = \"Mask\", }},");
        sb.AppendLine("\t\t\t},");
        sb.AppendLine($"\t\t\tViewInfo = OperatorInfo {{ Pos = {{ {pos.X}, {pos.Y} }} }},");
        sb.AppendLine("\t\t},");
    }

    private static void AppendMerge(StringBuilder sb, string name, string bg, string fg, bool passThrough, (int X, int Y) pos,
        IEnumerable<string>? extraInputs = null)
    {
        sb.AppendLine($"\t\t{name} = Merge {{");
        if (passThrough) sb.AppendLine("\t\t\tPassThrough = true,");
        sb.AppendLine("\t\t\tInputs = {");
        sb.AppendLine($"\t\t\t\tBackground = Input {{ SourceOp = \"{bg}\", Source = \"Output\", }},");
        sb.AppendLine($"\t\t\t\tForeground = Input {{ SourceOp = \"{fg}\", Source = \"Output\", }},");
        foreach (var line in extraInputs ?? []) sb.AppendLine($"\t\t\t\t{line}");
        sb.AppendLine("\t\t\t},");
        sb.AppendLine($"\t\t\tViewInfo = OperatorInfo {{ Pos = {{ {pos.X}, {pos.Y} }} }},");
        sb.AppendLine("\t\t},");
    }
}
