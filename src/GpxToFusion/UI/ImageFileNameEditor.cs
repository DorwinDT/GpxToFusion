using System.Windows.Forms.Design;

namespace GpxToFusion.UI;

/// <summary>PropertyGrid "…" button that opens a file dialog filtered to images.</summary>
public sealed class ImageFileNameEditor : FileNameEditor
{
    protected override void InitializeDialog(OpenFileDialog openFileDialog)
    {
        base.InitializeDialog(openFileDialog);
        openFileDialog.Title = "Image at line head";
        openFileDialog.Filter = "Images (*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.tga)|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.tga|All files|*.*";
    }
}
