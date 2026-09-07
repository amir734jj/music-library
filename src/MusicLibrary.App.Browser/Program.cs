using Avalonia;
using Avalonia.Browser;

namespace MusicLibrary.App.Browser;

internal static class Program
{
    private static Task Main(string[] args)
    {
        MusicLibrary.App.App.IsBrowserHost = true;
        return BuildAvaloniaApp().StartBrowserAppAsync("out");
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<MusicLibrary.App.App>();
    }
}