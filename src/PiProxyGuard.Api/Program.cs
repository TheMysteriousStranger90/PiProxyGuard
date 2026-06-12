using Microsoft.EntityFrameworkCore;
using PiProxyGuard.Api.Middleware;
using PiProxyGuard.Infrastructure;
using PiProxyGuard.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPiProxyGuardInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSystemd();

var app = builder.Build();

// Apply EF Core migrations on startup so API and Worker can start in any order.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<ApiKeyMiddleware>();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", timeUtc = DateTime.UtcNow }));

app.Run();
