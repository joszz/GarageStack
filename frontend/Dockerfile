FROM node:24-alpine AS build
WORKDIR /app
ARG VITE_DEMO_MODE=false
ENV VITE_DEMO_MODE=$VITE_DEMO_MODE
# OS_PATCH_DATE busts the layer cache so apk upgrade actually re-runs instead of
# reusing a stale cached layer from before an OS package fix was published.
ARG OS_PATCH_DATE=0
RUN echo "os patch date: ${OS_PATCH_DATE}" && apk upgrade --no-cache && corepack enable
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
# No version here: corepack takes it from package.json's packageManager field, so the pnpm
# version is pinned in one place for local installs, CI and every image. pnpm 12+ is a native
# binary that corepack's wrapper downloads on first use; `pnpm --version` fetches it inside this
# cached layer and prints the version the build runs.
RUN corepack prepare --activate && pnpm --version
RUN pnpm config set minimum-release-age 0 && pnpm install --frozen-lockfile --ignore-scripts
COPY . .
# Declared after install so a new version does not bust the dependency layer cache.
ARG APP_VERSION=0.0.0-local
ENV VITE_APP_VERSION=$APP_VERSION
# Vector basemap styles. Empty means the public OpenFreeMap styles; point these at your own tile
# server to keep map requests off a third party, and add that host to the Content-Security-Policy
# in nginx-security-headers.conf or the browser will block it.
ARG MAP_STYLE_DARK=
ARG MAP_STYLE_LIGHT=
ENV VITE_MAP_STYLE_DARK=$MAP_STYLE_DARK
ENV VITE_MAP_STYLE_LIGHT=$MAP_STYLE_LIGHT
RUN pnpm run build

FROM nginx:alpine AS runtime
ARG OS_PATCH_DATE=0
RUN echo "os patch date: ${OS_PATCH_DATE}" && apk upgrade --no-cache
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY nginx-security-headers.conf /etc/nginx/snippets/garagestack-security-headers.conf
EXPOSE 80
HEALTHCHECK --interval=10s --timeout=5s --start-period=5s --retries=5 \
	CMD wget --quiet --tries=1 --spider http://127.0.0.1/ || exit 1
