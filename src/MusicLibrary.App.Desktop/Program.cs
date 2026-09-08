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
        var offlineDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Music Library");
        NativeRadioActions.SupportsStreamRecorder = true;
        NativeRadioActions.ListenAsync = streamUri =>
        {
            Process.Start(new ProcessStartInfo(streamUri.AbsoluteUri) { UseShellExecute = true });
            return Task.CompletedTask;
        };
        NativeRadioActions.DownloadAsync = (streamUri, duration) => NativeStreamDownloader.DownloadAsync(streamUri,
            offlineDirectory, duration);
        NativeRadioActions.PlayFileAsync = async (content, _, fileName) =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "Music Library");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}{Path.GetExtension(Path.GetFileName(fileName))}");
            await File.WriteAllBytesAsync(temporaryPath, content);
            Process.Start(new ProcessStartInfo(temporaryPath) { UseShellExecute = true });
        };
        NativeRadioActions.PlayFileToCompletionAsync = DesktopTrackPlayer.PlayToCompletionAsync;
        NativeRadioActions.SaveFileAsync = (content, _, fileName) =>
            NativeStreamDownloader.SaveAsync(content, fileName, offlineDirectory);
        NativeRadioActions.ListOfflineTracksAsync = () => ListOfflineTracksAsync(offlineDirectory);
        NativeRadioActions.PlayOfflineTrackAsync = key =>
        {
            Process.Start(new ProcessStartInfo(ResolveOfflineTrackPath(offlineDirectory, key)) { UseShellExecute = true });
            return Task.CompletedTask;
        };
        NativeRadioActions.DeleteOfflineTrackAsync = key =>
        {
            File.Delete(ResolveOfflineTrackPath(offlineDirectory, key));
            return Task.CompletedTask;
        };
        _ = Task.Run(UpdateDesktopAppAsync);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static Task<IReadOnlyList<OfflineTrack>> ListOfflineTracksAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        IReadOnlyList<OfflineTrack> tracks = new DirectoryInfo(directory)
            .EnumerateFiles()
            .Where(file => IsAudioFile(file.Extension))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => new OfflineTrack(
                file.Name,
                Path.GetFileNameWithoutExtension(file.Name),
                file.Length,
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)))
            .ToList();
        return Task.FromResult(tracks);
    }

    private static string ResolveOfflineTrackPath(string directory, string key)
    {
        if (key != Path.GetFileName(key)) throw new InvalidOperationException("Invalid offline recording.");
        var path = Path.Combine(directory, key);
        if (!File.Exists(path)) throw new FileNotFoundException("The offline recording no longer exists.", key);
        return path;
    }

    private static bool IsAudioFile(string extension) => extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".aac", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".flac", StringComparison.OrdinalIgnoreCase);

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