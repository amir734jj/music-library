namespace MusicLibrary.Api.Services;

public sealed record MetadataProbeResult(string RawMetadata, string? Artist, string? Title);