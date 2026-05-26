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
RUN apt-get update \
	&& apt-get install -y --no-install-recommends libgssapi-krb5-2 \
	&& rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "GarageStack.Api.dll"]
