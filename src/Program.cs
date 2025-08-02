using GateWatcher;
using GateWatcher.Models;
using GateWatcher.Services;


// -------------------- Program entry --------------------
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
var configMgr = new ConfigManager(configPath);

using var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
{
    Timeout = TimeSpan.FromSeconds(15)
};

var client = new GateIoClient(http, () => configMgr.Current);

// notifier(s)
var notifiers = new List<INotifier>();
var consoleNotifier = new ConsoleNotifier();    // implements IAlertSource now

if (configMgr.Current.Notifications.Console.Enabled)
{
    notifiers.Add(consoleNotifier);
}

notifiers.Add(new TelegramNotifier(() => configMgr.Current, http));
var notifier = new CompositeNotifier(notifiers);
var csv = new CsvLogger();

// === WinForms UI boot (STA thread) ===
var ui = new UiController(configMgr, consoleNotifier);

var monitor = new MarketMonitor(configMgr, client, notifier, csv, () => ui.IsVisible);

// feed snapshots to UI
monitor.StatsUpdated += snap => ui.PushSnapshot(snap);

// keep UI’s X-step synced with config (poll interval)
configMgr.OnChanged += _ => ui.SetIntervalSeconds(configMgr.Current.Polling.PollIntervalSeconds);
// set at startup:
ui.SetIntervalSeconds(configMgr.Current.Polling.PollIntervalSeconds);

// show on start (optional)
ui.Open();

// feed snapshots to UI
//monitor.StatsUpdated += snap => { if (form is not null) form.UpdateSnapshot(snap); };

try
{
    await Task.WhenAll(
        monitor.RunAsync(cts.Token),
        CommandLoop.RunAsync(configMgr, ui, cts.Token)
    );
}
catch (Exception ex)
{
    Console.WriteLine($"[fatal] {ex}");
}
finally
{
    try { ui?.Dispose(); } catch { }
    try { configMgr?.Dispose(); } catch { }
}