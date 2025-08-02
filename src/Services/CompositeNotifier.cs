using GateWatcher.Models;

namespace GateWatcher.Services
{
    public sealed class CompositeNotifier(IEnumerable<INotifier> notifiers) : INotifier
    {
        private readonly List<INotifier> _notifiers = [.. notifiers];
        public Task NotifyAsync(string title, string message, CancellationToken ct)
            => Task.WhenAll(_notifiers.Select(n => n.NotifyAsync(title, message, ct)));
    }
}
