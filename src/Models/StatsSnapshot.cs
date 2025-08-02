namespace GateWatcher.Models
{
    public sealed record StatsSnapshot(IReadOnlyList<StatRow> TopUp, IReadOnlyList<StatRow> TopDown, DateTime LocalTime);
}
