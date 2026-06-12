using Microsoft.EntityFrameworkCore;
using PiProxyGuard.Infrastructure;
using PiProxyGuard.Infrastructure.Persistence;
using PiProxyGuard.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPiProxyGuardInfrastructure(builder.Configuration);

builder.Services.AddHostedService<LogIngestionService>();
builder.Services.AddHostedService<BlocklistUpdateService>();
builder.Services.AddHostedService<SuspiciousActivityService>();

// Lets systemd track the service state properly (Type=notify).
builder.Services.AddSystemd();

var host = builder.Build();

// Apply EF Core migrations on startup so the SQLite file is always up to date.
using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}

host.Run();
