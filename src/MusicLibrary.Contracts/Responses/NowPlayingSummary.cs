namespace MusicLibrary.Contracts.Responses;

public sealed record NowPlayingSummary(
    Guid StationId,
    string StationName,
    string? Artist,
    string? Title,
    string RawMetadata,
    DateTimeOffset ObservedAt,
    decimal Confidence,
    string? StreamUrl);