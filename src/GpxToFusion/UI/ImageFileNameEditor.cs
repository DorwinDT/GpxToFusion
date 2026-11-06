using System.Windows.Forms.Design;

namespace GpxToFusion.UI;

/// <summary>PropertyGrid "…" button that opens a file dialog filtered to images.</summary>
public sealed class ImageFileNameEditor : FileNameEditor
{
    protected override void InitializeDialog(OpenFileDialog openFileDialog)
    {
        base.InitializeDialog(openFileDialog);
        openFileDialog.Title = "Obrázok na čele čiary";
        openFileDialog.Filter = "Obrázky (*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.tga)|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.tga|Všetky súbory|*.*";
    }
}
