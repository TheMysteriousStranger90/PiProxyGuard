# PiProxyGuard

Home proxy statistics and protection for a Raspberry Pi, built with **.NET 9**.

Squid (or any proxy writing the Squid native log format) does the proxying; PiProxyGuard does everything around it:

- **Worker Service** tails `access.log`, parses every request and stores it in **SQLite** via **EF Core**
- **Web API** serves traffic statistics: totals, top sites, top devices, hourly timeline, status codes
- **Blocklist updater** downloads fresh blocklists (hosts files or HTML pages parsed with **AngleSharp**) on a schedule, merges them with your manual entries and rewrites the Squid ACL file, then runs `squid -k reconfigure`
- **Suspicious-activity detector** raises alerts for request floods, traffic spikes, repeated denied requests and contacts with blocklisted domains

## Architecture

```
                       ┌──────────────────────────── Raspberry Pi ───────────────────────────┐
 devices ── :3128 ──►  Squid ──► /var/log/squid/access.log                                    │
                       │             │ tail + parse                                           │
                       │             ▼                                                        │
                       │   PiProxyGuard.Worker ───────► SQLite (piproxyguard.db)              │
                       │   • LogIngestionService            ▲                                 │
                       │   • BlocklistUpdateService         │ EF Core                         │
                       │   • SuspiciousActivityService      │                                 │
                       │             │                 PiProxyGuard.Api ◄── :5080 ── you      │
                       │             ▼                 /api/stats, /api/alerts, /api/blocklist│
                       │   /etc/squid/blocked_domains.acl  (+ squid -k reconfigure)           │
                       └──────────────────────────────────────────────────────────────────────┘
```

| Project | Purpose |
|---|---|
| `PiProxyGuard.Domain` | Entities, enums, parser/feed abstractions — no dependencies |
| `PiProxyGuard.Infrastructure` | EF Core + SQLite, Squid log parser, file tailer, blocklist feeds (plain/hosts/HTML via AngleSharp), ACL writer, detector |
| `PiProxyGuard.Worker` | `BackgroundService` host: ingestion, blocklist updates, detection |
| `PiProxyGuard.Api` | ASP.NET Core Web API + Swagger, optional API key |
| `PiProxyGuard.Tests` | xUnit tests for parser, tailer, feeds and detector |

Worker and API are separate systemd services sharing one SQLite file (`Cache=Shared`; both apply migrations on startup, so start order does not matter).

## API overview

| Endpoint | Description |
|---|---|
| `GET /api/stats/summary?fromUtc=&toUtc=` | Requests, bytes, denied count, unique clients/hosts (default: last 24 h) |
| `GET /api/stats/top-hosts?count=20` | Most requested sites |
| `GET /api/stats/top-clients?count=20` | Most active devices by traffic |
| `GET /api/stats/timeline?interval=hour\|day` | Requests/bytes per bucket |
| `GET /api/stats/status-codes` | HTTP status distribution |
| `GET /api/alerts?onlyUnacknowledged=true` | Suspicious-activity alerts |
| `POST /api/alerts/{id}/acknowledge` | Close an alert |
| `GET /api/blocklist?source=Manual&search=ads` | Browse the blocklist |
| `POST /api/blocklist` `{ "domain": "ads.example.com", "reason": "..." }` | Block a domain (ACL rewritten immediately) |
| `DELETE /api/blocklist/{id}` | Unblock |
| `POST /api/blocklist/refresh` | Re-download all feeds now |
| `GET /health` | Liveness probe |

Swagger UI: `http://<pi>:5080/swagger`.

Set `Api:ApiKey` in `appsettings.json` to require an `X-Api-Key` header on every request — recommended if you expose the API outside your LAN (better: keep it behind WireGuard).

## Configuration highlights (`appsettings.json`)

```jsonc
"AccessLog":  { "Path": "/var/log/squid/access.log", "PollIntervalSeconds": 15, "RetentionDays": 90 },
"Blocklist": {
  "UpdateIntervalHours": 12,
  "AclFilePath": "/etc/squid/blocked_domains.acl",
  "ReloadCommand": "squid -k reconfigure",
  "Feeds": [
    { "Url": "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts", "Format": "hosts" },
    { "Url": "https://urlhaus.abuse.ch/downloads/hostfile/", "Format": "hosts" },
    // HTML feeds are parsed with AngleSharp using a CSS selector:
    { "Url": "https://example.com/threats.html", "Format": "html", "CssSelector": "table#threats td.domain" }
  ]
},
"Detection": {
  "IntervalMinutes": 5, "WindowMinutes": 5,
  "MaxRequestsPerWindow": 600, "MaxBytesPerWindow": 524288000, "MaxDeniedPerWindow": 20
}
```

## Deploying to the Raspberry Pi

1. **Install Squid** on the Pi and merge `deploy/squid.conf.sample` into `/etc/squid/squid.conf` (it adds the `dstdomain` ACL pointing at the generated blocklist).

2. **Publish** on your dev machine (no .NET runtime needed on the Pi — builds are self-contained):

   ```bash
   ./deploy/publish.sh                 # linux-arm64 (Raspberry Pi OS 64-bit)
   RID=linux-arm ./deploy/publish.sh   # 32-bit OS
   rsync -av publish/ pi@raspberrypi:/opt/piproxyguard/
   ```

3. **Prepare the Pi**:

   ```bash
   sudo useradd -r -s /usr/sbin/nologin piproxyguard
   sudo usermod -aG proxy piproxyguard                  # read squid logs
   sudo mkdir -p /var/lib/piproxyguard
   sudo chown piproxyguard /var/lib/piproxyguard        # sqlite db lives here
   sudo touch /etc/squid/blocked_domains.acl
   sudo chown piproxyguard /etc/squid/blocked_domains.acl
   # allow the worker to reload squid without full sudo:
   echo 'piproxyguard ALL=(root) NOPASSWD: /usr/sbin/squid -k reconfigure' | sudo tee /etc/sudoers.d/piproxyguard
   ```

   Then set `"ReloadCommand": "sudo /usr/sbin/squid -k reconfigure"` in the Worker's `appsettings.json`.

4. **Install the systemd units**:

   ```bash
   sudo cp deploy/piproxyguard-*.service /etc/systemd/system/
   sudo systemctl daemon-reload
   sudo systemctl enable --now piproxyguard-worker piproxyguard-api
   ```

5. Check: `journalctl -u piproxyguard-worker -f` and open `http://<pi>:5080/swagger`.

## Running locally (development)

```bash
dotnet test                                       # 36 unit tests
cd src/PiProxyGuard.Worker && dotnet run          # uses sample-logs/access.log, local sqlite + acl file
cd src/PiProxyGuard.Api    && dotnet run          # http://localhost:5080/swagger
```

`appsettings.Development.json` in both projects points at local files, so nothing touches `/etc` or `/var` on a dev machine.

## Repository conventions

- `Directory.Build.props` — net9.0, nullable, analyzers (`latest-recommended`), **TreatWarningsAsErrors** for every project
- `Directory.Packages.props` — Central Package Management (all package versions in one place)
- `.editorconfig` / `.gitattributes` / `nuget.config` — consistent style, line endings and a locked nuget.org source
- `.github/workflows/ci.yml` — build + tests on every push/PR
- `.github/workflows/release.yml` — pushing a tag like `v1.0.0` builds, tests, publishes self-contained **linux-arm64** binaries and attaches `PiProxyGuard-v1.0.0-linux-arm64.tar.gz` / `.zip` to a GitHub Release

## Notes & ideas

- The parser is pluggable (`IProxyLogParser`) — add a `ThreeProxyLogParser` if you switch from Squid to 3proxy.
- `Detection:AutoBlockSuspiciousHosts` is reserved for auto-blocking hosts behind repeated denied requests; wire it up in `SuspiciousActivityDetector` if you want fully automatic blocking.
- SQLite WAL + shared cache handles the two processes fine at home-network scale (tens of requests/second).
- Pair the Pi with WireGuard for remote access; the API then stays LAN-only.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Author

**Bohdan Harabadzhyu**
