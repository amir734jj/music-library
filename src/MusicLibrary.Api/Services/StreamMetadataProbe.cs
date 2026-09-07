using StreamRipper;
using StreamRipper.Models;

namespace MusicLibrary.Api.Services;

public sealed record MetadataProbeResult(string RawMetadata, string? Artist, string? Title);

public interface IStreamMetadataProbe
{
    Task<MetadataProbeResult?> ProbeAsync(Uri streamUri, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class StreamMetadataProbe : IStreamMetadataProbe
{
    public async Task<MetadataProbeResult?> ProbeAsync(Uri streamUri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var completion = new TaskCompletionSource<MetadataProbeResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var ripper = StreamRipperFactory.New(new StreamRipperOptions { Url = streamUri, MetadataOnly = true });

        ripper.MetadataChangedHandlers += (_, eventArgs) =>
        {
            var metadata = eventArgs.SongMetadata;
            completion.TrySetResult(new MetadataProbeResult(metadata.Raw, metadata.Artist, metadata.Title));
            return Task.CompletedTask;
        };
        ripper.StreamFailedHandlers += (_, _) =>
        {
            completion.TrySetResult(null);
            return Task.CompletedTask;
        };
        ripper.StreamEndedEventHandlers += (_, _) =>
        {
            completion.TrySetResult(null);
            return Task.CompletedTask;
        };

        ripper.Start();
        using var cancellationRegistration = timeoutSource.Token.Register(() => completion.TrySetResult(null));
        return await completion.Task;
    }
}