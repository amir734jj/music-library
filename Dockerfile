# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Install browser build tooling before restore so workload-provided packs are available.
RUN dotnet workload install wasm-tools --skip-manifest-update

COPY . .
RUN dotnet publish src/MusicLibrary.App.Browser/MusicLibrary.App.Browser.csproj \
    --configuration Release \
    --output /publish/browser
RUN mkdir -p src/MusicLibrary.Api/wwwroot && cp -a /publish/browser/out/. src/MusicLibrary.Api/wwwroot/
RUN dotnet publish src/MusicLibrary.Api/MusicLibrary.Api.csproj \
    --configuration Release \
    --output /publish/api

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /publish/api/ ./

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl --fail --silent http://127.0.0.1:8080/api/health || exit 1
ENTRYPOINT ["dotnet", "MusicLibrary.Api.dll"]