using System.Text;
using System.Text.Json;

namespace GateWatcher.Services
{
    public static class CommandLoop
    {
        public static async Task RunAsync(ConfigManager cfgMgr, IUiController ui, CancellationToken ct)
        {
            PrintHelp();
            while (!ct.IsCancellationRequested)
            {
                Console.Write("> ");
                var line = Console.ReadLine();
                if (line is null) continue;
                var args = SplitArgs(line);
                if (args.Count == 0) continue;
                var cmd = args[0].ToLowerInvariant();

                try
                {
                    switch (cmd)
                    {
                        case "help":
                        case "h":
                            PrintHelp();
                            break;
                        case "exit":
                        case "quit":
                        case "q":
                            Environment.Exit(0);
                            break;
                        case "config":
                            await HandleConfigAsync(cfgMgr, args);
                            ui.SetIntervalSeconds(cfgMgr.Current.Polling.PollIntervalSeconds);
                            break;
                        case "filter":
                            await HandleFilterAsync(cfgMgr, args);
                            break;
                        case "ui":
                            HandleUi(ui, args);
                            break;
                        default:
                            Console.WriteLine("Unknown command. Type 'help' for options.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[cmd] Error: {ex.Message}");
                }

                await Task.Yield();
            }
        }

        private static void HandleUi(IUiController ui, List<string> args)
        {
            if (args.Count < 2) { Console.WriteLine("ui open | ui close | ui toggle"); return; }
            switch (args[1].ToLowerInvariant())
            {
                case "open": ui.Open(); Console.WriteLine("UI opened."); break;
                case "close": ui.Close(); Console.WriteLine("UI hidden."); break;
                case "toggle":
                    // naive toggle: try show (if already visible it will just bring forward)
                    ui.Open(); break;
                default: Console.WriteLine("ui open | ui close | ui toggle"); break;
            }
        }

        private static Task HandleConfigAsync(ConfigManager cfgMgr, List<string> args)
        {
            if (args.Count == 1 || args[1].Equals("show", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(JsonSerializer.Serialize(cfgMgr.Current, new JsonSerializerOptions { WriteIndented = true }));
                return Task.CompletedTask;
            }

            if (args[1].Equals("set", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Count < 4) { Console.WriteLine("Usage: config set <key> <value>"); return Task.CompletedTask; }
                var key = args[2];
                var val = string.Join(' ', args.Skip(3));
                ApplySet(cfgMgr, key, val);
                return Task.CompletedTask;
            }

            if (args[1].Equals("include", StringComparison.OrdinalIgnoreCase))
            {
                var pairs = args.Skip(2).ToList();
                cfgMgr.Save(cfg => cfg.Filters.IncludePairs = pairs);
                Console.WriteLine("IncludePairs updated & saved.");
                return Task.CompletedTask;
            }

            if (args[1].Equals("exclude", StringComparison.OrdinalIgnoreCase))
            {
                var pairs = args.Skip(2).ToList();
                cfgMgr.Save(cfg => cfg.Filters.ExcludePairs = pairs);
                Console.WriteLine("ExcludePairs updated & saved.");
                return Task.CompletedTask;
            }

            Console.WriteLine("config show | config set <key> <value> | config include <pairs...> | config exclude <pairs...>");
            return Task.CompletedTask;
        }

        private static Task HandleFilterAsync(ConfigManager cfgMgr, List<string> args)
        {
            if (args.Count < 2) { Console.WriteLine("Usage: filter <quotes|minqv|minbv|new> ..."); return Task.CompletedTask; }

            var sub = args[1].ToLowerInvariant();
            switch (sub)
            {
                case "quotes":
                    cfgMgr.Save(c => c.Filters.QuoteCurrencies = args.Skip(2).ToList());
                    Console.WriteLine("QuoteCurrencies updated & saved.");
                    break;
                case "minqv":
                    if (args.Count < 3 || !double.TryParse(args[2], out var qv) || qv < 0) { Console.WriteLine("Usage: filter minqv <number>"); break; }
                    cfgMgr.Save(c => c.Filters.MinQuoteVolume24h = qv);
                    Console.WriteLine("MinQuoteVolume24h updated & saved.");
                    break;
                case "minbv":
                    if (args.Count < 3 || !double.TryParse(args[2], out var bv) || bv < 0) { Console.WriteLine("Usage: filter minbv <number>"); break; }
                    cfgMgr.Save(c => c.Filters.MinBaseVolume24h = bv);
                    Console.WriteLine("MinBaseVolume24h updated & saved.");
                    break;
                case "new":
                    if (args.Count < 3) { Console.WriteLine("Usage: filter new <true|false> [days]"); break; }
                    bool newOnly = args[2].Equals("true", StringComparison.OrdinalIgnoreCase) || args[2] == "1" || args[2].Equals("yes", StringComparison.OrdinalIgnoreCase);
                    int days = args.Count >= 4 && int.TryParse(args[3], out var d) ? Math.Max(1, d) : 30;
                    cfgMgr.Save(c => { c.Filters.NewOnly = newOnly; c.Filters.NewSinceDays = days; });
                    Console.WriteLine("NewOnly/NewSinceDays updated & saved.");
                    break;
                default:
                    Console.WriteLine("Usage: filter quotes <QUOTE...> | filter minqv <num> | filter minbv <num> | filter new <true|false> [days]");
                    break;
            }

            return Task.CompletedTask;
        }

        private static void ApplySet(ConfigManager mgr, string key, string value)
        {
            bool Bool(string s) => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1" || s.Equals("yes", StringComparison.OrdinalIgnoreCase);

            if (key.Equals("increase", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(value, out var v)) throw new Exception("Value must be number");
                mgr.Save(c => c.Thresholds.IncreaseThresholdPercent = v);
            }
            else if (key.Equals("decrease", StringComparison.CurrentCultureIgnoreCase))
            {
                if (!double.TryParse(value, out var v)) throw new Exception("Value must be number");
                mgr.Save(c => c.Thresholds.DecreaseThresholdPercent = v);
            }
            else if (key is "interval" or "pollinterval" or "pollintervalseconds")
            {
                if (!int.TryParse(value, out var v) || v < 1) throw new Exception("Value must be integer >= 1");
                mgr.Save(c => c.Polling.PollIntervalSeconds = v);
            }
            else if (key is "lookback" or "lookbackseconds")
            {
                if (!int.TryParse(value, out var v) || v < 1) throw new Exception("Value must be integer >= 1");
                mgr.Save(c => c.Polling.LookbackSeconds = v);
            }
            else if (key is "sampleswindow" or "window")
            {
                if (!int.TryParse(value, out var v) || v < 2) throw new Exception("Value must be integer >= 2");
                mgr.Save(c => c.Polling.SamplesWindow = v);
            }
            else if (key is "telegram.enabled")
            {
                mgr.Save(c => c.Notifications.Telegram.Enabled = Bool(value));
            }
            else if (key is "telegram.token")
            {
                mgr.Save(c => c.Notifications.Telegram.BotToken = value);
            }
            else if (key is "telegram.chatid")
            {
                mgr.Save(c => c.Notifications.Telegram.ChatId = value);
            }
            else
            {
                throw new Exception("Unknown key");
            }
            Console.WriteLine("Saved. Change will apply immediately.");
        }

        private static void PrintHelp()
        {
            Console.WriteLine("""
Commands:
  help                          Show this help
  config show                   Print current configuration
  config set increase <pct>     Set increase threshold percent (e.g., 20)
  config set decrease <pct>     Set decrease threshold percent (e.g., 20)
  config set interval <sec>     Set polling interval seconds (>=1, default 5)
  config set lookback <sec>     Set lookback horizon in seconds (default 20)
  config set window <n>         Set samples window size (>=2); auto-raised to lookback steps + 1

  config include <PAIR...>      Track only these pairs; empty => ALL
  config exclude <PAIR...>      Exclude these pairs

  filter quotes <QUOTE...>      Only pairs with these quote currencies (e.g., USDT USDC); empty => any
  filter minqv <num>            Only pairs with 24h quote_volume >= num
  filter minbv <num>            Only pairs with 24h base_volume  >= num
  filter new <true|false> [d]   “New” only (listed within last d days; default 30)

  ui open                       Show the GUI window
  ui close                      Hide the GUI window
  ui toggle                     Toggle the GUI window

  quit                          Exit
""");
        }

        private static List<string> SplitArgs(string input)
        {
            var res = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            foreach (var ch in input)
            {
                if (ch == '"') { inQuotes = !inQuotes; continue; }
                if (char.IsWhiteSpace(ch) && !inQuotes)
                {
                    if (sb.Length > 0) { res.Add(sb.ToString()); sb.Clear(); }
                }
                else sb.Append(ch);
            }
            if (sb.Length > 0) res.Add(sb.ToString());
            return res;
        }
    }
}
