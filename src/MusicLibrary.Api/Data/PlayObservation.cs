namespace MusicLibrary.Api.Data;

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