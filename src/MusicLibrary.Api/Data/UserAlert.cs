namespace MusicLibrary.Api.Data;

public sealed class UserAlert
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public Guid ArtistSubscriptionId { get; set; }
    public ArtistSubscription ArtistSubscription { get; set; } = null!;
    public Guid PlayObservationId { get; set; }
    public PlayObservation PlayObservation { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}