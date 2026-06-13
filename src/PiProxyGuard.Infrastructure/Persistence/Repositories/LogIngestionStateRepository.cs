using Microsoft.EntityFrameworkCore;
using PiProxyGuard.Domain.Abstractions.Repositories;
using PiProxyGuard.Domain.Entities;

namespace PiProxyGuard.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="ILogIngestionStateRepository"/>.</summary>
public class LogIngestionStateRepository : Repository<LogIngestionState>, ILogIngestionStateRepository
{
    public LogIngestionStateRepository(AppDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<LogIngestionState?> GetByFilePathAsync(
        string filePath, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(s => s.FilePath == filePath, cancellationToken);

    public async Task<IReadOnlyList<LogIngestionState>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Set.OrderBy(s => s.FilePath).ToListAsync(cancellationToken);
}
