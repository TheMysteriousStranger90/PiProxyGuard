using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using PiProxyGuard.Web.Components;
using PiProxyGuard.Web.Realtime;
using PiProxyGuard.Web.Services;

namespace PiProxyGuard.Web;

/// <summary>
/// Wires the Blazor Server dashboard into a host application (PiProxyGuard.Api).
/// Keeping registration and endpoint mapping here means the host only needs two
/// calls — <see cref="AddPiProxyGuardWeb"/> and <see cref="MapPiProxyGuardWeb"/> —
/// and the UI's internals stay encapsulated in this library.
/// </summary>
public static class WebHostingExtensions
{
    /// <summary>Registers Razor components, SignalR, the dashboard facade and the live-update heartbeat.</summary>
    public static IServiceCollection AddPiProxyGuardWeb(this IServiceCollection services)
    {
        services.AddRazorComponents().AddInteractiveServerComponents();
        services.AddSignalR();
        services.AddSingleton<DashboardService>();
        services.AddSingleton<LiveUpdateNotifier>();
        services.AddHostedService<LiveMonitorBackgroundService>();
        return services;
    }

    /// <summary>Maps the dashboard root component and the SignalR hub. Call after the host's other endpoints.</summary>
    public static WebApplication MapPiProxyGuardWeb(this WebApplication app)
    {
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        app.MapHub<DashboardHub>("/hubs/dashboard");
        return app;
    }
}
