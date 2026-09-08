namespace MusicLibrary.Api.Data;

public sealed class ArtistSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public string ArtistName { get; set; } = string.Empty;
    public string NormalizedArtistName { get; set; } = string.Empty;
    public bool CaptureEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}