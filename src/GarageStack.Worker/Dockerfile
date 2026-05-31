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
RUN apt-get update \
	&& apt-get upgrade -y \
	&& apt-get install -y --no-install-recommends libgssapi-krb5-2 \
	&& rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
ENTRYPOINT ["dotnet", "GarageStack.Worker.dll"]
