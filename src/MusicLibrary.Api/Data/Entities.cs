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
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

public sealed class Station
{
    public Guid Id { get; set; }
    public long DirectoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public bool IsProbeEnabled { get; set; } = true;
    public DateTimeOffset? LastProbedAt { get; set; }
    public DateTimeOffset? LastMetadataAt { get; set; }
    public string? CurrentRawMetadata { get; set; }
    public string? CurrentArtist { get; set; }
    public string? CurrentTitle { get; set; }
    public decimal CurrentConfidence { get; set; }
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

public sealed class CachedTrack
{
    public Guid Id { get; set; }
    public Guid PlayObservationId { get; set; }
    public PlayObservation PlayObservation { get; set; } = null!;
    public string Artist { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string NormalizedArtist { get; set; } = string.Empty;
    public string NormalizedTitle { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = "audio/mpeg";
    public long PlaintextLength { get; set; }
    public int? BitrateKbps { get; set; }
    public string KeyFingerprint { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

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