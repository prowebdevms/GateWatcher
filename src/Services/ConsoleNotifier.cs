using GateWatcher.Models;

namespace GateWatcher.Services
{
    public sealed class ConsoleNotifier : INotifier, IAlertSource
    {
        private readonly List<AlertItem> _history = new();
        public event Action<AlertItem>? Alert;

        public IReadOnlyList<AlertItem> History => _history;

        public Task NotifyAsync(string title, string message, CancellationToken ct)
        {
            try { Console.Beep(); } catch { }
            Console.WriteLine($"[ALERT] {title}\n{message}\n");

            // --- FIXED: detect direction from the TITLE, not from '-' in the message ---
            AlertDirectionType dir = AlertDirectionType.Neutral;
            if (title.IndexOf("increase", StringComparison.OrdinalIgnoreCase) >= 0 ||
                title.Contains('↑'))
                dir = AlertDirectionType.Increase;
            else if (title.IndexOf("decrease", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     title.Contains('↓'))
                dir = AlertDirectionType.Decrease;
            else
            {
                // Fallback: try to infer from the Δ sign in the message if present
                var idx = message.IndexOf("Δ=", StringComparison.Ordinal);
                if (idx >= 0 && idx + 2 < message.Length)
                {
                    // skip any whitespace after Δ=
                    int j = idx + 2;
                    while (j < message.Length && char.IsWhiteSpace(message[j])) j++;
                    if (j < message.Length)
                    {
                        if (message[j] == '+') dir = AlertDirectionType.Increase;
                        else if (message[j] == '-') dir = AlertDirectionType.Decrease;
                    }
                }
            }

            var item = new AlertItem
            {
                Time = DateTime.Now,
                Message = message,
                Direction = dir,
            };

            _history.Insert(0, item);
            if (_history.Count > 1000) _history.RemoveAt(_history.Count - 1);

            try { Alert?.Invoke(item); } catch { }

            return Task.CompletedTask;
        }
    }
}
