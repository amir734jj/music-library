namespace MusicLibrary.Contracts.Responses;

public sealed record ArtistSubscriptionSummary(Guid Id, string ArtistName, DateTimeOffset CreatedAt, bool CaptureEnabled);