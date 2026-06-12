using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PiProxyGuard.Infrastructure.Persistence;

/// <summary>
/// Used only by the dotnet-ef tooling to create migrations.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=piproxyguard.db")
            .Options;

        return new AppDbContext(options);
    }
}
