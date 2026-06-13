using Microsoft.EntityFrameworkCore;
using Prometheus;
using PiProxyGuard.Api.Middleware;
using PiProxyGuard.Infrastructure;
using PiProxyGuard.Web;
using PiProxyGuard.Infrastructure.Diagnostics;
using PiProxyGuard.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPiProxyGuardInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSystemd();

// Blazor Server dashboard (hosted from the separate PiProxyGuard.Web library).
builder.Services.AddPiProxyGuardWeb();

var app = builder.Build();

// Apply EF Core migrations on startup so API and Worker can start in any order.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseStaticFiles();

// Optional API-key protection — guards the REST API only (the dashboard and its
// SignalR hub stay open on the trusted LAN, matching the existing trust model).
app.UseMiddleware<ApiKeyMiddleware>();

// Prometheus: per-request HTTP metrics plus the /metrics scrape endpoint.
app.UseHttpMetrics();

app.UseAntiforgery();

app.MapControllers();
app.MapPiProxyGuardWeb();

app.MapGet("/health", () => Results.Ok(new { status = "ok", timeUtc = DateTime.UtcNow }));
app.MapGet("/health/ready", async (SystemDiagnostics diagnostics, CancellationToken ct) =>
{
    var report = await diagnostics.RunAsync(ct);
    return report.IsHealthy
        ? Results.Ok(new { status = report.Status.ToString(), report.GeneratedAtUtc })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
});
app.MapMetrics();

app.Run();
