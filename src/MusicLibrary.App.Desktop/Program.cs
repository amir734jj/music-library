using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using MusicLibrary.App;
using MusicLibrary.App.Services;
using Serilog;
using Velopack;

namespace MusicLibrary.App.Desktop;

internal static class Program
{
    private static CancellationTokenSource? _playbackCancellation;

    [STAThread]
    public static void Main(string[] args)
    {
        MusicLibraryApi.Configure(new Uri("https://music-library.coolify.hesamian.com/"));
        AppLogging.ConfigureFromApiAsync("desktop").GetAwaiter().GetResult();
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            Log.Fatal(eventArgs.ExceptionObject as Exception, "Unhandled desktop application exception");
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Log.Error(eventArgs.Exception, "Unobserved desktop task exception");
            eventArgs.SetObserved();
        };

        try
        {
            Log.Information("Starting Music Library desktop application");
            VelopackApp.Build().Run();
            AuthenticationSessionStorage.Load = DesktopAuthenticationSessionStorage.Load;
            AuthenticationSessionStorage.Save = DesktopAuthenticationSessionStorage.Save;
            var offlineDirectory = ResolveOfflineDirectory();
            NativeRadioActions.OfflineDirectoryPath = offlineDirectory;
            NativeRadioActions.SetOfflineDirectoryAsync = newDirectory =>
            {
                offlineDirectory = MoveOfflineDirectory(offlineDirectory, newDirectory);
                NativeRadioActions.OfflineDirectoryPath = offlineDirectory;
                DesktopSettingsStorage.SaveOfflineDirectory(offlineDirectory);
                return Task.CompletedTask;
            };
            using (var iconStream = AssetLoader.Open(new Uri("avares://MusicLibrary.App.Desktop/Assets/icon.png")))
            {
                AppIcon.Icon = new WindowIcon(iconStream);
            }
            NativeRadioActions.ListenAsync = streamUri =>
            {
                StartNativePlayback(cancellation => DesktopTrackPlayer.PlayStreamAsync(streamUri, cancellation));
                return Task.CompletedTask;
            };
            NativeRadioActions.PlayFileAsync = (content, contentType, fileName) =>
            {
                StartNativePlayback(cancellation => DesktopTrackPlayer.PlayToCompletionAsync(content, contentType, fileName, cancellation));
                return Task.CompletedTask;
            };
            NativeRadioActions.PlayFileToCompletionAsync = DesktopTrackPlayer.PlayToCompletionAsync;
            NativeRadioActions.StopPlaybackAsync = () =>
            {
                _playbackCancellation?.Cancel();
                return Task.CompletedTask;
            };
            NativeRadioActions.SaveFileAsync = (content, _, fileName) =>
                NativeStreamDownloader.SaveAsync(content, fileName, offlineDirectory);
            NativeRadioActions.ListOfflineTracksAsync = () => ListOfflineTracksAsync(offlineDirectory);
            NativeRadioActions.PlayOfflineTrackAsync = async key =>
            {
                var path = ResolveOfflineTrackPath(offlineDirectory, key);
                var content = await File.ReadAllBytesAsync(path);
                StartNativePlayback(cancellation => DesktopTrackPlayer.PlayToCompletionAsync(content, "audio/mpeg", Path.GetFileName(path), cancellation));
            };
            NativeRadioActions.DeleteOfflineTrackAsync = key =>
            {
                File.Delete(ResolveOfflineTrackPath(offlineDirectory, key));
                return Task.CompletedTask;
            };
            _ = Task.Run(UpdateDesktopAppAsync);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Desktop application terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void StartNativePlayback(Func<CancellationToken, Task> play)
    {
        _playbackCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _playbackCancellation = cancellation;
        _ = Task.Run(async () =>
        {
            try
            {
                await play(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Desktop native playback failed");
            }
        });
    }

    private static string ResolveOfflineDirectory()
    {
        var savedDirectory = DesktopSettingsStorage.LoadOfflineDirectory();
        var directory = string.IsNullOrWhiteSpace(savedDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Music Library")
            : savedDirectory;
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string MoveOfflineDirectory(string currentDirectory, string newDirectory)
    {
        newDirectory = Path.GetFullPath(newDirectory);
        Directory.CreateDirectory(newDirectory);
        if (string.Equals(Path.GetFullPath(currentDirectory), newDirectory, StringComparison.Ordinal)) return newDirectory;

        if (Directory.Exists(currentDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(currentDirectory))
            {
                var destination = Path.Combine(newDirectory, Path.GetFileName(file));
                if (!File.Exists(destination)) File.Move(file, destination);
            }
        }

        return newDirectory;
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
            Log.Warning(exception, "Desktop update check failed");
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
    }
}