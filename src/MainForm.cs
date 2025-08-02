using GateWatcher.Models;
using GateWatcher.Services;
using ScottPlot.WinForms;
using System.Globalization;

namespace GateWatcher;

public sealed class MainForm : Form
{
    private readonly ConfigManager _cfg;

    // stored event handlers so we can unsubscribe reliably
    private LinkLabelLinkClickedEventHandler? _onToggleConfig;
    private EventHandler? _onShowTextChanged;
    private EventHandler? _onAutoFocusChanged;
    private EventHandler? _onFocusModeChanged;
    private EventHandler? _onResetUp;
    private EventHandler? _onResetDown;

    // ==== TEXT (tables) ====
    private readonly DataGridView _gridUp = new();
    private readonly DataGridView _gridDown = new();
    private GroupBox _grpUpGrid = null!;
    private GroupBox _grpDownGrid = null!;

    // ==== GRAPHS ====
    private readonly FormsPlot _plotUp = new() { Dock = DockStyle.Fill };
    private readonly FormsPlot _plotDown = new() { Dock = DockStyle.Fill };

    // ==== HISTORY / SERIES ====
    private readonly int _historyPoints = 300; // visible time = historyPoints * intervalSec
    private int _intervalSec = 5;              // updated from config
    private readonly Dictionary<string, (double change, double price, double vol)> _prevUp = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (double change, double price, double vol)> _prevDown = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<(DateTime t, double v)>> _histUp = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<(DateTime t, double v)>> _histDown = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScottPlot.Plottables.Scatter> _lineUp = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScottPlot.Plottables.Scatter> _lineDown = new(StringComparer.OrdinalIgnoreCase);

    // start markers + labels
    private readonly Dictionary<string, ScottPlot.Plottables.VerticalLine> _startMarkersUp = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScottPlot.Plottables.VerticalLine> _startMarkersDown = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScottPlot.Plottables.Text> _startLabelsUp = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScottPlot.Plottables.Text> _startLabelsDown = new(StringComparer.OrdinalIgnoreCase);

    // ==== UI BEHAVIOR ====
    private bool _autoFocusEnabled = true;           // UI checkbox
    private string _focusMode = "Center";            // "Right" or "Center"
    private bool _userAdjustedUp = false;            // stops auto axes after user pan/zoom
    private bool _userAdjustedDown = false;
    private bool _yInitializedUp = false;            // Y initialized once per plot
    private bool _yInitializedDown = false;

    // NEW: “placeholder zero frame” and “force markers next frame”
    private bool _firstFrameUp = true, _firstFrameDown = true;
    private bool _forceStartMarkersNextUp = false, _forceStartMarkersNextDown = false;

    // cache last series + snapshot for reset
    private List<(string Symbol, double[] Xs, double[] Ys)> _lastSeriesUp = new();
    private List<(string Symbol, double[] Xs, double[] Ys)> _lastSeriesDown = new();
    private DateTime _lastSnapshotTime = DateTime.MinValue;

    // ==== CONFIG PANEL ====
    private Panel _configPanel = null!;
    private LinkLabel _toggleConfig = null!;
    private CheckBox _chkShowText = null!;
    private CheckBox _chkAutoFocus = null!;
    private ComboBox _cmbFocusMode = null!;
    private Button _btnResetUp = null!;
    private Button _btnResetDown = null!;

    // bound config inputs (save on focus-out)
    private NumericUpDown _numInterval = null!;
    private NumericUpDown _numLookback = null!;
    private NumericUpDown _numWindow = null!;
    private NumericUpDown _numInc = null!;
    private NumericUpDown _numDec = null!;
    private TextBox _txtQuotes = null!;
    private NumericUpDown _numMinQv = null!;
    private NumericUpDown _numMinBv = null!;
    private CheckBox _chkNewOnly = null!;
    private NumericUpDown _numNewDays = null!;

    // Information block (alerts)
    private Panel _infoPanel = null!;
    private ListView _alertsList = null!;
    private const int MaxAlertItems = 500;

    public MainForm(ConfigManager cfg)
    {
        _cfg = cfg;

        Text = "GateWatcher – Top 10 Increase / Decrease";
        Width = 1400;
        Height = 900;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        Controls.Add(root);

        // --- Config header + panel ---
        var configHeader = new Panel { Dock = DockStyle.Fill, Height = 150 };
        _toggleConfig = new LinkLabel { Text = "▼ Config (click to collapse/expand)", AutoSize = true, Location = new Point(6, 6) };
        _onToggleConfig = (_, __) => ToggleConfigPanel();
        _toggleConfig.LinkClicked += _onToggleConfig;

        _chkShowText = new CheckBox { Text = "Show text tables", Checked = true, Location = new Point(260, 6), AutoSize = true };
        _onShowTextChanged = (_, __) => ToggleTextTables(_chkShowText.Checked);
        _chkShowText.CheckedChanged += _onShowTextChanged;

        _chkAutoFocus = new CheckBox { Text = "Auto focus graphs", Checked = _autoFocusEnabled, Location = new Point(420, 6), AutoSize = true };
        // when the user re-enables auto-focus via checkbox, immediately autoscale both plots once
        _onAutoFocusChanged = (_, __) =>
        {
            _autoFocusEnabled = _chkAutoFocus.Checked;

            if (_autoFocusEnabled)
            {
                // user explicitly re-enabled: clear manual flags and re-lock Y after autoscale
                _userAdjustedUp = _userAdjustedDown = false;
                _yInitializedUp = _yInitializedDown = false;

                try { _plotUp.Plot.Axes.AutoScale(); _plotUp.Refresh(); } catch { }
                try { _plotDown.Plot.Axes.AutoScale(); _plotDown.Refresh(); } catch { }

                // lock Y so further updates won't change it automatically
                _yInitializedUp = _yInitializedDown = true;
            }
            // when unchecked, ApplyAxes() will not move X; we keep current view
        };
        _chkAutoFocus.CheckedChanged += _onAutoFocusChanged;


        _cmbFocusMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(580, 3), Width = 140 };
        _cmbFocusMode.Items.AddRange(new[] { "Right", "Center" });
        _cmbFocusMode.SelectedIndex = _focusMode == "Right" ? 0 : 1;
        _onFocusModeChanged = (_, __) => _focusMode = _cmbFocusMode.SelectedItem?.ToString() ?? "Center";
        _cmbFocusMode.SelectedIndexChanged += _onFocusModeChanged;

        // Reset view buttons
        _btnResetUp = new Button { Text = "Reset view (Increase)", Location = new Point(740, 2), Width = 160, Height = 26 };
        _onResetUp = (_, __) => ResetView(_plotUp, isUp: true);
        _btnResetUp.Click += _onResetUp;

        _btnResetDown = new Button { Text = "Reset view (Decrease)", Location = new Point(910, 2), Width = 170, Height = 26 };
        _onResetDown = (_, __) => ResetView(_plotDown, isUp: false);
        _btnResetDown.Click += _onResetDown;

        configHeader.Controls.AddRange(new Control[] { _toggleConfig, _chkShowText, _chkAutoFocus, _cmbFocusMode, _btnResetUp, _btnResetDown });

        _configPanel = BuildConfigPanel();
        configHeader.Controls.Add(_configPanel);

        _infoPanel = BuildInfoPanel();
        configHeader.Controls.Add(_infoPanel);

        // default state: config collapsed, info visible
        _configPanel.Visible = false;
        _infoPanel.Visible = true;

        root.Controls.Add(configHeader, 0, 0);

        // --- Text grids row ---
        var textRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        textRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        textRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _grpUpGrid = Group("Top 10 – Increase (Text)", _gridUp);
        _grpDownGrid = Group("Top 10 – Decrease (Text)", _gridDown);
        ConfigureGrid(_gridUp);
        ConfigureGrid(_gridDown);
        textRow.Controls.Add(_grpUpGrid, 0, 0);
        textRow.Controls.Add(_grpDownGrid, 1, 0);
        root.Controls.Add(textRow, 0, 1);

        // --- Graphs row ---
        var graphRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        graphRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        graphRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var grpUpPlot = Group("Increase – 24h% over time", _plotUp);
        var grpDownPlot = Group("Decrease – 24h% over time", _plotDown);
        graphRow.Controls.Add(grpUpPlot, 0, 0);
        graphRow.Controls.Add(grpDownPlot, 1, 0);
        root.Controls.Add(graphRow, 0, 2);

        // Plot setup
        _plotUp.Plot.Axes.DateTimeTicksBottom();
        _plotDown.Plot.Axes.DateTimeTicksBottom();
        _plotUp.Plot.Title("Top 10 Increase – 24h%");
        _plotDown.Plot.Title("Top 10 Decrease – 24h%");
        _plotUp.Plot.YLabel("24h %");
        _plotDown.Plot.YLabel("24h %");

        // Any mouse interaction disables auto-focus and preserves user zoom/pan
        // helper to turn OFF auto-focus from user actions and reflect in the checkbox
        void DisableAutoFocusFromUser()
        {
            _autoFocusEnabled = false;
            _chkAutoFocus.Checked = false; // keeps UI in sync
        }

        // any mouse interaction disables auto-focus and records that the user adjusted the plot
        _plotUp.MouseWheel += (_, __) => { _userAdjustedUp = true; DisableAutoFocusFromUser(); };
        _plotDown.MouseWheel += (_, __) => { _userAdjustedDown = true; DisableAutoFocusFromUser(); };

        _plotUp.MouseDown += (_, e) =>
        {
            if (e.Button is MouseButtons.Left or MouseButtons.Right or MouseButtons.Middle)
            { _userAdjustedUp = true; DisableAutoFocusFromUser(); }
        };
        _plotDown.MouseDown += (_, e) =>
        {
            if (e.Button is MouseButtons.Left or MouseButtons.Right or MouseButtons.Middle)
            { _userAdjustedDown = true; DisableAutoFocusFromUser(); }
        };

        // Seed config controls
        LoadConfigToUi(_cfg.Current);

        // When form is truly closing (app exit), cleanup
        this.FormClosed += (_, __) => CleanupAndDispose();
    }

    public void SetIntervalSeconds(int seconds) => _intervalSec = Math.Max(1, seconds);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // If user clicks [X], hide so it can be re-opened; on App exit we'll truly dispose.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    // ======= CONFIG PANEL =======
    private Panel BuildConfigPanel()
    {
        var p = new Panel { Left = 6, Top = 32, Width = 1300, Height = 110, BorderStyle = BorderStyle.FixedSingle };

        int y1 = 8, y2 = 34;
        int x = 8, dx = 220, w = 60, wWide = 140;

        System.Windows.Forms.Label L(string t, int lx, int ly) { var l = new System.Windows.Forms.Label { Text = t, Left = lx, Top = ly, AutoSize = true }; p.Controls.Add(l); return l; }
        Control Add(Control c) { p.Controls.Add(c); return c; }

        // Row 1: Poll/Lookback/Window, Thresholds
        L("Interval (sec):", x, y1); _numInterval = new NumericUpDown { Left = x + 100, Top = y1 - 2, Width = w, Minimum = 1, Maximum = 3600 }; Add(_numInterval);
        L("Lookback (sec):", x + dx, y1); _numLookback = new NumericUpDown { Left = x + dx + 110, Top = y1 - 2, Width = w, Minimum = 1, Maximum = 3600 }; Add(_numLookback);
        L("Window (samples):", x + 2 * dx, y1); _numWindow = new NumericUpDown { Left = x + 2 * dx + 130, Top = y1 - 2, Width = w, Minimum = 2, Maximum = 1000 }; Add(_numWindow);

        L("Increase %:", x + 3 * dx, y1); _numInc = new NumericUpDown { Left = x + 3 * dx + 80, Top = y1 - 2, Width = w, Minimum = 1, Maximum = 1000, DecimalPlaces = 2, Increment = 0.5M }; Add(_numInc);
        L("Decrease %:", x + 4 * dx, y1); _numDec = new NumericUpDown { Left = x + 4 * dx + 80, Top = y1 - 2, Width = w, Minimum = 1, Maximum = 1000, DecimalPlaces = 2, Increment = 0.5M }; Add(_numDec);

        // Row 2: Quotes, Volume, New
        L("Quotes (comma):", x, y2); _txtQuotes = new TextBox { Left = x + 100, Top = y2 - 4, Width = wWide }; Add(_txtQuotes);
        L("Min QuoteVol 24h:", x + dx, y2); _numMinQv = new NumericUpDown { Left = x + dx + 130, Top = y2 - 2, Width = 100, Minimum = 0, Maximum = decimal.MaxValue, DecimalPlaces = 2, Increment = 100 }; Add(_numMinQv);
        L("Min BaseVol 24h:", x + 2 * dx, y2); _numMinBv = new NumericUpDown { Left = x + 2 * dx + 120, Top = y2 - 2, Width = 100, Minimum = 0, Maximum = decimal.MaxValue, DecimalPlaces = 2, Increment = 10 }; Add(_numMinBv);

        _chkNewOnly = new CheckBox { Text = "New only", Left = x + 3 * dx, Top = y2 - 4, AutoSize = true }; Add(_chkNewOnly);
        L("New since days:", x + 4 * dx, y2); _numNewDays = new NumericUpDown { Left = x + 4 * dx + 120, Top = y2 - 2, Width = w, Minimum = 1, Maximum = 365 }; Add(_numNewDays);

        // Save on focus-out
        _numInterval.Leave += (_, __) => SaveCfg(c => c.Polling.PollIntervalSeconds = (int)_numInterval.Value);
        _numLookback.Leave += (_, __) => SaveCfg(c => c.Polling.LookbackSeconds = (int)_numLookback.Value);
        _numWindow.Leave += (_, __) => SaveCfg(c => c.Polling.SamplesWindow = (int)_numWindow.Value);
        _numInc.Leave += (_, __) => SaveCfg(c => c.Thresholds.IncreaseThresholdPercent = (double)_numInc.Value);
        _numDec.Leave += (_, __) => SaveCfg(c => c.Thresholds.DecreaseThresholdPercent = (double)_numDec.Value);
        _txtQuotes.Leave += (_, __) => SaveCfg(c => c.Filters.QuoteCurrencies = _txtQuotes.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList());
        _numMinQv.Leave += (_, __) => SaveCfg(c => c.Filters.MinQuoteVolume24h = (double)_numMinQv.Value);
        _numMinBv.Leave += (_, __) => SaveCfg(c => c.Filters.MinBaseVolume24h = (double)_numMinBv.Value);
        _chkNewOnly.CheckedChanged += (_, __) => SaveCfg(c => c.Filters.NewOnly = _chkNewOnly.Checked);
        _numNewDays.Leave += (_, __) => SaveCfg(c => c.Filters.NewSinceDays = (int)_numNewDays.Value);

        return p;
    }

    private Panel BuildInfoPanel()
    {
        var p = new Panel
        {
            Left = 6,
            Top = 32,
            Width = 1300,
            Height = 130,                  // same height as smaller config
            BorderStyle = BorderStyle.FixedSingle
        };

        var title = new System.Windows.Forms.Label
        {
            Text = "Recent Alerts",
            Left = 6,
            Top = 6,
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold)
        };
        p.Controls.Add(title);

        _alertsList = new ListView
        {
            View = View.Details,
            FullRowSelect = false,
            GridLines = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Left = 6,
            Top = 26,
            Width = p.Width - 12,
            Height = p.Height - 32,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom
        };

        _alertsList.Columns.Add("Time", 100, System.Windows.Forms.HorizontalAlignment.Left);
        _alertsList.Columns.Add("Message", _alertsList.Width - 130, System.Windows.Forms.HorizontalAlignment.Left);

        p.Controls.Add(_alertsList);
        return p;
    }

    private void ToggleConfigPanel()
    {
        _configPanel.Visible = !_configPanel.Visible;
        _infoPanel.Visible = !_configPanel.Visible; // mirror visibility
        _toggleConfig.Text = _configPanel.Visible
            ? "▼ Config (click to collapse/expand)"
            : "► Config (click to expand)";
    }

    private void ToggleTextTables(bool show)
    {
        _grpUpGrid.Visible = show;
        _grpDownGrid.Visible = show;
        _grpUpGrid.Parent?.PerformLayout();
    }

    private void LoadConfigToUi(AppConfig c)
    {
        _numInterval.Value = c.Polling.PollIntervalSeconds;
        _numLookback.Value = c.Polling.LookbackSeconds;
        _numWindow.Value = c.Polling.SamplesWindow;
        _numInc.Value = (decimal)c.Thresholds.IncreaseThresholdPercent;
        _numDec.Value = (decimal)c.Thresholds.DecreaseThresholdPercent;

        _txtQuotes.Text = (c.Filters.QuoteCurrencies?.Count ?? 0) == 0 ? "" : string.Join(", ", c.Filters.QuoteCurrencies);
        _numMinQv.Value = (decimal)c.Filters.MinQuoteVolume24h;
        _numMinBv.Value = (decimal)c.Filters.MinBaseVolume24h;
        _chkNewOnly.Checked = c.Filters.NewOnly;
        _numNewDays.Value = c.Filters.NewSinceDays;

        _intervalSec = c.Polling.PollIntervalSeconds;
    }

    private void SaveCfg(Action<AppConfig> mutate)
    {
        _cfg.Save(mutate);
        _intervalSec = _cfg.Current.Polling.PollIntervalSeconds;
    }

    private GroupBox Group(string title, Control child)
    {
        var g = new GroupBox { Text = title, Dock = DockStyle.Fill };
        child.Dock = DockStyle.Fill;
        g.Controls.Add(child);
        return g;
    }

    private void ConfigureGrid(DataGridView grid)
    {
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

        grid.Columns.Add("Rank", "Rank");
        grid.Columns.Add("Pair", "Pair");
        grid.Columns.Add("ChangePct", "24h % (prev)");
        grid.Columns.Add("QuoteVol", "Quote Vol (24h) (prev)");
        grid.Columns.Add("Last", "Last (prev)");
        grid.Columns.Add("Time", "Time");
    }

    // ======= PUBLIC ENTRY =======
    public void UpdateSnapshot(StatsSnapshot snap)
    {
        if (InvokeRequired) { BeginInvoke(() => UpdateSnapshot(snap)); return; }

        UpdateGrid(_gridUp, snap.TopUp, _prevUp, _histUp, _lineUp, _plotUp, snap.LocalTime, isUp: true);
        UpdateGrid(_gridDown, snap.TopDown, _prevDown, _histDown, _lineDown, _plotDown, snap.LocalTime, isUp: false);

        _lastSnapshotTime = snap.LocalTime;
        Text = $"GateWatcher – {snap.LocalTime:HH:mm:ss}";
    }

    public void PushAlert(AlertItem alert)
    {
        if (InvokeRequired) { BeginInvoke(new Action<AlertItem>(PushAlert), alert); return; }

        _alertsList.BeginUpdate();
        try
        {
            var lvi = new ListViewItem(alert.Time.ToString("HH:mm:ss")) { UseItemStyleForSubItems = false };
            var sub = new ListViewItem.ListViewSubItem(lvi, alert.Message);
            sub.ForeColor = alert.Direction switch
            {
                AlertDirectionType.Increase => Color.Green,
                AlertDirectionType.Decrease => Color.Red,
                _ => SystemColors.ControlText
            };
            lvi.SubItems.Add(sub);
            _alertsList.Items.Insert(0, lvi);
            while (_alertsList.Items.Count > MaxAlertItems)
                _alertsList.Items.RemoveAt(_alertsList.Items.Count - 1);
        }
        finally
        {
            _alertsList.EndUpdate();
        }
    }

    // ======= RESET VIEW BUTTONS =======
    private void ResetView(FormsPlot plot, bool isUp)
    {
        // re-enable auto focus; allow Y to initialize again; clear user-adjusted
        _autoFocusEnabled = true;
        _chkAutoFocus.Checked = true;

        if (isUp)
        {
            _userAdjustedUp = false;
            _yInitializedUp = false;
            if (_lastSeriesUp?.Count > 0 && _lastSnapshotTime != DateTime.MinValue)
            {
                ApplyAxes(plot, _lastSnapshotTime, _lastSeriesUp, isUp: true);
                plot.Refresh();
            }
        }
        else
        {
            _userAdjustedDown = false;
            _yInitializedDown = false;
            if (_lastSeriesDown?.Count > 0 && _lastSnapshotTime != DateTime.MinValue)
            {
                ApplyAxes(plot, _lastSnapshotTime, _lastSeriesDown, isUp: false);
                plot.Refresh();
            }
        }
    }

    // ======= CORE UPDATE =======
    private void UpdateGrid(
    DataGridView grid,
    IReadOnlyList<StatRow> rows,
    Dictionary<string, (double change, double price, double vol)> prev,
    Dictionary<string, List<(DateTime t, double v)>> hist,
    Dictionary<string, ScottPlot.Plottables.Scatter> lines,
    ScottPlot.WinForms.FormsPlot plot,
    DateTime snapshotTime,
    bool isUp)
    {
        grid.SuspendLayout();
        grid.Rows.Clear();

        // are we rendering the very first frame for this plot?
        bool isFirstFrame = isUp ? _firstFrameUp : _firstFrameDown;

        // Build ordered series arrays from history (we always maintain history even for first frame)
        var existedBefore = new HashSet<string>(lines.Keys, StringComparer.OrdinalIgnoreCase);
        var orderedSeries = new List<(string Symbol, double[] Xs, double[] Ys)>();

        int rank = 1;
        foreach (var r in rows)
        {
            prev.TryGetValue(r.Pair, out var pPrev);

            string fmtChange = FormatWithPrev(r.ChangePct, pPrev.change, "0.##");
            string fmtLast = FormatWithPrev(r.LastPrice, pPrev.price, "0.########");
            string fmtVol = FormatWithPrev(r.QuoteVol, pPrev.vol, "0.###");

            int rowIdx = grid.Rows.Add(rank.ToString(), r.Pair, fmtChange, fmtVol, fmtLast, r.LocalTime.ToString("HH:mm:ss"));
            var rowRef = grid.Rows[rowIdx];
            Colorize(rowRef.Cells["ChangePct"], r.ChangePct.CompareTo(pPrev.change));
            Colorize(rowRef.Cells["Last"], r.LastPrice.CompareTo(pPrev.price));
            Colorize(rowRef.Cells["QuoteVol"], r.QuoteVol.CompareTo(pPrev.vol));

            prev[r.Pair] = (r.ChangePct, r.LastPrice, r.QuoteVol);

            if (!hist.TryGetValue(r.Pair, out var list))
                hist[r.Pair] = list = new List<(DateTime t, double v)>(_historyPoints + 10);

            // add the real sample to history (even before first visual frame)
            list.Add((snapshotTime, r.ChangePct));
            while (list.Count > _historyPoints) list.RemoveAt(0);

            var xs = list.Select(x => x.t.ToOADate()).ToArray();
            var ys = list.Select(x => x.v).ToArray();
            orderedSeries.Add((r.Pair, xs, ys));

            rank++;
        }

        // choose maps for this plot
        var markerMap = isUp ? _startMarkersUp : _startMarkersDown;
        var labelMap = isUp ? _startLabelsUp : _startLabelsDown;

        // remove symbols that left Top-10 (and their markers/labels)
        var currentSymbols = new HashSet<string>(rows.Select(r => r.Pair), StringComparer.OrdinalIgnoreCase);
        foreach (var sym in lines.Keys.Except(currentSymbols).ToList())
        {
            try { plot.Plot.Remove(lines[sym]); } catch { }
            lines.Remove(sym);

            if (markerMap.TryGetValue(sym, out var v)) { try { plot.Plot.Remove(v); } catch { } markerMap.Remove(sym); }
            if (labelMap.TryGetValue(sym, out var t)) { try { plot.Plot.Remove(t); } catch { } labelMap.Remove(sym); }
        }

        // stable palette by rank
        var palette = ScottPlot.Colors.Category10;

        // ---------- FIRST FRAME (placeholder zeros) ----------
        if (isFirstFrame)
        {
            // wipe any lingering line plottables (clean slate)
            foreach (var kv in lines.ToList())
            {
                plot.Plot.Remove(kv.Value);
                lines.Remove(kv.Key);
            }

            for (int i = 0; i < orderedSeries.Count; i++)
            {
                var (symbol, _, _) = orderedSeries[i];

                // single point at (now, 0.0)
                var xsZero = new[] { snapshotTime.ToOADate() };
                var ysZero = new[] { 0.0 };
                var line = plot.Plot.Add.Scatter(xsZero, ysZero);
                line.Label = symbol;
                line.MarkerStyle.IsVisible = true;
                line.MarkerStyle.Size = 4;
                line.Color = palette[i % palette.Length];

                lines[symbol] = line;
            }

            // simple initial view: X per focus mode, Y = [-1, +1]
            double spanSec = Math.Max(1, _historyPoints * _intervalSec);
            double minX, maxX;
            if (_focusMode.Equals("Right", StringComparison.OrdinalIgnoreCase))
            {
                maxX = snapshotTime.ToOADate();
                minX = snapshotTime.AddSeconds(-spanSec).ToOADate();
            }
            else
            {
                double half = spanSec / 2.0;
                minX = snapshotTime.AddSeconds(-half).ToOADate();
                maxX = snapshotTime.AddSeconds(+half).ToOADate();
            }
            plot.Plot.Axes.SetLimits(minX, maxX, -1, +1);

            plot.Plot.Legend.IsVisible = lines.Count > 0;
            plot.Refresh();

            // flip flags so NEXT frame draws real data and autoscale()s once
            if (isUp) { _firstFrameUp = false; _yInitializedUp = false; }
            else { _firstFrameDown = false; _yInitializedDown = false; }

            grid.ResumeLayout();
            return;
        }

        // ---------- NORMAL FRAMES (real data) ----------
        for (int i = 0; i < orderedSeries.Count; i++)
        {
            var (symbol, xs, ys) = orderedSeries[i];

            if (lines.TryGetValue(symbol, out var existing))
                plot.Plot.Remove(existing);

            var line = plot.Plot.Add.Scatter(xs, ys);
            line.Label = symbol;
            line.MarkerStyle.IsVisible = true;
            line.MarkerStyle.Size = 4;
            line.Color = palette[i % palette.Length];

            // If this symbol wasn't there before (or we just came from zero frame), add start marker+label
            bool comingFromZeroFrame = isUp ? !_yInitializedUp : !_yInitializedDown; // i.e., first real render
            bool isNewSymbol = !existedBefore.Contains(symbol) || comingFromZeroFrame;

            if (isNewSymbol)
            {
                double xStart = xs.Length > 0 ? xs[0] : snapshotTime.ToOADate();
                double yStart = ys.Length > 0 ? ys[0] : 0.0;

                var (vLine, lbl) = AddStartMarker(plot, symbol, xStart, yStart, line.Color);
                markerMap[symbol] = vLine;
                labelMap[symbol] = lbl;
            }

            lines[symbol] = line;
        }

        // --- On the first REAL update for this plot, autoscale both axes once ---
        bool yInitialized = isUp ? _yInitializedUp : _yInitializedDown;
        if (!yInitialized)
        {
            try { plot.Plot.Axes.AutoScale(); } catch { }
            if (isUp) _yInitializedUp = true; else _yInitializedDown = true;
        }

        // cache for reset buttons
        if (isUp) _lastSeriesUp = orderedSeries; else _lastSeriesDown = orderedSeries;
        _lastSnapshotTime = snapshotTime;

        // now only step X (Y remains as established by the first autoscale)
        ApplyAxes(plot, snapshotTime, orderedSeries, isUp);
        plot.Plot.Legend.IsVisible = lines.Count > 0;
        plot.Refresh();

        grid.ResumeLayout();
    }



    // ======= HELPERS =======
    private static string FormatWithPrev(double current, double prev, string fmt)
        => $"{current.ToString(fmt, CultureInfo.InvariantCulture)} ({prev.ToString(fmt, CultureInfo.InvariantCulture)})";

    private static void Colorize(DataGridViewCell cell, int cmp)
    {
        if (cmp > 0) cell.Style.ForeColor = System.Drawing.Color.Green;
        else if (cmp < 0) cell.Style.ForeColor = System.Drawing.Color.Red;
        else cell.Style.ForeColor = SystemColors.ControlText;
        cell.Style.Font = new Font(SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold);
    }

    // dashed vertical line + label placed LEFT of first dot at same Y as line
    private (ScottPlot.Plottables.VerticalLine vLine, ScottPlot.Plottables.Text label)
    AddStartMarker(ScottPlot.WinForms.FormsPlot plot, string symbol, double xStartOADate, double yStart, ScottPlot.Color color)
    {
        var v = plot.Plot.Add.VerticalLine(xStartOADate);
        v.Color = color;
        v.LinePattern = ScottPlot.LinePattern.Dashed;
        v.LineWidth = 1;

        double leftOffsetDays = (_intervalSec * 0.35) / 86400.0; // ~35% of interval to the left
        var label = plot.Plot.Add.Text(symbol, xStartOADate - leftOffsetDays, yStart);
        label.Alignment = ScottPlot.Alignment.MiddleRight;
        label.Color = color;
        label.FontSize = 9;

        return (v, label);
    }

    // auto-focus logic respecting user zoom; Y initializes once per plot
    private void ApplyAxes(
        ScottPlot.WinForms.FormsPlot plot,
        DateTime snapshotTime,
        List<(string Symbol, double[] Xs, double[] Ys)> series,
        bool isUp)
    {
        // Only step X when auto-focus is ON and the user hasn’t adjusted this plot
        bool userAdjusted = isUp ? _userAdjustedUp : _userAdjustedDown;
        if (!_autoFocusEnabled || userAdjusted)
            return;

        double spanSec = Math.Max(1, _historyPoints * _intervalSec);
        double minX, maxX;

        if (_focusMode.Equals("Right", StringComparison.OrdinalIgnoreCase))
        {
            maxX = snapshotTime.ToOADate();
            minX = snapshotTime.AddSeconds(-spanSec).ToOADate();
        }
        else // Center
        {
            double half = spanSec / 2.0;
            minX = snapshotTime.AddSeconds(-half).ToOADate();
            maxX = snapshotTime.AddSeconds(+half).ToOADate();
        }

        plot.Plot.Axes.SetLimitsX(minX, maxX);

        // previous version
        //bool userAdjusted = isUp ? _userAdjustedUp : _userAdjustedDown;

        //// --- X: step-right only when auto-focus is ON and user hasn't adjusted ---
        //if (_autoFocusEnabled && !userAdjusted)
        //{
        //    double spanSec = Math.Max(1, _historyPoints * _intervalSec);
        //    double minX, maxX;
        //    if (_focusMode.Equals("Right", StringComparison.OrdinalIgnoreCase))
        //    {
        //        maxX = snapshotTime.ToOADate();
        //        minX = snapshotTime.AddSeconds(-spanSec).ToOADate();
        //    }
        //    else
        //    {
        //        double half = spanSec / 2.0;
        //        minX = snapshotTime.AddSeconds(-half).ToOADate();
        //        maxX = snapshotTime.AddSeconds(+half).ToOADate();
        //    }
        //    plot.Plot.Axes.SetLimitsX(minX, maxX);
        //}

        //// --- Y: initialize ONCE per plot using the FIRST value of each visible series ---
        //bool yInitialized = isUp ? _yInitializedUp : _yInitializedDown;
        //if (!yInitialized)
        //{
        //    var firstYs = new List<double>(series.Count);
        //    foreach (var (_, _, ys) in series)
        //        if (ys.Length > 0) firstYs.Add(ys[0]);

        //    if (firstYs.Count == 0)
        //    {
        //        // no real data yet: narrow symmetric band
        //        plot.Plot.Axes.SetLimitsY(-1, +1);
        //        if (isUp) _yInitializedUp = true; else _yInitializedDown = true;
        //        return;
        //    }

        //    double minFirst = firstYs.Min();
        //    double maxFirst = firstYs.Max();
        //    double spread = Math.Abs(maxFirst - minFirst);

        //    double minY, maxY;
        //    if (spread < 0.5) // low first % → zoom in tightly
        //    {
        //        double mid = (maxFirst + minFirst) / 2.0;
        //        double half = Math.Max(0.5, spread * 0.75); // at least ±0.5%
        //        minY = mid - half;
        //        maxY = mid + half;
        //    }
        //    else
        //    {
        //        double pad = Math.Max(0.2, spread * 0.10);  // 10% pad, ≥0.2%
        //        minY = minFirst - pad;
        //        maxY = maxFirst + pad;
        //    }

        //    if (Math.Abs(maxY - minY) < 1e-6) { minY -= 1.0; maxY += 1.0; }
        //    plot.Plot.Axes.SetLimitsY(minY, maxY);

        //    if (isUp) _yInitializedUp = true; else _yInitializedDown = true;
        //}
    }



    // ======= CLEANUP =======
    /// <summary>Called when the form is really closing (app exit). Clears plots and disposes controls.</summary>
    public void CleanupAndDispose()
    {
        try
        {
            // Detach handlers to avoid callbacks during dispose
            try
            {
                if (_onToggleConfig is not null) _toggleConfig.LinkClicked -= _onToggleConfig;
                if (_onShowTextChanged is not null) _chkShowText.CheckedChanged -= _onShowTextChanged;
                if (_onAutoFocusChanged is not null) _chkAutoFocus.CheckedChanged -= _onAutoFocusChanged;
                if (_onFocusModeChanged is not null) _cmbFocusMode.SelectedIndexChanged -= _onFocusModeChanged;
                if (_onResetUp is not null) _btnResetUp.Click -= _onResetUp;
                if (_onResetDown is not null) _btnResetDown.Click -= _onResetDown;
            }
            catch { /* ignore */ }

            //// Detach handlers to avoid callbacks during dispose
            //_toggleConfig.LinkClicked -= (_, __) => ToggleConfigPanel();
            //_chkShowText.CheckedChanged -= (_, __) => ToggleTextTables(_chkShowText.Checked);
            //_chkAutoFocus.CheckedChanged += (_, __) =>
            //{
            //    _autoFocusEnabled = _chkAutoFocus.Checked;

            //    if (_autoFocusEnabled)
            //    {
            //        // user explicitly re-enabled tracking → clear manual flags and autoscale now
            //        _userAdjustedUp = _userAdjustedDown = false;

            //        // mark Y as "not initialized" so we will lock it right after autoscale
            //        _yInitializedUp = _yInitializedDown = false;

            //        // If we already have lines, autoscale both plots immediately
            //        try { _plotUp.Plot.Axes.AutoScale(); _plotUp.Refresh(); } catch { }
            //        try { _plotDown.Plot.Axes.AutoScale(); _plotDown.Refresh(); } catch { }

            //        // lock Y after this autoscale (we only autoscale Y once)
            //        _yInitializedUp = _yInitializedDown = true;
            //    }
            //    else
            //    {
            //        // nothing else to do; ApplyAxes() won’t move X when disabled
            //    }
            //};
            //_cmbFocusMode.SelectedIndexChanged -= (_, __) => _focusMode = _cmbFocusMode.SelectedItem?.ToString() ?? "Center";
            //_btnResetUp.Click -= (_, __) => ResetView(_plotUp, isUp: true);
            //_btnResetDown.Click -= (_, __) => ResetView(_plotDown, isUp: false);

            // Clear plots and remove all plottables
            void ClearPlot(FormsPlot fp)
            {
                try { fp.Plot.Clear(); } catch { }
                try { fp.Refresh(); } catch { }
            }
            ClearPlot(_plotUp);
            ClearPlot(_plotDown);

            _lineUp.Clear();
            _lineDown.Clear();
            _histUp.Clear();
            _histDown.Clear();
            _startMarkersUp.Clear();
            _startMarkersDown.Clear();
            _startLabelsUp.Clear();
            _startLabelsDown.Clear();

            // Dispose child controls (Form.Dispose will also do this, but explicit is fine)
            try { _plotUp.Dispose(); } catch { }
            try { _plotDown.Dispose(); } catch { }
            try { _gridUp.Dispose(); } catch { }
            try { _gridDown.Dispose(); } catch { }
            try { _configPanel?.Dispose(); } catch { }
        }
        catch { /* ignore */ }
        finally
        {
            try { base.Dispose(true); } catch { }
        }
    }
}