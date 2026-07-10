FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ARG APP_VERSION=0.0.0-local
COPY ["src/GarageStack.Core/GarageStack.Core.csproj", "GarageStack.Core/"]
COPY ["src/GarageStack.Data/GarageStack.Data.csproj", "GarageStack.Data/"]
COPY ["src/GarageStack.Api/GarageStack.Api.csproj", "GarageStack.Api/"]
RUN dotnet restore "GarageStack.Api/GarageStack.Api.csproj"
COPY src/GarageStack.Core/ GarageStack.Core/
COPY src/GarageStack.Data/ GarageStack.Data/
COPY src/GarageStack.Api/ GarageStack.Api/
WORKDIR /src/GarageStack.Api
RUN dotnet publish -c Release -o /app /p:MinVerVersionOverride=${APP_VERSION}

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# OS_PATCH_DATE busts the layer cache so apt-get upgrade actually re-runs instead of
# reusing a stale cached layer from before an OS package fix was published.
ARG OS_PATCH_DATE=0
RUN echo "os patch date: ${OS_PATCH_DATE}" \
	&& apt-get update \
	&& apt-get upgrade -y \
	&& apt-get install -y --no-install-recommends libgssapi-krb5-2 gosu curl \
	&& rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
COPY src/GarageStack.Api/docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=15s --retries=5 \
	CMD curl -fsS http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["/docker-entrypoint.sh"]
