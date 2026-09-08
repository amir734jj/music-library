using Avalonia;
using Avalonia.Browser;
using System.Runtime.InteropServices.JavaScript;

namespace MusicLibrary.App.Browser;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        await JSHost.ImportAsync(BrowserAuthenticationSessionStorage.ModuleName, "/authenticationSession.js");
        MusicLibrary.App.App.IsBrowserHost = true;
        MusicLibrary.App.AuthenticationSessionStorage.Load = BrowserAuthenticationSessionStorage.Load;
        MusicLibrary.App.AuthenticationSessionStorage.Save = BrowserAuthenticationSessionStorage.Save;
        MusicLibrary.App.NativeRadioActions.PlayFileAsync =
            (content, contentType, _) => BrowserAuthenticationSessionStorage.PlayFileAsync(content, contentType);
        MusicLibrary.App.NativeRadioActions.SaveFileAsync = (content, contentType, fileName) =>
        {
            BrowserAuthenticationSessionStorage.DownloadFile(content, contentType, fileName);
            return Task.FromResult(fileName);
        };
        if (args.Length > 0 && Uri.TryCreate(args[0], UriKind.Absolute, out var pageUri))
        {
            MusicLibrary.App.MusicLibraryApi.Configure(new Uri(pageUri.GetLeftPart(UriPartial.Authority)));
        }
        await BuildAvaloniaApp().StartBrowserAppAsync("out");
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<MusicLibrary.App.App>();
    }
}