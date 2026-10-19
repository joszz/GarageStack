# syntax=docker/dockerfile:1.7

# ── Stage 1: Build frontend ───────────────────────────────────────────────────
FROM node:26-alpine AS frontend-build
WORKDIR /app
# OS_PATCH_DATE busts the layer cache so apk upgrade actually re-runs instead of
# reusing a stale cached layer from before an OS package fix was published.
ARG OS_PATCH_DATE=0
RUN echo "os patch date: ${OS_PATCH_DATE}" && apk upgrade --no-cache
COPY frontend/package.json frontend/pnpm-lock.yaml frontend/pnpm-workspace.yaml ./
# No version here: npm installs the one in package.json's packageManager field, so the pnpm
# version is pinned in one place for local installs, CI and every image. Node 25+ images no
# longer bundle corepack. The pnpm package's install script links the native binary for this
# platform (musl), so npm's allowScripts policy has to let it run, and `pnpm --version` prints
# the version the build runs.
# The pnpm version is read from package.json, which hadolint cannot see.
# hadolint ignore=DL3016
RUN --mount=type=cache,id=pnpm-store,target=/pnpm/store \
    npm install --global --no-fund --no-audit --allow-scripts=pnpm "$(node -p "require('./package.json').packageManager.split('+')[0]")" && \
    pnpm --version && \
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
ARG MAP_STYLE_COLORFUL=
ENV VITE_MAP_STYLE_DARK=$MAP_STYLE_DARK
ENV VITE_MAP_STYLE_LIGHT=$MAP_STYLE_LIGHT
ENV VITE_MAP_STYLE_COLORFUL=$MAP_STYLE_COLORFUL
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
RUN dotnet restore GarageStack.Api/GarageStack.Api.csproj \
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
FROM saicismartapi/saic-python-mqtt-gateway:0.13.0-rc5 AS saic-source
# The gateway reports its version from RELEASE_VERSION, an ENV of this image that copying its
# files leaves behind, so keep it in a file for supervisord to pass on (see supervisord.conf).
# The image runs as an unprivileged user, hence /tmp. Without the variable the gateway says
# "unknown" itself, so the file says that too.
RUN printf '%s\n' "${RELEASE_VERSION:-unknown}" > /tmp/release-version

# ── Stage 4: .NET runtime ─────────────────────────────────────────────────────
# The ASP.NET Core runtime (which includes the base .NET runtime) is copied from Microsoft's own
# image, the one the separate API image runs on. Trixie replaced gnupg with sqv for apt signature
# verification, and sqv rejects SHA1-signed keys (policy change since 2026-02-01), which breaks
# the Microsoft apt repo. Copying also needs no install script fetched and run at build time.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS dotnet-runtime

# ── Stage 5: Final all-in-one runtime image ───────────────────────────────────
# python:3.14-slim is Debian trixie (13) -- the same base the saic gateway uses,
# so the copied venv's Python symlinks resolve correctly.
FROM python:3.14-slim

ARG DEBIAN_FRONTEND=noninteractive

# OS_PATCH_DATE busts the layer cache so apt-get upgrade actually re-runs instead of
# reusing a stale cached layer from before an OS package fix was published.
ARG OS_PATCH_DATE=0

# bash          -- required by entrypoint.sh and the wait-for scripts (slim images ship dash only)
# libgssapi-krb5-2 -- required by the .NET runtime
# libicu76      -- required by .NET globalization (UseRequestLocalization uses CultureInfo).
#                  The library alone, not libicu-dev, whose headers add ~74 MB. 76 is trixie's
#                  ICU version, so it moves with the base image like the pgdg suite below.
# tzdata        -- IANA time zones, which climate schedules keep their start time in. Named
#                  explicitly: trixie no longer counts it as a required package.
# No curl (http-check.py does the HTTP checks), no gosu (supervisord's user= and setpriv drop
# privileges) and no Debian supervisor package (installed with pip below).
RUN echo "os patch date: ${OS_PATCH_DATE}" \
    && apt-get update \
    && apt-get upgrade -y --no-install-recommends \
    && apt-get install -y --no-install-recommends \
        bash ca-certificates \
        nginx mosquitto \
        libgssapi-krb5-2 libicu76 tzdata \
    && rm -rf /var/lib/apt/lists/*

# Dedicated non-root user for the .NET API/Worker and the SAIC gateway. supervisord starts every
# program as its own unprivileged user (user= in supervisord.conf): postgres, mosquitto, or this
# one. nginx drops its worker processes to www-data via the Debian package default, only its
# master process stays root to bind port 80.
RUN useradd --system --create-home --home-dir /home/appuser --shell /usr/sbin/nologin appuser

# .NET 10 ASP.NET Core runtime, from the dotnet-runtime stage above.
COPY --from=dotnet-runtime /usr/share/dotnet /usr/local/dotnet

# PostgreSQL's own bin directory comes first, so psql, pg_dump and the rest run directly rather
# than through postgresql-common's Perl wrappers in /usr/bin, which need the Perl removed below.
ENV PATH="/usr/lib/postgresql/18/bin:/usr/local/dotnet:$PATH"

# PostgreSQL 18 -- official apt repo, trixie suite. apt reads the ASCII-armored key as is.
ADD --chmod=644 https://www.postgresql.org/media/keys/ACCC4CF8.asc /usr/share/keyrings/postgresql.asc
# postgresql-common pulls in ssl-cert, which generates a self-signed "snakeoil" key pair at
# install. Nothing here serves TLS (initdb leaves ssl off), so the private key goes rather than
# sit in every image for scanners to flag.
#
# postgresql-common's scripts need Perl, and postgresql-18 requires locales: ~63 MB together that
# nothing here uses. Every PostgreSQL program runs from its own bin directory (see PATH above),
# and initdb uses the C locale, which glibc has built in. Only purging them in this same RUN
# keeps them out of the image. --force-depends leaves apt counting those dependencies as broken,
# so an apt-get install inside a running container asks for `apt --fix-broken install` first,
# which brings them back. 5.40 is trixie's Perl, so the names move with the base image like
# libicu76 above.
RUN echo "deb [signed-by=/usr/share/keyrings/postgresql.asc] http://apt.postgresql.org/pub/repos/apt trixie-pgdg main" \
        > /etc/apt/sources.list.d/pgdg.list \
    && mkdir -p /etc/postgresql-common \
    && printf "create_main_cluster = false\n" > /etc/postgresql-common/createcluster.conf \
    && apt-get update \
    && apt-get install -y --no-install-recommends postgresql-18 \
    && dpkg --purge --force-depends perl libperl5.40 perl-modules-5.40 libjson-perl locales libc-l10n \
    && rm -rf /var/lib/apt/lists/* \
    && rm -f /etc/ssl/private/ssl-cert-snakeoil.key /etc/ssl/certs/ssl-cert-snakeoil.pem

# SAIC MQTT Gateway: copy the pre-built venv and source from the official image.
# Layout in the saic image: WORKDIR=/usr/src/app, venv at /usr/src/app/.venv,
# entry point: python ./main.py
# The files stay root-owned: appuser only reads them, like the upstream image's own
# non-root user does.
COPY --from=saic-source /usr/src/app /opt/saic/app
COPY --from=saic-source /tmp/release-version /opt/saic/release-version
# The gateway pins anyio 4.12.1 (transitively, via httpx), which carries CVE-2026-63374
# (TLS certificate spoofing through IDNA 2003 host name encoding) and CVE-2026-64847
# (process-pool workers deadlock on an undrained stderr pipe). Both are fixed in 4.14.2
# and the newest gateway release still ships 4.12.1, so patch the copied venv here.
# httpx 0.28 accepts any anyio 4.x, so the upgrade stays inside the gateway's own bounds.
# Drop this once the pinned gateway tag ships anyio >= 4.14.2.
#
# Importing main.py loads every gateway module, so a venv that no longer runs on this image's
# Python, or a package the upgrade above broke, fails the build here instead of the gateway at
# startup.
#
# supervisord comes from PyPI into the image's own Python rather than from apt: Debian's package
# pulls in a whole second Python (3.13, ~23 MB). requirements.txt pins it with hashes, and
# Dependabot keeps that pin current.
#
# Nothing in this image installs Python packages at runtime, so remove pip afterwards from
# both the gateway venv and the base interpreter. Even the newest pip vendors outdated copies
# of msgpack and setuptools' pkg_resources that image scans keep flagging (GHSA-6v7p-g79w-8964,
# CVE-2025-47273, CVE-2026-59890), and dropping it also retires the earlier pip CVE patching.
# The import check runs from the app directory, as the gateway does, without moving WORKDIR.
# hadolint ignore=DL3003
RUN --mount=type=bind,source=docker/all-in-one/requirements.txt,target=/tmp/requirements.txt \
    /opt/saic/app/.venv/bin/python -m pip install --no-cache-dir --upgrade "anyio==4.15.1" \
    && cd /opt/saic/app && .venv/bin/python -B -c "import main" \
    && /opt/saic/app/.venv/bin/python -m pip uninstall --yes pip \
    && python -m pip install --no-cache-dir --require-hashes -r /tmp/requirements.txt \
    && python -m pip uninstall --yes pip

# Frontend SPA
COPY --from=frontend-build /app/dist /usr/share/nginx/html

# .NET apps
COPY --from=dotnet-build /publish/api    /app/api
COPY --from=dotnet-build /publish/worker /app/worker

# Configuration
COPY docker/all-in-one/nginx.conf       /etc/nginx/conf.d/default.conf
# Same security-header snippet the frontend image uses, so the two never drift apart.
COPY frontend/nginx-security-headers.conf /etc/nginx/snippets/garagestack-security-headers.conf
# And the same locations, behind this image's own upstream (docker/all-in-one/nginx.conf).
COPY frontend/nginx-app.conf /etc/nginx/snippets/garagestack-app.conf
COPY docker/all-in-one/mosquitto.conf   /etc/mosquitto/conf.d/garagestack.conf
COPY docker/all-in-one/supervisord.conf /etc/supervisor/supervisord.conf
COPY --chmod=755 \
    docker/all-in-one/entrypoint.sh \
    docker/all-in-one/wait-for-postgres.sh \
    docker/all-in-one/wait-for-mosquitto.sh \
    docker/all-in-one/wait-for-api.sh \
    docker/all-in-one/http-check.py \
    docker/resolve-saic-rest-uri.sh \
    docker/mosquitto-auth.sh \
    /

# The API/Worker resolve "logs" and "keys" relative to their working directory (which
# supervisord pins to /app/api and /app/worker, see supervisord.conf), so symlink those
# names to the persisted /data volume the same way postgres/mosquitto already are. Reuses
# /data/dataprotection so upgrades from older images keep the same DataProtection keys
# (and therefore the same auth cookies) instead of silently invalidating every login.
RUN rm -f /etc/nginx/sites-enabled/default \
    && ln -sfn /data/api /app/api/logs \
    && ln -sfn /data/dataprotection /app/api/keys \
    && ln -sfn /data/worker /app/worker/logs \
    && mkdir -p /data/db/postgres /data/db/mosquitto /data/logs /data/dataprotection /data/api /data/worker

# Port 80  -- nginx (web UI + API proxy)
# Port 1883 -- Mosquitto MQTT broker
EXPOSE 80 1883

# The readiness check: the database, the broker and the Worker's heartbeat. The separate images
# keep the API's healthcheck on /health, since there the Worker waits for the API to be healthy;
# nothing waits on this container's health. The longer start period covers a first run, which
# initialises PostgreSQL before anything else starts.
HEALTHCHECK --interval=10s --timeout=5s --start-period=120s --retries=5 \
    CMD /http-check.py http://127.0.0.1:9000/api/health || exit 1

VOLUME ["/data"]

ENTRYPOINT ["/entrypoint.sh"]
