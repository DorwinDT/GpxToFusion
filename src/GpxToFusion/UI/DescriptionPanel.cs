using System.ComponentModel;

namespace GpxToFusion.UI;

/// <summary>
/// Bold title + wrapped text that grows to fit its content (docked top/bottom).
/// Replaces the PropertyGrid help pane, whose fixed height cuts longer descriptions off.
/// </summary>
public sealed class DescriptionPanel : Control
{
    private const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;
    private string _title = "";
    private Font _bold;

    public DescriptionPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        Padding = new Padding(6, 5, 6, 6);
        _bold = new Font(Font, FontStyle.Bold);
    }

    /// <summary>Text shown in bold above <see cref="Control.Text"/>; empty = no title line.</summary>
    [DefaultValue("")]
    public string Title
    {
        get => _title;
        set { _title = value ?? ""; UpdateHeight(); Invalidate(); }
    }

    /// <summary>Upper bound for the height, so a very long text does not swallow the panel above.</summary>
    [DefaultValue(400)]
    public int MaxHeight { get; set; } = 400;

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); UpdateHeight(); Invalidate(); }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateHeight(); }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _bold.Dispose();
        _bold = new Font(Font, FontStyle.Bold);
        UpdateHeight();
    }

    private int TextWidth => Math.Max(20, Width - Padding.Horizontal);

    private int TitleHeight => _title.Length == 0 ? 0
        : TextRenderer.MeasureText(_title, _bold, new Size(TextWidth, int.MaxValue), Flags).Height + 2;

    private void UpdateHeight()
    {
        int textH = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font, new Size(TextWidth, int.MaxValue), Flags).Height;
        int h = Math.Min(MaxHeight, Padding.Vertical + TitleHeight + textH);
        if (h != Height) Height = h; // SizeChanged re-enters with the same width → no loop
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using (var pen = new Pen(SystemColors.ControlDark)) e.Graphics.DrawLine(pen, 0, 0, Width, 0);
        int y = Padding.Top, w = TextWidth;
        if (_title.Length > 0)
        {
            TextRenderer.DrawText(e.Graphics, _title, _bold, new Rectangle(Padding.Left, y, w, Height), ForeColor, Flags);
            y += TitleHeight;
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(Padding.Left, y, w, Height - y), ForeColor, Flags);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _bold.Dispose();
        base.Dispose(disposing);
    }
}
