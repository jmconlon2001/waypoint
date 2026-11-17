# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Waypoint.Api/Waypoint.Api.csproj src/Waypoint.Api/
RUN dotnet restore src/Waypoint.Api/Waypoint.Api.csproj
COPY src/ src/
RUN dotnet publish src/Waypoint.Api/Waypoint.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DataProtection__KeysPath=/data/keys
# Data Protection keys (encrypt the Google refresh token) live on a volume mounted here; the
# directory is created owned by the app user so a fresh named volume inherits that ownership.
RUN mkdir -p /data/keys && chown -R $APP_UID /data
COPY --from=build /app .
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Waypoint.Api.dll"]
