namespace MusicLibrary.Contracts.Responses;

public sealed record TrendingSummary(
    string Artist,
    string? Title,
    int ObservationCount,
    int StationCount,
    DateTimeOffset LastObservedAt,
    int? BitrateKbps,
    Guid? CachedTrackId,
    DateTimeOffset? CachedUntil);