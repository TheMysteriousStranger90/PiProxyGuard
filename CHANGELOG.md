# Changelog

## 1.3.0 — Dashboard

A UI release. Everything the REST API exposes is now available in a built-in
**Blazor Server** web dashboard, served from the API process — no separate
frontend, no Node build. The same release adds a Docker Hub publish pipeline.

### Web dashboard (new)
- **Separate `PiProxyGuard.Web` project** — the dashboard is a Razor Class
  Library (components, view models, realtime) hosted in-process by the API
  through `AddPiProxyGuardWeb()` / `MapPiProxyGuardWeb()`. One process/port, but
  UI and REST API are separate assemblies.
- **Blazor Server UI** at `http://<pi>:5080/`, dark and responsive, with six
  pages: Dashboard, Traffic, Blocklist, Allowlist, Alerts and Diagnostics.
- **Dashboard** — live summary cards (requests, denied, devices, hosts), top
  destinations & devices, open alerts and a system-health panel, with a
  1h/24h/7d/30d range picker.
- **Traffic** — traffic by category and by HTTP status code (as bars) plus the
  full top-destinations table.
- **Blocklist / Allowlist** — browse, search and filter; block/allow a domain
  (optional TTL on blocks), remove entries and refresh feeds — all inline, with
  the Squid ACL rewritten on every change.
- **Alerts** — acknowledge alerts in one click; new alerts appear live as
  toast notifications.
- **Diagnostics** — the self-check report rendered as a status list.

### Real-time
- **SignalR hub** at `/hubs/dashboard` plus a single `LiveMonitorBackgroundService`
  heartbeat (5 s) that detects new alerts written by the Worker, pushes them to
  every open page and SignalR client, and triggers a data refresh — no
  per-page polling.
- **`DashboardService`** — a scope-per-call facade over the Unit of Work so a
  Blazor circuit never shares a DbContext across overlapping renders.

### Operability
- **Docker Hub pipeline** — `.github/workflows/dockerhub-publish.yml` builds and
  pushes the multi-arch (`amd64`+`arm64`) API and Worker images to Docker Hub on
  every `v*.*.*` tag (secrets: `DOCKERHUB_USERNAME`, `DOCKERHUB_TOKEN`). The
  existing GHCR pipeline is unchanged.

### Security
- The optional API key now guards **only `/api/*`**; the dashboard, its SignalR
  hub and `/health` · `/swagger` · `/metrics` stay open on the trusted LAN.

### Tests
- New `DashboardServiceTests` (read paths over a real SQLite + DI container) and
  `FormatTests`. **81 tests**, 0 warnings (analyzers as errors).

## 1.2.0 — Protect & Reliability

A protection- and reliability-focused release. The detector gets smarter, the
ACL writer gets safer, and a set of new endpoints make the box easier to
operate, back up and monitor.

### Protection
- **Allowlist** — domains that must never be blocked. The allowlist always
  overrides the blocklist and feeds, and rewrites the Squid ACL on every change.
  `GET/POST/DELETE /api/allowlist`.
- **Auto-block with TTL** — suspicious hosts (and manual blocks) can carry an
  expiry; the updater sweeps and releases expired auto-blocks every cycle so
  temporary blocks self-heal. `expiresInHours` on `POST /api/blocklist`.
- **Per-client profiles** — tighten or loosen detection thresholds per device or
  subnet (exact IP or CIDR), by explicit override or a strictness multiplier.
- **Traffic-spike detection** — flags a client whose request rate jumps well
  above its own recent baseline (configurable multiplier / minimum).
- **DGA / suspicious-domain detection** — Shannon-entropy analysis of the
  significant domain label flags algorithmically-generated hostnames, with
  optional auto-blocking.
- **Notifications** — Telegram and SMTP e-mail channels behind a dispatcher;
  new alerts are pushed to every configured channel (respecting a minimum
  severity). Each channel self-disables until configured.
- **Threat-intel lookup** — pluggable interface with a free URLhaus client
  (VirusTotal / AbuseIPDB reserved behind the same interface).
  `GET /api/threat-intel/check?domain=`.

### Reliability
- **ACL backup + rollback** — the previous ACL is backed up before every
  rewrite; if `squid -k reconfigure` fails the file is restored automatically,
  so a malformed blocklist can never take the proxy down.
- **Self-diagnostics** — database, log-ingestion freshness, blocklist and ACL
  checks. `GET /api/diagnostics` (503 when unhealthy) and a `/health/ready`
  readiness probe.

### Operability
- **Backup / restore** — export manual + auto blocklist entries and the full
  allowlist as portable JSON, and re-import them. `GET /api/backup/export`,
  `POST /api/backup/import`.
- **Digest reports** — a human-readable traffic-and-security summary for a
  period. `GET /api/reports/digest?period=day|week|month`.
- **Domain categorization** — offline rule-based classification (ads, tracking,
  social, streaming, CDN, malware). `GET /api/stats/categories`.
- **Prometheus metrics** — per-request HTTP metrics at `GET /metrics`
  (excluded from API-key auth so it is scrapeable).
- **Docker (multi-arch)** — `Dockerfile.api`, `Dockerfile.worker` and a
  `docker-compose.yml` that brings up Squid + Worker + API with shared volumes.
  Images are cross-built for `linux/amd64` + `linux/arm64` (Raspberry Pi) via a
  `docker-bake.hcl`, a `scripts/build-multiarch.sh` helper and a
  `docker-publish.yml` GitHub Actions workflow that pushes to GHCR on each tag.

### New API endpoints
- `GET/POST/DELETE /api/allowlist`
- `GET /api/stats/categories`
- `GET /api/diagnostics`, `GET /health/ready`
- `GET /api/reports/digest`
- `GET /api/backup/export`, `POST /api/backup/import`
- `GET /api/threat-intel/check`
- `GET /metrics` (Prometheus)
- `POST /api/blocklist` now accepts `expiresInHours`

### Data
- Migration `AddAllowlistAndAutoBlockTtl`: new `AllowedDomains` table and a
  nullable `ExpiresAtUtc` column on `BlockedDomains` (plus supporting indexes).
  Applied automatically on startup.

### Tests
- 64 tests pass (was 40). New coverage: DGA entropy, domain categorizer,
  client-profile resolver, notification dispatcher fan-out, ACL backup/rollback,
  allowlist repository, auto-block expiry sweep and DGA auto-blocking.

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
