#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Build a .deb from a self-contained publish dir (one that contains api/ and
# worker/ produced by deploy/publish.sh).
#
#   deploy/build-deb.sh <publish-dir> <version> [arch]
#
# Example:
#   ./deploy/publish.sh                              # -> ./publish/{api,worker}
#   ./deploy/build-deb.sh ./publish 1.3.0 arm64      # -> piproxyguard_1.3.0_arm64.deb
#
# Output goes to $OUT (default: current dir). Needs dpkg-deb.
# ---------------------------------------------------------------------------
set -euo pipefail

PUBLISH_DIR="${1:?usage: build-deb.sh <publish-dir> <version> [arch]}"
VERSION="${2:?usage: build-deb.sh <publish-dir> <version> [arch]}"
ARCH="${3:-arm64}"
OUT="${OUT:-.}"
VERSION="${VERSION#v}"   # accept a tag like v1.3.0

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEB_DIR="$SCRIPT_DIR/debian"

command -v dpkg-deb >/dev/null 2>&1 || { echo "dpkg-deb not found" >&2; exit 1; }
[ -f "$PUBLISH_DIR/api/PiProxyGuard.Api" ]       || { echo "missing $PUBLISH_DIR/api/PiProxyGuard.Api" >&2; exit 1; }
[ -f "$PUBLISH_DIR/worker/PiProxyGuard.Worker" ] || { echo "missing $PUBLISH_DIR/worker/PiProxyGuard.Worker" >&2; exit 1; }

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

# ---- payload: binaries ----
install -d "$STAGE/opt/piproxyguard/api" "$STAGE/opt/piproxyguard/worker"
cp -a "$PUBLISH_DIR/api/."    "$STAGE/opt/piproxyguard/api/"
cp -a "$PUBLISH_DIR/worker/." "$STAGE/opt/piproxyguard/worker/"
chmod +x "$STAGE/opt/piproxyguard/api/PiProxyGuard.Api" \
         "$STAGE/opt/piproxyguard/worker/PiProxyGuard.Worker"

# ---- payload: systemd units + squid sample ----
install -d "$STAGE/lib/systemd/system"
cp "$SCRIPT_DIR/piproxyguard-api.service"    "$STAGE/lib/systemd/system/"
cp "$SCRIPT_DIR/piproxyguard-worker.service" "$STAGE/lib/systemd/system/"
install -d "$STAGE/usr/share/piproxyguard"
cp "$SCRIPT_DIR/squid.conf.sample" "$STAGE/usr/share/piproxyguard/squid.conf.sample"

# ---- payload: transparent (whole-LAN) engine + user command ----
install -d "$STAGE/usr/lib/piproxyguard"
install -m 0755 "$SCRIPT_DIR/transparent/setup-transparent.sh" \
        "$STAGE/usr/lib/piproxyguard/transparent-setup.sh"
install -d "$STAGE/usr/bin"
cat > "$STAGE/usr/bin/piproxyguard-transparent" <<'EOF'
#!/bin/sh
# Thin wrapper around the PiProxyGuard transparent-mode engine.
exec /usr/lib/piproxyguard/transparent-setup.sh "$@"
EOF
chmod 0755 "$STAGE/usr/bin/piproxyguard-transparent"

# ---- control + maintainer scripts ----
install -d "$STAGE/DEBIAN"
sed -e "s/__VERSION__/$VERSION/" -e "s/__ARCH__/$ARCH/" \
    "$DEB_DIR/control.template" > "$STAGE/DEBIAN/control"
SIZE="$(du -sk "$STAGE/opt" "$STAGE/lib" "$STAGE/usr" | awk '{s+=$1} END{print s}')"
echo "Installed-Size: $SIZE" >> "$STAGE/DEBIAN/control"
for s in postinst prerm postrm; do
    cp "$DEB_DIR/$s" "$STAGE/DEBIAN/$s"
    chmod 0755 "$STAGE/DEBIAN/$s"
done

mkdir -p "$OUT"
DEB="$OUT/piproxyguard_${VERSION}_${ARCH}.deb"
dpkg-deb --build --root-owner-group "$STAGE" "$DEB"
echo "Built $DEB"
