#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Removes the PiProxyGuard native install created by deploy/install.sh.
# Keeps the database and Squid by default.
#
#     sudo bash deploy/uninstall.sh
# ---------------------------------------------------------------------------
set -euo pipefail

[ "$(id -u)" -eq 0 ] || { echo "Please run as root: sudo bash deploy/uninstall.sh"; exit 1; }

echo "[*] Stopping and removing services ..."
systemctl disable --now piproxyguard-api piproxyguard-worker 2>/dev/null || true
rm -f /etc/systemd/system/piproxyguard-api.service \
      /etc/systemd/system/piproxyguard-worker.service
systemctl daemon-reload

rm -f /etc/sudoers.d/piproxyguard
rm -rf /opt/piproxyguard

echo
echo "[+] Removed services, /opt/piproxyguard and the sudoers rule."
echo "    Kept (remove manually if you want):"
echo "      database : sudo rm -rf /var/lib/piproxyguard"
echo "      squid    : sudo apt-get remove --purge squid"
echo "      config   : /etc/squid/squid.conf (+ any .bak backups)"
