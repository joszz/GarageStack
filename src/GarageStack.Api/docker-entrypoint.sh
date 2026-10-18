#!/bin/sh
set -e

# Volume-mounted directories may already be root-owned from images built before this
# container dropped root, or from a fresh named volume Docker always creates as root.
# Reclaim them for APP_UID on every start so an existing deployment keeps working after
# upgrading, then hand off to the app as that non-root user.
mkdir -p /app/logs /app/keys
chown -R "$APP_UID" /app/logs /app/keys

# setpriv (util-linux, part of the base image) leaves HOME at root's, so point it at the app
# user's own.
export HOME=/home/app
exec setpriv --reuid="$APP_UID" --regid="$APP_UID" --init-groups dotnet GarageStack.Api.dll
