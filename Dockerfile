# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Install browser build tooling before restore so workload-provided packs are available.
RUN dotnet workload install wasm-tools --skip-manifest-update

COPY Directory.Build.props ./
COPY src/MusicLibrary.Contracts/MusicLibrary.Contracts.csproj src/MusicLibrary.Contracts/
COPY src/MusicLibrary.App/MusicLibrary.App.csproj src/MusicLibrary.App/
COPY src/MusicLibrary.App.Browser/MusicLibrary.App.Browser.csproj src/MusicLibrary.App.Browser/
RUN dotnet restore src/MusicLibrary.App.Browser/MusicLibrary.App.Browser.csproj

COPY src/MusicLibrary.Contracts/ src/MusicLibrary.Contracts/
COPY src/MusicLibrary.App/ src/MusicLibrary.App/
COPY src/MusicLibrary.App.Browser/ src/MusicLibrary.App.Browser/
RUN dotnet publish src/MusicLibrary.App.Browser/MusicLibrary.App.Browser.csproj \
    --configuration Release \
    --no-restore \
    --output /publish

FROM nginx:1.27-alpine AS runtime
COPY docker/nginx-web.conf /etc/nginx/conf.d/default.conf
COPY --from=build /publish/wwwroot/ /usr/share/nginx/html/

EXPOSE 8080
CMD ["nginx", "-g", "daemon off;"]