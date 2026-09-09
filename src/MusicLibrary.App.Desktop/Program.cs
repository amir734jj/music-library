using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Threading;
using MusicLibrary.App;
using MusicLibrary.App.Services;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace MusicLibrary.App.Desktop;

internal static class Program
{
    private static CancellationTokenSource? _playbackCancellation;

    [STAThread]
    public static void Main(string[] args)
    {
        if (RestartWithBundledLibVlc(args)) return;

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
            using var stationCacheSynchronizer = new DesktopStationCacheSynchronizer(offlineDirectory);
            NativeRadioActions.OfflineDirectoryPath = offlineDirectory;
            NativeRadioActions.SetOfflineDirectoryAsync = newDirectory =>
            {
                offlineDirectory = MoveOfflineDirectory(offlineDirectory, newDirectory);
                stationCacheSynchronizer.SetCacheDirectory(offlineDirectory);
                NativeRadioActions.OfflineDirectoryPath = offlineDirectory;
                DesktopSettingsStorage.SaveOfflineDirectory(offlineDirectory);
                return Task.CompletedTask;
            };
            NativeRadioActions.ListenAsync = streamUri =>
            {
                StartNativePlayback(cancellation => DesktopTrackPlayer.PlayStreamAsync(streamUri, cancellation));
                return Task.CompletedTask;
            };
            NativeRadioActions.ListenToStationAsync = async stationId =>
            {
                var streamUri = await MusicLibraryApi.CreateLiveStreamUriAsync(stationId);
                StartNativePlayback(cancellation => DesktopTrackPlayer.PlayStreamAsync(streamUri, cancellation));
            };
            NativeRadioActions.PlayFileAsync = (content, contentType, fileName) =>
            {
                StartNativePlayback(cancellation => DesktopTrackPlayer.PlayToCompletionAsync(content, contentType, fileName, cancellation));
                return Task.CompletedTask;
            };
            NativeRadioActions.PlayFileToCompletionAsync = DesktopTrackPlayer.PlayToCompletionAsync;
            NativeRadioActions.ToggleFilePlaybackAsync = DesktopTrackPlayer.TogglePlaybackAsync;
            NativeRadioActions.GetPlaybackStateAsync = DesktopTrackPlayer.GetPlaybackStateAsync;
            NativeRadioActions.StopPlaybackAsync = () =>
            {
                _playbackCancellation?.Cancel();
                return Task.CompletedTask;
            };
            NativeRadioActions.SaveFileAsync = (content, _, fileName) =>
                NativeStreamDownloader.SaveAsync(content, fileName, offlineDirectory);
            NativeRadioActions.ListOfflineTracksAsync = () => ListOfflineTracksAsync(offlineDirectory);
            NativeRadioActions.ListStationSubscriptionsAsync = () =>
                Task.FromResult<IReadOnlyList<LocalStationSubscription>>(stationCacheSynchronizer.ListSubscriptions());
            NativeRadioActions.SubscribeToStationAsync = stationCacheSynchronizer.SubscribeAsync;
            NativeRadioActions.UnsubscribeFromStationAsync = stationCacheSynchronizer.UnsubscribeAsync;
            NativeRadioActions.PlayOfflineTrackAsync = async key =>
            {
                var path = ResolveOfflineTrackPath(offlineDirectory, key);
                var content = await File.ReadAllBytesAsync(path);
                StartNativePlayback(cancellation => DesktopTrackPlayer.PlayToCompletionAsync(content, "audio/mpeg", Path.GetFileName(path), cancellation));
            };
            NativeRadioActions.PlayOfflineTrackToCompletionAsync = async (key, cancellationToken) =>
            {
                var path = ResolveOfflineTrackPath(offlineDirectory, key);
                var content = await File.ReadAllBytesAsync(path, cancellationToken);
                await DesktopTrackPlayer.PlayToCompletionAsync(content, "audio/mpeg", Path.GetFileName(path), cancellationToken);
            };
            NativeRadioActions.DeleteOfflineTrackAsync = key =>
            {
                var path = ResolveOfflineTrackPath(offlineDirectory, key);
                File.Delete(path);
                var parent = Directory.GetParent(path);
                if (parent is not null
                    && !string.Equals(parent.FullName, Path.GetFullPath(offlineDirectory), StringComparison.OrdinalIgnoreCase)
                    && !parent.EnumerateFileSystemInfos().Any())
                {
                    parent.Delete();
                }
                return Task.CompletedTask;
            };
            BuildAvaloniaApp()
                .AfterSetup(_ =>
                {
                    using var iconStream = AssetLoader.Open(new Uri("avares://MusicLibrary.App.Desktop/Assets/icon.png"));
                    AppIcon.Icon = new WindowIcon(iconStream);
                    Task.Run(UpdateDesktopAppAsync);
                })
                .StartWithClassicDesktopLifetime(args);
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

    private static bool RestartWithBundledLibVlc(string[] args)
    {
        if (!OperatingSystem.IsLinux()) return false;

        var vlcDirectory = Path.Combine(AppContext.BaseDirectory, "vlc");
        var libraryDirectory = Path.Combine(vlcDirectory, "lib");
        var pluginDirectory = Path.Combine(vlcDirectory, "plugins");
        if (!Directory.Exists(libraryDirectory) || !Directory.Exists(pluginDirectory)) return false;
        if (string.Equals(Environment.GetEnvironmentVariable("MUSIC_LIBRARY_LIBVLC_PATH"), libraryDirectory, StringComparison.Ordinal))
        {
            return false;
        }

        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the desktop application executable path.");
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false
        };
        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var existingLibraryPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        startInfo.Environment["LD_LIBRARY_PATH"] = string.IsNullOrWhiteSpace(existingLibraryPath)
            ? libraryDirectory
            : $"{libraryDirectory}{Path.PathSeparator}{existingLibraryPath}";
        startInfo.Environment["VLC_PLUGIN_PATH"] = pluginDirectory;
        startInfo.Environment["MUSIC_LIBRARY_LIBVLC_PATH"] = libraryDirectory;
        Process.Start(startInfo);
        return true;
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
            foreach (var file in Directory.EnumerateFiles(currentDirectory, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(newDirectory, Path.GetRelativePath(currentDirectory, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (!File.Exists(destination)) File.Move(file, destination);
            }
        }

        return newDirectory;
    }

    private static Task<IReadOnlyList<OfflineTrack>> ListOfflineTracksAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        IReadOnlyList<OfflineTrack> tracks = new DirectoryInfo(directory)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(file => IsAudioFile(file.Extension))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => new OfflineTrack(
                Path.GetRelativePath(directory, file.FullName),
                GetOfflineTrackName(file.Name),
                file.Length,
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
                GetStationName(directory, file)))
            .ToList();
        return Task.FromResult(tracks);
    }

    private static string ResolveOfflineTrackPath(string directory, string key)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, key));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid offline recording.");
        }
        if (!File.Exists(path)) throw new FileNotFoundException("The offline recording no longer exists.", key);
        return path;
    }

    private static string? GetStationName(string directory, FileInfo file)
    {
        var relativeDirectory = Path.GetRelativePath(directory, file.DirectoryName!);
        return relativeDirectory == "." ? null : relativeDirectory.Split(Path.DirectorySeparatorChar)[0];
    }

    private static string GetOfflineTrackName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var idStart = name.LastIndexOf(" [", StringComparison.Ordinal);
        return idStart >= 0 && name.Length - idStart == 35 && name.EndsWith(']')
            ? name[..idStart]
            : name;
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
            var source = new GithubSource("https://github.com/amir734jj/music-library", null, prerelease: true);
            var updateManager = new UpdateManager(source);
            if (!updateManager.IsInstalled) return;

            var update = await updateManager.CheckForUpdatesAsync();
            if (update is null) return;
            if (!await ConfirmUpdateAsync(update.TargetFullRelease.Version.ToString())) return;

            await updateManager.DownloadUpdatesAsync(update);
            updateManager.ApplyUpdatesAndRestart(update.TargetFullRelease);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Desktop update check failed");
        }
    }

    private static Task<bool> ConfirmUpdateAsync(string version)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (owner is null)
                {
                    completion.TrySetResult(false);
                    return;
                }

                var downloadButton = new Button { Content = "Download and restart" };
                var laterButton = new Button { Content = "Later" };
                var dialog = new Window
                {
                    Title = "Music Library update",
                    Width = 420,
                    SizeToContent = SizeToContent.Height,
                    CanResize = false,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(24),
                        Spacing = 16,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"Music Library {version} is available. Download it and restart now?",
                                TextWrapping = Avalonia.Media.TextWrapping.Wrap
                            },
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                HorizontalAlignment = HorizontalAlignment.Right,
                                Spacing = 8,
                                Children = { laterButton, downloadButton }
                            }
                        }
                    }
                };
                laterButton.Click += (_, _) => dialog.Close(false);
                downloadButton.Click += (_, _) => dialog.Close(true);
                completion.TrySetResult(await dialog.ShowDialog<bool>(owner));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
    }
}