FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ARG APP_VERSION=0.0.0-local
# Shared build settings (target framework, analyzers) and central package versions. MSBuild
# finds them by walking up from each project folder, so they sit one level above the projects.
COPY ["Directory.Build.props", "Directory.Packages.props", ".editorconfig", "./"]
COPY ["src/GarageStack.Core/GarageStack.Core.csproj", "GarageStack.Core/"]
COPY ["src/GarageStack.Data/GarageStack.Data.csproj", "GarageStack.Data/"]
COPY ["src/GarageStack.Worker/GarageStack.Worker.csproj", "GarageStack.Worker/"]
RUN dotnet restore "GarageStack.Worker/GarageStack.Worker.csproj"
COPY src/GarageStack.Core/ GarageStack.Core/
COPY src/GarageStack.Data/ GarageStack.Data/
COPY src/GarageStack.Worker/ GarageStack.Worker/
WORKDIR /src/GarageStack.Worker
# ${APP_VERSION#v} strips a leading "v" (e.g. a tag name), which MinVer rejects
RUN dotnet publish -c Release -o /app /p:MinVerVersionOverride=${APP_VERSION#v}

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
# OS_PATCH_DATE busts the layer cache so apt-get upgrade actually re-runs instead of
# reusing a stale cached layer from before an OS package fix was published.
ARG OS_PATCH_DATE=0
RUN echo "os patch date: ${OS_PATCH_DATE}" \
	&& apt-get update \
	&& apt-get upgrade -y \
	&& apt-get install -y --no-install-recommends libgssapi-krb5-2 tzdata \
	&& rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
COPY --chmod=755 src/GarageStack.Worker/docker-entrypoint.sh /docker-entrypoint.sh
# The Worker has no HTTP listener to probe (unlike Api/frontend). Its heartbeat
# (WorkerHeartbeatService) touches this file every 15 seconds while it is subscribed to the
# broker and reaches the database, so a file older than a minute means it lost one of them,
# hangs, or stopped.
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
	CMD test -n "$(find /tmp/garagestack-worker.heartbeat -mmin -1 2>/dev/null)" || exit 1
ENTRYPOINT ["/docker-entrypoint.sh"]
