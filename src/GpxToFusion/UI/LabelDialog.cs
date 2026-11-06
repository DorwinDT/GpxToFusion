using GpxToFusion.Export;

namespace GpxToFusion.UI;

/// <summary>
/// Edits a map label in place (live preview through <c>onChanged</c>). Cancel restores the original values.
/// DialogResult.Abort = the user pressed "Zmazať".
/// </summary>
public sealed class LabelDialog : Form
{
    private readonly MapLabel _label;
    private readonly MapLabel _original;

    public LabelDialog(MapLabel label, bool isNew, Action onChanged)
    {
        _label = label;
        _original = label.Clone();

        Text = isNew ? "Nový popis" : "Upraviť popis";
        Font = new Font("Segoe UI", 9f);
        Width = 420;
        Height = 520;
        MinimumSize = new Size(340, 380);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;

        var grid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            SelectedObject = label,
            ToolbarVisible = false,
            PropertySort = PropertySort.Categorized,
            HelpVisible = true,
        };
        grid.PropertyValueChanged += (_, _) => onChanged();

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Zrušiť", DialogResult = DialogResult.Cancel, AutoSize = true };
        var delete = new Button { Text = "Zmazať", DialogResult = DialogResult.Abort, AutoSize = true, Visible = !isNew };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 40,
            Padding = new Padding(6),
        };
        buttons.Controls.AddRange([cancel, ok, delete]);

        Controls.Add(grid);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;

        Shown += (_, _) =>
        {
            // Start on the Text row so the user can type right away.
            var textItem = FindItem(grid.SelectedGridItem, nameof(MapLabel.Text));
            if (textItem != null) grid.SelectedGridItem = textItem;
            grid.Focus();
        };
        FormClosed += (_, _) =>
        {
            if (DialogResult == DialogResult.Cancel)
            {
                _label.CopyFrom(_original);
                onChanged();
            }
        };
    }

    private static GridItem? FindItem(GridItem? any, string propertyName)
    {
        var root = any;
        while (root?.Parent != null) root = root.Parent;
        return root == null ? null : Find(root, propertyName);

        static GridItem? Find(GridItem item, string name)
        {
            if (item.PropertyDescriptor?.Name == name) return item;
            foreach (GridItem child in item.GridItems)
                if (Find(child, name) is { } found) return found;
            return null;
        }
    }
}
