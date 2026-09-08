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
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.EndOfStreamReached += (_, _) => completion.TrySetResult();
        output.MasterMixer.AddComponent(player);
        try
        {
            output.Start();
            player.Play();
            await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            player.Stop();
            output.Stop();
            output.MasterMixer.RemoveComponent(player);
        }
    }
}