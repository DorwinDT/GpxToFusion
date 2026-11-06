using System.Drawing.Text;

namespace GpxToFusion.UI;

/// <summary>Menu/toolbar icons drawn from the Segoe MDL2 Assets font (part of Windows 10/11), so no image files are needed.</summary>
public static class Icons
{
    public const char OpenFile = '';
    public const char OpenProject = '';
    public const char Save = '';
    public const char Export = '';
    public const char Copy = '';
    public const char Cancel = '';
    public const char Frame = '';
    public const char Map = '';
    public const char Fit = '';
    public const char Label = '';
    public const char Flag = '';
    public const char Delete = '';
    public const char Undo = '';
    public const char Help = '';

    private static readonly Dictionary<(char, int, int), Bitmap> Cache = [];

    public static Bitmap Get(char glyph, int dpi, Color? color = null)
    {
        var c = color ?? Color.FromArgb(40, 40, 40);
        var key = (glyph, dpi, c.ToArgb());
        if (Cache.TryGetValue(key, out var bmp)) return bmp;
        int size = (int)Math.Round(16 * dpi / 96.0);
        bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        using (var font = new Font("Segoe MDL2 Assets", size * 0.75f, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(c))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(glyph.ToString(), font, brush, new RectangleF(0, 0, size, size), fmt);
        }
        Cache[key] = bmp;
        return bmp;
    }
}
