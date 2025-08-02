using System.Text.Json.Serialization;

namespace GateWatcher.Models
{
    // Ticker fields from GET /spot/tickers (see docs). :contentReference[oaicite:4]{index=4}
    public sealed class SpotTicker
    {
        [JsonPropertyName("currency_pair")] public string CurrencyPair { get; set; } = "";
        [JsonPropertyName("last")] public string Last { get; set; } = "0";
        [JsonPropertyName("change_percentage")] public string ChangePercentage { get; set; } = "0";
        [JsonPropertyName("base_volume")] public string BaseVolume { get; set; } = "0";
        [JsonPropertyName("quote_volume")] public string QuoteVolume { get; set; } = "0";
    }

}
