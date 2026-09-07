using System.Collections.Concurrent;

namespace MusicLibrary.Api.Services;

public sealed class StationProbeStatusStore
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _activeProbes = new();
    private readonly object _batchLock = new();
    private DateTimeOffset? _lastBatchStartedAt;
    private DateTimeOffset? _lastBatchCompletedAt;

    public void BatchStarted()
    {
        lock (_batchLock)
        {
            _lastBatchStartedAt = DateTimeOffset.UtcNow;
        }
    }

    public void BatchCompleted()
    {
        lock (_batchLock)
        {
            _lastBatchCompletedAt = DateTimeOffset.UtcNow;
        }
    }

    public void ProbeStarted(Guid stationId)
    {
        _activeProbes[stationId] = DateTimeOffset.UtcNow;
    }

    public void ProbeCompleted(Guid stationId)
    {
        _activeProbes.TryRemove(stationId, out _);
    }

    public StationProbeRuntimeSnapshot GetSnapshot()
    {
        DateTimeOffset? lastBatchStartedAt;
        DateTimeOffset? lastBatchCompletedAt;
        lock (_batchLock)
        {
            lastBatchStartedAt = _lastBatchStartedAt;
            lastBatchCompletedAt = _lastBatchCompletedAt;
        }

        return new StationProbeRuntimeSnapshot(
            lastBatchStartedAt,
            lastBatchCompletedAt,
            _activeProbes.ToDictionary(entry => entry.Key, entry => entry.Value));
    }
}

public sealed record StationProbeRuntimeSnapshot(
    DateTimeOffset? LastBatchStartedAt,
    DateTimeOffset? LastBatchCompletedAt,
    IReadOnlyDictionary<Guid, DateTimeOffset> ActiveProbes);