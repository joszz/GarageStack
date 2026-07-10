#!/bin/sh
set -e

# Volume-mounted logs may already be root-owned from images built before this container
# dropped root, or from a fresh named volume Docker always creates as root. Reclaim it for
# APP_UID on every start so an existing deployment keeps working after upgrading, then hand
# off to the app as that non-root user.
mkdir -p /app/logs
chown -R "$APP_UID" /app/logs

exec gosu "$APP_UID" dotnet GarageStack.Worker.dll
