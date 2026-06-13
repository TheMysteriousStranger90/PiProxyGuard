using Microsoft.AspNetCore.SignalR;

namespace PiProxyGuard.Web.Realtime;

/// <summary>
/// Public SignalR hub (mapped at <c>/hubs/dashboard</c>) that streams live
/// events to any connected client — the built-in dashboard uses the Blazor
/// Server circuit, but external consumers (a future WASM client, a mobile app,
/// a notifier script) can connect here too. The server pushes two events:
/// <c>alertRaised</c> (a single new alert) and <c>dataChanged</c> (a heartbeat
/// telling clients fresh stats are available).
/// </summary>
public sealed class DashboardHub : Hub
{
}
