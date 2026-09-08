using MusicLibrary.Contracts.Responses;

namespace MusicLibrary.Api.Services;

public interface IGlobalConfigService
{
    Task<GlobalConfigModel> GetAsync(CancellationToken cancellationToken);
    Task EnsureTrendingCacheEncryptionKeyAsync(CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyDictionary<string, string> values, Guid userId, CancellationToken cancellationToken);
}