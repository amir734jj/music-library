namespace MusicLibrary.Contracts.Responses;

public sealed record StationProbeStatusSummary(
    Guid Id,
    string Name,
    string Genre,
    string StreamUrl,
    bool IsProbeEnabled,
    bool IsProbing,
    DateTimeOffset? ProbeStartedAt,
    DateTimeOffset? LastProbedAt,
    DateTimeOffset? LastMetadataAt,
    int ConsecutiveProbeFailures);