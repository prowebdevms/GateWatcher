using GateWatcher.Models;
using System.Net.Http.Json;

namespace GateWatcher.Services
{
    public sealed class TelegramNotifier(Func<AppConfig> cfgProvider, HttpClient http) : INotifier
    {
        public async Task NotifyAsync(string title, string message, CancellationToken ct)
        {
            var cfg = cfgProvider();
            if (!cfg.Notifications.Telegram.Enabled) return;
            if (string.IsNullOrWhiteSpace(cfg.Notifications.Telegram.BotToken) ||
                string.IsNullOrWhiteSpace(cfg.Notifications.Telegram.ChatId)) return;

            var url = $"https://api.telegram.org/bot{cfg.Notifications.Telegram.BotToken}/sendMessage";
            var payload = new { chat_id = cfg.Notifications.Telegram.ChatId, text = $"*{title}*\n{message}", parse_mode = "Markdown" };
            using var resp = await http.PostAsJsonAsync(url, payload, ct);
            resp.EnsureSuccessStatusCode();
        }
    }
}
