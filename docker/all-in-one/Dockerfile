# syntax=docker/dockerfile:1.7

# ── Stage 1: Build frontend ───────────────────────────────────────────────────
FROM node:24-alpine AS frontend-build
WORKDIR /app
# OS_PATCH_DATE busts the layer cache so apk upgrade actually re-runs instead of
# reusing a stale cached layer from before an OS package fix was published.
ARG OS_PATCH_DATE=0
RUN echo "os patch date: ${OS_PATCH_DATE}" && apk upgrade --no-cache && corepack enable
COPY frontend/package.json frontend/pnpm-lock.yaml frontend/pnpm-workspace.yaml ./
# No version here: corepack takes it from package.json's packageManager field, so the pnpm
# version is pinned in one place for local installs, CI and every image. pnpm 12+ is a native
# binary that corepack's wrapper downloads on first use; `pnpm --version` fetches it inside this
# cached layer and prints the version the build runs.
RUN corepack prepare --activate && pnpm --version
RUN --mount=type=cache,id=pnpm-store,target=/pnpm/store \
    pnpm config set store-dir /pnpm/store && \
    pnpm config set minimum-release-age 0 && \
    NODE_ENV=development NPM_CONFIG_PRODUCTION=false pnpm install --frozen-lockfile --ignore-scripts --prod=false --config.production=false
COPY frontend/ .
# Declared after install so a new version does not bust the dependency layer cache.
ARG APP_VERSION=0.0.0-local
ENV VITE_APP_VERSION=$APP_VERSION
# Vector basemap styles. Empty means the public OpenFreeMap styles; point these at your own tile
# server to keep map requests off a third party, and add that host to the Content-Security-Policy
# in frontend/nginx-security-headers.conf or the browser will block it.
ARG MAP_STYLE_DARK=
ARG MAP_STYLE_LIGHT=
ENV VITE_MAP_STYLE_DARK=$MAP_STYLE_DARK
ENV VITE_MAP_STYLE_LIGHT=$MAP_STYLE_LIGHT
RUN pnpm run build

# ── Stage 2: Build .NET API and Worker ────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src
ARG APP_VERSION=0.0.0-local
# Shared build settings (target framework, analyzers) and central package versions. MSBuild
# finds them by walking up from each project folder, so they sit one level above the projects.
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
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
# ${APP_VERSION#v} strips a leading "v" (e.g. a tag name), which MinVer rejects
RUN dotnet publish GarageStack.Api/GarageStack.Api.csproj -c Release -o /publish/api /p:MinVerVersionOverride=${APP_VERSION#v} \
 && dotnet publish GarageStack.Worker/GarageStack.Worker.csproj -c Release -o /publish/worker /p:MinVerVersionOverride=${APP_VERSION#v}

# ── Stage 3: Pull SAIC MQTT Gateway (source of truth for the gateway app) ────
# The gateway is not on PyPI. We copy its pre-built venv + source directly from
# the official image so we always match the tested Python/dependency versions.
FROM saicismartapi/saic-python-mqtt-gateway:0.12.0 AS saic-source

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

# OS_PATCH_DATE busts the layer cache so apt-get upgrade actually re-runs instead of
# reusing a stale cached layer from before an OS package fix was published.
ARG OS_PATCH_DATE=0

# bash          -- required by entrypoint.sh and dotnet-install.sh (slim images ship dash only)
# libgssapi-krb5-2 -- required by the .NET runtime
# libicu-dev    -- required by .NET globalization (UseRequestLocalization uses CultureInfo)
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt/lists,sharing=locked \
    echo "os patch date: ${OS_PATCH_DATE}" \
    && apt-get update \
    && apt-get upgrade -y --no-install-recommends \
    && apt-get install -y --no-install-recommends \
        bash curl gnupg ca-certificates gosu supervisor \
        nginx mosquitto mosquitto-clients \
        libgssapi-krb5-2 libicu-dev \
    && rm -rf /var/lib/apt/lists/*

# Dedicated non-root user for the .NET API/Worker and the SAIC gateway. Postgres and
# Mosquitto already run as their own unprivileged users via gosu (see supervisord.conf),
# and nginx already drops its worker processes to www-data via the Debian package default,
# only its master process stays root to bind port 80.
RUN useradd --system --create-home --home-dir /home/appuser --shell /usr/sbin/nologin appuser

# .NET 10 ASP.NET Core runtime -- installed via the official script, no apt repo needed.
RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --runtime aspnetcore --channel 10.0 --install-dir /usr/local/dotnet \
    && rm /tmp/dotnet-install.sh

ENV PATH="/usr/local/dotnet:$PATH"

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
# The gateway pins anyio 4.12.1 (transitively, via httpx), which carries CVE-2026-63374
# (TLS certificate spoofing through IDNA 2003 host name encoding) and CVE-2026-64847
# (process-pool workers deadlock on an undrained stderr pipe). Both are fixed in 4.14.2
# and the newest gateway release still ships 4.12.1, so patch the copied venv here.
# httpx 0.28 accepts any anyio 4.x, so the upgrade stays inside the gateway's own bounds.
# Drop this once the pinned gateway tag ships anyio >= 4.14.2.
#
# Nothing in this image installs Python packages at runtime, so remove pip afterwards from
# both the gateway venv and the base interpreter. Even the newest pip vendors outdated copies
# of msgpack and setuptools' pkg_resources that image scans keep flagging (GHSA-6v7p-g79w-8964,
# CVE-2025-47273, CVE-2026-59890), and dropping it also retires the earlier pip CVE patching.
RUN /opt/saic/app/.venv/bin/python -m pip install --no-cache-dir --upgrade "anyio==4.15.1" \
    && /opt/saic/app/.venv/bin/python -m pip uninstall --yes pip \
    && python -m pip uninstall --yes pip \
    && chown -R appuser:appuser /opt/saic/app

# Frontend SPA
COPY --from=frontend-build /app/dist /usr/share/nginx/html

# .NET apps
COPY --from=dotnet-build /publish/api    /app/api
COPY --from=dotnet-build /publish/worker /app/worker

# The API/Worker resolve "logs" and "keys" relative to their working directory (which
# supervisord pins to /app/api and /app/worker, see supervisord.conf), so symlink those
# names to the persisted /data volume the same way postgres/mosquitto already are. Reuses
# /data/dataprotection so upgrades from older images keep the same DataProtection keys
# (and therefore the same auth cookies) instead of silently invalidating every login.
RUN ln -sfn /data/api /app/api/logs \
 && ln -sfn /data/dataprotection /app/api/keys \
 && ln -sfn /data/worker /app/worker/logs

# Configuration
COPY docker/all-in-one/nginx.conf       /etc/nginx/conf.d/default.conf
# Same security-header snippet the frontend image uses, so the two never drift apart.
COPY frontend/nginx-security-headers.conf /etc/nginx/snippets/garagestack-security-headers.conf
COPY docker/all-in-one/mosquitto.conf   /etc/mosquitto/conf.d/garagestack.conf
COPY docker/all-in-one/supervisord.conf /etc/supervisor/supervisord.conf
COPY docker/all-in-one/entrypoint.sh   /entrypoint.sh
COPY docker/all-in-one/wait-for-postgres.sh  /wait-for-postgres.sh
COPY docker/all-in-one/wait-for-mosquitto.sh /wait-for-mosquitto.sh
COPY docker/resolve-saic-rest-uri.sh /resolve-saic-rest-uri.sh
COPY docker/mosquitto-auth.sh /mosquitto-auth.sh

RUN rm -f /etc/nginx/sites-enabled/default \
    && chmod +x /entrypoint.sh /wait-for-postgres.sh /wait-for-mosquitto.sh /resolve-saic-rest-uri.sh /mosquitto-auth.sh \
    && mkdir -p /data/db/postgres /data/db/mosquitto /data/logs /data/dataprotection /data/api /data/worker

# Port 80  -- nginx (web UI + API proxy)
# Port 1883 -- Mosquitto MQTT broker
EXPOSE 80 1883

HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=5 \
    CMD curl -fsS http://127.0.0.1:9000/health || exit 1

VOLUME ["/data"]

ENTRYPOINT ["/entrypoint.sh"]
