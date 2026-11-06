using System.ComponentModel;
using GpxToFusion.Export;
using GpxToFusion.Geo;
using GpxToFusion.Gpx;
using GpxToFusion.Map;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace GpxToFusion.UI;

public enum PreviewMode
{
    ExportFrame,
    FullTrack,
}

/// <summary>Which end of the track a click on the map extends.</summary>
public enum ExtendMode
{
    None,
    Start,
    End,
}

/// <summary>
/// Map preview. Shows the export frame (map.png) letterboxed in the control with surrounding context dimmed,
/// the whole track in grey, the cropped part in the line color and start/end markers.
/// </summary>
public sealed class MapPreview : SKControl
{
    private readonly TileCache _cache;
    private int _invalidatePending;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Track? FullTrack { get; set; }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CropStart { get; set; }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CropEnd { get; set; }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ExportSettings Settings { get; set; } = new();
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MapFrame? ExportFrame { get; set; }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PreviewMode Mode { get; set; } = PreviewMode.ExportFrame;
    /// <summary>Point index to highlight (hover in the timeline), -1 = none.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int HighlightIndex { get; set; } = -1;

    /// <summary>When on, a left click on the map raises <see cref="LabelAddRequested"/>.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AddLabelMode
    {
        get => _addLabelMode;
        set { _addLabelMode = value; Cursor = value || _extendMode != ExtendMode.None ? Cursors.Cross : Cursors.Default; }
    }

    /// <summary>Points added before/after the recorded track (shared with ExportSettings, edited in place).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<GeoPoint> ExtraStart { get; set; } = [];
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<GeoPoint> ExtraEnd { get; set; } = [];
    /// <summary>First / last recorded GPX point - the chains of added points hang on these.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TrackPoint? StartAnchor { get; set; }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TrackPoint? EndAnchor { get; set; }

    /// <summary>When not None: click = add point to that end, drag = move, click on a segment = insert, right click = delete.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ExtendMode ExtendMode
    {
        get => _extendMode;
        set { _extendMode = value; Cursor = value != ExtendMode.None || _addLabelMode ? Cursors.Cross : Cursors.Default; Invalidate(); }
    }

    /// <summary>Added points changed. Argument: true = edit finished, false = still dragging (the view is frozen meanwhile).</summary>
    public event Action<bool>? ExtensionChanged;

    /// <summary>Click on the map in add mode: (lat, lon).</summary>
    public event Action<double, double>? LabelAddRequested;
    /// <summary>Double-click on a label.</summary>
    public event Action<MapLabel>? LabelEditRequested;
    /// <summary>Right-click menu → delete.</summary>
    public event Action<MapLabel>? LabelDeleteRequested;
    /// <summary>A label was dragged to a new place.</summary>
    public event Action<MapLabel>? LabelMoved;
    /// <summary>The user panned/zoomed the export frame. Argument 2: true = finished, false = still dragging.</summary>
    public event Action<MapFrame, bool>? FrameChanged;

    private bool _addLabelMode;
    private ExtendMode _extendMode;
    private (List<GeoPoint> List, int Index)? _hoverPt, _dragPt;
    private MapFrame? _frozenView;
    private SKRect _exportRect, _frozenRect;
    private MapFrame? _view;
    private List<(MapLabel Label, SKRect Bounds)> _labelHits = [];
    private MapLabel? _hover, _drag;
    private SKPoint _dragOffset;
    private bool _dragMoved;
    private readonly ContextMenuStrip _labelMenu = new();
    private MapLabel? _menuLabel;
    // Panning the export frame: frame and mouse position at drag start.
    private MapFrame? _panFrame;
    private Point _panStart;
    private bool _panning;
    /// <summary>Left press in add-label mode: a click adds a label, a drag pans.</summary>
    private bool _panIsLabelClick;

    public MapPreview(TileCache cache)
    {
        _cache = cache;
        _cache.TileLoaded += _ => RequestRedraw();
        _labelMenu.Items.Add("Edit label…", null, (_, _) => { if (_menuLabel != null) LabelEditRequested?.Invoke(_menuLabel); });
        _labelMenu.Items.Add("Delete label", null, (_, _) => { if (_menuLabel != null) LabelDeleteRequested?.Invoke(_menuLabel); });
    }

    private MapLabel? HitLabel(Point p)
    {
        for (int i = _labelHits.Count - 1; i >= 0; i--) // topmost (last drawn) first
        {
            var r = _labelHits[i].Bounds;
            r.Inflate(4, 4);
            if (r.Contains(p.X, p.Y)) return _labelHits[i].Label;
        }
        return null;
    }

    private IEnumerable<(List<GeoPoint> List, TrackPoint Anchor)> Chains()
    {
        if (StartAnchor != null) yield return (ExtraStart, StartAnchor);
        if (EndAnchor != null) yield return (ExtraEnd, EndAnchor);
    }

    private (List<GeoPoint> List, int Index)? HitExtraPoint(Point p)
    {
        if (_view == null) return null;
        foreach (var (list, _) in Chains())
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var (x, y) = _view.ToPixel(list[i].Lat, list[i].Lon);
                if (Math.Abs(x - p.X) <= 7 && Math.Abs(y - p.Y) <= 7) return (list, i);
            }
        return null;
    }

    /// <summary>Segment of an added chain under the cursor; Index = where a new point is inserted.</summary>
    private (List<GeoPoint> List, int Index)? HitExtraSegment(Point p)
    {
        if (_view == null) return null;
        foreach (var (list, anchor) in Chains())
        {
            var (px, py) = _view.ToPixel(anchor.Lat, anchor.Lon);
            for (int i = 0; i < list.Count; i++)
            {
                var (qx, qy) = _view.ToPixel(list[i].Lat, list[i].Lon);
                if (DistToSegment(p.X, p.Y, px, py, qx, qy) <= 5) return (list, i);
                (px, py) = (qx, qy);
            }
        }
        return null;
    }

    private static double DistToSegment(double x, double y, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
        double t = len2 <= 0 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / len2, 0, 1);
        double ex = ax + t * dx - x, ey = ay + t * dy - y;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    private void BeginPointDrag(List<GeoPoint> list, int index)
    {
        _dragPt = (list, index);
        _frozenView = _view;
        _frozenRect = _exportRect;
        Capture = true;
    }

    /// <returns>true when the click was consumed by point editing.</returns>
    private bool ExtendMouseDown(MouseEventArgs e)
    {
        if (_extendMode == ExtendMode.None || _view == null) return false;
        var pt = HitExtraPoint(e.Location);
        if (e.Button == MouseButtons.Right)
        {
            if (pt is not { } del) return false;
            del.List.RemoveAt(del.Index);
            _hoverPt = null;
            ExtensionChanged?.Invoke(true);
            return true;
        }
        if (e.Button != MouseButtons.Left) return false;
        if (pt is { } hit)
        {
            BeginPointDrag(hit.List, hit.Index);
            return true;
        }
        if (HitLabel(e.Location) != null) return false; // let the label be dragged
        if (ToGeo(e.X, e.Y) is not { } g) return false;
        if (HitExtraSegment(e.Location) is { } seg)
        {
            seg.List.Insert(seg.Index, new GeoPoint(g.Lat, g.Lon));
            BeginPointDrag(seg.List, seg.Index);
            ExtensionChanged?.Invoke(false);
            return true;
        }
        var target = _extendMode == ExtendMode.Start ? ExtraStart : ExtraEnd;
        target.Add(new GeoPoint(g.Lat, g.Lon));
        ExtensionChanged?.Invoke(true);
        return true;
    }

    private (double Lat, double Lon)? ToGeo(float x, float y) =>
        _view == null ? null : WebMercator.FromWorld(_view.Left + x, _view.Top + y, _view.Zoom);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (ExtendMouseDown(e)) return;
        var hit = HitLabel(e.Location);
        if (e.Button == MouseButtons.Right && hit != null)
        {
            _menuLabel = hit;
            _labelMenu.Show(this, e.Location);
            return;
        }
        if (e.Button == MouseButtons.Middle) { BeginPan(e.Location, labelClick: false); return; }
        if (e.Button != MouseButtons.Left) return;
        if (hit != null && _view != null)
        {
            var (ax, ay) = _view.ToPixel(hit.Lat, hit.Lon);
            _drag = hit;
            _dragOffset = new SKPoint((float)ax - e.X, (float)ay - e.Y);
            _dragMoved = false;
            Capture = true;
        }
        else if (AddLabelMode)
        {
            BeginPan(e.Location, labelClick: true);
        }
        else if (Mode == PreviewMode.ExportFrame || _exportRect.Contains(e.X, e.Y))
        {
            BeginPan(e.Location, labelClick: false);
        }
    }

    // =============== pan / zoom of the export frame ===============

    private void BeginPan(Point p, bool labelClick)
    {
        if (ExportFrame == null || _view == null) return;
        _panFrame = ExportFrame;
        _panStart = p;
        _panning = false;
        _panIsLabelClick = labelClick;
        Capture = true;
    }

    /// <summary>Export-frame px per control px (the view and the export frame differ only in zoom).</summary>
    private double FramePxPerViewPx(MapFrame ef) => _view == null ? 1 : Math.Pow(2, ef.Zoom - _view.Zoom);

    private void UpdatePan(Point p)
    {
        if (_panFrame is not { } f) return;
        int dx = p.X - _panStart.X, dy = p.Y - _panStart.Y;
        if (!_panning)
        {
            if (Math.Abs(dx) + Math.Abs(dy) < 4) return;
            _panning = true;
            Cursor = Cursors.SizeAll;
        }
        double k = FramePxPerViewPx(f);
        // Export view: the map follows the hand (frame fixed on screen). Full track: the frame rectangle follows the hand.
        double sign = Mode == PreviewMode.ExportFrame ? -1 : 1;
        var nf = f with { CenterX = f.CenterX + sign * dx * k, CenterY = f.CenterY + sign * dy * k };
        ExportFrame = nf;
        FrameChanged?.Invoke(nf, false);
        Invalidate();
    }

    /// <returns>true when a pan (or pending add-label click) was in progress.</returns>
    private bool EndPan()
    {
        if (_panFrame == null) return false;
        bool panned = _panning, labelClick = _panIsLabelClick;
        var start = _panStart;
        _panFrame = null;
        _panning = false;
        Capture = false;
        Cursor = AddLabelMode || _extendMode != ExtendMode.None ? Cursors.Cross : Cursors.Default;
        if (panned && ExportFrame != null) FrameChanged?.Invoke(ExportFrame, true);
        else if (labelClick && ToGeo(start.X, start.Y) is { } g) LabelAddRequested?.Invoke(g.Lat, g.Lon);
        return true;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (ExportFrame is not { } ef || _view == null || _panFrame != null || _dragPt != null || _drag != null) return;
        double step = (ModifierKeys & Keys.Shift) != 0 ? 0.05 : 0.25;
        double z = Math.Clamp(ef.Zoom + e.Delta / 120.0 * step, 1, Settings.TileSource.MaxZoom);
        double dz = z - ef.Zoom;
        if (Math.Abs(dz) < 1e-9) return;
        // Anchor = the map point under the cursor (in export-frame world px); it stays put. Outside the frame: its center.
        double ax = ef.CenterX, ay = ef.CenterY;
        if (_exportRect.Contains(e.X, e.Y))
        {
            double k = FramePxPerViewPx(ef);
            ax = (_view.Left + e.X) * k;
            ay = (_view.Top + e.Y) * k;
        }
        double m = Math.Pow(2, dz);
        var nf = ef with { CenterX = ax * m + ef.CenterX - ax, CenterY = ay * m + ef.CenterY - ay, Zoom = z };
        ExportFrame = nf;
        FrameChanged?.Invoke(nf, true);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panFrame != null) { UpdatePan(e.Location); return; }
        if (_dragPt is { } dp)
        {
            if (ToGeo(e.X, e.Y) is { } g && dp.Index < dp.List.Count)
            {
                dp.List[dp.Index].Lat = g.Lat;
                dp.List[dp.Index].Lon = g.Lon;
                ExtensionChanged?.Invoke(false);
                Invalidate();
            }
            return;
        }
        if (_extendMode != ExtendMode.None)
        {
            var hp = HitExtraPoint(e.Location);
            if (!Equals(hp, _hoverPt)) { _hoverPt = hp; Invalidate(); }
            if (hp != null) { Cursor = Cursors.SizeAll; return; }
            if (HitExtraSegment(e.Location) != null && HitLabel(e.Location) == null) { Cursor = Cursors.Hand; return; }
        }
        if (_drag != null)
        {
            if (ToGeo(e.X + _dragOffset.X, e.Y + _dragOffset.Y) is { } g)
            {
                (_drag.Lat, _drag.Lon) = g;
                _dragMoved = true;
                Invalidate();
            }
            return;
        }
        var hit = HitLabel(e.Location);
        if (hit != _hover)
        {
            _hover = hit;
            Invalidate();
        }
        Cursor = hit != null ? Cursors.SizeAll : AddLabelMode || _extendMode != ExtendMode.None ? Cursors.Cross : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (EndPan()) return;
        if (_dragPt != null)
        {
            _dragPt = null;
            _frozenView = null;
            Capture = false;
            ExtensionChanged?.Invoke(true);
            Invalidate();
            return;
        }
        if (_drag == null) return;
        var moved = _drag;
        _drag = null;
        Capture = false;
        if (_dragMoved) LabelMoved?.Invoke(moved);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && HitLabel(e.Location) is { } hit) LabelEditRequested?.Invoke(hit);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != null) { _hover = null; Invalidate(); }
    }

    /// <summary>Thread-safe, coalesced invalidate.</summary>
    public void RequestRedraw()
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (Interlocked.Exchange(ref _invalidatePending, 1) == 1) return;
        try
        {
            BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _invalidatePending, 0);
                Invalidate();
            });
        }
        catch (InvalidOperationException) { /* closing */ }
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        var canvas = e.Surface.Canvas;
        int cw = e.Info.Width, ch = e.Info.Height;
        canvas.Clear(new SKColor(40, 42, 46));
        if (FullTrack == null || FullTrack.Count < 2 || ExportFrame == null || cw < 10 || ch < 10)
        {
            _view = null;
            _labelHits = [];
            using var f = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), 16);
            using var p = new SKPaint { Color = SKColors.Gray, IsAntialias = true };
            canvas.DrawText("Open a GPX file (Ctrl+O) or drag it here", cw / 2f, ch / 2f, SKTextAlign.Center, f, p);
            return;
        }

        var src = Settings.TileSource;
        var ef = ExportFrame;
        MapFrame view;
        SKRect exportRect;

        if (_frozenView != null && _frozenView.Width == cw && _frozenView.Height == ch)
        {
            // Dragging an added point: keep the view still, otherwise the refit would move the point away from the cursor.
            view = _frozenView;
            exportRect = _frozenRect;
        }
        else if (Mode == PreviewMode.ExportFrame)
        {
            double scale = Math.Min((cw - 20.0) / ef.Width, (ch - 20.0) / ef.Height);
            double zoom = ef.Zoom + Math.Log2(scale);
            // Same center, expressed at the view zoom.
            double k = Math.Pow(2, zoom - ef.Zoom);
            view = new MapFrame(ef.CenterX * k, ef.CenterY * k, zoom, cw, ch);
            float rw = (float)(ef.Width * scale), rh = (float)(ef.Height * scale);
            exportRect = new SKRect((cw - rw) / 2, (ch - rh) / 2, (cw + rw) / 2, (ch + rh) / 2);
        }
        else
        {
            var b = FullTrack.Bounds();
            double zoom = WebMercator.FitZoom(b.MinLat, b.MinLon, b.MaxLat, b.MaxLon, cw, ch, 0.06, src.MaxZoom);
            var (x0, y0) = WebMercator.ToWorld(b.MaxLat, b.MinLon, zoom);
            var (x1, y1) = WebMercator.ToWorld(b.MinLat, b.MaxLon, zoom);
            view = new MapFrame((x0 + x1) / 2, (y0 + y1) / 2, zoom, cw, ch);
            // Export frame corners projected into the view.
            var (lat0, lon0) = WebMercator.FromWorld(ef.Left, ef.Top, ef.Zoom);
            var (lat1, lon1) = WebMercator.FromWorld(ef.Left + ef.Width, ef.Top + ef.Height, ef.Zoom);
            var (ax, ay) = view.ToPixel(lat0, lon0);
            var (bx, by) = view.ToPixel(lat1, lon1);
            exportRect = new SKRect((float)ax, (float)ay, (float)bx, (float)by);
        }

        MapComposer.DrawTiles(canvas, view, src, 1.0, (z, x, y) => _cache.TryGet(src, z, x, y),
            (z, x, y) => _cache.IsNotFound(src, z, x, y));

        // Darken preview inside export rect as it would be in map.png
        if (Settings.DarkenPercent > 0)
        {
            using var dp = new SKPaint { Color = new SKColor(0, 0, 0, (byte)Math.Clamp(Settings.DarkenPercent * 255 / 100, 0, 204)) };
            canvas.DrawRect(exportRect, dp);
        }

        // Dim everything outside the export frame
        using (var dim = new SKPaint { Color = new SKColor(20, 20, 24, 170) })
        {
            var ob = new SKPathBuilder { FillType = SKPathFillType.EvenOdd };
            ob.AddRect(new SKRect(0, 0, cw, ch));
            ob.AddRect(exportRect);
            using var outside = ob.Detach();
            canvas.DrawPath(outside, dim);
        }
        using (var border = new SKPaint { Color = SKColors.White.WithAlpha(200), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true })
            canvas.DrawRect(exportRect, border);

        // Track
        float lineW = (float)Math.Max(1.5, Settings.LineWidthPx * exportRect.Width / ef.Width);
        using var ghostPath = BuildPath(view, 0, FullTrack.Count - 1);
        using var cropPath = BuildPath(view, CropStart, CropEnd);
        using (var ghost = new SKPaint
               {
                   IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1.5f, lineW * 0.6f),
                   Color = new SKColor(120, 120, 130, 170), StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round,
               })
            canvas.DrawPath(ghostPath, ghost);

        var lc = Settings.LineColor;
        using (var line = new SKPaint
               {
                   IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = lineW,
                   Color = new SKColor(lc.R, lc.G, lc.B), StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round,
               })
            canvas.DrawPath(cropPath, line);

        DrawExtensions(canvas, view, lineW);

        // Labels (same renderer as "bake into map.png"), scaled from export px to the view
        _view = view;
        _exportRect = exportRect;
        double viewScale = Math.Pow(2, view.Zoom - ef.Zoom);
        _labelHits = LabelRenderer.Draw(canvas, view, Settings.Labels, Settings.LabelSizeScale * viewScale);
        var active = _drag ?? _hover;
        if (active != null && _labelHits.FirstOrDefault(h => h.Label == active) is { Label: not null } ah)
        {
            using var sel = new SKPaint
            {
                IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = SKColors.Yellow,
                PathEffect = SKPathEffect.CreateDash([4, 3], 0),
            };
            var r = ah.Bounds;
            r.Inflate(3, 3);
            canvas.DrawRect(r, sel);
            var (ax, ay) = view.ToPixel(active.Lat, active.Lon);
            using var dot = new SKPaint { IsAntialias = true, Color = SKColors.Yellow };
            canvas.DrawCircle((float)ax, (float)ay, 3, dot);
        }

        DrawMarker(canvas, view, CropStart, new SKColor(40, 200, 60));
        DrawMarker(canvas, view, CropEnd, new SKColor(230, 40, 40));
        if (HighlightIndex >= 0 && HighlightIndex < FullTrack.Count)
            DrawMarker(canvas, view, HighlightIndex, SKColors.White, 5);
    }

    /// <summary>Added (not recorded) parts: dashed overlay; handles while editing.</summary>
    private void DrawExtensions(SKCanvas canvas, MapFrame view, float lineW)
    {
        bool editing = _extendMode != ExtendMode.None;
        using var dash = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1f, lineW * 0.35f),
            Color = SKColors.White.WithAlpha(220), PathEffect = SKPathEffect.CreateDash([6, 5], 0), StrokeCap = SKStrokeCap.Round,
        };
        using var fill = new SKPaint { IsAntialias = true };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = SKColors.White };
        foreach (var (list, anchor) in Chains())
        {
            if (list.Count == 0) continue;
            var pb = new SKPathBuilder();
            var (ax, ay) = view.ToPixel(anchor.Lat, anchor.Lon);
            pb.MoveTo((float)ax, (float)ay);
            foreach (var g in list)
            {
                var (x, y) = view.ToPixel(g.Lat, g.Lon);
                pb.LineTo((float)x, (float)y);
            }
            using var path = pb.Detach();
            canvas.DrawPath(path, dash);
            if (!editing) continue;
            bool active = (_extendMode == ExtendMode.Start) == (list == ExtraStart);
            for (int i = 0; i < list.Count; i++)
            {
                var (x, y) = view.ToPixel(list[i].Lat, list[i].Lon);
                bool hot = (_hoverPt is { } h && h.List == list && h.Index == i) || (_dragPt is { } d && d.List == list && d.Index == i);
                fill.Color = hot ? SKColors.Yellow : active ? new SKColor(255, 160, 0) : new SKColor(160, 160, 170);
                float r = hot ? 6 : 5;
                canvas.DrawCircle((float)x, (float)y, r, fill);
                canvas.DrawCircle((float)x, (float)y, r, stroke);
            }
        }
    }

    private SKPath BuildPath(MapFrame view, int from, int to)
    {
        var path = new SKPathBuilder();
        var pts = FullTrack!.Points;
        double lastX = double.NaN, lastY = double.NaN;
        for (int i = Math.Max(0, from); i <= to && i < pts.Count; i++)
        {
            var (x, y) = view.ToPixel(pts[i].Lat, pts[i].Lon);
            if (i == from) path.MoveTo((float)x, (float)y);
            else if (Math.Abs(x - lastX) + Math.Abs(y - lastY) < 0.5 && i != to) continue; // skip sub-pixel steps
            else path.LineTo((float)x, (float)y);
            lastX = x; lastY = y;
        }
        return path.Detach();
    }

    private void DrawMarker(SKCanvas canvas, MapFrame view, int index, SKColor color, float r = 7)
    {
        var p = FullTrack!.Points[Math.Clamp(index, 0, FullTrack.Count - 1)];
        var (x, y) = view.ToPixel(p.Lat, p.Lon);
        using var fill = new SKPaint { Color = color, IsAntialias = true };
        using var stroke = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        canvas.DrawCircle((float)x, (float)y, r, fill);
        canvas.DrawCircle((float)x, (float)y, r, stroke);
    }
}
