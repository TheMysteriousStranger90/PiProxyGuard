#!/usr/bin/env bash
# Publishes self-contained linux-arm64 builds of the Worker and the API.
# Run from the repository root: ./deploy/publish.sh
# For a 32-bit Raspberry Pi OS use RID=linux-arm ./deploy/publish.sh
set -euo pipefail

RID="${RID:-linux-arm64}"
OUT="${OUT:-./publish}"

echo "Publishing for $RID into $OUT ..."

dotnet publish src/PiProxyGuard.Worker -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=true -p:PublishTrimmed=false -o "$OUT/worker"

dotnet publish src/PiProxyGuard.Api -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=true -p:PublishTrimmed=false -o "$OUT/api"

echo
echo "Done. Copy to the Pi, e.g.:"
echo "  rsync -av $OUT/ pi@raspberrypi:/opt/piproxyguard/"
