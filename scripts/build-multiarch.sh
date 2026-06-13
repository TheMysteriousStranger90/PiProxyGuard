#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Build PiProxyGuard multi-arch images (linux/amd64 + linux/arm64 for the Pi).
#
# Run this on any machine with Docker (your laptop, a CI runner, or the Pi
# itself). It sets up QEMU + a buildx builder the first time, then builds both
# images via docker-bake.hcl.
#
# Usage:
#   ./scripts/build-multiarch.sh                 # build (no push), arm64+amd64
#   REGISTRY=ghcr.io/you TAG=1.2.0 \
#     ./scripts/build-multiarch.sh --push        # build + push manifest
#   PLATFORMS=linux/arm64 ./scripts/build-multiarch.sh --load  # arm64 only, load
#
# Notes:
#   * A true multi-arch manifest can only be --push'ed to a registry, not
#     --load'ed into the local daemon. Use --load only with a single PLATFORMS.
#   * For GHCR: docker login ghcr.io -u <user> -p <token-with-write:packages>.
# ---------------------------------------------------------------------------
set -euo pipefail
cd "$(dirname "$0")/.."

REGISTRY="${REGISTRY:-piproxyguard}"
TAG="${TAG:-1.2.0}"
PLATFORMS="${PLATFORMS:-linux/amd64,linux/arm64}"
ACTION="${1:-}"

echo "==> PiProxyGuard multi-arch build"
echo "    REGISTRY=${REGISTRY}  TAG=${TAG}  PLATFORMS=${PLATFORMS}  ACTION=${ACTION:-build-only}"

# 1. Register QEMU emulators so the runtime base images for foreign arches work.
if [ -z "$(ls /proc/sys/fs/binfmt_misc/ 2>/dev/null | grep -i qemu || true)" ]; then
  echo "==> Installing QEMU binfmt handlers (needs Docker)"
  docker run --privileged --rm tonistiigi/binfmt --install all
fi

# 2. Ensure a buildx builder with the docker-container driver exists.
if ! docker buildx inspect piproxyguard-builder >/dev/null 2>&1; then
  echo "==> Creating buildx builder 'piproxyguard-builder'"
  docker buildx create --name piproxyguard-builder --driver docker-container --use
else
  docker buildx use piproxyguard-builder
fi
docker buildx inspect --bootstrap >/dev/null

# 3. Bake.
export REGISTRY TAG PLATFORMS
case "$ACTION" in
  --push) docker buildx bake --push ;;
  --load) docker buildx bake --load ;;        # single-arch only
  *)      docker buildx bake ;;               # build to cache (verify only)
esac

echo "==> Done. Images:"
echo "    ${REGISTRY}/piproxyguard-api:${TAG}"
echo "    ${REGISTRY}/piproxyguard-worker:${TAG}"
