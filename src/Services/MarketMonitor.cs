using GateWatcher.Models;
using System.Collections.Concurrent;
using System.Globalization;

namespace GateWatcher.Services
{
    public sealed class MarketMonitor
    {
        private readonly ConfigManager _config;
        private readonly GateIoClient _client;
        private readonly INotifier _notifier;
        private readonly CsvLogger _csv;

        private readonly ConcurrentDictionary<string, FixedWindow> _windows = new();
        private readonly ConcurrentDictionary<string, CurrencyPairInfo> _pairs = new();

        private volatile bool _restartTimer;
        private DateTime _lastPairsRefresh = DateTime.MinValue;

        private readonly Func<bool> _uiIsVisible;

        public event Action<StatsSnapshot>? StatsUpdated;

        public MarketMonitor(ConfigManager config, GateIoClient client, INotifier notifier, CsvLogger csv, Func<bool> uiIsVisible)
        {
            _config = config;
            _client = client;
            _notifier = notifier;
            _csv = csv;
            _uiIsVisible = uiIsVisible;
            _config.OnChanged += _ => _restartTimer = true;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var interval = Math.Max(1, _config.Current.Polling.PollIntervalSeconds);
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
                _restartTimer = false;

                do
                {
                    try
                    {
                        await EnsurePairsAsync(ct);
                        await PollOnce(ct);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[poll] Error: {ex.Message}");
                    }
                }
                while (!_restartTimer && await timer.WaitForNextTickAsync(ct));
            }
        }

        private async Task EnsurePairsAsync(CancellationToken ct)
        {
            if (_pairs.IsEmpty || (DateTime.UtcNow - _lastPairsRefresh) > TimeSpan.FromMinutes(10))
            {
                var list = await _client.GetAllCurrencyPairsAsync(ct);
                _pairs.Clear();
                foreach (var p in list)
                    if (!string.IsNullOrWhiteSpace(p.Id))
                        _pairs[p.Id] = p;
                _lastPairsRefresh = DateTime.UtcNow;
                Console.WriteLine($"[pairs] Loaded {list.Count} currency pairs");
            }
        }

        private async Task PollOnce(CancellationToken ct)
        {
            var cfg = _config.Current;
            var list = await _client.GetAllSpotTickersAsync(ct);

            // compute lookback steps and enforce window size
            int steps = Math.Max(1, (int)Math.Round((double)cfg.Polling.LookbackSeconds / Math.Max(1, cfg.Polling.PollIntervalSeconds)));
            int requiredWindow = steps + 1;
            if (cfg.Polling.SamplesWindow < requiredWindow)
            {
                _config.Save(c => c.Polling.SamplesWindow = requiredWindow);
                Console.WriteLine($"[detect] Auto-set SamplesWindow={requiredWindow} (steps={steps})");
            }
            else if (cfg.Polling.LookbackSeconds % cfg.Polling.PollIntervalSeconds != 0)
            {
                Console.WriteLine($"[detect] Lookback {cfg.Polling.LookbackSeconds}s not multiple of interval {cfg.Polling.PollIntervalSeconds}s; using {steps} steps (~{steps * cfg.Polling.PollIntervalSeconds}s).");
            }

            int seen = 0, passed = 0;
            var displayRows = new List<(string Pair, double ChangePct, double QuoteVol, double LastPrice)>(1024);

            foreach (var t in list)
            {
                if (string.IsNullOrWhiteSpace(t.CurrencyPair)) continue;
                seen++;

                if (!PassesBasicLists(cfg, t.CurrencyPair)) continue;
                if (!_pairs.TryGetValue(t.CurrencyPair, out var meta)) continue;
                if (!PassesAdvancedFilters(cfg, meta, t)) continue;

                passed++;

                double change = ParseDbl(t.ChangePercentage);
                double last = ParseDbl(t.Last);
                double qv = ParseDbl(t.QuoteVolume);

                displayRows.Add((t.CurrencyPair, change, qv, last));

                var win = _windows.GetOrAdd(t.CurrencyPair, _ => new FixedWindow(cfg.Polling.SamplesWindow));
                win.SetWindowSize(cfg.Polling.SamplesWindow);
                var values = win.Push(change);

                if (values.Count >= requiredWindow)
                {
                    int lastIdx = values.Count - 1;
                    int lookbackIdx = lastIdx - steps;
                    if (lookbackIdx >= 0)
                    {
                        var newest = values[lastIdx];
                        var lookback = values[lookbackIdx];
                        var diff = newest - lookback;

                        bool upHit = diff >= cfg.Thresholds.IncreaseThresholdPercent;
                        bool downHit = diff <= -cfg.Thresholds.DecreaseThresholdPercent;

                        if (upHit || downHit)
                        {
                            var direction = upHit ? "↑ increase" : "↓ decrease";
                            var msg =
                                $"{t.CurrencyPair} | " + // ({meta.Base}/{meta.Quote}) 
                                $"24h% now={newest:0.##}, {cfg.Polling.LookbackSeconds}s-ago={lookback:0.##}, Δ={diff:0.##}% " +
                                $"| last={last.ToString("0.########", CultureInfo.InvariantCulture)} " +
                                $"| vol24h(quote)={qv:0.###}";

                            await _notifier.NotifyAsync($"Gate.io change alert ({direction})", msg, ct);
                        }
                    }
                }
            }

            // ---- Stats: Top 10 ↑ and ↓ by 24h % ----
            var localNow = DateTime.Now;
            var topUp = displayRows.OrderByDescending(r => r.ChangePct).Take(10).ToList();
            var topDown = displayRows.OrderBy(r => r.ChangePct).Take(10).ToList();

            if (!_uiIsVisible())
            {
                PrintTable("Top 10 - 24H % increase", topUp, localNow);
                PrintTable("Top 10 - 24H % decrease", topDown, localNow);
            }

            // CSV logging
            var csvRows = new List<string[]>();
            int rank = 1;
            foreach (var r in topUp)
            {
                csvRows.Add(new[] {
                localNow.ToString("yyyy-MM-dd HH:mm:ss"),
                "top_up",
                rank.ToString(),
                r.Pair,
                r.LastPrice.ToString("0.########", CultureInfo.InvariantCulture),
                r.QuoteVol.ToString("0.###", CultureInfo.InvariantCulture),
                r.ChangePct.ToString("0.##", CultureInfo.InvariantCulture)
            });
                rank++;
            }
            rank = 1;
            foreach (var r in topDown)
            {
                csvRows.Add(new[] {
                localNow.ToString("yyyy-MM-dd HH:mm:ss"),
                "top_down",
                rank.ToString(),
                r.Pair,
                r.LastPrice.ToString("0.########", CultureInfo.InvariantCulture),
                r.QuoteVol.ToString("0.###", CultureInfo.InvariantCulture),
                r.ChangePct.ToString("0.##", CultureInfo.InvariantCulture)
            });
                rank++;
            }
            _csv.AppendMany(csvRows);

            // Push snapshot to GUI
            var snapshot = new StatsSnapshot(
                topUp.Select(r => new StatRow(r.Pair, r.ChangePct, r.QuoteVol, r.LastPrice, localNow)).ToList(),
                topDown.Select(r => new StatRow(r.Pair, r.ChangePct, r.QuoteVol, r.LastPrice, localNow)).ToList(),
                localNow
            );
            StatsUpdated?.Invoke(snapshot);

            Console.WriteLine($"[poll] {DateTime.Now:HH:mm:ss} tickers={list.Count}, matched filters={passed}/{seen}");
        }

        private static void PrintTable(string title, List<(string Pair, double ChangePct, double QuoteVol, double LastPrice)> rows, DateTime localNow)
        {
            Console.WriteLine($"\n{title} @ {localNow:HH:mm:ss}");
            Console.WriteLine("Rank  Pair               24h%     QuoteVol(24h)    Last(USDT)");
            int i = 1;
            foreach (var r in rows)
            {
                Console.WriteLine($"{i,4}  {r.Pair,-16}  {r.ChangePct,6:0.##}%  {r.QuoteVol,14:0.###}  {r.LastPrice,12:0.########}");
                i++;
            }
            if (rows.Count == 0) Console.WriteLine("(no matches)");
        }

        private static bool PassesBasicLists(AppConfig cfg, string pair)
        {
            if (cfg.Filters.IncludePairs.Count > 0 && !cfg.Filters.IncludePairs.Contains(pair, StringComparer.OrdinalIgnoreCase))
                return false;
            if (cfg.Filters.ExcludePairs.Contains(pair, StringComparer.OrdinalIgnoreCase))
                return false;
            return true;
        }

        private static double ParseDbl(string s)
            => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.0;

        private static DateTime FromUnix(long sec) => DateTimeOffset.FromUnixTimeSeconds(sec <= 0 ? 0 : sec).UtcDateTime;

        private static bool IsNew(AppConfig cfg, CurrencyPairInfo meta)
        {
            if (!cfg.Filters.NewOnly) return true;
            var since = DateTime.UtcNow.AddDays(-Math.Max(1, cfg.Filters.NewSinceDays));
            var listed = FromUnix(meta.BuyStart);
            return meta.BuyStart > 0 && listed >= since;
        }

        private static bool QuoteAllowed(AppConfig cfg, CurrencyPairInfo meta)
        {
            if (cfg.Filters.QuoteCurrencies.Count == 0) return true;
            return cfg.Filters.QuoteCurrencies.Contains(meta.Quote, StringComparer.OrdinalIgnoreCase);
        }

        private static bool PassesAdvancedFilters(AppConfig cfg, CurrencyPairInfo meta, SpotTicker t)
        {
            if (!QuoteAllowed(cfg, meta)) return false;
            if (!IsNew(cfg, meta)) return false;

            var qv = ParseDbl(t.QuoteVolume);
            var bv = ParseDbl(t.BaseVolume);

            if (cfg.Filters.MinQuoteVolume24h > 0 && qv < cfg.Filters.MinQuoteVolume24h) return false;
            if (cfg.Filters.MinBaseVolume24h > 0 && bv < cfg.Filters.MinBaseVolume24h) return false;

            if (!string.IsNullOrEmpty(meta.TradeStatus) &&
                !meta.TradeStatus.Equals("tradable", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private sealed class FixedWindow
        {
            private readonly LinkedList<double> _q = new();
            private int _size;
            public FixedWindow(int size) => _size = Math.Max(1, size);
            public void SetWindowSize(int size) => _size = Math.Max(1, size);
            public IReadOnlyList<double> Push(double value)
            {
                _q.AddLast(value);
                while (_q.Count > _size) _q.RemoveFirst();
                return [.. _q];
            }
        }
    }
}
