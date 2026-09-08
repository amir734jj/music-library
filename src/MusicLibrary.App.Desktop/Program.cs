using Avalonia;
using System.Diagnostics;
using MusicLibrary.App.Services;
using Velopack;

namespace MusicLibrary.App.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        MusicLibraryApi.Configure(new Uri("https://music-library.coolify.hesamian.com/"));
        AuthenticationSessionStorage.Load = DesktopAuthenticationSessionStorage.Load;
        AuthenticationSessionStorage.Save = DesktopAuthenticationSessionStorage.Save;
        NativeRadioActions.ListenAsync = streamUri =>
        {
            Process.Start(new ProcessStartInfo(streamUri.AbsoluteUri) { UseShellExecute = true });
            return Task.CompletedTask;
        };
        NativeRadioActions.DownloadAsync = (streamUri, duration) => NativeStreamDownloader.DownloadAsync(streamUri,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Music Library"), duration);
        NativeRadioActions.PlayFileAsync = async (content, _, fileName) =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "Music Library");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}{Path.GetExtension(Path.GetFileName(fileName))}");
            await File.WriteAllBytesAsync(temporaryPath, content);
            Process.Start(new ProcessStartInfo(temporaryPath) { UseShellExecute = true });
        };
        NativeRadioActions.PlayFileToCompletionAsync = DesktopTrackPlayer.PlayToCompletionAsync;
        NativeRadioActions.SaveFileAsync = (content, _, fileName) => NativeStreamDownloader.SaveAsync(content, fileName,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Music Library"));
        _ = Task.Run(UpdateDesktopAppAsync);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static async Task UpdateDesktopAppAsync()
    {
        try
        {
            var updateManager = new UpdateManager("https://github.com/amir734jj/music-library/releases/download/latest");
            if (!updateManager.IsInstalled) return;

            var update = await updateManager.CheckForUpdatesAsync();
            if (update is null) return;

            await updateManager.DownloadUpdatesAsync(update);
            updateManager.ApplyUpdatesAndRestart(update.TargetFullRelease);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Desktop update check failed: {exception}");
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
    }
}