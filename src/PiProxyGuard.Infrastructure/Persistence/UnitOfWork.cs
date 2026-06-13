using PiProxyGuard.Domain.Abstractions;
using PiProxyGuard.Domain.Abstractions.Repositories;
using PiProxyGuard.Infrastructure.Persistence.Repositories;

namespace PiProxyGuard.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/>. Owns one
/// <see cref="AppDbContext"/> (its lifetime is managed by the DI scope) and
/// exposes the repositories that share it. Repositories are created lazily
/// so a request that only touches one aggregate pays for only that one.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _dbContext;

    private IProxyLogRepository? _proxyLogs;
    private IBlockedDomainRepository? _blockedDomains;
    private IAllowedDomainRepository? _allowedDomains;
    private IAlertRepository? _alerts;
    private ILogIngestionStateRepository? _ingestionStates;

    public UnitOfWork(AppDbContext dbContext) => _dbContext = dbContext;

    public IProxyLogRepository ProxyLogs => _proxyLogs ??= new ProxyLogRepository(_dbContext);

    public IBlockedDomainRepository BlockedDomains => _blockedDomains ??= new BlockedDomainRepository(_dbContext);

    public IAllowedDomainRepository AllowedDomains => _allowedDomains ??= new AllowedDomainRepository(_dbContext);

    public IAlertRepository Alerts => _alerts ??= new AlertRepository(_dbContext);

    public ILogIngestionStateRepository IngestionStates =>
        _ingestionStates ??= new LogIngestionStateRepository(_dbContext);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
