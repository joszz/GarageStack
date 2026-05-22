FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/GarageStack.Core/GarageStack.Core.csproj", "GarageStack.Core/"]
COPY ["src/GarageStack.Data/GarageStack.Data.csproj", "GarageStack.Data/"]
COPY ["src/GarageStack.Worker/GarageStack.Worker.csproj", "GarageStack.Worker/"]
RUN dotnet restore "GarageStack.Worker/GarageStack.Worker.csproj"
COPY src/GarageStack.Core/ GarageStack.Core/
COPY src/GarageStack.Data/ GarageStack.Data/
COPY src/GarageStack.Worker/ GarageStack.Worker/
WORKDIR /src/GarageStack.Worker
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "GarageStack.Worker.dll"]
