# Upstream tunnel (route selected domains through a parent proxy)

`v1.8.0`

The **upstream tunnel** lets PiProxyGuard forward *only the domains you choose*
through an upstream/parent proxy (a VPN-side proxy, a company proxy, a Tor
`privoxy`/`polipo`, another Squid, ...) while every other domain keeps going out
directly. It is the building block for "this handful of sites must leave through
a different exit, the rest stay local".

It is the mirror image of the blocklist/allowlist: a DB-backed domain list that
PiProxyGuard renders into a Squid `dstdomain` ACL (`/etc/squid/tunnel_domains.acl`)
and reloads Squid whenever it changes.

## How it works

```
client --> Squid (Pi) --> tunneled domain?  --> yes --> cache_peer (parent proxy) --> internet
                                            --> no  --> direct --> internet
```

Two halves:

1. **The domain list** — managed at runtime from the dashboard
   (**Upstream tunnel** page) or the REST API (`/api/tunnel`). PiProxyGuard writes
   it to `tunnel_domains.acl` (leading-dot, suffix match) and runs
   `squid -k reconfigure`. No restart, no redeploy.
2. **The parent proxy wiring** — `cache_peer` + `cache_peer_access` +
   `never_direct`, added to `squid.conf` once by `setup-upstream.sh`.

## Setup (.deb install — recommended)

The package ships the engine at `/usr/lib/piproxyguard/upstream-setup.sh` with a thin
`piproxyguard-upstream` command in `/usr/bin` (exactly like `piproxyguard-transparent`).
After `sudo apt install ./piproxyguard_*_arm64.deb`, wire the parent proxy once:

```bash
sudo piproxyguard-upstream enable <host> <port>   # e.g. 10.8.0.1 8888
sudo piproxyguard-upstream status
sudo piproxyguard-upstream disable                # all domains go direct again
```

It is **not** enabled automatically at install (it needs an external parent proxy and its
`host:port`). `apt remove` strips the `cache_peer` block from `squid.conf` for you (the
`tunnel_domains.acl` list is kept).

## Setup (native install from a checkout)

```bash
cd deploy/upstream-tunnel
sudo ./setup-upstream.sh enable <host> <port>     # e.g. 10.8.0.1 8888
```

Then open the dashboard -> **Upstream tunnel**, add the domains you want routed
through the parent (e.g. `example.com`, `chatgpt.com`). Matching is suffix-based,
so `example.com` also tunnels `cdn.example.com`.

Disable / inspect:

```bash
sudo ./setup-upstream.sh status
sudo ./setup-upstream.sh disable                  # all domains go direct again
```

`disable` keeps `tunnel_domains.acl` so your list survives; it only removes the
`cache_peer` block from `squid.conf`.

## Docker

In the container the worker seeds an empty `tunnel_domains.acl` on startup, and
the dashboard keeps it in sync. You still need to add the `cache_peer` block to
the mounted `squid.conf` once (run `setup-upstream.sh enable ...` against the
config in the `squid-config` volume, or paste the commented block from
`deploy/squid.conf.sample`). As with the blocklist, **Squid in Docker does not
auto-reload**, so after wiring the peer run on the host:

```bash
docker exec piproxyguard-squid squid -k reconfigure
```

## Caveats

- **`never_direct` means no fallback.** If the parent proxy is unreachable, the
  tunneled domains stop working until the peer is back or you run `disable`.
  Non-tunneled traffic is unaffected.
- The parent must be a real HTTP proxy that accepts `CONNECT` for HTTPS.
- This routes traffic; it does **not** add encryption by itself. Point it at a
  proxy that already lives on the secure side (VPN / Tor) if hiding from the ISP
  is the goal.
