using Microsoft.EntityFrameworkCore;
using PiProxyGuard.Domain.Entities;

namespace PiProxyGuard.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<ProxyLogEntry> LogEntries => Set<ProxyLogEntry>();
    public DbSet<BlockedDomain> BlockedDomains => Set<BlockedDomain>();
    public DbSet<SuspiciousActivityAlert> Alerts => Set<SuspiciousActivityAlert>();
    public DbSet<LogIngestionState> IngestionStates => Set<LogIngestionState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProxyLogEntry>(builder =>
        {
            builder.Property(e => e.ClientIp).HasMaxLength(45);
            builder.Property(e => e.ResultCode).HasMaxLength(40);
            builder.Property(e => e.Method).HasMaxLength(16);
            builder.Property(e => e.Url).HasMaxLength(2048);
            builder.Property(e => e.Host).HasMaxLength(253);
            builder.Property(e => e.ContentType).HasMaxLength(120);

            builder.HasIndex(e => e.TimestampUtc);
            builder.HasIndex(e => new { e.TimestampUtc, e.ClientIp });
            builder.HasIndex(e => new { e.TimestampUtc, e.Host });
        });

        modelBuilder.Entity<BlockedDomain>(builder =>
        {
            builder.Property(e => e.Domain).HasMaxLength(253);
            builder.Property(e => e.Reason).HasMaxLength(500);
            builder.HasIndex(e => e.Domain).IsUnique();
        });

        modelBuilder.Entity<SuspiciousActivityAlert>(builder =>
        {
            builder.Property(e => e.ClientIp).HasMaxLength(45);
            builder.Property(e => e.Description).HasMaxLength(1000);
            builder.HasIndex(e => e.DetectedAtUtc);
        });

        modelBuilder.Entity<LogIngestionState>(builder =>
        {
            builder.Property(e => e.FilePath).HasMaxLength(1024);
        });
    }
}
