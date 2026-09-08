using Avalonia;
using Avalonia.Browser;
using System.Runtime.InteropServices.JavaScript;
using MusicLibrary.App.Services;
using Serilog;

namespace MusicLibrary.App.Browser;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var baseUri = args.Length > 0 && Uri.TryCreate(args[0], UriKind.Absolute, out var pageUri)
            ? new Uri(pageUri.GetLeftPart(UriPartial.Authority))
            : throw new InvalidOperationException("The browser application URL is required.");
        MusicLibraryApi.Configure(baseUri);
        await AppLogging.ConfigureFromApiAsync("browser");

        try
        {
            Log.Information("Starting Music Library browser application");
            await JSHost.ImportAsync(BrowserAuthenticationSessionStorage.ModuleName, "/authenticationSession.js");
            App.IsBrowserHost = true;
            AuthenticationSessionStorage.Load = BrowserAuthenticationSessionStorage.Load;
            AuthenticationSessionStorage.Save = BrowserAuthenticationSessionStorage.Save;
            NativeRadioActions.ListenToStationAsync = async stationId =>
            {
                var streamUri = await MusicLibraryApi.CreateLiveStreamUriAsync(stationId);
                await BrowserAuthenticationSessionStorage.ListenLiveAsync(streamUri.AbsoluteUri);
            };
            NativeRadioActions.PlayFileAsync =
                (content, contentType, _) => BrowserAuthenticationSessionStorage.PlayFileAsync(content, contentType);
            NativeRadioActions.PlayFileToCompletionAsync = async (content, contentType, _, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var registration = cancellationToken.Register(BrowserAuthenticationSessionStorage.StopPlayback);
                try
                {
                    await BrowserAuthenticationSessionStorage.PlayFileToCompletionAsync(content, contentType);
                }
                catch (Exception exception) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("Playback stopped.", exception, cancellationToken);
                }
            };
            NativeRadioActions.ToggleFilePlaybackAsync = BrowserAuthenticationSessionStorage.ToggleFilePlaybackAsync;
            NativeRadioActions.GetPlaybackStateAsync = () => Task.FromResult(BrowserAuthenticationSessionStorage.GetPlaybackState());
            NativeRadioActions.StopPlaybackAsync = () =>
            {
                BrowserAuthenticationSessionStorage.StopPlayback();
                return Task.CompletedTask;
            };
            NativeRadioActions.SaveFileAsync = (content, contentType, fileName) =>
            {
                BrowserAuthenticationSessionStorage.DownloadFile(content, contentType, fileName);
                return Task.FromResult(fileName);
            };
            await BuildAvaloniaApp().StartBrowserAppAsync("out");
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Browser application terminated unexpectedly");
            throw;
        }
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>();
    }
}