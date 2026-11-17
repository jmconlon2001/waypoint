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
    ASPNETCORE_ENVIRONMENT=Production
COPY --from=build /app .
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Waypoint.Api.dll"]
