# PiProxyGuard — whole-network (transparent / intercept) blocking

By default PiProxyGuard is a **forward proxy** on `<pi>:3128`: it only filters traffic that is
explicitly pointed at it. A device that is not configured to use the proxy bypasses it.

**Transparent mode** makes the blocklist apply to **every device on the LAN** (phones, TVs, …)
with no per-device proxy settings and **without decrypting HTTPS**. The Raspberry Pi becomes the
network gateway, `iptables` redirects ports 80/443 into Squid, and Squid peeks at the TLS
ClientHello (SNI) to terminate blocked domains while splicing everything else through untouched
(`ssl_bump peek` + `terminate`/`splice`).

The whole Pi-side setup is **automated by the package** — you do not edit configs or run a list of
commands. There is exactly **one** thing software on the Pi physically cannot do for you: route the
LAN through the Pi. That is a router/DHCP setting (see step 2).

---

## 1. Turn it on (everything else is automatic)

**Option A — at install time:**
```bash
sudo PIPROXYGUARD_TRANSPARENT=1 apt install ./piproxyguard_*_arm64.deb
```

**Option B — any time after install:**
```bash
sudo piproxyguard-transparent enable
```

Either way the package automatically:
- enables IPv4/IPv6 forwarding (`/etc/sysctl.d/99-piproxyguard-forward.conf`);
- detects whether Squid was built `--with-openssl` (needed for HTTPS interception) and, if so,
  generates a self-signed cert for `https_port` in `/etc/squid/ssl` (clients never need it — we
  don't impersonate certificates);
- appends a managed intercept block to `/etc/squid/squid.conf` (with a timestamped backup),
  validates it with `squid -k parse` and reloads Squid;
- installs `iptables` REDIRECT rules (`80→3129`, `443→3130`) and `MASQUERADE`, all tagged with a
  comment so they can be removed cleanly, and persists them via `netfilter-persistent`;
- remembers the choice in `/etc/piproxyguard/transparent.conf` and **re-applies it on every
  upgrade** automatically.

If Squid lacks `--with-openssl`, transparent mode still sets up **HTTP (port 80)** interception and
skips the HTTPS parts (you'll see a warning). Check with:
```bash
squid -v | grep -o -- '--with-openssl'
```

## 2. The one manual step: route the LAN through the Pi

The package cannot reconfigure your router, so point your LAN's default gateway at the Pi:

- **Whole network:** on your router's DHCP settings, set the **gateway / router** option to the
  Pi's IP. (If the router won't allow it, disable the router's DHCP and run one on the Pi, e.g.
  `dnsmasq`, advertising the Pi as the gateway.)
- **Single device (testing):** in the device's static Wi-Fi settings, set **Gateway = Pi IP**.

Without this, client traffic never reaches the Pi and there is nothing to intercept.

## 3. Verify (from a client, with NO proxy configured)
```bash
curl -I http://www.facebook.com      # -> HTTP/1.1 403 Forbidden   (HTTP intercept)
curl -I https://www.facebook.com     # -> connection reset / TLS error  (SNI terminate)
```
The dashboard will now show **real device IPs** instead of just `127.0.0.1` — a nice side effect.

## 4. Status / disable
```bash
piproxyguard-transparent status      # show what's active
sudo piproxyguard-transparent disable
```
`disable` removes the tagged iptables rules, strips the managed block from `squid.conf`, reloads
Squid, removes the forwarding sysctl and records the choice. Removing the package (`apt remove`)
reverts transparent mode automatically; purge (`apt purge`) also deletes `/etc/piproxyguard`.

---

## How blocking works under the hood
- **HTTP (port 80 → 3129 intercept):** the existing `http_access deny piproxyguard_blocked`
  (`dstdomain` from `/etc/squid/blocked_domains.acl`) returns a normal **403** page.
- **HTTPS (port 443 → 3130 intercept, ssl-bump):** `ssl_bump peek` reads the SNI from the
  ClientHello; if the host matches `blocked_domains.acl` (as `ssl::server_name`) the connection is
  `terminate`d, otherwise `splice`d through **without decryption**. Same ACL file, no MITM.

## Honest limitations
- A blocked **HTTPS** site gets a **connection reset**, not a friendly 403 page. To show a block
  page over HTTPS you'd have to MITM (`ssl_bump bump`) and install the Pi's CA on every device —
  not needed just to block, so we don't.
- **DoH/DoT** (browser DNS-over-HTTPS) and future **Encrypted ClientHello (ECH)** hide the SNI and
  can bypass SNI-based blocking. Mitigate by blocking known DoH resolvers; ECH has no fix yet.
- **Certificate-pinned apps** keep working (we splice, never decrypt); blocked ones are still cut
  by SNI. Pinning isn't broken because there is no MITM.
- The Pi carries **all** LAN traffic — it's a bottleneck and a single point of failure. Fine for a
  home network.
