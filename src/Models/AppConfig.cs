namespace GateWatcher.Models
{

    public sealed class AppConfig
    {
        public PollingConfig Polling { get; set; } = new();
        public ThresholdsConfig Thresholds { get; set; } = new();
        public FiltersConfig Filters { get; set; } = new();
        public NotificationsConfig Notifications { get; set; } = new();
        public GateIoConfig GateIo { get; set; } = new();

        /// <summary>
        /// Factory with sane defaults so the app can run on first start without any manual config.
        /// </summary>
        public static AppConfig Default() => new AppConfig
        {
            Polling = new PollingConfig
            {
                // UI shows historyPoints * intervalSec seconds of data
                // (your MainForm binds SamplesWindow to the UI "Window (samples)" control)
                PollIntervalSeconds = 5,     // every 5 seconds
                LookbackSeconds = 20,    // last 20s (last 3–4 polls) for the threshold check
                SamplesWindow = 300,   // keep 300 samples in memory/plot (~25 minutes at 5s)
            },
            Thresholds = new ThresholdsConfig
            {
                IncreaseThresholdPercent = 20.0,   // notify if >= +20% vs 20s ago
                DecreaseThresholdPercent = 20.0,   // notify if <= -20% vs 20s ago
            },
            Filters = new FiltersConfig
            {
                // If both IncludeSymbols and ExcludeSymbols are empty → handle ALL coins
                QuoteCurrencies = new List<string> { "USDT" },  // default quote filter
                MinQuoteVolume24h = 10_000.0,                      // ≥ 10,000 USDT 24h quote volume
                MinBaseVolume24h = 0.0,                           // not used by default
                NewOnly = false,                         // show ALL by default
                NewSinceDays = 7,                             // used only if NewOnly = true
            }
        };
    }

    public sealed class PollingConfig
    {
        public int PollIntervalSeconds { get; set; } = 5;      // default 5s
        public int SamplesWindow { get; set; } = 5;            // autosized >= LookbackSteps+1
        public int LookbackSeconds { get; set; } = 20;         // compare now vs this many seconds ago
    }

    public sealed class ThresholdsConfig
    {
        public double IncreaseThresholdPercent { get; set; } = 20.0;
        public double DecreaseThresholdPercent { get; set; } = 20.0;
    }

    public sealed class FiltersConfig
    {
        public List<string> IncludePairs { get; set; } = new();
        public List<string> ExcludePairs { get; set; } = new();
        public List<string> QuoteCurrencies { get; set; } = new() { "USDT" };
        public double MinQuoteVolume24h { get; set; } = 10000.0;
        public double MinBaseVolume24h { get; set; } = 0.0;
        public bool NewOnly { get; set; } = false;
        public int NewSinceDays { get; set; } = 30;
    }

    public sealed class NotificationsConfig
    {
        public ConsoleConfig Console { get; set; } = new();
        public TelegramConfig Telegram { get; set; } = new();
    }

    public sealed class ConsoleConfig { public bool Enabled { get; set; } = true; }

    public sealed class TelegramConfig
    {
        public bool Enabled { get; set; } = false;
        public string BotToken { get; set; } = "";
        public string ChatId { get; set; } = "";
    }

    public sealed class GateIoConfig
    {
        public string BaseUrl { get; set; } = "https://api.gateio.ws/api/v4";
    }
}
