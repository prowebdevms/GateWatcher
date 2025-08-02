using System.Text.Json.Serialization;

namespace GateWatcher.Models
{
    // Currency pair metadata from GET /spot/currency_pairs (see docs). :contentReference[oaicite:5]{index=5}
    public sealed class CurrencyPairInfo
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("base")] public string Base { get; set; } = "";
        [JsonPropertyName("quote")] public string Quote { get; set; } = "";
        [JsonPropertyName("trade_status")] public string TradeStatus { get; set; } = "";
        [JsonPropertyName("buy_start")] public long BuyStart { get; set; }
        [JsonPropertyName("sell_start")] public long SellStart { get; set; }
        [JsonPropertyName("delisting_time")] public long DelistingTime { get; set; }
        [JsonPropertyName("st_tag")] public bool? StTag { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
    }

}
