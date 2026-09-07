# Music Library

An Avalonia client suite and ASP.NET Core backend for discovering currently playing radio artists, following artists, and managing a curated monitored-station network.

The API owns radio probing. It is disabled by default and only probes stations explicitly marked as enabled. This prevents a deployment from opening unbounded connections to the Shoutcast directory.

## Hosts

- `MusicLibrary.Api`: PostgreSQL, Entity Framework Core, ASP.NET Core Identity, JWT API, administration, and bounded probe worker.
- `MusicLibrary.App`: shared Avalonia views and visual rules.
- `MusicLibrary.App.Desktop`: desktop host.
- `MusicLibrary.App.Android`: Android host.
- `MusicLibrary.App.Browser`: WebAssembly host for the user and administrator web experience.

Configure `ConnectionStrings:MusicLibrary` and replace `Jwt:Key` before starting the API. For the first startup, supply `BootstrapAdmin:Email` and `BootstrapAdmin:Password` through development secrets or environment configuration; the API creates that account and assigns `Admin`. Do not store a real bootstrap password in `appsettings.json`.

## Android release

The GitHub Actions workflow in `.github/workflows/android-release.yml` builds a signed Android APK on every `master` push and updates the single prerelease tag named `latest`. Configure these repository Action secrets before the first run:

- `ANDROID_KEYSTORE_BASE64`: Base64-encoded persistent Android keystore.
- `ANDROID_KEYSTORE_PASSWORD`: Keystore password.
- `ANDROID_KEY_ALIAS`: Signing key alias.
- `ANDROID_KEY_PASSWORD`: Signing key password.

The signing key must remain the same between releases so Android can install updates over an existing application. The workflow fails rather than publish an unsigned or ephemeral-key APK when any signing secret is absent.