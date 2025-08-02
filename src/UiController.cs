using GateWatcher.Models;
using GateWatcher.Services;

namespace GateWatcher;

public interface IUiController : IDisposable
{
    void Open();
    void Close();   // hide (re-openable)
    void SetIntervalSeconds(int seconds);
    void PushSnapshot(StatsSnapshot snap);
    bool IsVisible { get; }
}

public sealed class UiController : IUiController
{
    private readonly ConfigManager _cfg;
    private readonly IAlertSource? _alerts;
    private bool _alertsHooked = false;
    private Action<AlertItem>? _alertHandler;
    private Thread? _uiThread;
    private MainForm? _form;

    public UiController(ConfigManager cfg, IAlertSource? alerts = null)
    {
        _cfg = cfg;
        _alerts = alerts;
        StartThreadIfNeeded();

        TryHookAlerts();
    }

    private void TryHookAlerts()
    {
        if (_alertsHooked) return;
        if (_alerts is null) return;
        if (_form is null || _form.IsDisposed) return;

        // Push existing history to the info panel once
        foreach (var a in _alerts.History)
            _form.PushAlert(a);

        // Subscribe exactly once for live updates
        _alertHandler = a => OnUI(() => _form?.PushAlert(a));
        _alerts.Alert += _alertHandler;

        _alertsHooked = true;
    }

    private void StartThreadIfNeeded()
    {
        if (_uiThread is { IsAlive: true }) return;

        _uiThread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            _form = new MainForm(_cfg);
            TryHookAlerts();
            Application.Run(_form);
        });
        _uiThread.IsBackground = true;
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();

        SpinWait.SpinUntil(() => _form is not null, 2000);
    }

    private void OnUI(Action action)
    {
        var f = _form;
        if (f is null) return;

        if (!f.IsHandleCreated)
        {
            try { var _ = f.Handle; } catch { }
        }

        try
        {
            if (f.InvokeRequired) f.BeginInvoke(action);
            else action();
        }
        catch { /* ignore during shutdown */ }
    }

    public void Open()
    {
        StartThreadIfNeeded();
        OnUI(() =>
        {
            if (_form is null) return;
            if (!_form.Visible) _form.Show();
            if (_form.WindowState == FormWindowState.Minimized)
                _form.WindowState = FormWindowState.Normal;
            _form.Activate();
            _form.BringToFront();
        });
    }

    public void Close() => OnUI(() => _form?.Hide());

    public bool IsVisible
    {
        get
        {
            var f = _form;
            if (f is null || f.IsDisposed) return false;
            try { if (!f.IsHandleCreated) { var _ = f.Handle; } return f.Visible; }
            catch { return false; }
        }
    }

    public void SetIntervalSeconds(int seconds) => OnUI(() => _form?.SetIntervalSeconds(seconds));

    public void PushSnapshot(StatsSnapshot snap) => OnUI(() => _form?.UpdateSnapshot(snap));

    /// <summary>Dispose UI thread and free all WinForms resources.</summary>
    public void Dispose()
    {
        try
        {
            if (_alertsHooked && _alerts is not null && _alertHandler is not null)
            {
                try { _alerts.Alert -= _alertHandler; } catch { }
                _alertHandler = null;
                _alertsHooked = false;
            }

            var f = _form;
            if (f != null)
            {
                // Run cleanup on UI thread
                OnUI(() =>
                {
                    try { f.CleanupAndDispose(); } catch { }
                    try { f.Close(); } catch { }     // closes the form & ends Application.Run
                });
            }

            // Give the message loop time to exit
            if (_uiThread != null && _uiThread.IsAlive)
            {
                if (!_uiThread.Join(2000))
                {
                    try { _uiThread.Interrupt(); } catch { }
                    try { _uiThread.Join(1000); } catch { }
                }
            }
        }
        catch { /* ignore */ }
        finally
        {
            _form = null;
            _uiThread = null;
        }
    }
}
