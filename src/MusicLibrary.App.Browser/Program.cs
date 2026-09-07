using Avalonia;
using Avalonia.Browser;

namespace MusicLibrary.App.Browser;

internal static class Program
{
    private static Task Main(string[] args)
    {
        MusicLibrary.App.App.IsBrowserHost = true;
        if (args.Length > 0 && Uri.TryCreate(args[0], UriKind.Absolute, out var pageUri))
        {
            MusicLibrary.App.MusicLibraryApi.Configure(new Uri(pageUri.GetLeftPart(UriPartial.Authority)));
        }
        return BuildAvaloniaApp().StartBrowserAppAsync("out");
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<MusicLibrary.App.App>();
    }
}