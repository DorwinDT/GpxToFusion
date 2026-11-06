using System.ComponentModel;
using System.Drawing.Drawing2D;
using GpxToFusion.Gpx;

namespace GpxToFusion.UI;

/// <summary>
/// Dual-handle range slider over track point indices with a speed / elevation profile in the background.
/// </summary>
public sealed class CropTimeline : Control
{
    private Track? _track;
    private float[] _speed = [];
    private float[] _ele = [];
    private int _start, _end;
    private int _dragHandle = -1; // 0 = start, 1 = end, 2 = whole range
    private int _dragAnchorIndex, _dragAnchorStart, _dragAnchorEnd;
    private int _focusHandle;
    private int _hoverIndex = -1;
    private const int HandleHalf = 6;
    private const int PadX = 10;

    public event EventHandler? RangeChanged;
    /// <summary>Raised while the mouse hovers; argument is the point index or -1.</summary>
    public event EventHandler<int>? HoverChanged;

    public CropTimeline()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint | ControlStyles.Selectable, true);
        TabStop = true;
        Height = 110;
        BackColor = Color.FromArgb(32, 34, 38);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Track? Track
    {
        get => _track;
        set
        {
            _track = value;
            int n = value?.Count ?? 0;
            _speed = new float[n];
            _ele = new float[n];
            if (value != null)
            {
                for (int i = 0; i < n; i++)
                {
                    _speed[i] = (float)value.SpeedKmh(i);
                    _ele[i] = (float)(value.Points[i].Ele ?? 0);
                }
            }
            _start = 0;
            _end = Math.Max(0, n - 1);
            Invalidate();
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Start => _start;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int End => _end;

    private int MaxIndex => Math.Max(0, (_track?.Count ?? 1) - 1);

    public void SetRange(int start, int end, bool raise = true)
    {
        start = Math.Clamp(start, 0, MaxIndex);
        end = Math.Clamp(end, 0, MaxIndex);
        if (end < start + 1 && MaxIndex > 0)
        {
            if (start >= MaxIndex) start = MaxIndex - 1;
            end = Math.Max(end, start + 1);
        }
        if (start == _start && end == _end) return;
        _start = start;
        _end = end;
        Invalidate();
        if (raise) RangeChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- geometry: x axis is TIME (so pauses are visible as flat segments) ----

    private Rectangle Plot => new(PadX, 6, Math.Max(1, Width - 2 * PadX), Math.Max(1, Height - 24));

    private float XOf(int index)
    {
        if (_track == null || _track.Count < 2) return Plot.Left;
        double total = _track.TotalSeconds;
        double t = total > 0 ? _track.CumTime[index] / total : (double)index / MaxIndex;
        return (float)(Plot.Left + t * Plot.Width);
    }

    private int IndexAt(int x)
    {
        if (_track == null || _track.Count < 2) return 0;
        double t = Math.Clamp((x - Plot.Left) / (double)Plot.Width, 0, 1) * _track.TotalSeconds;
        int idx = Array.BinarySearch(_track.CumTime, t);
        if (idx < 0) idx = Math.Clamp(~idx, 0, MaxIndex);
        return idx;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var plot = Plot;
        using var font = new Font("Segoe UI", 8f);

        if (_track == null || _track.Count < 2)
        {
            TextRenderer.DrawText(g, "Open a GPX file (or drag it into the window)", Font, ClientRectangle, Color.Gray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        // Elevation (filled, subtle)
        DrawSeries(g, plot, _ele, Color.FromArgb(60, 120, 160, 220), fill: true);
        // Speed (line)
        DrawSeries(g, plot, _speed, Color.FromArgb(220, 255, 200, 80), fill: false, clipMax: 12f);

        // Dim outside of crop
        float xs = XOf(_start), xe = XOf(_end);
        using (var dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
        {
            g.FillRectangle(dim, plot.Left, plot.Top, xs - plot.Left, plot.Height);
            g.FillRectangle(dim, xe, plot.Top, plot.Right - xe, plot.Height);
        }
        using (var sel = new Pen(Color.FromArgb(255, 90, 31), 1))
            g.DrawRectangle(sel, xs, plot.Top, Math.Max(1, xe - xs), plot.Height - 1);

        DrawHandle(g, xs, plot, Color.LimeGreen, _focusHandle == 0 && Focused);
        DrawHandle(g, xe, plot, Color.Red, _focusHandle == 1 && Focused);

        if (_hoverIndex >= 0)
        {
            float xh = XOf(_hoverIndex);
            using var hp = new Pen(Color.FromArgb(120, 255, 255, 255)) { DashStyle = DashStyle.Dot };
            g.DrawLine(hp, xh, plot.Top, xh, plot.Bottom);
        }

        // Time axis labels
        double total = _track.TotalSeconds;
        int steps = Math.Max(1, plot.Width / 90);
        for (int i = 0; i <= steps; i++)
        {
            float x = plot.Left + plot.Width * i / (float)steps;
            var ts = TimeSpan.FromSeconds(total * i / steps);
            string s = ts.ToString(@"h\:mm");
            var size = TextRenderer.MeasureText(s, font);
            TextRenderer.DrawText(g, s, font, new Point((int)(x - size.Width / 2f), plot.Bottom + 2), Color.Silver);
        }
        TextRenderer.DrawText(g, "speed", font, new Point(plot.Left + 2, plot.Top), Color.FromArgb(255, 200, 80));
        TextRenderer.DrawText(g, "elevation", font, new Point(plot.Left + 60, plot.Top), Color.FromArgb(120, 160, 220));
    }

    private void DrawSeries(Graphics g, Rectangle plot, float[] data, Color color, bool fill, float? clipMax = null)
    {
        if (data.Length < 2) return;
        float min = data.Min(), max = data.Max();
        if (clipMax.HasValue) { min = 0; max = Math.Min(max, clipMax.Value); }
        if (max - min < 1e-3f) max = min + 1;

        int cols = plot.Width;
        var pts = new PointF[cols];
        // Bucket by x: take the mean of the samples falling into each pixel column.
        var sums = new double[cols];
        var counts = new int[cols];
        for (int i = 0; i < data.Length; i++)
        {
            int c = Math.Clamp((int)(XOf(i) - plot.Left), 0, cols - 1);
            sums[c] += data[i];
            counts[c]++;
        }
        float lastV = data[0];
        for (int c = 0; c < cols; c++)
        {
            float v = counts[c] > 0 ? (float)(sums[c] / counts[c]) : lastV;
            lastV = v;
            float y = plot.Bottom - (Math.Clamp(v, min, max) - min) / (max - min) * (plot.Height - 4);
            pts[c] = new PointF(plot.Left + c, y);
        }

        if (fill)
        {
            var poly = new PointF[cols + 2];
            pts.CopyTo(poly, 0);
            poly[cols] = new PointF(plot.Right, plot.Bottom);
            poly[cols + 1] = new PointF(plot.Left, plot.Bottom);
            using var b = new SolidBrush(color);
            g.FillPolygon(b, poly);
        }
        else
        {
            using var p = new Pen(color, 1.2f);
            g.DrawLines(p, pts);
        }
    }

    private static void DrawHandle(Graphics g, float x, Rectangle plot, Color color, bool focused)
    {
        using var pen = new Pen(color, 2);
        g.DrawLine(pen, x, plot.Top, x, plot.Bottom);
        var r = new RectangleF(x - HandleHalf, plot.Top + plot.Height / 2f - 12, HandleHalf * 2, 24);
        using var b = new SolidBrush(color);
        g.FillRectangle(b, r);
        if (focused)
        {
            using var fp = new Pen(Color.White, 1) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(fp, r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_track == null || e.Button != MouseButtons.Left) return;
        float xs = XOf(_start), xe = XOf(_end);
        float ds = Math.Abs(e.X - xs), de = Math.Abs(e.X - xe);
        if (Math.Min(ds, de) <= HandleHalf + 4)
            _dragHandle = ds <= de ? 0 : 1;
        else if (e.X > xs && e.X < xe)
            _dragHandle = 2;
        else
            _dragHandle = e.X < xs ? 0 : 1;

        if (_dragHandle is 0 or 1) _focusHandle = _dragHandle;
        _dragAnchorIndex = IndexAt(e.X);
        _dragAnchorStart = _start;
        _dragAnchorEnd = _end;
        if (_dragHandle is 0 or 1) OnMouseMove(e);
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_track == null) return;
        int idx = IndexAt(e.X);
        if (_hoverIndex != idx)
        {
            _hoverIndex = idx;
            HoverChanged?.Invoke(this, idx);
            Invalidate();
        }
        switch (_dragHandle)
        {
            case 0: SetRange(Math.Min(idx, _end - 1), _end); break;
            case 1: SetRange(_start, Math.Max(idx, _start + 1)); break;
            case 2:
                int delta = idx - _dragAnchorIndex;
                int len = _dragAnchorEnd - _dragAnchorStart;
                int s = Math.Clamp(_dragAnchorStart + delta, 0, MaxIndex - len);
                SetRange(s, s + len);
                break;
        }
        Cursor = _dragHandle == 2 ? Cursors.SizeAll : Cursors.SizeWE;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragHandle = -1;
        Capture = false;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        HoverChanged?.Invoke(this, -1);
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int step = e.Shift ? 60 : e.Control ? 600 : 1;
        int d = e.KeyCode == Keys.Left ? -step : e.KeyCode == Keys.Right ? step : 0;
        if (e.KeyCode == Keys.Tab) return;
        if (e.KeyCode is Keys.Home) { _focusHandle = 0; Invalidate(); }
        if (e.KeyCode is Keys.End) { _focusHandle = 1; Invalidate(); }
        if (d == 0) return;
        if (_focusHandle == 0) SetRange(Math.Min(_start + d, _end - 1), _end);
        else SetRange(_start, Math.Max(_end + d, _start + 1));
        e.Handled = true;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}
