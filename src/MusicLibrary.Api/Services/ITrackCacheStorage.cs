using EfCoreRepository.Interfaces;
using MusicLibrary.Api.Data;
using MusicLibrary.Contracts.Responses;

namespace MusicLibrary.Api.Services;

public interface ITrackCacheStorage
{
    string CreateEncryptedPath(Guid trackId, byte[] encryptionKey);
    bool Exists(string path);
    Task<bool> TrySaveAsync(IEfRepository repository, CachedTrack track, byte[] encrypted, long maximumCacheBytes, CancellationToken cancellationToken);
    Task<CachedTrackRead?> TryReadAsync(IEfRepository repository, Guid id, byte[] encryptionKey, CancellationToken cancellationToken);
    Task ClearAsync(IEfRepository repository, CancellationToken cancellationToken);
    Task RotateKeyAsync(IEfRepository repository, byte[] oldKey, byte[] newKey, Func<Task> saveConfiguration, CancellationToken cancellationToken);
    Task RemoveAsync(IEfRepository repository, CachedTrack track, CancellationToken cancellationToken);
    Task EnforceLimitAsync(IEfRepository repository, byte[]? encryptionKey, long maximumCacheBytes, CancellationToken cancellationToken);
    Task<TrendingCacheStatusSummary> GetStatusAsync(CancellationToken cancellationToken);
}