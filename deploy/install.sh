#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# PiProxyGuard one-command installer for the self-contained linux-arm64 release.
#
# Usage (from the extracted release directory):
#     tar -xzf PiProxyGuard-*-linux-arm64.tar.gz
#     sudo bash deploy/install.sh
#
# What it does (idempotent — safe to re-run):
#   1. installs Squid (and jq) via apt
#   2. lays down a working /etc/squid/squid.conf (backs up any existing one)
#   3. creates the 'piproxyguard' service user + data dir + ACL file
#   4. copies the api/ and worker/ binaries to /opt/piproxyguard
#   5. lets the worker reload Squid without a password (sudoers)
#   6. installs + starts the systemd units (worker + api)
#
# No .NET runtime is required — the binaries are self-contained.
# ---------------------------------------------------------------------------
set -euo pipefail

# ----------------------------- settings ------------------------------------
APP_USER="piproxyguard"
INSTALL_DIR="/opt/piproxyguard"
DATA_DIR="/var/lib/piproxyguard"
SQUID_CONF="/etc/squid/squid.conf"
ACL_FILE="/etc/squid/blocked_domains.acl"
SQUID_LOG="/var/log/squid/access.log"

# ------------------------- locate release root -----------------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

log()  { printf '\033[1;32m[+]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[!]\033[0m %s\n' "$*"; }
die()  { printf '\033[1;31m[x]\033[0m %s\n' "$*" >&2; exit 1; }

# ------------------------------ pre-checks ---------------------------------
[ "$(id -u)" -eq 0 ] || die "Please run as root:  sudo bash deploy/install.sh"
command -v apt-get >/dev/null 2>&1 || die "apt-get not found. This installer targets Raspberry Pi OS / Debian / Ubuntu."
[ -f "$ROOT/api/PiProxyGuard.Api" ]       || die "api/PiProxyGuard.Api not found next to deploy/. Run from the extracted release folder."
[ -f "$ROOT/worker/PiProxyGuard.Worker" ] || die "worker/PiProxyGuard.Worker not found next to deploy/."

# --------------------------- 1. packages -----------------------------------
log "Installing Squid + jq ..."
export DEBIAN_FRONTEND=noninteractive
apt-get update -y
apt-get install -y squid jq

SQUID_BIN="$(command -v squid || echo /usr/sbin/squid)"

# ------------------------- 2. service user ---------------------------------
if id "$APP_USER" >/dev/null 2>&1; then
    log "User '$APP_USER' already exists"
else
    log "Creating service user '$APP_USER'"
    useradd -r -s /usr/sbin/nologin "$APP_USER"
fi
if getent group proxy >/dev/null 2>&1; then
    usermod -aG proxy "$APP_USER"   # so the worker can read Squid's access.log
fi

# ----------------------- 3. copy binaries ----------------------------------
log "Installing binaries to $INSTALL_DIR"
mkdir -p "$INSTALL_DIR/api" "$INSTALL_DIR/worker"
cp -a "$ROOT/api/."    "$INSTALL_DIR/api/"
cp -a "$ROOT/worker/." "$INSTALL_DIR/worker/"
chmod +x "$INSTALL_DIR/api/PiProxyGuard.Api" "$INSTALL_DIR/worker/PiProxyGuard.Worker"

# ------------------------ 4. data dir + acl --------------------------------
mkdir -p "$DATA_DIR" "$(dirname "$SQUID_LOG")"
touch "$ACL_FILE"
chmod 644 "$ACL_FILE"
chown -R "$APP_USER":"$APP_USER" "$DATA_DIR" "$INSTALL_DIR"
chown "$APP_USER":"$APP_USER" "$ACL_FILE"

# ------------------------- 5. squid.conf -----------------------------------
if grep -q "blocked_domains.acl" "$SQUID_CONF" 2>/dev/null; then
    log "Squid already configured for PiProxyGuard — leaving $SQUID_CONF unchanged"
else
    if [ -f "$SQUID_CONF" ]; then
        backup="${SQUID_CONF}.bak.$(date +%Y%m%d%H%M%S)"
        cp -a "$SQUID_CONF" "$backup"
        warn "Existing squid.conf backed up to $backup"
    fi
    cp "$ROOT/deploy/squid.conf.sample" "$SQUID_CONF"
    log "Installed PiProxyGuard squid.conf"
fi

# --------------- 6. let the worker reload squid (sudoers) -------------------
echo "$APP_USER ALL=(root) NOPASSWD: $SQUID_BIN -k reconfigure" > /etc/sudoers.d/piproxyguard
chmod 440 /etc/sudoers.d/piproxyguard

# --------------- 7. point the worker config at sudo reload -----------------
WORKER_CFG="$INSTALL_DIR/worker/appsettings.json"
if [ -f "$WORKER_CFG" ]; then
    tmp="$(mktemp)"
    jq --arg rc "sudo $SQUID_BIN -k reconfigure" \
       '.Blocklist.ReloadCommand = $rc
        | .AccessLog.Path = "/var/log/squid/access.log"
        | .ConnectionStrings.Default = "Data Source=/var/lib/piproxyguard/piproxyguard.db"' \
       "$WORKER_CFG" > "$tmp" && mv "$tmp" "$WORKER_CFG"
    chown "$APP_USER":"$APP_USER" "$WORKER_CFG"
    log "Patched worker appsettings.json (ReloadCommand / AccessLog / DB path)"
fi

# ------------------------- 8. systemd units --------------------------------
log "Installing + starting systemd units"
cp "$ROOT/deploy/piproxyguard-api.service"    /etc/systemd/system/
cp "$ROOT/deploy/piproxyguard-worker.service" /etc/systemd/system/
systemctl daemon-reload
systemctl enable squid >/dev/null 2>&1 || true
systemctl restart squid
systemctl enable --now piproxyguard-worker piproxyguard-api

# ----------------------------- 9. summary ----------------------------------
IP="$(hostname -I 2>/dev/null | awk '{print $1}')"
IP="${IP:-<pi-ip>}"
echo
log "Done! PiProxyGuard is up."
echo
echo "  Dashboard : http://$IP:5080/"
echo "  Swagger   : http://$IP:5080/swagger"
echo "  Proxy     : $IP:3128    <- set this as the HTTP/HTTPS proxy on your devices"
echo
echo "  Status : systemctl status piproxyguard-worker piproxyguard-api"
echo "  Logs   : journalctl -u piproxyguard-worker -f"
echo
echo "  Uninstall: sudo bash deploy/uninstall.sh"
