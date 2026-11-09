FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Telemetry-Analytics_API.csproj", "./"]
RUN dotnet restore "Telemetry-Analytics_API.csproj"

COPY . .
RUN dotnet publish "Telemetry-Analytics_API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.Microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Telemetry-Analytics_API.dll"]
