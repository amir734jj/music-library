namespace MusicLibrary.Contracts.Responses;

public sealed record UserAlertSummary(Guid Id, string ArtistName, string StationName, string? TrackTitle, DateTimeOffset ObservedAt);