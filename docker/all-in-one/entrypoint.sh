#!/bin/bash
set -e

# Ensure volume subdirectories exist on a fresh mount
mkdir -p /data/db/postgres /data/db/mosquitto /data/logs /data/dataprotection

PGDATA="/data/db/postgres"
POSTGRES_DB="${POSTGRES_DB:-garagestack}"
POSTGRES_USER="${POSTGRES_USER:-garagestack}"
POSTGRES_PASSWORD="${POSTGRES_PASSWORD:?POSTGRES_PASSWORD environment variable is required}"
SAIC_USER="${SAIC_USER:?SAIC_USER environment variable is required}"
SAIC_PASSWORD="${SAIC_PASSWORD:?SAIC_PASSWORD environment variable is required}"

# Dedicated internal MQTT broker credentials — decoupled from SAIC account credentials
# so a LAN-exposed broker does not leak the user's cloud account password.
MQTT_BROKER_USERNAME="${MQTT_BROKER_USERNAME:-garagestack}"
if [ -z "${MQTT_BROKER_PASSWORD:-}" ]; then
    MQTT_BROKER_PASSWORD="$(openssl rand -hex 32)"
fi

# Derived connection string for .NET services
export ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"

# Internal MQTT (Mosquitto runs in this container)
export Mqtt__Host="127.0.0.1"
export Mqtt__Port="1883"
export Mqtt__Username="${MQTT_BROKER_USERNAME}"
export Mqtt__Password="${MQTT_BROKER_PASSWORD}"
export MQTT_URI="tcp://127.0.0.1:1883"
export MQTT_USER="${MQTT_BROKER_USERNAME}"
export MQTT_PASSWORD="${MQTT_BROKER_PASSWORD}"

# VAPID subject defaults to the SAIC account email
export Vapid__PublicKey="${VAPID_PUBLIC_KEY:-}"
export Vapid__PrivateKey="${VAPID_PRIVATE_KEY:-}"
export Vapid__Subject="${Vapid__Subject:-mailto:${SAIC_USER}}"

# JWT
export Jwt__Secret="${JWT_SECRET:?JWT_SECRET environment variable is required}"
export Auth__Username="${SAIC_USER}"
export Auth__Password="${SAIC_PASSWORD}"
# Default false: this container is usually served over plain HTTP on a LAN.
# Set AUTH_COOKIE_SECURE=true when sitting behind a TLS-terminating proxy.
export Auth__CookieSecure="${AUTH_COOKIE_SECURE:-false}"

# CORS: the URL users open in their browser
export Cors__Origins__0="${CORS_ORIGIN:-http://localhost:8080}"

# Homepage widget API key (optional -- leave empty to disable the widget endpoint)
export Widget__ApiKey="${WIDGET_API_KEY:-}"

# .NET API listens on an internal port; nginx proxies port 80 to it
export ASPNETCORE_URLS="http://127.0.0.1:9000"

# Data Protection keys persist to the data volume
mkdir -p /data/dataprotection /root/.aspnet
ln -sfn /data/dataprotection /root/.aspnet/DataProtection-Keys

# Generate Mosquitto password and ACL files from SAIC credentials.
mosquitto_passwd -b -c /etc/mosquitto/conf.d/passwd "${MQTT_BROKER_USERNAME}" "${MQTT_BROKER_PASSWORD}"
printf 'user %s\ntopic readwrite #\n' "${MQTT_BROKER_USERNAME}" > /etc/mosquitto/conf.d/acl
chmod 600 /etc/mosquitto/conf.d/passwd /etc/mosquitto/conf.d/acl

# ── PostgreSQL initialisation ──────────────────────────────────────────────────
if [ ! -f "$PGDATA/PG_VERSION" ]; then
    echo "[garagestack] Initialising PostgreSQL data directory..."
    chown -R postgres:postgres /data/db
    gosu postgres /usr/lib/postgresql/18/bin/initdb \
        -D "$PGDATA" --auth-host=md5 --auth-local=trust -E UTF8 --locale=C

    # Start temporarily to create the role and database
    gosu postgres /usr/lib/postgresql/18/bin/pg_ctl -D "$PGDATA" \
        -l /data/logs/postgres-init.log start -w

    gosu postgres psql -v ON_ERROR_STOP=1 \
        -v pguser="${POSTGRES_USER}" \
        -v pgpassword="${POSTGRES_PASSWORD}" \
        -v pgdb="${POSTGRES_DB}" <<-'EOSQL'
        CREATE USER :"pguser" WITH PASSWORD :'pgpassword';
        CREATE DATABASE :"pgdb" OWNER :"pguser";
        GRANT ALL PRIVILEGES ON DATABASE :"pgdb" TO :"pguser";
EOSQL

    gosu postgres /usr/lib/postgresql/18/bin/pg_ctl -D "$PGDATA" stop -w
    echo "[garagestack] PostgreSQL initialised."
fi

chown -R postgres:postgres /data/db

echo "[garagestack] Starting all services via supervisord..."
exec /usr/bin/supervisord -n -c /etc/supervisor/supervisord.conf
