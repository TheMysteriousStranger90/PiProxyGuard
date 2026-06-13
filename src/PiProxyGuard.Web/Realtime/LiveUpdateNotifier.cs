using PiProxyGuard.Web.Models;

namespace PiProxyGuard.Web.Realtime;

/// <summary>
/// In-process pub/sub the dashboard components subscribe to for live updates.
/// A single background heartbeat (<see cref="LiveMonitorBackgroundService"/>)
/// raises these events; each open Blazor Server circuit handles them and calls
/// <c>StateHasChanged</c>, so updates flow to the browser over the circuit's
/// SignalR transport without any per-component polling.
/// </summary>
public sealed class LiveUpdateNotifier
{
    /// <summary>Raised once for every newly detected alert.</summary>
    public event Action<AlertDto>? AlertRaised;

    /// <summary>Raised on every heartbeat so pages can refresh their data.</summary>
    public event Action? DataChanged;

    public void RaiseAlert(AlertDto alert) => AlertRaised?.Invoke(alert);

    public void RaiseDataChanged() => DataChanged?.Invoke();
}
