namespace MusicLibrary.Api.Data;

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