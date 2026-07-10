#!/bin/bash
# Polls the embedded Mosquitto broker instead of a fixed sleep before starting the SAIC
# gateway. Falls through after the timeout so a stuck broker doesn't hang supervisord forever --
# the gateway's own startretries and autorestart take over from there.
set -u

for _ in $(seq 1 30); do
    timeout 1 bash -c 'cat < /dev/tcp/127.0.0.1/1883' 2>/dev/null && exit 0
    sleep 1
done

echo "[garagestack] mosquitto did not become ready within 30s, starting anyway" >&2
