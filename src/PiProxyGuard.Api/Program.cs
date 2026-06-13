using Microsoft.EntityFrameworkCore;
using Prometheus;
using PiProxyGuard.Api.Middleware;
using System.Threading.RateLimiting;
using PiProxyGuard.Infrastructure;
using PiProxyGuard.Web;
using PiProxyGuard.Infrastructure.Diagnostics;
using PiProxyGuard.Infrastructure.Persistence;
using PiProxyGuard.Infrastructure.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPiProxyGuardInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSystemd();

// Blazor Server dashboard (hosted from the separate PiProxyGuard.Web library).
builder.Services.AddPiProxyGuardWeb();

// 1.4.0: optional hardening for the REST API, both off by default.
var apiOptions = builder.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>()
    ?? new ApiOptions();

// Fixed-window rate limiter on /api/* (per client IP). Other paths (dashboard,
// SignalR, /health, /metrics, /swagger) are never limited.
if (apiOptions.RateLimitPerMinute > 0)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (!context.Request.Path.StartsWithSegments("/api"))
            {
                return RateLimitPartition.GetNoLimiter("unlimited");
            }

            var clientKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(clientKey, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = apiOptions.RateLimitPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
        });
    });
}

var app = builder.Build();

// Apply EF Core migrations on startup so API and Worker can start in any order.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}

// Optional HTTPS: HSTS + HTTP->HTTPS redirect (requires an HTTPS endpoint/cert).
if (apiOptions.UseHttpsRedirection)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Optional rate limiting (only registered when RateLimitPerMinute > 0).
if (apiOptions.RateLimitPerMinute > 0)
{
    app.UseRateLimiter();
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
