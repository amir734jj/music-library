using Microsoft.AspNetCore.Identity;

namespace MusicLibrary.Api.Data;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
}

public sealed class ApplicationRole : IdentityRole<Guid>;

public sealed class GlobalConfigRow
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

public sealed class Station
{
    public Guid Id { get; set; }
    public long DirectoryId { get; set; }
    public required string Name { get; set; }
    public required string Genre { get; set; }
    public required string StreamUrl { get; set; }
    public bool IsProbeEnabled { get; set; }
    public DateTimeOffset? LastProbedAt { get; set; }
    public DateTimeOffset? LastMetadataAt { get; set; }
    public int ConsecutiveProbeFailures { get; set; }
}

public sealed class PlayObservation
{
    public Guid Id { get; set; }
    public Guid StationId { get; set; }
    public Station Station { get; set; } = null!;
    public string RawMetadata { get; set; } = string.Empty;
    public string? Artist { get; set; }
    public string? Title { get; set; }
    public decimal Confidence { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
}

public sealed class ArtistSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public required string ArtistName { get; set; }
    public required string NormalizedArtistName { get; set; }
    public bool CaptureEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

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