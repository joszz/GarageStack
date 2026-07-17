FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ARG APP_VERSION=0.0.0-local
COPY ["src/GarageStack.Core/GarageStack.Core.csproj", "GarageStack.Core/"]
COPY ["src/GarageStack.Data/GarageStack.Data.csproj", "GarageStack.Data/"]
COPY ["src/GarageStack.Worker/GarageStack.Worker.csproj", "GarageStack.Worker/"]
RUN dotnet restore "GarageStack.Worker/GarageStack.Worker.csproj"
COPY src/GarageStack.Core/ GarageStack.Core/
COPY src/GarageStack.Data/ GarageStack.Data/
COPY src/GarageStack.Worker/ GarageStack.Worker/
WORKDIR /src/GarageStack.Worker
RUN dotnet publish -c Release -o /app /p:MinVerVersionOverride=${APP_VERSION}

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# OS_PATCH_DATE busts the layer cache so apt-get upgrade actually re-runs instead of
# reusing a stale cached layer from before an OS package fix was published.
ARG OS_PATCH_DATE=0
RUN echo "os patch date: ${OS_PATCH_DATE}" \
	&& apt-get update \
	&& apt-get upgrade -y \
	&& apt-get install -y --no-install-recommends libgssapi-krb5-2 gosu procps \
	&& rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
COPY src/GarageStack.Worker/docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh
# The Worker has no HTTP listener to probe (unlike Api/frontend), so this only checks the
# dotnet process is still alive - weaker than a real liveness signal (can't detect a hang),
# but still correctly flips unhealthy if the process crashes or exits.
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
	CMD pgrep -f GarageStack.Worker.dll || exit 1
ENTRYPOINT ["/docker-entrypoint.sh"]
