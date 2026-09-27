#!/bin/bash
set -e

# Ensure volume subdirectories exist on a fresh mount
mkdir -p /data/db/postgres /data/db/mosquitto /data/logs /data/dataprotection /data/api /data/worker

PGDATA="/data/db/postgres"
POSTGRES_DB="${POSTGRES_DB:-garagestack}"
POSTGRES_USER="${POSTGRES_USER:-garagestack}"
SAIC_USER="${SAIC_USER:?SAIC_USER environment variable is required}"
SAIC_PASSWORD="${SAIC_PASSWORD:?SAIC_PASSWORD environment variable is required}"

# Derives SAIC_REST_URI from SAIC_REGION for known regions (unless already set) so
# the SAIC gateway hits the right API endpoint -- see the script for details.
. /resolve-saic-rest-uri.sh

# Auto-generate POSTGRES_PASSWORD on first run and persist it so subsequent
# restarts use the same password as the already-initialised database cluster.
if [ -z "${POSTGRES_PASSWORD:-}" ]; then
    if [ -f "/data/.postgres_password" ]; then
        POSTGRES_PASSWORD="$(cat /data/.postgres_password)"
    else
        POSTGRES_PASSWORD="$(openssl rand -hex 32)"
        echo "${POSTGRES_PASSWORD}" > /data/.postgres_password
        chmod 600 /data/.postgres_password
        echo "[garagestack] POSTGRES_PASSWORD not set -- auto-generated and saved to /data/.postgres_password"
    fi
fi

# Dedicated internal MQTT broker credentials -- decoupled from SAIC account credentials
# so a LAN-exposed broker does not leak the user's cloud account password.
MQTT_BROKER_USERNAME="${MQTT_BROKER_USERNAME:-garagestack}"
if [ -z "${MQTT_BROKER_PASSWORD:-}" ]; then
    MQTT_BROKER_PASSWORD="$(openssl rand -hex 32)"
fi

# The .NET services read the template's variables (OIDC_AUTHORITY, WIDGET_API_KEY, ...) under
# the names the template uses (src/GarageStack.Core/Configuration/EnvironmentAliases.cs), and
# treat an empty one as "use the default". Only what this container derives is set here, under
# each setting's own key, which wins over the template's variable of the same meaning.

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
export Vapid__Subject="${Vapid__Subject:-mailto:${SAIC_USER}}"

# Default false so plain-HTTP LAN installs work out of the box.
# Set AUTH_COOKIE_SECURE=true when the app is served behind a TLS-terminating proxy.
export Auth__CookieSecure="${AUTH_COOKIE_SECURE:-false}"

# CORS: the URL users open in their browser
export Cors__Origins__0="${CORS_ORIGIN:-http://localhost:8080}"

# .NET API listens on an internal port; nginx proxies port 80 to it
export ASPNETCORE_URLS="http://127.0.0.1:9000"

# /app/api/logs, /app/api/keys, and /app/worker/logs are symlinked (in the Dockerfile) into
# these /data paths, so the non-root API/Worker processes need write access here. Volume
# contents may be root-owned from an image built before these processes dropped root, or
# from a fresh named volume Docker always creates as root -- reclaim them on every start.
chown -R appuser:appuser /data/api /data/worker /data/dataprotection

# Generate Mosquitto password and ACL files: the internal broker login above, plus the optional
# Home Assistant login (HA_MQTT_USERNAME / HA_MQTT_PASSWORD, see documentation/HOME_ASSISTANT.md).
# Mosquitto 2.x refuses to load password/ACL files unless they are owned by
# the user it runs as (it checks the literal "mosquitto" account via getpwnam,
# not the process's actual uid) -- run the broker as that user via gosu below.
MQTT_BROKER_USERNAME="${MQTT_BROKER_USERNAME}" MQTT_BROKER_PASSWORD="${MQTT_BROKER_PASSWORD}" \
    sh /mosquitto-auth.sh /etc/mosquitto/conf.d
chown mosquitto:mosquitto /data/db/mosquitto

# ── PostgreSQL initialisation ──────────────────────────────────────────────────
# Guard on a dedicated marker file rather than PG_VERSION so we can recover
# from a partial initialisation: a container that crashed after initdb wrote
# PG_VERSION but before CREATE ROLE executed leaves the cluster without the
# garagestack role, causing 28P01 ("password authentication failed") on every
# subsequent start because Postgres hides role-not-found behind that error code.
DB_INIT_MARKER="/data/.db_initialized"

if [ ! -f "$DB_INIT_MARKER" ]; then
    if [ ! -f "$PGDATA/PG_VERSION" ]; then
        echo "[garagestack] Initialising PostgreSQL data directory..."
        chown -R postgres:postgres /data/db/postgres /data/logs
        gosu postgres /usr/lib/postgresql/18/bin/initdb \
            -D "$PGDATA" --auth-host=md5 --auth-local=trust -E UTF8 --locale=C
    else
        echo "[garagestack] Resuming PostgreSQL setup (partial initialisation detected)..."
        chown -R postgres:postgres /data/db/postgres /data/logs
    fi

    gosu postgres /usr/lib/postgresql/18/bin/pg_ctl -D "$PGDATA" \
        -l /data/logs/postgres-init.log start -w

    # PostgreSQL has no IF NOT EXISTS for roles or databases, so each CREATE is generated only
    # when the catalog lacks the object and run through \gexec. That keeps a rerun after a
    # partial init idempotent. ALTER ROLE ensures the password matches .postgres_password even
    # when the role already existed from a prior partial run with a different password.
    gosu postgres psql -v ON_ERROR_STOP=1 \
        -v pguser="${POSTGRES_USER}" \
        -v pgpassword="${POSTGRES_PASSWORD}" \
        -v pgdb="${POSTGRES_DB}" <<-'EOSQL'
        SELECT format('CREATE ROLE %I WITH LOGIN PASSWORD %L', :'pguser', :'pgpassword')
        WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'pguser') \gexec
        ALTER ROLE :"pguser" WITH PASSWORD :'pgpassword';
        SELECT format('CREATE DATABASE %I OWNER %I', :'pgdb', :'pguser')
        WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = :'pgdb') \gexec
        GRANT ALL PRIVILEGES ON DATABASE :"pgdb" TO :"pguser";
EOSQL

    gosu postgres /usr/lib/postgresql/18/bin/pg_ctl -D "$PGDATA" stop -w
    touch "$DB_INIT_MARKER"
    echo "[garagestack] PostgreSQL initialised."
fi

chown -R postgres:postgres /data/db/postgres /data/logs

echo "[garagestack] Starting all services via supervisord..."
exec /usr/bin/supervisord -n -c /etc/supervisor/supervisord.conf
