namespace GateWatcher.Models
{
    public interface IAlertSource
    {
        event Action<AlertItem>? Alert;
        IReadOnlyList<AlertItem> History { get; }
    }
}
