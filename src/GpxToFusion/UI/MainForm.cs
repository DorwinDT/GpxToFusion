using System.Diagnostics;
using GpxToFusion.Export;
using GpxToFusion.Geo;
using GpxToFusion.Gpx;
using GpxToFusion.Map;

namespace GpxToFusion.UI;

public sealed class MainForm : Form
{
    private readonly TileCache _cache = new();
    private readonly MapPreview _preview;
    private readonly CropTimeline _timeline;
    private readonly PropertyGrid _grid;
    private readonly DescriptionPanel _info, _help;
    private readonly NumericUpDown _numStart, _numEnd;
    private readonly Label _lblStart, _lblEnd, _lblResult;
    private readonly ToolStripButton _btnExport, _btnAddLabel, _btnAutoFrame, _btnViewFrame, _btnViewFull;
    private readonly ToolStripDropDownButton _btnExtend, _btnCancel;
    private readonly ToolStripMenuItem _btnExtendStart, _btnExtendEnd;
    private readonly ToolStripMenuItem _miExport, _miAddLabel, _miExtendStart, _miExtendEnd, _miViewFrame, _miViewFull, _miAutoFrame;
    /// <summary>Menu items and buttons that are enabled only with a loaded track.</summary>
    private readonly List<ToolStripItem> _needTrack = [];
    private readonly ToolStripStatusLabel _status;
    private readonly ToolStripProgressBar _progress;

    private ExportSettings _settings = new();
    private Track? _track;
    /// <summary>Points as recorded in the GPX file; <see cref="_track"/> = these plus the user-added ones.</summary>
    private List<TrackPoint>? _recorded;
    /// <summary>How many added points precede the recorded ones in the current <see cref="_track"/>.</summary>
    private int _builtExtraStart;
    private string? _gpxPath;
    private string? _lastExportFolder;
    private CancellationTokenSource? _exportCts;
    private bool _suppressNumeric;

    public MainForm(string? startupFile)
    {
        Text = "GPX → Fusion";
        Width = 1400;
        Height = 900;
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        KeyPreview = true;
        Font = new Font("Segoe UI", 9f);

        // ---------- menu + toolbar ----------
        // Menu = every command; toolbar = the frequent ones, grouped. Items that need a loaded track go to _needTrack.
        int dpi = DeviceDpi;
        Image Ico(char g) => Icons.Get(g, dpi);
        ToolStripMenuItem Mi(string text, char? icon, EventHandler onClick, Keys keys = Keys.None, bool needTrack = false)
        {
            var mi = new ToolStripMenuItem(text, icon is { } g ? Ico(g) : null, onClick) { ShortcutKeys = keys };
            if (needTrack) _needTrack.Add(mi);
            return mi;
        }
        ToolStripButton Tb(string text, char icon, string tip, EventHandler onClick, bool needTrack = false)
        {
            var b = new ToolStripButton(text, Ico(icon), onClick)
            {
                ToolTipText = tip,
                DisplayStyle = text.Length == 0 ? ToolStripItemDisplayStyle.Image : ToolStripItemDisplayStyle.ImageAndText,
            };
            if (needTrack) _needTrack.Add(b);
            return b;
        }

        // File
        var miFile = new ToolStripMenuItem("&File");
        miFile.DropDownItems.AddRange([
            Mi("&Open GPX…", Icons.OpenFile, (_, _) => OpenGpxDialog(), Keys.Control | Keys.O),
            Mi("&Load project…", Icons.OpenProject, (_, _) => LoadProjectDialog(), Keys.Control | Keys.Shift | Keys.O),
            Mi("&Save project…", Icons.Save, (_, _) => SaveProjectDialog(), Keys.Control | Keys.S, needTrack: true),
            new ToolStripSeparator(),
            Mi("Save &GPX with added points…", null, (_, _) => SaveGpxDialog(), needTrack: true),
            new ToolStripSeparator(),
            Mi("E&xit", null, (_, _) => Close(), Keys.Alt | Keys.F4),
        ]);

        // Export
        _miExport = Mi("&Export…", Icons.Export, async (_, _) => await ExportAsync(), Keys.Control | Keys.E, needTrack: true);
        var miOpenFolder = Mi("Open export &folder", null, (_, _) => OpenExportFolder());
        var miExportMenu = new ToolStripMenuItem("&Export");
        miExportMenu.DropDownItems.AddRange([
            _miExport,
            Mi("&Copy .setting to clipboard", Icons.Copy, (_, _) => CopySetting(), Keys.Control | Keys.Shift | Keys.C, needTrack: true),
            new ToolStripSeparator(),
            miOpenFolder,
        ]);
        miExportMenu.DropDownOpening += (_, _) => miOpenFolder.Enabled = _lastExportFolder != null && Directory.Exists(_lastExportFolder);

        // View
        _miViewFrame = Mi("Export &frame", Icons.Frame, (_, _) => SetPreviewMode(PreviewMode.ExportFrame), Keys.F5);
        _miViewFull = Mi("F&ull track", Icons.Map, (_, _) => SetPreviewMode(PreviewMode.FullTrack), Keys.F6);
        _miAutoFrame = Mi("&Automatic frame", Icons.Fit, (_, _) => ResetFrame(), Keys.Control | Keys.D0);
        var miView = new ToolStripMenuItem("&View");
        miView.DropDownItems.AddRange([_miViewFrame, _miViewFull, new ToolStripSeparator(), _miAutoFrame]);

        // Edit
        _miAddLabel = Mi("Add &label", Icons.Label, (_, _) => SetAddLabelMode(!_preview!.AddLabelMode), Keys.Control | Keys.L, needTrack: true);
        _miExtendStart = Mi("Extend track &start", Icons.Flag,
            (_, _) => SetExtendMode(_preview!.ExtendMode == ExtendMode.Start ? ExtendMode.None : ExtendMode.Start), needTrack: true);
        _miExtendEnd = Mi("Extend track &end", Icons.Flag,
            (_, _) => SetExtendMode(_preview!.ExtendMode == ExtendMode.End ? ExtendMode.None : ExtendMode.End), needTrack: true);
        var miClearStart = Mi("Delete added start", Icons.Delete, (_, _) => { _settings.ExtraStart.Clear(); RebuildTrack(); });
        var miClearEnd = Mi("Delete added end", Icons.Delete, (_, _) => { _settings.ExtraEnd.Clear(); RebuildTrack(); });
        var miEdit = new ToolStripMenuItem("&Edit");
        miEdit.DropDownItems.AddRange([_miAddLabel, new ToolStripSeparator(), _miExtendStart, _miExtendEnd, miClearStart, miClearEnd,
            new ToolStripSeparator(), Mi("Reset &crop", Icons.Undo, (_, _) => ResetCrop(), needTrack: true)]);
        miEdit.DropDownOpening += (_, _) =>
        {
            miClearStart.Enabled = _settings.ExtraStart.Count > 0;
            miClearEnd.Enabled = _settings.ExtraEnd.Count > 0;
        };

        // Help
        var miHelp = new ToolStripMenuItem("&Help");
        miHelp.DropDownItems.Add(Mi("&Controls…", Icons.Help, (_, _) => ShowHelp(), Keys.F1));

        var menu = new MenuStrip { Padding = new Padding(4, 2, 0, 2) };
        menu.Items.AddRange([miFile, miExportMenu, miView, miEdit, miHelp]);
        MainMenuStrip = menu;

        // Toolbar: File | Export | Preview | Edit
        _btnExport = Tb("Export", Icons.Export, "Ctrl+E – map.png, GpxTrack.setting, track.png and CSV into a folder",
            async (_, _) => await ExportAsync(), needTrack: true);
        _btnExport.Font = new Font(Font, FontStyle.Bold);
        _btnViewFrame = Tb("Frame", Icons.Frame, "F5 – preview exactly as map.png", (_, _) => SetPreviewMode(PreviewMode.ExportFrame));
        _btnViewFull = Tb("Full track", Icons.Map, "F6 – full track with the frame outline", (_, _) => SetPreviewMode(PreviewMode.FullTrack));
        _btnAutoFrame = Tb("Auto frame", Icons.Fit,
            "Ctrl+0 – restores the automatic frame fitted to the cropped track.\nPan the frame by dragging the map, zoom with the wheel (Shift = fine).",
            (_, _) => ResetFrame());
        _btnAddLabel = Tb("Label", Icons.Label, "Ctrl+L – then click in the map. Label: double-click = edit, drag = move, right button = menu.",
            (_, _) => SetAddLabelMode(!_preview!.AddLabelMode), needTrack: true);
        _btnExtend = new ToolStripDropDownButton("Extend track", Ico(Icons.Flag))
        {
            ToolTipText = "Adds points before the start / after the end of the GPX (e.g. when you started the tracker late).",
        };
        _needTrack.Add(_btnExtend);
        _btnExtendStart = new ToolStripMenuItem("Start (click in the map)", null,
            (_, _) => SetExtendMode(_preview!.ExtendMode == ExtendMode.Start ? ExtendMode.None : ExtendMode.Start));
        _btnExtendEnd = new ToolStripMenuItem("End (click in the map)", null,
            (_, _) => SetExtendMode(_preview!.ExtendMode == ExtendMode.End ? ExtendMode.None : ExtendMode.End));
        _btnExtend.DropDownItems.AddRange([_btnExtendStart, _btnExtendEnd]);

        var iconSize = Icons.Get(Icons.Save, dpi).Size;
        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 3, 6, 3), ImageScalingSize = iconSize };
        tools.Items.AddRange([
            Tb("", Icons.OpenFile, "Open GPX… (Ctrl+O)", (_, _) => OpenGpxDialog()),
            Tb("", Icons.Save, "Save project… (Ctrl+S)", (_, _) => SaveProjectDialog(), needTrack: true),
            new ToolStripSeparator(),
            _btnExport,
            Tb("Copy .setting", Icons.Copy, "Ctrl+Shift+C – then in Resolve → Fusion → Node editor → Ctrl+V",
                (_, _) => CopySetting(), needTrack: true),
            new ToolStripSeparator(),
            new ToolStripLabel("Preview:") { ForeColor = SystemColors.GrayText },
            _btnViewFrame, _btnViewFull, _btnAutoFrame,
            new ToolStripSeparator(),
            new ToolStripLabel("Edit:") { ForeColor = SystemColors.GrayText },
            _btnAddLabel, _btnExtend,
        ]);
        menu.ImageScalingSize = iconSize;

        // ---------- status ----------
        var statusStrip = new StatusStrip { ImageScalingSize = iconSize };
        _status = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _progress = new ToolStripProgressBar { Visible = false, Width = 200, Style = ProgressBarStyle.Marquee };
        _btnCancel = new ToolStripDropDownButton("Cancel export", Ico(Icons.Cancel)) { Visible = false, ShowDropDownArrow = false };
        _btnCancel.Click += (_, _) => _exportCts?.Cancel();
        statusStrip.Items.AddRange([_status, _progress, _btnCancel]);

        // ---------- preview + settings ----------
        _preview = new MapPreview(_cache) { Dock = DockStyle.Fill, Settings = _settings };
        _preview.LabelAddRequested += AddLabel;
        _preview.LabelEditRequested += label => EditLabel(label, isNew: false);
        _preview.LabelDeleteRequested += DeleteLabel;
        _preview.LabelMoved += _ => _grid!.Refresh();
        _preview.ExtensionChanged += final => RebuildTrack(final);
        _preview.FrameChanged += OnFrameChanged;

        _grid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            SelectedObject = _settings,
            ToolbarVisible = false,
            PropertySort = PropertySort.Categorized,
            HelpVisible = false, // own _help panel below: the built-in one has a fixed height and cuts descriptions off
        };
        _grid.PropertyValueChanged += (_, e) =>
        {
            string? name = e.ChangedItem?.PropertyDescriptor?.Name;
            if (name == nameof(ExportSettings.ExtensionSpeedKmh)) { RebuildTrack(); return; }
            // Padding and zoom-out only make sense for the automatic frame: changing them switches back to it.
            if (name is nameof(ExportSettings.PaddingPercent) or nameof(ExportSettings.ExtraZoomOut) && _settings.ManualFrame != null)
            {
                _settings.ManualFrame = null;
                _grid!.Refresh();
            }
            Recompute();
        };

        _help = new DescriptionPanel { Dock = DockStyle.Bottom, BackColor = SystemColors.Control, ForeColor = SystemColors.ControlText };
        _help.MinimumSize = new Size(0, Font.Height * 3 + 14); // title + 2 lines, so short texts don't make the grid jump
        _grid.SelectedGridItemChanged += (_, e) =>
        {
            _help.Title = e.NewSelection?.Label ?? "";
            _help.Text = e.NewSelection?.PropertyDescriptor?.Description ?? "";
        };

        _info = new DescriptionPanel { Dock = DockStyle.Bottom, BackColor = SystemColors.Info, ForeColor = SystemColors.InfoText };

        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(_grid);
        right.Controls.Add(_help);
        right.Controls.Add(_info);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel2,
            Orientation = Orientation.Vertical,
        };
        split.Panel1.Controls.Add(_preview);
        split.Panel2.Controls.Add(right);

        // ---------- crop area ----------
        _timeline = new CropTimeline { Dock = DockStyle.Fill };
        _timeline.RangeChanged += (_, _) => OnCropChanged(fromNumeric: false);
        _timeline.HoverChanged += (_, idx) => { _preview.HighlightIndex = idx; _preview.Invalidate(); };

        _numStart = new NumericUpDown { Width = 80, Minimum = 0, Maximum = 0 };
        _numEnd = new NumericUpDown { Width = 80, Minimum = 0, Maximum = 0 };
        _numStart.ValueChanged += (_, _) => { if (!_suppressNumeric) _timeline.SetRange((int)_numStart.Value, _timeline.End); };
        _numEnd.ValueChanged += (_, _) => { if (!_suppressNumeric) _timeline.SetRange(_timeline.Start, (int)_numEnd.Value); };
        _lblStart = new Label { AutoSize = true, Margin = new Padding(4, 7, 16, 0) };
        _lblEnd = new Label { AutoSize = true, Margin = new Padding(4, 7, 16, 0) };
        _lblResult = new Label { AutoSize = true, Margin = new Padding(4, 7, 16, 0), Font = new Font(Font, FontStyle.Bold) };
        var btnReset = new Button { Text = "Reset crop", AutoSize = true };
        btnReset.Click += (_, _) => ResetCrop();

        var cropRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(6, 4, 6, 0), WrapContents = false };
        cropRow.Controls.AddRange([
            new Label { Text = "Start:", AutoSize = true, Margin = new Padding(0, 7, 2, 0), ForeColor = Color.Green }, _numStart, _lblStart,
            new Label { Text = "End:", AutoSize = true, Margin = new Padding(0, 7, 2, 0), ForeColor = Color.Red }, _numEnd, _lblEnd,
            _lblResult, btnReset,
        ]);
        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 20,
            Padding = new Padding(8, 3, 0, 0),
            Text = "Crop: drag the green/red handle, the middle moves the whole selection. Arrows ←/→ (Shift = 1 min, Ctrl = 10 min), Home/End switches the handle.",
            ForeColor = SystemColors.GrayText,
        };
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 170 };
        bottom.Controls.Add(_timeline);
        bottom.Controls.Add(hint);
        bottom.Controls.Add(cropRow);

        Controls.Add(split);
        Controls.Add(bottom);
        Controls.Add(tools);
        Controls.Add(menu);
        Controls.Add(statusStrip);

        Load += (_, _) =>
        {
            split.SplitterDistance = Math.Max(300, split.Width - 380);
            SetPreviewMode(PreviewMode.ExportFrame);
            if (startupFile != null) OpenAny(startupFile);
            else Recompute();
        };

        DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) OpenAny(files[0]);
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && _preview.AddLabelMode) { SetAddLabelMode(false); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape && _preview.ExtendMode != ExtendMode.None) { SetExtendMode(ExtendMode.None); e.Handled = true; }
        };
        FormClosing += (_, _) => _exportCts?.Cancel();
    }

    // =============== loading ===============

    private void OpenAny(string path)
    {
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) LoadProject(path);
        else OpenGpx(path);
    }

    private void OpenGpxDialog()
    {
        using var dlg = new OpenFileDialog { Filter = "GPX (*.gpx)|*.gpx|All files|*.*", Title = "Open GPX" };
        if (_gpxPath != null) dlg.InitialDirectory = Path.GetDirectoryName(_gpxPath);
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenGpx(dlg.FileName);
    }

    /// <param name="fromProject">true = keep the added points from the (just loaded) project settings.</param>
    private bool OpenGpx(string path, int? cropStart = null, int? cropEnd = null, bool fromProject = false)
    {
        try
        {
            var points = GpxParser.Parse(path);
            if (points.Count < 2)
            {
                MessageBox.Show(this, "The GPX contains no track (at least 2 points).", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (!fromProject)
            {
                _settings.ExtraStart.Clear();
                _settings.ExtraEnd.Clear();
                _settings.ManualFrame = null;
            }
            _recorded = points;
            _gpxPath = path;
            _lastExportFolder = null;
            Text = $"GPX → Fusion – {Path.GetFileName(path)}";
            SetTrack(cropStart ?? 0,cropEnd ?? int.MaxValue);
            _grid.Refresh();
            _status.Text = $"Loaded: {_recorded.Count:N0} points, {FormatDuration(_track!.TotalSeconds)}, {_track.TotalDistance / 1000:0.00} km";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not load the GPX:\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void LoadProjectDialog()
    {
        using var dlg = new OpenFileDialog { Filter = "GPX→Fusion project (*.g2f.json)|*.g2f.json|JSON|*.json", Title = "Load project" };
        if (dlg.ShowDialog(this) == DialogResult.OK) LoadProject(dlg.FileName);
    }

    private void LoadProject(string path)
    {
        try
        {
            var p = ProjectFile.Load(path);
            _settings = p.Settings;
            _grid.SelectedObject = _settings;
            _preview.Settings = _settings;
            if (p.GpxPath == null || !File.Exists(p.GpxPath))
            {
                MessageBox.Show(this, $"The project GPX file was not found:\n{p.GpxPath}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Recompute();
                return;
            }
            if (OpenGpx(p.GpxPath, p.CropStart, p.CropEnd, fromProject: true))
                _lastExportFolder = p.LastExportFolder;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not load the project:\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveProjectDialog()
    {
        if (_gpxPath == null) return;
        using var dlg = new SaveFileDialog
        {
            Filter = "GPX→Fusion project (*.g2f.json)|*.g2f.json",
            FileName = Path.GetFileNameWithoutExtension(_gpxPath) + ".g2f.json",
            InitialDirectory = Path.GetDirectoryName(_gpxPath),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        new ProjectFile
        {
            GpxPath = _gpxPath,
            CropStart = _timeline.Start,
            CropEnd = _timeline.End,
            LastExportFolder = _lastExportFolder,
            Settings = _settings,
        }.Save(dlg.FileName);
        _status.Text = $"Project saved: {dlg.FileName}";
    }

    // =============== added points ===============

    /// <summary>Builds <see cref="_track"/> from the recorded points + added points and applies the crop.</summary>
    private void SetTrack(int cropStart, int cropEnd)
    {
        var points = TrackExtension.Extend(_recorded!, _settings.ExtraStart, _settings.ExtraEnd, _settings.ExtensionSpeedKmh);
        _track = new Track(points);
        _builtExtraStart = _settings.ExtraStart.Count;
        _timeline.Track = _track;
        _preview.FullTrack = _track;
        _preview.ExtraStart = _settings.ExtraStart;
        _preview.ExtraEnd = _settings.ExtraEnd;
        _preview.StartAnchor = _recorded![0];
        _preview.EndAnchor = _recorded[^1];
        _suppressNumeric = true;
        _numStart.Maximum = _numEnd.Maximum = _track.Count - 1;
        _suppressNumeric = false;
        _timeline.SetRange(cropStart, Math.Min(cropEnd, _track.Count - 1), raise: false);
        OnCropChanged(fromNumeric: false);
    }

    /// <summary>
    /// Added points changed: rebuild the track and keep the crop on the same recorded points.
    /// A crop at the very start/end stays there, so new points are included right away.
    /// </summary>
    private void RebuildTrack(bool final = true)
    {
        if (_recorded == null || _track == null) return;
        int oldLast = _track.Count - 1;
        int shift = _settings.ExtraStart.Count - _builtExtraStart;
        int start = _timeline.Start == 0 ? 0 : Math.Max(0, _timeline.Start + shift);
        int end = _timeline.End == oldLast ? int.MaxValue : Math.Max(0, _timeline.End + shift);
        SetTrack(start, end);
        if (final)
        {
            _grid.Refresh();
            int n = _settings.ExtraStart.Count + _settings.ExtraEnd.Count;
            if (_preview.ExtendMode != ExtendMode.None)
                _status.Text = $"Added points: start {_settings.ExtraStart.Count}, end {_settings.ExtraEnd.Count}. " + ExtendHint;
            else if (n == 0)
                _status.Text = "Added points deleted.";
        }
    }

    private const string ExtendHint = "Click = new point, drag = move, click on a segment = insert point, right button = delete, Esc = done.";

    private void SetExtendMode(ExtendMode mode)
    {
        if (mode != ExtendMode.None && _preview.AddLabelMode) SetAddLabelMode(false);
        _preview.ExtendMode = mode;
        _miExtendStart.Checked = _btnExtendStart.Checked = mode == ExtendMode.Start;
        _miExtendEnd.Checked = _btnExtendEnd.Checked = mode == ExtendMode.End;
        _btnExtend.Text = mode switch
        {
            ExtendMode.Start => "Extend track: START",
            ExtendMode.End => "Extend track: END",
            _ => "Extend track",
        };
        _btnExtend.Font = new Font(Font, mode == ExtendMode.None ? FontStyle.Regular : FontStyle.Bold);
        if (mode == ExtendMode.None) return;
        string where = mode == ExtendMode.Start ? "before the start (outward from the green point)" : "after the end (onward from the red point)";
        string crop = mode == ExtendMode.Start && _timeline.Start > 0 || mode == ExtendMode.End && _track != null && _timeline.End < _track.Count - 1
            ? " ⚠ The track is cropped at this end – added points will not be in the export until you reset the crop."
            : "";
        _status.Text = $"Adding points {where}. {ExtendHint}{crop}";
    }

    private void SaveGpxDialog()
    {
        if (_track == null || _gpxPath == null) return;
        using var dlg = new SaveFileDialog
        {
            Filter = "GPX (*.gpx)|*.gpx",
            FileName = Path.GetFileNameWithoutExtension(_gpxPath) + "_extended.gpx",
            InitialDirectory = Path.GetDirectoryName(_gpxPath),
            Title = "Save GPX with added points",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(_gpxPath), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "The original GPX is not overwritten (the project refers to it). Choose a different name.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            GpxWriter.Write(dlg.FileName, _track.Points, Path.GetFileNameWithoutExtension(dlg.FileName));
            _status.Text = $"GPX saved ({_track.Count:N0} points, full track without crop): {dlg.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not save the GPX:\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // =============== crop / recompute ===============

    private void OnCropChanged(bool fromNumeric)
    {
        if (_track == null) return;
        _suppressNumeric = true;
        _numStart.Value = _timeline.Start;
        _numEnd.Value = _timeline.End;
        _suppressNumeric = false;

        _lblStart.Text = DescribePoint(_timeline.Start);
        _lblEnd.Text = DescribePoint(_timeline.End);
        double secs = _track.CumTime[_timeline.End] - _track.CumTime[_timeline.Start];
        double km = (_track.CumDist[_timeline.End] - _track.CumDist[_timeline.Start]) / 1000;
        _lblResult.Text = $"Result: {FormatDuration(secs)} · {km:0.00} km";
        Recompute();
    }

    private string DescribePoint(int i)
    {
        var t = _track!.Points[i].Time;
        string local = t.HasValue ? $" ({t.Value.ToLocalTime():HH:mm:ss})" : "";
        return $"{FormatDuration(_track.CumTime[i])}{local} · {_track.CumDist[i] / 1000:0.00} km";
    }

    private static string FormatDuration(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss");

    private Track? Cropped => _track?.Sub(_timeline.Start, _timeline.End);

    private void Recompute()
    {
        bool has = _track != null;
        foreach (var it in _needTrack) it.Enabled = has;
        _btnExport.Enabled = _miExport.Enabled = has && _exportCts == null;
        _btnAutoFrame.Enabled = _miAutoFrame.Enabled = has && _settings.ManualFrame != null;
        _preview.Settings = _settings;
        if (_track == null)
        {
            _info.Text = "No track.";
            _preview.ExportFrame = null;
            _preview.Invalidate();
            return;
        }

        var cropped = Cropped!;
        var frame = MapFrame.Fit(cropped, _settings);
        var geo = TrackGeometry.Build(cropped, frame, _settings.MinPointDistanceM, _settings.SimplifyTolerancePx);
        int tz = frame.TileZoom(_settings.TileSource.MaxZoom);
        int tiles = frame.Tiles(tz).Count;

        _preview.FullTrack = _track;
        _preview.CropStart = _timeline.Start;
        _preview.CropEnd = _timeline.End;
        _preview.ExportFrame = frame;
        _preview.Invalidate();

        double metersPerPx = 156543.03392 * Math.Cos(cropped.Points[0].Lat * Math.PI / 180) / Math.Pow(2, frame.Zoom);
        string warn = tiles > 2000 ? $"\n⚠ Many tiles ({tiles}) – downloading will take long, consider a smaller resolution." : "";
        _info.Text =
            $"map.png: {_settings.Width} × {_settings.Height} px, zoom {frame.Zoom:0.00} (tiles z{tz}, {tiles} pcs)\n" +
            $"Scale: {metersPerPx:0.00} m/px\n" +
            $"Points: {cropped.Count:N0} → after filter {geo.Full.Count:N0} → polyline {geo.Simplified.Count:N0}\n" +
            $"Line width: {_settings.LineWidthPx:0.#} px" + warn;
    }

    // =============== view / modes ===============

    private void SetPreviewMode(PreviewMode mode)
    {
        _preview.Mode = mode;
        _btnViewFrame.Checked = _miViewFrame.Checked = mode == PreviewMode.ExportFrame;
        _btnViewFull.Checked = _miViewFull.Checked = mode == PreviewMode.FullTrack;
        _preview.Invalidate();
    }

    private void SetAddLabelMode(bool on)
    {
        if (on && _track == null) return;
        _preview.AddLabelMode = on;
        _btnAddLabel.Checked = _miAddLabel.Checked = on;
        if (!on) return;
        SetExtendMode(ExtendMode.None);
        _status.Text = "Click in the map where you want the label (Esc = cancel).";
    }

    private void ResetFrame()
    {
        if (_settings.ManualFrame == null) return;
        _settings.ManualFrame = null;
        Recompute();
        _grid.Refresh();
        _status.Text = "The frame is automatic again (fitted to the cropped track).";
    }

    private void ResetCrop()
    {
        if (_track != null) _timeline.SetRange(0, _track.Count - 1);
    }

    private void OpenExportFolder()
    {
        if (_lastExportFolder != null && Directory.Exists(_lastExportFolder))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_lastExportFolder}\"") { UseShellExecute = true });
    }

    private void ShowHelp() => MessageBox.Show(this,
        "Map (preview)\n" +
        "  • drag the map / middle button – pan the frame\n" +
        "  • wheel – zoom the frame (Shift = fine), Ctrl+0 – automatic frame\n" +
        "  • F5 / F6 – preview the frame / the full track\n\n" +
        "Labels (Ctrl+L)\n" +
        "  • click in the map – new label, double-click – edit, drag – move, right button – menu\n\n" +
        "Extending the track (Edit → Extend track start/end)\n" +
        "  • click – new point, drag – move, click on a segment – insert point, right button – delete, Esc – done\n\n" +
        "Crop (timeline at the bottom)\n" +
        "  • green/red handle, the middle moves the whole selection\n" +
        "  • arrows ←/→ (Shift = 1 min, Ctrl = 10 min), Home/End switches the handle\n\n" +
        "File: Ctrl+O open GPX, Ctrl+S save project · Export: Ctrl+E, Ctrl+Shift+C copy .setting",
        "Controls", MessageBoxButtons.OK, MessageBoxIcon.Information);

    /// <summary>The user panned/zoomed the map in the preview: store it as the manual export frame.</summary>
    private void OnFrameChanged(MapFrame frame, bool final)
    {
        bool wasAuto = _settings.ManualFrame == null;
        var (lat, lon) = WebMercator.FromWorld(frame.CenterX, frame.CenterY, frame.Zoom);
        _settings.ManualFrame = new FrameOverride { Lat = lat, Lon = lon, Zoom = frame.Zoom };
        if (!final) return; // the preview already shows the moved frame; refresh the rest once the drag ends
        Recompute();
        _grid.Refresh();
        if (wasAuto) _status.Text = "Frame adjusted manually (drag = pan, wheel = zoom). 'Auto frame' restores automatic.";
    }

    // =============== labels ===============

    private void AddLabel(double lat, double lon)
    {
        SetAddLabelMode(false);
        // New labels inherit the style of the last one, so a set of labels stays consistent.
        var label = _settings.Labels.LastOrDefault()?.Clone() ?? new MapLabel();
        label.Text = "Label";
        label.Lat = lat;
        label.Lon = lon;
        _settings.Labels.Add(label);
        _preview.Invalidate();
        if (!EditLabel(label, isNew: true))
        {
            _settings.Labels.Remove(label);
            _preview.Invalidate();
        }
        _grid.Refresh();
    }

    /// <returns>false when the dialog was cancelled.</returns>
    private bool EditLabel(MapLabel label, bool isNew)
    {
        using var dlg = new LabelDialog(label, isNew, () => _preview.Invalidate());
        var result = dlg.ShowDialog(this);
        if (result == DialogResult.Abort) DeleteLabel(label);
        _preview.Invalidate();
        return result == DialogResult.OK;
    }

    private void DeleteLabel(MapLabel label)
    {
        _settings.Labels.Remove(label);
        _preview.Invalidate();
        _grid.Refresh();
        _status.Text = "Label deleted.";
    }

    // =============== export ===============

    private string DefaultExportFolder()
    {
        if (_lastExportFolder != null) return _lastExportFolder;
        string dir = Path.GetDirectoryName(_gpxPath!) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(dir, Path.GetFileNameWithoutExtension(_gpxPath) + "_fusion");
    }

    private async Task ExportAsync()
    {
        if (_track == null || _exportCts != null) return;
        using var dlg = new FolderBrowserDialog
        {
            Description = "Folder for map.png, GpxTrack.setting, track.png and track_points.csv",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        string def = DefaultExportFolder();
        dlg.InitialDirectory = Directory.Exists(def) ? def : Path.GetDirectoryName(def) ?? "";
        dlg.SelectedPath = def;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string folder = dlg.SelectedPath;

        if (File.Exists(Path.Combine(folder, Exporter.MapFileName)) &&
            MessageBox.Show(this, $"The folder already contains an export. Overwrite?\n{folder}", Text, MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question) != DialogResult.OK)
            return;

        var cropped = Cropped!;
        var settings = _settings.Clone();
        _exportCts = new CancellationTokenSource();
        SetBusy(true);
        var status = new Progress<string>(s => _status.Text = s);
        try
        {
            var result = await Task.Run(() => Exporter.ExportAsync(cropped, settings, _cache, folder, status, _exportCts.Token));
            _lastExportFolder = folder;
            _status.Text = $"Done: {folder}";
            string missing = result.MissingTiles > 0 ? $"\n\n⚠ {result.MissingTiles} tiles could not be downloaded (gray areas). Try exporting again." : "";
            var answer = MessageBox.Show(this,
                $"Export finished.\n\n{Exporter.MapFileName}, {Exporter.SettingFileName} ({result.PolylinePoints} polyline points), " +
                $"{Exporter.TrackFileName}, {Exporter.CsvFileName}{missing}\n\nOpen the folder?",
                Text, MessageBoxButtons.YesNo, result.MissingTiles > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            if (answer == DialogResult.Yes)
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Export canceled.";
        }
        catch (Exception ex)
        {
            _status.Text = "Export failed.";
            MessageBox.Show(this, $"Export failed:\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _exportCts.Dispose();
            _exportCts = null;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _progress.Visible = busy;
        _btnCancel.Visible = busy;
        _btnExport.Enabled = _miExport.Enabled = !busy && _track != null;
        _grid.Enabled = !busy;
        _timeline.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void CopySetting()
    {
        if (_track == null) return;
        string mapPath;
        if (_lastExportFolder != null && File.Exists(Path.Combine(_lastExportFolder, Exporter.MapFileName)))
        {
            mapPath = Path.Combine(_lastExportFolder, Exporter.MapFileName);
        }
        else
        {
            var r = MessageBox.Show(this,
                "map.png has not been exported yet. Copy anyway? (The Loader will point to the default folder " +
                "until you export map.png there.)\n\nRecommended: Export… first",
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (r != DialogResult.OK) return;
            mapPath = Path.Combine(DefaultExportFolder(), Exporter.MapFileName);
        }
        string setting;
        try
        {
            setting = Exporter.BuildSetting(Cropped!, _settings, mapPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Clipboard.SetText(setting);
        _status.Text = "Fusion .setting is on the clipboard → Resolve → Fusion page → Node editor → Ctrl+V";
    }
}
