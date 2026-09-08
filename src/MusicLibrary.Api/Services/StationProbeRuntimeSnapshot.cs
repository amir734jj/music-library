namespace MusicLibrary.Api.Services;

public sealed record StationProbeRuntimeSnapshot(
    DateTimeOffset? LastBatchStartedAt,
    DateTimeOffset? LastBatchCompletedAt,
    IReadOnlyDictionary<Guid, DateTimeOffset> ActiveProbes);