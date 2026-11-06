using System.Diagnostics;
using System.Reflection;

namespace GpxToFusion.UI;

/// <summary>Help → About: version (from the assembly), author, license and links.</summary>
public sealed class AboutDialog : Form
{
    public const string RepoUrl = "https://github.com/DorwinDT/GpxToFusion";
    public const string GuideUrl = "https://dorwindt.github.io/GpxToFusion/";
    public const string LicenseUrl = RepoUrl + "/blob/main/LICENSE";

    /// <summary>Version from the csproj (e.g. "1.0.0"), without the "+commit" build metadata.</summary>
    public static string AppVersion
    {
        get
        {
            var asm = Assembly.GetEntryAssembly() ?? typeof(AboutDialog).Assembly;
            string? v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                        ?? asm.GetName().Version?.ToString(3);
            if (string.IsNullOrEmpty(v)) return "?";
            int plus = v.IndexOf('+');
            return plus >= 0 ? v[..plus] : v;
        }
    }

    private static string Copyright =>
        (Assembly.GetEntryAssembly() ?? typeof(AboutDialog).Assembly).GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser available */ }
    }

    public AboutDialog()
    {
        Text = "About GPX → Fusion";
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(20, 16, 20, 12),
        };

        Label Lbl(string text, float size = 9f, FontStyle style = FontStyle.Regular, Color? color = null, int top = 0) => new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(380, 0),
            Font = new Font("Segoe UI", size, style),
            ForeColor = color ?? SystemColors.ControlText,
            Margin = new Padding(0, top, 0, 2),
        };

        LinkLabel Link(string text, string url)
        {
            var l = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
            l.LinkClicked += (_, _) => OpenUrl(url);
            return l;
        }

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 14, 0, 0) };

        layout.Controls.AddRange([
            Lbl("GPX → Fusion", 16f, FontStyle.Bold),
            Lbl($"Version {AppVersion}", 9f, FontStyle.Regular, SystemColors.GrayText),
            Lbl("Turns a GPX track into a map background and an animatable Fusion polyline for DaVinci Resolve.", top: 10),
            Lbl(Copyright, top: 10),
            Lbl("Free and open source under the MIT License."),
            Link("User guide", GuideUrl),
            Link("GitHub repository", RepoUrl),
            Link("License", LicenseUrl),
            Lbl("Map data © OpenStreetMap contributors, © Esri, © CARTO – each source has its own terms.",
                8f, FontStyle.Regular, SystemColors.GrayText, top: 10),
            ok,
        ]);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = ok;
    }
}
