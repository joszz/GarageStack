#!/bin/bash
set -e

# Ensure volume subdirectories exist on a fresh mount
mkdir -p /data/db/postgres /data/db/mosquitto /data/logs /data/dataprotection

PGDATA="/data/db/postgres"
POSTGRES_DB="${POSTGRES_DB:-garagestack}"
POSTGRES_USER="${POSTGRES_USER:-garagestack}"
POSTGRES_PASSWORD="${POSTGRES_PASSWORD:?POSTGRES_PASSWORD environment variable is required}"

# Derived connection string for .NET services
export ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"

# Internal MQTT (Mosquitto runs in this container)
export Mqtt__Host="127.0.0.1"
export Mqtt__Port="1883"
export MQTT_URI="tcp://127.0.0.1:1883"

# VAPID subject defaults to the SAIC account email
export Vapid__PublicKey="${VAPID_PUBLIC_KEY:-}"
export Vapid__PrivateKey="${VAPID_PRIVATE_KEY:-}"
export Vapid__Subject="${Vapid__Subject:-mailto:${SAIC_USER}}"

# JWT
export Jwt__Secret="${JWT_SECRET:?JWT_SECRET environment variable is required}"

# CORS: the URL users open in their browser
export Cors__Origins__0="${CORS_ORIGIN:-http://localhost:8080}"

# .NET API listens on an internal port; nginx proxies port 80 to it
export ASPNETCORE_URLS="http://127.0.0.1:9000"

# Data Protection keys persist to the data volume
mkdir -p /data/dataprotection /root/.aspnet
ln -sfn /data/dataprotection /root/.aspnet/DataProtection-Keys

# ── PostgreSQL initialisation ──────────────────────────────────────────────────
if [ ! -f "$PGDATA/PG_VERSION" ]; then
    echo "[garagestack] Initialising PostgreSQL data directory..."
    chown -R postgres:postgres /data/db
    gosu postgres /usr/lib/postgresql/18/bin/initdb \
        -D "$PGDATA" --auth-host=md5 --auth-local=trust -E UTF8 --locale=C

    # Start temporarily to create the role and database
    gosu postgres /usr/lib/postgresql/18/bin/pg_ctl -D "$PGDATA" \
        -l /data/logs/postgres-init.log start -w

    gosu postgres psql -v ON_ERROR_STOP=1 <<-EOSQL
        CREATE USER "${POSTGRES_USER}" WITH PASSWORD '${POSTGRES_PASSWORD}';
        CREATE DATABASE "${POSTGRES_DB}" OWNER "${POSTGRES_USER}";
        GRANT ALL PRIVILEGES ON DATABASE "${POSTGRES_DB}" TO "${POSTGRES_USER}";
EOSQL

    gosu postgres /usr/lib/postgresql/18/bin/pg_ctl -D "$PGDATA" stop -w
    echo "[garagestack] PostgreSQL initialised."
fi

chown -R postgres:postgres /data/db

echo "[garagestack] Starting all services via supervisord..."
exec /usr/bin/supervisord -n -c /etc/supervisor/supervisord.conf
