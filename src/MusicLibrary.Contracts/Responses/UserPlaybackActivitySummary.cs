namespace MusicLibrary.Contracts.Responses;

public sealed record UserPlaybackActivitySummary(
    Guid UserId,
    string UserDisplayName,
    string PlaybackDescription,
    bool IsLiveStation,
    DateTimeOffset StartedAt);