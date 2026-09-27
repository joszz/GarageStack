#!/bin/bash
# Holds the Worker back until the API answers its health check. The API applies the database
# migrations before it starts listening, so on a first start with an empty volume the Worker
# would otherwise query tables that do not exist yet (compose gets the same ordering from
# depends_on: service_healthy). Falls through after the timeout so a stuck API doesn't hang
# supervisord forever -- the Worker's own retries and autorestart take over from there.
set -u

for _ in $(seq 1 180); do
    curl -fsS -o /dev/null http://127.0.0.1:9000/health && exit 0
    sleep 1
done

echo "[garagestack] api did not become healthy within 180s, starting the worker anyway" >&2
