using NAudio.Wave;
using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Interfaces;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace MusicLibrary.App.Desktop;

internal static class DesktopTrackPlayer
{
    private static readonly object PlaybackControlsLock = new();
    private static PlaybackControls? _playbackControls;

    public static Task<int> TogglePlaybackAsync()
    {
        lock (PlaybackControlsLock)
        {
            return Task.FromResult(_playbackControls?.Toggle() ?? -1);
        }
    }

    public static Task<int> GetPlaybackStateAsync()
    {
        lock (PlaybackControlsLock)
        {
            return Task.FromResult(_playbackControls?.GetState() ?? -1);
        }
    }

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
            await PlayWithMiniAudioAsync(source, cancellationToken);
            return;
        }

        await using var reader = new MediaFoundationReader(source);
        using var output = new WaveOutEvent();
        var controls = new PlaybackControls(
            () => output.PlaybackState switch
            {
                NAudio.Wave.PlaybackState.Playing => Pause(output),
                NAudio.Wave.PlaybackState.Paused => Play(output),
                _ => -1
            },
            () => output.PlaybackState switch
            {
                NAudio.Wave.PlaybackState.Playing => 1,
                NAudio.Wave.PlaybackState.Paused => 0,
                _ => -1
            });
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
        SetPlaybackControls(controls);
        try
        {
            output.Play();
            await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            ClearPlaybackControls(controls);
        }
    }

    private static async Task PlayWithMiniAudioAsync(string source, CancellationToken cancellationToken)
    {
        using AudioEngine engine = new MiniAudioEngine();
        var format = AudioFormat.DvdHq;
        using ISoundDataProvider provider = Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? new NetworkDataProvider(engine, format, source)
                : new AssetDataProvider(engine, source);
        using var output = engine.InitializePlaybackDevice(null, format);
        using var player = new SoundPlayer(engine, format, provider);
        var controls = new PlaybackControls(
            () => player.State switch
            {
                SoundFlow.Enums.PlaybackState.Playing => Pause(player),
                SoundFlow.Enums.PlaybackState.Paused => Play(player),
                _ => -1
            },
            () => player.State switch
            {
                SoundFlow.Enums.PlaybackState.Playing => 1,
                SoundFlow.Enums.PlaybackState.Paused => 0,
                _ => -1
            });
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.EndOfStreamReached += (_, _) => completion.TrySetResult();
        output.MasterMixer.AddComponent(player);
        SetPlaybackControls(controls);
        try
        {
            output.Start();
            player.Play();
            await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            ClearPlaybackControls(controls);
            player.Stop();
            output.Stop();
            output.MasterMixer.RemoveComponent(player);
        }
    }

    private static int Pause(WaveOutEvent output)
    {
        output.Pause();
        return 0;
    }

    private static int Play(WaveOutEvent output)
    {
        output.Play();
        return 1;
    }

    private static int Pause(SoundPlayer player)
    {
        player.Pause();
        return 0;
    }

    private static int Play(SoundPlayer player)
    {
        player.Play();
        return 1;
    }

    private static void SetPlaybackControls(PlaybackControls controls)
    {
        lock (PlaybackControlsLock)
        {
            _playbackControls = controls;
        }
    }

    private static void ClearPlaybackControls(PlaybackControls controls)
    {
        lock (PlaybackControlsLock)
        {
            if (ReferenceEquals(_playbackControls, controls)) _playbackControls = null;
        }
    }

    private sealed record PlaybackControls(Func<int> Toggle, Func<int> GetState);
}