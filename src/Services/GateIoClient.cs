using GateWatcher.Models;
using System.Net.Http.Json;

namespace GateWatcher.Services
{
    public sealed class GateIoClient(HttpClient http, Func<AppConfig> cfgProvider)
    {
        private string Base() => cfgProvider().GateIo.BaseUrl.TrimEnd('/');

        public async Task<List<SpotTicker>> GetAllSpotTickersAsync(CancellationToken ct)
        {
            using var resp = await http.GetAsync($"{Base()}/spot/tickers", ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<List<SpotTicker>>(cancellationToken: ct)
                   ?? [];
        }

        public async Task<List<CurrencyPairInfo>> GetAllCurrencyPairsAsync(CancellationToken ct)
        {
            using var resp = await http.GetAsync($"{Base()}/spot/currency_pairs", ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<List<CurrencyPairInfo>>(cancellationToken: ct)
                   ?? [];
        }
    }
}
