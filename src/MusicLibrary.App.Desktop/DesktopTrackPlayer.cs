using System.ComponentModel;
using System.Diagnostics;
using NAudio.Wave;

namespace MusicLibrary.App.Desktop;

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
            await PlaySourceAsync(temporaryPath, cancellationToken);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public static Task PlayStreamAsync(Uri streamUri, CancellationToken cancellationToken)
    {
        return PlaySourceAsync(streamUri.AbsoluteUri, cancellationToken);
    }

    private static async Task PlaySourceAsync(string source, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            await PlayWithCommandAsync(source, cancellationToken);
            return;
        }

        await using var reader = new MediaFoundationReader(source);
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
        await using var cancellationRegistration = cancellationToken.Register(output.Stop);
        output.Init(reader);
        output.Play();
        await completion.Task.WaitAsync(cancellationToken);
    }

    private static async Task PlayWithCommandAsync(string path, CancellationToken cancellationToken)
    {
        var players = new[]
        {
            (Command: "mpv", Arguments: ["--no-video", "--really-quiet"]),
            (Command: "ffplay", Arguments: ["-nodisp", "-autoexit", "-loglevel", "quiet"]),
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
                await using var cancellationRegistration = cancellationToken.Register(() => TryKill(process));
                await process.WaitForExitAsync(cancellationToken);
                if (process.ExitCode == 0) return;
            }
            catch (Win32Exception)
            {
            }
        }

        throw new InvalidOperationException("Continuous playback requires mpv, ffplay, or VLC on Linux.");
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