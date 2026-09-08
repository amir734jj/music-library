namespace MusicLibrary.Api.Data;

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