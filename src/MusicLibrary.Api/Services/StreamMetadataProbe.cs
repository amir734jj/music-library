using System.Text.RegularExpressions;
using StreamRipper.Interfaces;
using StreamRipper.Models;

namespace MusicLibrary.Api.Services;

public sealed partial class StreamMetadataProbe(
    IStreamRipperFactory streamRipperFactory,
    ILogger<StreamMetadataProbe> logger) : IStreamMetadataProbe
{
    [GeneratedRegex("(?:^|;)\\s*StreamTitle='(?<value>(?:\\\\.|[^'])*)'", RegexOptions.IgnoreCase)]
    private static partial Regex StreamTitlePattern();

    public async Task<MetadataProbeResult?> ProbeAsync(Uri streamUri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var completion = new TaskCompletionSource<MetadataProbeResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var ripper = streamRipperFactory.New(new StreamRipperOptions
        {
            Url = streamUri,
            MetadataOnly = true
        });

        ripper.MetadataChangedHandlers += (_, eventArgs) =>
        {
            var metadata = eventArgs.SongMetadata;
            completion.TrySetResult(CreateResult(metadata.Raw, metadata.Artist, metadata.Title));
        };
        ripper.StreamFailedHandlers += (_, _) =>
        {
            logger.LogWarning("Stream failed while probing station metadata from {StreamUri}.", streamUri);
            completion.TrySetResult(null);
        };
        ripper.StreamEndedEventHandlers += (_, _) =>
        {
            completion.TrySetResult(null);
        };

        ripper.Start();
        await using var cancellationRegistration = timeoutSource.Token.Register(() => completion.TrySetResult(null));
        return await completion.Task;
    }

    internal static MetadataProbeResult CreateResult(string rawMetadata, string? artist, string? title)
    {
        var raw = rawMetadata.Trim();
        var streamTitle = StreamTitlePattern().Match(raw);
        if (streamTitle.Success)
        {
            raw = streamTitle.Groups["value"].Value
                .Replace("\\'", "'", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal)
                .Trim();
        }

        if (ContainsIcyField(artist)
            || ContainsIcyField(title)
            || !TrackMetadataValidation.IsMeaningful(artist, title))
        {
            artist = null;
            title = null;
        }

        return new MetadataProbeResult(raw, NullIfWhiteSpace(artist), NullIfWhiteSpace(title));
    }

    private static bool ContainsIcyField(string? value) =>
        value?.Contains("StreamTitle=", StringComparison.OrdinalIgnoreCase) == true
        || value?.Contains("StreamUrl=", StringComparison.OrdinalIgnoreCase) == true
        || value?.Contains("StreamArtwork=", StringComparison.OrdinalIgnoreCase) == true;

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
