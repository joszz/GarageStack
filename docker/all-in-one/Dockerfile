# syntax=docker/dockerfile:1.7

# ── Stage 1: Build frontend ───────────────────────────────────────────────────
FROM node:24-alpine AS frontend-build
WORKDIR /app
RUN corepack enable && corepack prepare pnpm@11.1.3 --activate
COPY frontend/package.json frontend/pnpm-lock.yaml ./
RUN --mount=type=cache,id=pnpm-store,target=/pnpm/store \
    pnpm config set store-dir /pnpm/store \
    && NODE_ENV=development NPM_CONFIG_PRODUCTION=false pnpm install --frozen-lockfile --prod=false --config.production=false
COPY frontend/ .
RUN pnpm run build

# ── Stage 2: Build .NET API and Worker ────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src
COPY src/GarageStack.Core/GarageStack.Core.csproj GarageStack.Core/
COPY src/GarageStack.Data/GarageStack.Data.csproj GarageStack.Data/
COPY src/GarageStack.Api/GarageStack.Api.csproj GarageStack.Api/
COPY src/GarageStack.Worker/GarageStack.Worker.csproj GarageStack.Worker/
RUN --mount=type=cache,id=nuget-packages,target=/root/.nuget/packages \
    dotnet restore GarageStack.Api/GarageStack.Api.csproj \
    && dotnet restore GarageStack.Worker/GarageStack.Worker.csproj
COPY src/GarageStack.Core/ GarageStack.Core/
COPY src/GarageStack.Data/ GarageStack.Data/
COPY src/GarageStack.Api/ GarageStack.Api/
COPY src/GarageStack.Worker/ GarageStack.Worker/
RUN dotnet publish GarageStack.Api/GarageStack.Api.csproj -c Release -o /publish/api \
 && dotnet publish GarageStack.Worker/GarageStack.Worker.csproj -c Release -o /publish/worker

# ── Stage 3: Pull SAIC MQTT Gateway (source of truth for the gateway app) ────
# The gateway is not on PyPI. We copy its pre-built venv + source directly from
# the official image so we always match the tested Python/dependency versions.
FROM saicismartapi/saic-python-mqtt-gateway:latest AS saic-source

# ── Stage 4: Final all-in-one runtime image ───────────────────────────────────
# python:3.14-slim is Debian trixie (13) -- the same base the saic gateway uses,
# so the copied venv's Python symlinks resolve correctly.
#
# Trixie replaced gnupg with sqv for apt signature verification. sqv rejects
# SHA1-signed keys (policy change since 2026-02-01), which breaks the Microsoft
# apt repo. We install .NET via the official install script instead, which needs
# no apt repo at all.
FROM python:3.14-slim

ARG DEBIAN_FRONTEND=noninteractive

# bash  -- required by entrypoint.sh and dotnet-install.sh (slim images ship dash only)
# libgssapi-krb5-2 -- required by the .NET runtime
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt/lists,sharing=locked \
    apt-get update \
    && apt-get install -y --no-install-recommends \
        bash curl gnupg ca-certificates gosu supervisor \
        nginx mosquitto mosquitto-clients \
        libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

# .NET 10 ASP.NET Core runtime -- installed via the official script, no apt repo needed.
RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --runtime aspnetcore --channel 10.0 --install-dir /usr/local/dotnet \
    && rm /tmp/dotnet-install.sh

ENV PATH="/usr/local/dotnet:$PATH"
# Removes the libicu dependency; fine for a telemetry API that uses invariant formatting.
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true

# PostgreSQL 18 -- official apt repo, trixie suite.
RUN curl -sSL https://www.postgresql.org/media/keys/ACCC4CF8.asc \
        | gpg --dearmor > /usr/share/keyrings/postgresql.gpg \
    && echo "deb [signed-by=/usr/share/keyrings/postgresql.gpg] http://apt.postgresql.org/pub/repos/apt trixie-pgdg main" \
        > /etc/apt/sources.list.d/pgdg.list \
    && mkdir -p /etc/postgresql-common \
    && printf "create_main_cluster = false\n" > /etc/postgresql-common/createcluster.conf

RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt/lists,sharing=locked \
    apt-get update \
    && apt-get install -y --no-install-recommends postgresql-18 \
    && rm -rf /var/lib/apt/lists/*

# SAIC MQTT Gateway: copy the pre-built venv and source from the official image.
# Layout in the saic image: WORKDIR=/usr/src/app, venv at /usr/src/app/.venv,
# entry point: python ./main.py
COPY --from=saic-source /usr/src/app /opt/saic/app

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
