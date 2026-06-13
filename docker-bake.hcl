# ---------------------------------------------------------------------------
# Buildx bake definition for PiProxyGuard multi-arch images.
#
#   # build both images for amd64 + arm64 and load locally (single arch only):
#   docker buildx bake
#
#   # build and PUSH the full multi-arch manifest to a registry:
#   REGISTRY=ghcr.io/youruser TAG=1.2.0 docker buildx bake --push
#
# Variables (override with env or --set):
#   REGISTRY  image name prefix      (default: piproxyguard)
#   TAG       image tag              (default: 1.2.0)
#   PLATFORMS comma-separated list   (default: linux/amd64,linux/arm64)
# ---------------------------------------------------------------------------
variable "REGISTRY" { default = "piproxyguard" }
variable "TAG"      { default = "1.2.0" }
variable "PLATFORMS" { default = "linux/amd64,linux/arm64" }

group "default" {
  targets = ["api", "worker"]
}

target "_common" {
  context    = "."
  platforms  = split(",", PLATFORMS)
  args       = { VERSION = "${TAG}" }
  labels     = { "org.opencontainers.image.source" = "https://github.com/AndreyDavydov/PiProxyGuard" }
}

target "api" {
  inherits   = ["_common"]
  dockerfile = "Dockerfile.api"
  tags = [
    "${REGISTRY}/piproxyguard-api:${TAG}",
    "${REGISTRY}/piproxyguard-api:latest",
  ]
}

target "worker" {
  inherits   = ["_common"]
  dockerfile = "Dockerfile.worker"
  tags = [
    "${REGISTRY}/piproxyguard-worker:${TAG}",
    "${REGISTRY}/piproxyguard-worker:latest",
  ]
}
