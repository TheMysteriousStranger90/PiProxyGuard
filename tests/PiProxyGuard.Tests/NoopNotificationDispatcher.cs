using PiProxyGuard.Domain.Abstractions;

namespace PiProxyGuard.Tests;

/// <summary>Test double: a dispatcher with no channels that delivers nothing.</summary>
internal sealed class NoopNotificationDispatcher : INotificationDispatcher
{
    public bool HasEnabledChannels => false;

    public Task<int> DispatchAsync(NotificationMessage message, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}
