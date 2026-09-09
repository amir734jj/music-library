namespace MusicLibrary.Contracts.Responses;

public sealed record StationCachedTrackSummary(
    Guid CachedTrackId,
    string Artist,
    string? Title,
    DateTimeOffset ObservedAt,
    DateTimeOffset CachedUntil);