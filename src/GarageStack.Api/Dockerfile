FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/GarageStack.Core/GarageStack.Core.csproj", "GarageStack.Core/"]
COPY ["src/GarageStack.Data/GarageStack.Data.csproj", "GarageStack.Data/"]
COPY ["src/GarageStack.Api/GarageStack.Api.csproj", "GarageStack.Api/"]
RUN dotnet restore "GarageStack.Api/GarageStack.Api.csproj"
COPY src/GarageStack.Core/ GarageStack.Core/
COPY src/GarageStack.Data/ GarageStack.Data/
COPY src/GarageStack.Api/ GarageStack.Api/
WORKDIR /src/GarageStack.Api
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "GarageStack.Api.dll"]
