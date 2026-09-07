using Avalonia;
using MusicLibrary.App;
using System.Diagnostics;

namespace MusicLibrary.App.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        NativeRadioActions.ListenAsync = streamUri =>
        {
            Process.Start(new ProcessStartInfo(streamUri.AbsoluteUri) { UseShellExecute = true });
            return Task.CompletedTask;
        };
        NativeRadioActions.DownloadAsync = (streamUri, duration) => NativeStreamDownloader.DownloadAsync(streamUri,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Music Library"), duration);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<MusicLibrary.App.App>().UsePlatformDetect().WithInterFont().LogToTrace();
    }
}