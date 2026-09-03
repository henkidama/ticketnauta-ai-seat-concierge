FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY NuGet.Config ./
COPY src/Ticketnauta.WebMcp.Api/Ticketnauta.WebMcp.Api.csproj src/Ticketnauta.WebMcp.Api/
RUN dotnet restore src/Ticketnauta.WebMcp.Api/Ticketnauta.WebMcp.Api.csproj --configfile NuGet.Config

COPY src/Ticketnauta.WebMcp.Api/ src/Ticketnauta.WebMcp.Api/
RUN dotnet publish src/Ticketnauta.WebMcp.Api/Ticketnauta.WebMcp.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080

USER $APP_UID
HEALTHCHECK --interval=15s --timeout=5s --start-period=35s --retries=4 \
    CMD ["dotnet", "Ticketnauta.WebMcp.Api.dll", "--health-check"]

ENTRYPOINT ["dotnet", "Ticketnauta.WebMcp.Api.dll"]
