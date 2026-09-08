using Avalonia;
using MusicLibrary.App;
using NAudio.Wave;
using System.ComponentModel;
using System.Diagnostics;
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

internal static class DesktopTrackPlayer
{
    public static async Task PlayToCompletionAsync(
        byte[] content,
        string contentType,
        string fileName,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Music Library");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}{Path.GetExtension(Path.GetFileName(fileName))}");
        await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken);

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                await PlayWithCommandAsync(temporaryPath, cancellationToken);
                return;
            }

            using var reader = new MediaFoundationReader(temporaryPath);
            using var output = new WaveOutEvent();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            output.PlaybackStopped += (_, eventArgs) =>
            {
                if (eventArgs.Exception is not null)
                {
                    completion.TrySetException(eventArgs.Exception);
                    return;
                }

                completion.TrySetResult();
            };
            using var cancellationRegistration = cancellationToken.Register(output.Stop);
            output.Init(reader);
            output.Play();
            await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static async Task PlayWithCommandAsync(string path, CancellationToken cancellationToken)
    {
        var players = OperatingSystem.IsMacOS()
            ? new[] { (Command: "afplay", Arguments: Array.Empty<string>()) }
            : new[]
            {
                (Command: "mpv", Arguments: new[] { "--no-video", "--really-quiet" }),
                (Command: "ffplay", Arguments: new[] { "-nodisp", "-autoexit", "-loglevel", "quiet" }),
                (Command: "cvlc", Arguments: new[] { "--play-and-exit", "--intf", "dummy" })
            };

        foreach (var player in players)
        {
            try
            {
                var startInfo = new ProcessStartInfo(player.Command) { UseShellExecute = false };
                foreach (var argument in player.Arguments) startInfo.ArgumentList.Add(argument);
                startInfo.ArgumentList.Add(path);
                using var process = Process.Start(startInfo);
                if (process is null) continue;
                using var cancellationRegistration = cancellationToken.Register(() => TryKill(process));
                await process.WaitForExitAsync(cancellationToken);
                if (process.ExitCode == 0) return;
            }
            catch (Win32Exception)
            {
            }
        }

        throw new InvalidOperationException(OperatingSystem.IsMacOS()
            ? "Continuous playback requires afplay on macOS."
            : "Continuous playback requires mpv, ffplay, or VLC on Linux.");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}

internal static class DesktopAuthenticationSessionStorage
{
    private static readonly string SessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Music Library",
        "authentication.json");

    public static string? Load()
    {
        try
        {
            return File.Exists(SessionPath) ? File.ReadAllText(SessionPath) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(string? value)
    {
        var temporaryPath = $"{SessionPath}.tmp";
        try
        {
            if (value is null)
            {
                File.Delete(SessionPath);
                return;
            }

            var directory = Path.GetDirectoryName(SessionPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(temporaryPath, value);
            File.Move(temporaryPath, SessionPath, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}