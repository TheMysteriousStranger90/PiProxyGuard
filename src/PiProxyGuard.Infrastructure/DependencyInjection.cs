using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Abstractions.Repositories;
using PiProxyGuard.Infrastructure.Blocklists;
using PiProxyGuard.Infrastructure.Detection;
using PiProxyGuard.Infrastructure.Options;
using PiProxyGuard.Infrastructure.Parsing;
using PiProxyGuard.Infrastructure.Persistence;
using PiProxyGuard.Infrastructure.Persistence.Repositories;
using PiProxyGuard.Infrastructure.Squid;

namespace PiProxyGuard.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers everything the Worker and the Web API share: the SQLite
    /// DbContext, the log parser, blocklist machinery and the detector.
    /// </summary>
    public static IServiceCollection AddPiProxyGuardInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AccessLogOptions>(configuration.GetSection(AccessLogOptions.SectionName));
        services.Configure<BlocklistOptions>(configuration.GetSection(BlocklistOptions.SectionName));
        services.Configure<DetectionOptions>(configuration.GetSection(DetectionOptions.SectionName));
        services.Configure<ApiOptions>(configuration.GetSection(ApiOptions.SectionName));

        var connectionString = configuration.GetConnectionString("Default")
            ?? "Data Source=piproxyguard.db";

        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

        services.AddScoped<IProxyLogRepository, ProxyLogRepository>();
        services.AddScoped<IBlockedDomainRepository, BlockedDomainRepository>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<ILogIngestionStateRepository, LogIngestionStateRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddHttpClient("blocklists", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PiProxyGuard/1.1");
        });

        services.AddSingleton<IProxyLogParser, SquidAccessLogParser>();
        services.AddScoped<SquidAclWriter>();
        services.AddScoped<BlocklistUpdater>();
        services.AddScoped<SuspiciousActivityDetector>();

        return services;
    }
}
