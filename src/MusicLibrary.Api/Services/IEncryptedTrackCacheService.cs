namespace MusicLibrary.Api.Services;

public interface IEncryptedTrackCacheService
{
    Task<CachedTrackDownload?> GetAsync(Guid id, CancellationToken cancellationToken);
}