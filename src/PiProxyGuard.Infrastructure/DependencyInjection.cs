using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
using PiProxyGuard.Infrastructure.Settings;
using PiProxyGuard.Infrastructure.Squid;
using PiProxyGuard.Infrastructure.ThreatIntel;
using PiProxyGuard.Infrastructure.Tunneling;

namespace PiProxyGuard.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers everything the Worker and the Web API share: the SQLite
    /// DbContext, the log parser, blocklist machinery, the detector, plus the
    /// notification, enrichment, threat-intel, reporting and diagnostics services
    /// </summary>
    public static IServiceCollection AddPiProxyGuardInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AccessLogOptions>(configuration.GetSection(AccessLogOptions.SectionName));
        services.Configure<BlocklistOptions>(configuration.GetSection(BlocklistOptions.SectionName));
        services.Configure<TunnelOptions>(configuration.GetSection(TunnelOptions.SectionName));
        services.Configure<DetectionOptions>(configuration.GetSection(DetectionOptions.SectionName));
        services.Configure<ApiOptions>(configuration.GetSection(ApiOptions.SectionName));
        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.SectionName));
        services.Configure<GeoIpOptions>(configuration.GetSection(GeoIpOptions.SectionName));
        services.Configure<ThreatIntelOptions>(configuration.GetSection(ThreatIntelOptions.SectionName));
        services.Configure<ReportOptions>(configuration.GetSection(ReportOptions.SectionName));
        services.Configure<ThreatIntelScanOptions>(configuration.GetSection(ThreatIntelScanOptions.SectionName));

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
        services.AddScoped<ITunneledDomainRepository, TunneledDomainRepository>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<ILogIngestionStateRepository, LogIngestionStateRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Outgoing HTTP clients are wrapped with the standard resilience
        // pipeline (retries with exponential backoff + jitter, per-attempt and
        // total timeouts, and a circuit breaker) so a flaky feed mirror or
        // notification endpoint can never hang or crash the Worker. Every
        // client advertises a dynamic "PiProxyGuard/<version>" User-Agent.
        services.AddHttpClient("blocklists", client =>
        {
            // Blocklist feeds can be large; give the overall request plenty of
            // time and leave the per-attempt/total budgets to the resilience handler.
            client.Timeout = TimeSpan.FromSeconds(150);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }).AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
            // CircuitBreaker.SamplingDuration must be >= 2 x AttemptTimeout.
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
        });
        services.AddHttpClient("notifications", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(40);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }).AddStandardResilienceHandler(options => { options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30); });
        services.AddHttpClient("threatintel", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(40);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        }).AddStandardResilienceHandler(options => { options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30); });

        services.AddSingleton<IProxyLogParser, SquidAccessLogParser>();
        services.AddScoped<SquidAclWriter>();
        services.AddScoped<BlocklistUpdater>();

        // Upstream tunnel: a second domain list + Squid ACL (tunnel_domains.acl)
        // that the cache_peer_access / never_direct rules route through a parent
        // proxy. Edited from the dashboard, seeded by the Worker on startup.
        services.AddScoped<TunnelAclWriter>();
        services.AddScoped<TunnelAclUpdater>();

        // Detection support.
        services.AddSingleton(sp =>
            new ClientProfileResolver(sp.GetRequiredService<IOptions<DetectionOptions>>().Value));
        services.AddScoped<SuspiciousActivityDetector>();

        // Notifications: settings are stored in the database (editable from the
        // dashboard) and cached by a singleton store that a background refresher
        // keeps fresh, so the separate Worker and API processes share one source
        // of truth. Every channel is registered; each one self-disables until
        // configured, and the dispatcher fans out to the enabled ones.
        services.AddSingleton<INotificationSettingsStore, NotificationSettingsStore>();
        services.AddHostedService<NotificationSettingsRefresher>();
        services.AddSingleton<INotificationSender, TelegramNotificationSender>();
        services.AddSingleton<INotificationSender, EmailNotificationSender>();
        services.AddSingleton<INotificationDispatcher, NotificationDispatcher>();

        // Security/integration settings (GeoIP, threat-intel providers, the
        // scheduled digest and the background scan) are likewise stored in the
        // database and editable from the dashboard, cached by a singleton store
        // a background refresher keeps fresh across the Worker and API processes.
        // When no row has been saved the store falls back to the matching
        // appsettings sections, so config-driven installs keep working.
        services.AddSingleton<ISecuritySettingsStore, SecuritySettingsStore>();
        services.AddHostedService<SecuritySettingsRefresher>();

        // Enrichment: the MaxMind GeoLite2-backed resolver reads its database
        // paths from the security settings store and (re)opens the .mmdb files
        // when they change, so GeoIP can be turned on from the dashboard without
        // a restart; with no database configured it returns empty country/ASN data.
        services.AddSingleton<IGeoIpResolver, MaxMindGeoIpResolver>();

        // Threat intel: every provider self-disables until its key is set; the
        // composite fans a lookup out across all enabled providers (URLhaus is
        // free and on by default) and the public IThreatIntelClient resolves to it.
        services.AddSingleton<UrlhausThreatIntelClient>();
        services.AddSingleton<VirusTotalThreatIntelClient>();
        services.AddSingleton<AbuseIpDbThreatIntelClient>();
        services.AddSingleton<IThreatIntelClient>(sp => new CompositeThreatIntelClient(
            new IThreatIntelClient[]
            {
                sp.GetRequiredService<UrlhausThreatIntelClient>(),
                sp.GetRequiredService<VirusTotalThreatIntelClient>(),
                sp.GetRequiredService<AbuseIpDbThreatIntelClient>(),
            }));

        services.AddSingleton<IDomainCategorizer, RuleBasedDomainCategorizer>();
        services.AddScoped<DigestReportBuilder>();
        services.AddScoped<SystemDiagnostics>();

        return services;
    }

    /// <summary>
    /// Cached "PiProxyGuard/&lt;version&gt;" User-Agent built from the assembly
    /// informational version (git-hash metadata stripped).
    /// </summary>
    private static readonly string UserAgent = $"PiProxyGuard/{ResolveVersion()}";

    private static string ResolveVersion()
    {
        var assembly = typeof(DependencyInjection).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "1.0";
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
