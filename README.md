# Music Library

An Avalonia client suite and ASP.NET Core backend for discovering currently playing radio artists, following artists, and managing a curated monitored-station network.

The API owns radio probing. It is disabled by default and only probes stations explicitly marked as enabled. This prevents a deployment from opening unbounded connections to the Shoutcast directory.

## Hosts

- `MusicLibrary.Api`: PostgreSQL, Entity Framework Core, ASP.NET Core Identity, JWT API, administration, and bounded probe worker.
- `MusicLibrary.App`: shared Avalonia views and visual rules.
- `MusicLibrary.App.Desktop`: desktop host.
- `MusicLibrary.App.Android`: Android host.
- `MusicLibrary.App.Browser`: WebAssembly host for the user and administrator web experience.

Configure `ConnectionStrings:MusicLibrary` and replace `Jwt:Key` before starting the API. The first account created through `POST /api/auth/register` is automatically granted the `Admin` role; that admin can promote other users to `Admin` later via `PUT /api/admin/users/{id}`.

## Building locally

Prerequisites:

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) (`dotnet --list-sdks` should show a `10.0.x` entry).
- A PostgreSQL instance reachable via the connection string in `src/MusicLibrary.Api/appsettings.json` (`ConnectionStrings:MusicLibrary`).
- A JDK and the .NET Android workload, only if you intend to build `MusicLibrary.App.Android` (see below).

Restore and build the non-Android hosts:

```bash
dotnet build src/MusicLibrary.Api/MusicLibrary.Api.csproj
dotnet build src/MusicLibrary.App.Desktop/MusicLibrary.App.Desktop.csproj
dotnet build src/MusicLibrary.App.Browser/MusicLibrary.App.Browser.csproj
```

### Building the Android host

`MusicLibrary.App.Android` targets `net10.0-android36.0` (the version must match the installed `Microsoft.Android.Ref` pack; check with `dotnet workload list`). One-time setup:

```bash
# Install the .NET Android workload
dotnet workload install android

# Download the Android SDK + accept licenses (adjust paths as needed)
dotnet build src/MusicLibrary.App.Android/MusicLibrary.App.Android.csproj \
  -t:InstallAndroidDependencies -f net10.0-android36.0 \
  -p:AndroidSdkDirectory=$HOME/Android/Sdk \
  -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk-amd64 \
  -p:AcceptAndroidSdkLicenses=True
```

Then build with the same SDK paths on every subsequent build (or set them as persistent MSBuild/environment properties):

```bash
dotnet build src/MusicLibrary.App.Android/MusicLibrary.App.Android.csproj \
  -p:AndroidSdkDirectory=$HOME/Android/Sdk \
  -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk-amd64
```

## Android release

The GitHub Actions workflow in `.github/workflows/android-release.yml` builds a debug-signed Android APK on every `master` push and updates the single prerelease tag named `latest`. Because the APK is debug-signed (not a persistent release keystore), it installs with Android's "unknown/untrusted developer" warning and updates may require uninstalling the previous build first.

## Web container

The root `Dockerfile` publishes `MusicLibrary.App.Browser`, copies the static WebAssembly assets into the API's `wwwroot`, and serves both the SPA and API from one ASP.NET Core container on port `8080`. The container health check calls `GET /api/health`.

```bash
docker build --tag music-library-web .
docker run --rm --publish 8080:8080 music-library-web
```

Supply production database and JWT configuration with environment variables, for example `ConnectionStrings__MusicLibrary` and `Jwt__Key`.