namespace GateWatcher.Models
{
    public sealed record StatRow(string Pair, double ChangePct, double QuoteVol, double LastPrice, DateTime LocalTime);
}
