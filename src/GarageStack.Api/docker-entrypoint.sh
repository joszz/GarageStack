#!/bin/sh
set -e

# Volume-mounted directories may already be root-owned from images built before this
# container dropped root, or from a fresh named volume Docker always creates as root.
# Reclaim them for APP_UID on every start so an existing deployment keeps working after
# upgrading, then hand off to the app as that non-root user.
mkdir -p /app/logs /app/keys
chown -R "$APP_UID" /app/logs /app/keys

exec gosu "$APP_UID" dotnet GarageStack.Api.dll
