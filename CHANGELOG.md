# Changelog

## 1.1.0

Refactor to the **Repository** and **Unit of Work** patterns, and full use of the
previously under-utilized models in `StatsDtos.cs` / `ProxyLogEntry`.

### Highlights
- Data access no longer injects `AppDbContext` directly. Controllers, the
  suspicious-activity detector, the blocklist updater and the worker now depend
  on `IUnitOfWork` and aggregate repositories. A single `SaveChangesAsync`
  commits all staged changes in one transaction.
- Aggregations run as SQL `GROUP BY` inside the repositories and return compact
  read models (`PiProxyGuard.Domain.Statistics`), keeping EF Core / `IQueryable`
  out of the API and worker layers.
- The fields the parser already captured but nothing exposed — `Method`,
  `ContentType`, `ResultCode`, `ElapsedMs` and the `LogIngestionState`
  bookmark — now power five new API endpoints.

### New API endpoints
- `GET /api/stats/methods` — HTTP method distribution (`Method`)
- `GET /api/stats/content-types` — response content-type distribution (`ContentType`)
- `GET /api/stats/result-codes` — Squid result-code distribution (`ResultCode`)
- `GET /api/stats/performance` — slowest hosts by average latency (`ElapsedMs`)
- `GET /api/stats/ingestion` — log ingestion bookmarks (`LogIngestionState`)

### New files
- `src/PiProxyGuard.Domain/Statistics/StatisticsReadModels.cs`
- `src/PiProxyGuard.Domain/Abstractions/IUnitOfWork.cs`
- `src/PiProxyGuard.Domain/Abstractions/Repositories/IRepository.cs`
- `src/PiProxyGuard.Domain/Abstractions/Repositories/IProxyLogRepository.cs`
- `src/PiProxyGuard.Domain/Abstractions/Repositories/IBlockedDomainRepository.cs`
- `src/PiProxyGuard.Domain/Abstractions/Repositories/IAlertRepository.cs`
- `src/PiProxyGuard.Domain/Abstractions/Repositories/ILogIngestionStateRepository.cs`
- `src/PiProxyGuard.Infrastructure/Persistence/UnitOfWork.cs`
- `src/PiProxyGuard.Infrastructure/Persistence/Repositories/Repository.cs`
- `src/PiProxyGuard.Infrastructure/Persistence/Repositories/ProxyLogRepository.cs`
- `src/PiProxyGuard.Infrastructure/Persistence/Repositories/BlockedDomainRepository.cs`
- `src/PiProxyGuard.Infrastructure/Persistence/Repositories/AlertRepository.cs`
- `src/PiProxyGuard.Infrastructure/Persistence/Repositories/LogIngestionStateRepository.cs`
- `tests/PiProxyGuard.Tests/ProxyLogRepositoryTests.cs`
- `CHANGELOG.md`

### Updated files
- `Directory.Build.props` — version bumped to `1.1.0`
- `src/PiProxyGuard.Api/Contracts/StatsDtos.cs` — new DTOs for the five endpoints
- `src/PiProxyGuard.Api/Controllers/StatsController.cs` — uses `IUnitOfWork`; five new actions
- `src/PiProxyGuard.Api/Controllers/AlertsController.cs` — uses `IUnitOfWork`
- `src/PiProxyGuard.Api/Controllers/BlocklistController.cs` — uses `IUnitOfWork`
- `src/PiProxyGuard.Infrastructure/DependencyInjection.cs` — registers repositories + UoW
- `src/PiProxyGuard.Infrastructure/Detection/SuspiciousActivityDetector.cs` — uses `IUnitOfWork`
- `src/PiProxyGuard.Infrastructure/Blocklists/BlocklistUpdater.cs` — uses `IUnitOfWork`
- `src/PiProxyGuard.Worker/Services/LogIngestionService.cs` — uses `IUnitOfWork`
- `tests/PiProxyGuard.Tests/SuspiciousActivityDetectorTests.cs` — constructs the detector via `UnitOfWork`
- `README.md` — documents the pattern and new endpoints

### Compatibility
- No database schema change — existing migrations and SQLite files keep working.
- All existing endpoints keep their routes and response shapes.
- Build is clean with `TreatWarningsAsErrors`; **40** unit tests pass (36 existing + 4 new).

## 1.0.0
- Initial release.
