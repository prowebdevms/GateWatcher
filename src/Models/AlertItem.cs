namespace GateWatcher.Models
{
    public sealed class AlertItem
    {
        public DateTime Time { get; init; }
        public string Message { get; init; } = "";
        public AlertDirectionType Direction { get; init; }
    }
}
