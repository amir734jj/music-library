using Avalonia;
using Avalonia.Browser;
using System.Runtime.InteropServices.JavaScript;
using MusicLibrary.App.Services;

namespace MusicLibrary.App.Browser;

internal static class Program
{
    private static async Task Main(string[] args)
    {
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
        NativeRadioActions.ToggleFilePlaybackAsync = BrowserAuthenticationSessionStorage.ToggleFilePlaybackAsync;
        NativeRadioActions.SaveFileAsync = (content, contentType, fileName) =>
        {
            BrowserAuthenticationSessionStorage.DownloadFile(content, contentType, fileName);
            return Task.FromResult(fileName);
        };
        if (args.Length > 0 && Uri.TryCreate(args[0], UriKind.Absolute, out var pageUri))
        {
            MusicLibraryApi.Configure(new Uri(pageUri.GetLeftPart(UriPartial.Authority)));
        }
        await BuildAvaloniaApp().StartBrowserAppAsync("out");
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>();
    }
}