namespace MusicLibrary.Contracts.Responses;

public sealed record ProbeStatusSummary(
    bool ProbingEnabled,
    DateTimeOffset? LastBatchStartedAt,
    DateTimeOffset? LastBatchCompletedAt,
    int ActiveProbeCount,
    int EnabledStationCount,
    int MatchingStationCount,
    int Page,
    int PageSize,
    IReadOnlyCollection<StationProbeStatusSummary> Stations);