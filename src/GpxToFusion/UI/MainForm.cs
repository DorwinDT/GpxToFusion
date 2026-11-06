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
    private readonly Label _info;
    private readonly NumericUpDown _numStart, _numEnd;
    private readonly Label _lblStart, _lblEnd, _lblResult;
    private readonly ToolStripButton _btnExport, _btnCopy, _btnCancel, _btnAddLabel, _btnAutoFrame;
    private readonly ToolStripComboBox _cmbPreview;
    private readonly ToolStripDropDownButton _btnExtend;
    private readonly ToolStripMenuItem _miExtendStart, _miExtendEnd;
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

        // ---------- toolbar ----------
        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 2, 6, 2) };
        var btnOpen = new ToolStripButton("Otvoriť GPX…") { ToolTipText = "Ctrl+O" };
        btnOpen.Click += (_, _) => OpenGpxDialog();
        var btnLoad = new ToolStripButton("Načítať projekt…");
        btnLoad.Click += (_, _) => LoadProjectDialog();
        var btnSave = new ToolStripButton("Uložiť projekt…") { ToolTipText = "Ctrl+S" };
        btnSave.Click += (_, _) => SaveProjectDialog();
        _btnExport = new ToolStripButton("Exportovať…") { Font = new Font(Font, FontStyle.Bold), Enabled = false, ToolTipText = "Ctrl+E – map.png + GpxTrack.setting + track.png + CSV" };
        _btnExport.Click += async (_, _) => await ExportAsync();
        _btnCopy = new ToolStripButton("Kopírovať .setting do schránky") { Enabled = false, ToolTipText = "Potom v Resolve → Fusion → Node editor → Ctrl+V" };
        _btnCopy.Click += (_, _) => CopySetting();
        _btnCancel = new ToolStripButton("Zrušiť export") { Visible = false };
        _btnCancel.Click += (_, _) => _exportCts?.Cancel();
        _cmbPreview = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        _cmbPreview.Items.AddRange(["Náhľad: výrez exportu", "Náhľad: celá trasa"]);
        _cmbPreview.SelectedIndex = 0;
        _cmbPreview.SelectedIndexChanged += (_, _) =>
        {
            _preview!.Mode = _cmbPreview.SelectedIndex == 0 ? PreviewMode.ExportFrame : PreviewMode.FullTrack;
            _preview.Invalidate();
        };
        _btnAutoFrame = new ToolStripButton("Auto výrez") { Enabled = false,
            ToolTipText = "Výrez mapy si v náhľade posunieš ťahaním (alebo stredným tlačidlom) a priblížiš kolieskom myši (Shift = jemne).\n" +
                          "Toto tlačidlo vráti automatický výrez podľa orezanej trasy." };
        _btnAutoFrame.Click += (_, _) =>
        {
            _settings.ManualFrame = null;
            Recompute();
            _grid!.Refresh();
            _status!.Text = "Výrez je znova automatický (podľa orezanej trasy).";
        };
        _btnAddLabel = new ToolStripButton("Pridať popis") { CheckOnClick = true, Enabled = false,
            ToolTipText = "Potom klikni do mapy. Popis: dvojklik = upraviť, ťahanie = posunúť, pravé tlačidlo = zmazať." };
        _btnAddLabel.CheckedChanged += (_, _) =>
        {
            _preview!.AddLabelMode = _btnAddLabel.Checked;
            if (_btnAddLabel.Checked)
            {
                SetExtendMode(ExtendMode.None);
                _status!.Text = "Klikni do mapy, kam chceš popis (Esc = zrušiť).";
            }
        };
        _btnExtend = new ToolStripDropDownButton("Doplniť trasu") { Enabled = false,
            ToolTipText = "Doplní body pred začiatok / za koniec GPX (napr. keď si tracker zapol neskôr)." };
        _miExtendStart = new ToolStripMenuItem("Doplniť začiatok (klikaj do mapy)");
        _miExtendStart.Click += (_, _) => SetExtendMode(_preview!.ExtendMode == ExtendMode.Start ? ExtendMode.None : ExtendMode.Start);
        _miExtendEnd = new ToolStripMenuItem("Doplniť koniec (klikaj do mapy)");
        _miExtendEnd.Click += (_, _) => SetExtendMode(_preview!.ExtendMode == ExtendMode.End ? ExtendMode.None : ExtendMode.End);
        var miClearStart = new ToolStripMenuItem("Zmazať doplnený začiatok");
        miClearStart.Click += (_, _) => { _settings.ExtraStart.Clear(); RebuildTrack(); };
        var miClearEnd = new ToolStripMenuItem("Zmazať doplnený koniec");
        miClearEnd.Click += (_, _) => { _settings.ExtraEnd.Clear(); RebuildTrack(); };
        var miSaveGpx = new ToolStripMenuItem("Uložiť GPX ako…");
        miSaveGpx.Click += (_, _) => SaveGpxDialog();
        _btnExtend.DropDownItems.AddRange([_miExtendStart, _miExtendEnd, new ToolStripSeparator(), miClearStart, miClearEnd,
            new ToolStripSeparator(), miSaveGpx]);
        _btnExtend.DropDownOpening += (_, _) =>
        {
            miClearStart.Enabled = _settings.ExtraStart.Count > 0;
            miClearEnd.Enabled = _settings.ExtraEnd.Count > 0;
        };
        tools.Items.AddRange([btnOpen, btnLoad, btnSave, new ToolStripSeparator(), _btnExport, _btnCopy, _btnCancel,
            new ToolStripSeparator(), _cmbPreview, _btnAutoFrame, _btnAddLabel, _btnExtend]);

        // ---------- status ----------
        var statusStrip = new StatusStrip();
        _status = new ToolStripStatusLabel("Pripravené") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _progress = new ToolStripProgressBar { Visible = false, Width = 200, Style = ProgressBarStyle.Marquee };
        statusStrip.Items.AddRange([_status, _progress]);

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
            HelpVisible = true,
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

        _info = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 110,
            Padding = new Padding(6),
            BackColor = SystemColors.Info,
            ForeColor = SystemColors.InfoText,
        };

        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(_grid);
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
        var btnReset = new Button { Text = "Zrušiť orezanie", AutoSize = true };
        btnReset.Click += (_, _) => { if (_track != null) _timeline.SetRange(0, _track.Count - 1); };

        var cropRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(6, 4, 6, 0), WrapContents = false };
        cropRow.Controls.AddRange([
            new Label { Text = "Začiatok:", AutoSize = true, Margin = new Padding(0, 7, 2, 0), ForeColor = Color.Green }, _numStart, _lblStart,
            new Label { Text = "Koniec:", AutoSize = true, Margin = new Padding(0, 7, 2, 0), ForeColor = Color.Red }, _numEnd, _lblEnd,
            _lblResult, btnReset,
        ]);
        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 20,
            Padding = new Padding(8, 3, 0, 0),
            Text = "Orezanie: ťahaj zelené/červené ťahadlo, stred posúva celý výber. Šípky ←/→ (Shift = 1 min, Ctrl = 10 min), Home/End prepína ťahadlo.",
            ForeColor = SystemColors.GrayText,
        };
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 170 };
        bottom.Controls.Add(_timeline);
        bottom.Controls.Add(hint);
        bottom.Controls.Add(cropRow);

        Controls.Add(split);
        Controls.Add(bottom);
        Controls.Add(tools);
        Controls.Add(statusStrip);

        Load += (_, _) =>
        {
            split.SplitterDistance = Math.Max(300, split.Width - 380);
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
        KeyDown += async (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.O) { OpenGpxDialog(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.S) { SaveProjectDialog(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.E && _btnExport.Enabled) { e.Handled = true; await ExportAsync(); }
            else if (e.KeyCode == Keys.Escape && _btnAddLabel.Checked) { _btnAddLabel.Checked = false; e.Handled = true; }
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
        using var dlg = new OpenFileDialog { Filter = "GPX (*.gpx)|*.gpx|Všetky súbory|*.*", Title = "Otvoriť GPX" };
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
                MessageBox.Show(this, "GPX neobsahuje trasu (aspoň 2 body).", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            _status.Text = $"Načítané: {_recorded.Count:N0} bodov, {FormatDuration(_track!.TotalSeconds)}, {_track.TotalDistance / 1000:0.00} km";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Nepodarilo sa načítať GPX:\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void LoadProjectDialog()
    {
        using var dlg = new OpenFileDialog { Filter = "Projekt GPX→Fusion (*.g2f.json)|*.g2f.json|JSON|*.json", Title = "Načítať projekt" };
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
                MessageBox.Show(this, $"GPX súbor projektu sa nenašiel:\n{p.GpxPath}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Recompute();
                return;
            }
            if (OpenGpx(p.GpxPath, p.CropStart, p.CropEnd, fromProject: true))
                _lastExportFolder = p.LastExportFolder;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Nepodarilo sa načítať projekt:\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveProjectDialog()
    {
        if (_gpxPath == null) return;
        using var dlg = new SaveFileDialog
        {
            Filter = "Projekt GPX→Fusion (*.g2f.json)|*.g2f.json",
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
        _status.Text = $"Projekt uložený: {dlg.FileName}";
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
                _status.Text = $"Doplnené body: začiatok {_settings.ExtraStart.Count}, koniec {_settings.ExtraEnd.Count}. " + ExtendHint;
            else if (n == 0)
                _status.Text = "Doplnené body zmazané.";
        }
    }

    private const string ExtendHint = "Klik = nový bod, ťahanie = posun, klik na úsek = vložiť bod, pravé tlačidlo = zmazať, Esc = koniec.";

    private void SetExtendMode(ExtendMode mode)
    {
        if (mode != ExtendMode.None && _btnAddLabel.Checked) _btnAddLabel.Checked = false;
        _preview.ExtendMode = mode;
        _miExtendStart.Checked = mode == ExtendMode.Start;
        _miExtendEnd.Checked = mode == ExtendMode.End;
        _btnExtend.Text = mode switch
        {
            ExtendMode.Start => "Doplniť trasu: ZAČIATOK",
            ExtendMode.End => "Doplniť trasu: KONIEC",
            _ => "Doplniť trasu",
        };
        _btnExtend.Font = new Font(Font, mode == ExtendMode.None ? FontStyle.Regular : FontStyle.Bold);
        if (mode == ExtendMode.None) return;
        string where = mode == ExtendMode.Start ? "pred začiatok (od zeleného bodu smerom von)" : "za koniec (od červeného bodu ďalej)";
        string crop = mode == ExtendMode.Start && _timeline.Start > 0 || mode == ExtendMode.End && _track != null && _timeline.End < _track.Count - 1
            ? " ⚠ Trasa je na tomto konci orezaná – doplnené body nebudú v exporte, kým orezanie nezrušíš."
            : "";
        _status.Text = $"Doplňovanie {where}. {ExtendHint}{crop}";
    }

    private void SaveGpxDialog()
    {
        if (_track == null || _gpxPath == null) return;
        using var dlg = new SaveFileDialog
        {
            Filter = "GPX (*.gpx)|*.gpx",
            FileName = Path.GetFileNameWithoutExtension(_gpxPath) + "_doplnene.gpx",
            InitialDirectory = Path.GetDirectoryName(_gpxPath),
            Title = "Uložiť GPX s doplnenými bodmi",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(_gpxPath), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Pôvodný GPX sa neprepisuje (projekt naň odkazuje). Zvoľ iný názov.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            GpxWriter.Write(dlg.FileName, _track.Points, Path.GetFileNameWithoutExtension(dlg.FileName));
            _status.Text = $"GPX uložený ({_track.Count:N0} bodov, celá trasa bez orezania): {dlg.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"GPX sa nepodarilo uložiť:\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        _lblResult.Text = $"Výsledok: {FormatDuration(secs)} · {km:0.00} km";
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
        _btnExport.Enabled = has && _exportCts == null;
        _btnCopy.Enabled = has;
        _btnAddLabel.Enabled = has;
        _btnExtend.Enabled = has;
        _btnAutoFrame.Enabled = has && _settings.ManualFrame != null;
        _preview.Settings = _settings;
        if (_track == null)
        {
            _info.Text = "Žiadna trasa.";
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
        string warn = tiles > 2000 ? $"\n⚠ Veľa dlaždíc ({tiles}) – sťahovanie bude trvať dlho, zváž menšie rozlíšenie." : "";
        _info.Text =
            $"map.png: {_settings.Width} × {_settings.Height} px, zoom {frame.Zoom:0.00} (dlaždice z{tz}, {tiles} ks)\n" +
            $"Mierka: {metersPerPx:0.00} m/px\n" +
            $"Body: {cropped.Count:N0} → po filtri {geo.Full.Count:N0} → polyline {geo.Simplified.Count:N0}\n" +
            $"Hrúbka čiary: {_settings.LineWidthPx:0.#} px" + warn;
    }

    /// <summary>The user panned/zoomed the map in the preview: store it as the manual export frame.</summary>
    private void OnFrameChanged(MapFrame frame, bool final)
    {
        bool wasAuto = _settings.ManualFrame == null;
        var (lat, lon) = WebMercator.FromWorld(frame.CenterX, frame.CenterY, frame.Zoom);
        _settings.ManualFrame = new FrameOverride { Lat = lat, Lon = lon, Zoom = frame.Zoom };
        if (!final) return; // the preview already shows the moved frame; refresh the rest once the drag ends
        Recompute();
        _grid.Refresh();
        if (wasAuto) _status.Text = "Výrez upravený ručne (ťahanie = posun, koliesko = zoom). 'Auto výrez' vráti automatický.";
    }

    // =============== labels ===============

    private void AddLabel(double lat, double lon)
    {
        _btnAddLabel.Checked = false;
        // New labels inherit the style of the last one, so a set of labels stays consistent.
        var label = _settings.Labels.LastOrDefault()?.Clone() ?? new MapLabel();
        label.Text = "Popis";
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
        _status.Text = "Popis zmazaný.";
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
            Description = "Priečinok pre map.png, GpxTrack.setting, track.png a track_points.csv",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        string def = DefaultExportFolder();
        dlg.InitialDirectory = Directory.Exists(def) ? def : Path.GetDirectoryName(def) ?? "";
        dlg.SelectedPath = def;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string folder = dlg.SelectedPath;

        if (File.Exists(Path.Combine(folder, Exporter.MapFileName)) &&
            MessageBox.Show(this, $"V priečinku už je export. Prepísať?\n{folder}", Text, MessageBoxButtons.OKCancel,
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
            _status.Text = $"Hotovo: {folder}";
            string missing = result.MissingTiles > 0 ? $"\n\n⚠ {result.MissingTiles} dlaždíc sa nepodarilo stiahnuť (sivé miesta). Skús export zopakovať." : "";
            var answer = MessageBox.Show(this,
                $"Export hotový.\n\n{Exporter.MapFileName}, {Exporter.SettingFileName} ({result.PolylinePoints} bodov polyline), " +
                $"{Exporter.TrackFileName}, {Exporter.CsvFileName}{missing}\n\nOtvoriť priečinok?",
                Text, MessageBoxButtons.YesNo, result.MissingTiles > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            if (answer == DialogResult.Yes)
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Export zrušený.";
        }
        catch (Exception ex)
        {
            _status.Text = "Export zlyhal.";
            MessageBox.Show(this, $"Export zlyhal:\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        _btnExport.Enabled = !busy && _track != null;
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
                "map.png ešte nebola exportovaná. Skopírovať aj tak? (Loader bude ukazovať na predvolený priečinok, " +
                "kým tam map.png neexportuješ.)\n\nOdporúčané: najprv Exportovať…",
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
        _status.Text = "Fusion .setting je v schránke → Resolve → Fusion stránka → Node editor → Ctrl+V";
    }
}
