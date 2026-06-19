#!/usr/bin/env bash
# PiProxyGuard — transparent / intercept engine.
#
# Configures the *Pi side* of whole-LAN blocking: IP forwarding, a self-signed
# cert for https_port, the Squid intercept block, and iptables REDIRECT rules.
# SNI-based blocking, NO decryption. Everything is idempotent and reversible.
#
# This script is run automatically by the .deb postinst when transparent mode is
# enabled. You normally don't call it by hand — use:  piproxyguard-transparent {enable|disable|status}
#
#   piproxyguard-transparent enable     # set everything up
#   piproxyguard-transparent disable    # remove everything we added
#   piproxyguard-transparent status     # show current state
#
# Interfaces are auto-detected from the default route; override with env vars:
#   LAN_IF=eth0 WAN_IF=eth0 piproxyguard-transparent enable
set -euo pipefail

SQUID_CONF=/etc/squid/squid.conf
SSL_DIR=/etc/squid/ssl
ACL=/etc/squid/blocked_domains.acl
CONF_DIR=/etc/piproxyguard
CONF=$CONF_DIR/transparent.conf
SYSCTL=/etc/sysctl.d/99-piproxyguard-forward.conf
TAG=piproxyguard-transparent
BEGIN="# >>> ${TAG} >>>"
END="# <<< ${TAG} <<<"
HTTP_PORT=3129
HTTPS_PORT=3130

log()  { echo ">> $*"; }
warn() { echo "!! $*" >&2; }

need_root() { [ "$(id -u)" -eq 0 ] || { warn "run as root (sudo)"; exit 1; }; }

detect_ifaces() {
  local def
  def="$(ip route show default 2>/dev/null | awk '/default/ {print $5; exit}')"
  LAN_IF="${LAN_IF:-${def:-eth0}}"
  WAN_IF="${WAN_IF:-${def:-eth0}}"
}

squid_has_ssl() {
  squid -v 2>/dev/null | tr ' ' '\n' | grep -qi -- '--with-openssl'
}

write_conf() {
  mkdir -p "$CONF_DIR"
  cat > "$CONF" <<EOF
# Managed by piproxyguard-transparent. Set TRANSPARENT_ENABLED=0 (or run
# 'piproxyguard-transparent disable') to turn whole-LAN interception off.
TRANSPARENT_ENABLED=$1
LAN_IF=$LAN_IF
WAN_IF=$WAN_IF
EOF
}

gen_cert() {
  [ -f "$SSL_DIR/squid-self.crt" ] && return 0
  log "generating self-signed cert for https_port"
  mkdir -p "$SSL_DIR"
  openssl req -new -newkey rsa:2048 -days 3650 -nodes -x509 \
    -subj "/CN=PiProxyGuard" \
    -keyout "$SSL_DIR/squid-self.key" \
    -out    "$SSL_DIR/squid-self.crt" >/dev/null 2>&1
  # squid runs as user 'proxy' on Debian
  chown -R proxy:proxy "$SSL_DIR" 2>/dev/null || true
  chmod 600 "$SSL_DIR/squid-self.key"
}

remove_squid_block() {
  [ -f "$SQUID_CONF" ] || return 0
  if grep -qF "$BEGIN" "$SQUID_CONF"; then
    sed -i "/$(printf '%s' "$BEGIN" | sed 's/[][\.*^$/]/\\&/g')/,/$(printf '%s' "$END" | sed 's/[][\.*^$/]/\\&/g')/d" "$SQUID_CONF"
  fi
}

add_squid_block() {
  local ssl_ok="$1"
  cp -a "$SQUID_CONF" "${SQUID_CONF}.bak.$(date +%Y%m%d%H%M%S)" 2>/dev/null || true
  remove_squid_block
  # drop trailing blank lines so repeated enable/disable can't pile them up
  sed -i -e :a -e '/^[[:space:]]*$/{$d;N;ba;}' "$SQUID_CONF" 2>/dev/null || true
  {
    echo ""
    echo "$BEGIN"
    echo "# Whole-LAN intercept. Managed by piproxyguard-transparent; edits here are overwritten."
    echo "http_port ${HTTP_PORT} intercept"
    if [ "$ssl_ok" = "1" ]; then
      echo "https_port ${HTTPS_PORT} intercept ssl-bump \\"
      echo "    tls-cert=${SSL_DIR}/squid-self.crt \\"
      echo "    tls-key=${SSL_DIR}/squid-self.key \\"
      echo "    generate-host-certificates=off"
      echo "acl piproxyguard_blocked_ssl ssl::server_name \"${ACL}\""
      echo "acl piproxyguard_step1 at_step SslBump1"
      echo "ssl_bump peek piproxyguard_step1"
      echo "ssl_bump terminate piproxyguard_blocked_ssl"
      echo "ssl_bump splice all"
    fi
    echo "$END"
  } >> "$SQUID_CONF"
}

flush_iptables() {
  command -v iptables >/dev/null 2>&1 || return 0
  # delete every rule we tagged, in any nat chain
  iptables-save -t nat 2>/dev/null | grep -- "--comment ${TAG}" | sed 's/^-A/-D/' | while read -r rule; do
    # shellcheck disable=SC2086
    iptables -t nat $rule 2>/dev/null || true
  done
}

add_iptables() {
  command -v iptables >/dev/null 2>&1 || { warn "iptables not found — skipping NAT rules"; return 1; }
  local ssl_ok="$1"
  flush_iptables   # avoid duplicates
  iptables -t nat -A PREROUTING -i "$LAN_IF" -p tcp --dport 80  -m comment --comment "$TAG" -j REDIRECT --to-port "$HTTP_PORT"
  if [ "$ssl_ok" = "1" ]; then
    iptables -t nat -A PREROUTING -i "$LAN_IF" -p tcp --dport 443 -m comment --comment "$TAG" -j REDIRECT --to-port "$HTTPS_PORT"
  fi
  iptables -t nat -A POSTROUTING -o "$WAN_IF" -m comment --comment "$TAG" -j MASQUERADE
}

persist_iptables() {
  if command -v netfilter-persistent >/dev/null 2>&1; then
    netfilter-persistent save >/dev/null 2>&1 || true
  fi
  # Always drop a rules file too, so iptables-persistent/netfilter-persistent
  # restores it on boot even if it was configured after us in the same apt run.
  mkdir -p /etc/iptables
  command -v iptables-save  >/dev/null 2>&1 && iptables-save  > /etc/iptables/rules.v4 2>/dev/null || true
  command -v ip6tables-save >/dev/null 2>&1 && ip6tables-save > /etc/iptables/rules.v6 2>/dev/null || true
}

enable_forwarding() {
  cat > "$SYSCTL" <<'EOF'
net.ipv4.ip_forward=1
net.ipv6.conf.all.forwarding=1
EOF
  sysctl --system >/dev/null 2>&1 || true
}

reload_squid() {
  if squid -k parse >/dev/null 2>&1; then
    systemctl restart squid 2>/dev/null || squid -k reconfigure 2>/dev/null || true
  else
    warn "squid -k parse failed — NOT reloading. Check $SQUID_CONF"
    return 1
  fi
}

cmd_enable() {
  need_root
  detect_ifaces
  log "enabling transparent mode (LAN_IF=$LAN_IF WAN_IF=$WAN_IF)"
  local ssl_ok=0
  if squid_has_ssl; then ssl_ok=1; log "squid has --with-openssl (HTTPS intercept ON)";
  else warn "squid built WITHOUT --with-openssl — HTTPS intercept OFF, HTTP-only"; fi

  enable_forwarding
  [ "$ssl_ok" = "1" ] && gen_cert
  add_squid_block "$ssl_ok"
  reload_squid || { warn "squid did not reload; leaving iptables unchanged"; return 1; }
  add_iptables "$ssl_ok" || true
  persist_iptables
  write_conf 1
  log "DONE. Transparent mode is active on the Pi."
  echo ""
  echo "   One step the package CANNOT do for you: route your LAN through this Pi."
  echo "   On your router, set the DHCP 'gateway/router' option to this Pi's IP"
  echo "   ($(hostname -I 2>/dev/null | awk '{print $1}')), or set it per-device for testing."
  echo "   Then from a client (no proxy configured):"
  echo "     curl -I http://www.facebook.com    # -> 403"
  [ "$ssl_ok" = "1" ] && echo "     curl -I https://www.facebook.com   # -> connection reset (SNI terminate)"
}

cmd_disable() {
  need_root
  log "disabling transparent mode"
  flush_iptables
  persist_iptables
  remove_squid_block
  reload_squid || true
  rm -f "$SYSCTL"; sysctl --system >/dev/null 2>&1 || true
  write_conf 0
  log "DONE. Transparent mode removed. (Set your LAN gateway back to the router.)"
}

cmd_status() {
  echo "transparent.conf : $([ -f "$CONF" ] && grep -m1 TRANSPARENT_ENABLED "$CONF" || echo 'absent')"
  echo "squid block      : $(grep -qF "$BEGIN" "$SQUID_CONF" 2>/dev/null && echo present || echo absent)"
  echo "ip_forward       : $(cat /proc/sys/net/ipv4/ip_forward 2>/dev/null)"
  echo "squid --with-openssl: $(squid_has_ssl && echo yes || echo no)"
  if command -v iptables >/dev/null 2>&1; then
    echo "iptables rules   :"; iptables-save -t nat 2>/dev/null | grep -- "--comment ${TAG}" | sed 's/^/   /' || true
  fi
}

case "${1:-enable}" in
  enable)  cmd_enable ;;
  disable) cmd_disable ;;
  status)  cmd_status ;;
  *) echo "usage: $0 {enable|disable|status}"; exit 2 ;;
esac
