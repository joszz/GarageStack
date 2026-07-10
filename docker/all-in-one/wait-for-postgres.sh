#!/bin/bash
# Polls the embedded PostgreSQL instance instead of a fixed sleep before starting api/worker.
# Falls through after the timeout so a stuck postgres doesn't hang supervisord forever --
# the .NET services' own connection retries and supervisord's autorestart take over from there.
set -u

for _ in $(seq 1 60); do
    /usr/lib/postgresql/18/bin/pg_isready -h 127.0.0.1 -p 5432 -q && exit 0
    sleep 1
done

echo "[garagestack] postgres did not become ready within 60s, starting anyway" >&2
