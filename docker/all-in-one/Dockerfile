# ── Stage 1: Build frontend ───────────────────────────────────────────────────
FROM node:24-alpine AS frontend-build
WORKDIR /app
RUN corepack enable && corepack prepare pnpm@latest --activate
COPY frontend/package.json frontend/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY frontend/ .
RUN pnpm run build

# ── Stage 2: Build .NET API and Worker ────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src
COPY src/GarageStack.Core/GarageStack.Core.csproj GarageStack.Core/
COPY src/GarageStack.Data/GarageStack.Data.csproj GarageStack.Data/
COPY src/GarageStack.Api/GarageStack.Api.csproj GarageStack.Api/
COPY src/GarageStack.Worker/GarageStack.Worker.csproj GarageStack.Worker/
RUN dotnet restore GarageStack.Api/GarageStack.Api.csproj \
 && dotnet restore GarageStack.Worker/GarageStack.Worker.csproj
COPY src/GarageStack.Core/ GarageStack.Core/
COPY src/GarageStack.Data/ GarageStack.Data/
COPY src/GarageStack.Api/ GarageStack.Api/
COPY src/GarageStack.Worker/ GarageStack.Worker/
RUN dotnet publish GarageStack.Api/GarageStack.Api.csproj -c Release -o /publish/api \
 && dotnet publish GarageStack.Worker/GarageStack.Worker.csproj -c Release -o /publish/worker

# ── Stage 3: Final all-in-one runtime image ───────────────────────────────────
FROM debian:bookworm-slim

# Base packages
RUN apt-get update && apt-get install -y --no-install-recommends \
        curl gnupg ca-certificates gosu supervisor \
        nginx mosquitto mosquitto-clients \
        python3 python3-pip python3-venv \
    && rm -rf /var/lib/apt/lists/*

# Microsoft packages repo for .NET 10
RUN curl -sSL https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb \
        -o /tmp/ms.deb \
    && dpkg -i /tmp/ms.deb \
    && rm /tmp/ms.deb

# PostgreSQL 17 official repo
RUN curl -sSL https://www.postgresql.org/media/keys/ACCC4CF8.asc \
        | gpg --dearmor > /usr/share/keyrings/postgresql.gpg \
    && echo "deb [signed-by=/usr/share/keyrings/postgresql.gpg] http://apt.postgresql.org/pub/repos/apt bookworm-pgdg main" \
        > /etc/apt/sources.list.d/pgdg.list

RUN apt-get update && apt-get install -y --no-install-recommends \
        aspnetcore-runtime-10.0 \
        postgresql-17 \
    && rm -rf /var/lib/apt/lists/*

# SAIC MQTT Gateway in an isolated virtualenv
RUN python3 -m venv /opt/saic-venv \
    && /opt/saic-venv/bin/pip install --no-cache-dir saic-python-mqtt-gateway

# Frontend SPA
COPY --from=frontend-build /app/dist /usr/share/nginx/html

# .NET apps
COPY --from=dotnet-build /publish/api    /app/api
COPY --from=dotnet-build /publish/worker /app/worker

# Configuration
COPY docker/all-in-one/nginx.conf       /etc/nginx/conf.d/default.conf
COPY docker/all-in-one/mosquitto.conf   /etc/mosquitto/conf.d/garagestack.conf
COPY docker/all-in-one/supervisord.conf /etc/supervisor/supervisord.conf
COPY docker/all-in-one/entrypoint.sh   /entrypoint.sh

RUN rm -f /etc/nginx/sites-enabled/default \
    && chmod +x /entrypoint.sh \
    && mkdir -p /data/db/postgres /data/db/mosquitto /data/logs /data/dataprotection

# Port 80  -- nginx (web UI + API proxy)
# Port 1883 -- Mosquitto MQTT broker
EXPOSE 80 1883

VOLUME ["/data"]

ENTRYPOINT ["/entrypoint.sh"]
