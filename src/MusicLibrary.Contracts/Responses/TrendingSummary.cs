namespace MusicLibrary.Contracts.Responses;

public sealed record TrendingSummary(
    string Artist,
    string? Title,
    int ObservationCount,
    int StationCount,
    DateTimeOffset LastObservedAt,
    Guid LastStationId,
    string LastStationName,
    string? LastStationStreamUrl,
    int? BitrateKbps,
    int? DurationMs,
    Guid? CachedTrackId,
    DateTimeOffset? CachedUntil);