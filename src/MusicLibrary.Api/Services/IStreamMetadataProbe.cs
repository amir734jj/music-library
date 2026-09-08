namespace MusicLibrary.Api.Services;

public interface IStreamMetadataProbe
{
    Task<MetadataProbeResult?> ProbeAsync(Uri streamUri, TimeSpan timeout, CancellationToken cancellationToken);
}