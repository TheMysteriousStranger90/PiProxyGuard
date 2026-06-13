using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Abstractions.Repositories;
using PiProxyGuard.Infrastructure.Blocklists;
using PiProxyGuard.Infrastructure.Detection;
using PiProxyGuard.Infrastructure.Diagnostics;
using PiProxyGuard.Infrastructure.Enrichment;
using PiProxyGuard.Infrastructure.Notifications;
using PiProxyGuard.Infrastructure.Options;
using PiProxyGuard.Infrastructure.Parsing;
using PiProxyGuard.Infrastructure.Persistence;
using PiProxyGuard.Infrastructure.Persistence.Repositories;
using PiProxyGuard.Infrastructure.Reports;
using PiProxyGuard.Infrastructure.Squid;
using PiProxyGuard.Infrastructure.ThreatIntel;

namespace PiProxyGuard.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers everything the Worker and the Web API share: the SQLite
    /// DbContext, the log parser, blocklist machinery, the detector, plus the
    /// notification, enrichment, threat-intel, reporting and diagnostics
    /// services introduced in 1.2.0.
    /// </summary>
    public static IServiceCollection AddPiProxyGuardInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AccessLogOptions>(configuration.GetSection(AccessLogOptions.SectionName));
        services.Configure<BlocklistOptions>(configuration.GetSection(BlocklistOptions.SectionName));
        services.Configure<DetectionOptions>(configuration.GetSection(DetectionOptions.SectionName));
        services.Configure<ApiOptions>(configuration.GetSection(ApiOptions.SectionName));
        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.SectionName));
        services.Configure<GeoIpOptions>(configuration.GetSection(GeoIpOptions.SectionName));
        services.Configure<ThreatIntelOptions>(configuration.GetSection(ThreatIntelOptions.SectionName));

        var connectionString = NormalizeSqliteConnectionString(
            configuration.GetConnectionString("Default") ?? "Data Source=piproxyguard.db");

        // WAL + busy_timeout (via the interceptor) let the Worker and the API
        // share one SQLite file without "database is locked" errors during the
        // large blocklist refresh.
        services.AddDbContext<AppDbContext>(options => options
            .UseSqlite(connectionString)
            .AddInterceptors(SqlitePragmaConnectionInterceptor.Instance));

        services.AddScoped<IProxyLogRepository, ProxyLogRepository>();
        services.AddScoped<IBlockedDomainRepository, BlockedDomainRepository>();
        services.AddScoped<IAllowedDomainRepository, AllowedDomainRepository>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<ILogIngestionStateRepository, LogIngestionStateRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddHttpClient("blocklists", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PiProxyGuard/1.2");
        });
        services.AddHttpClient("notifications", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PiProxyGuard/1.2");
        });
        services.AddHttpClient("threatintel", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PiProxyGuard/1.2");
        });

        services.AddSingleton<IProxyLogParser, SquidAccessLogParser>();
        services.AddScoped<SquidAclWriter>();
        services.AddScoped<BlocklistUpdater>();

        // Detection support.
        services.AddSingleton(sp =>
            new ClientProfileResolver(sp.GetRequiredService<IOptions<DetectionOptions>>().Value));
        services.AddScoped<SuspiciousActivityDetector>();

        // Notifications: every channel is registered; each one self-disables
        // until configured, and the dispatcher fans out to the enabled ones.
        services.AddSingleton<INotificationSender, TelegramNotificationSender>();
        services.AddSingleton<INotificationSender, EmailNotificationSender>();
        services.AddSingleton<INotificationDispatcher, NotificationDispatcher>();

        // Enrichment, threat-intel, categorization, reporting, diagnostics.
        services.AddSingleton<IGeoIpResolver, NullGeoIpResolver>();
        services.AddSingleton<IThreatIntelClient, UrlhausThreatIntelClient>();
        services.AddSingleton<IDomainCategorizer, RuleBasedDomainCategorizer>();
        services.AddScoped<DigestReportBuilder>();
        services.AddScoped<SystemDiagnostics>();

        return services;
    }

    /// <summary>
    /// Drops shared-cache mode from a keyword-style SQLite connection string.
    /// Shared cache surfaces "SQLite Error 6: database table is locked" on
    /// cross-connection read/write; WAL plus a private cache is the robust
    /// combination. URI-style data sources (used by the in-memory test
    /// fixtures) are left untouched.
    /// </summary>
    private static string NormalizeSqliteConnectionString(string connectionString)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            if (builder.Cache == SqliteCacheMode.Shared)
            {
                builder.Cache = SqliteCacheMode.Default;
                return builder.ToString();
            }
        }
        catch (ArgumentException)
        {
            // Not a parseable keyword string (e.g. a raw URI) — use as-is.
        }

        return connectionString;
    }
}
