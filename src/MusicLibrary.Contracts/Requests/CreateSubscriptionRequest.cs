namespace MusicLibrary.Contracts.Requests;

public sealed record CreateSubscriptionRequest(string ArtistName, bool CaptureEnabled);