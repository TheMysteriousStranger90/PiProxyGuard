using Microsoft.Extensions.Options;
using PiProxyGuard.Infrastructure.Options;

namespace PiProxyGuard.Api.Middleware;

/// <summary>
/// Optional API-key protection. When Api:ApiKey is set in configuration,
/// every request to /api/* must send the same value
/// in the X-Api-Key header. Leave the key empty on a trusted LAN.
/// </summary>
public class ApiKeyMiddleware
{
    private const string HeaderName = "X-Api-Key";

    private readonly RequestDelegate _next;
    private readonly string? _apiKey;

    public ApiKeyMiddleware(RequestDelegate next, IOptions<ApiOptions> options)
    {
        _next = next;
        _apiKey = options.Value.ApiKey;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (string.IsNullOrEmpty(_apiKey) || !IsProtectedPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var provided) || provided != _apiKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid API key." });
            return;
        }

        await _next(context);
    }

    // Only the REST API under /api is key-protected. The Blazor dashboard, its
    // SignalR hub and static assets, plus /health, /swagger and /metrics, stay open.
    private static bool IsProtectedPath(PathString path) =>
        path.StartsWithSegments("/api");
}
