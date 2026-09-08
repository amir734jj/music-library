using EfCoreRepository.Interfaces;

namespace MusicLibrary.Api.Services;

public sealed class EncryptedTrackCacheService(
    IGlobalConfigService configService,
    IEfRepository repository,
    ITrackCacheStorage storage) : IEncryptedTrackCacheService
{
    public async Task<CachedTrackDownload?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var config = await configService.GetAsync(cancellationToken);
        if (!TrackCacheCryptography.TryGetKey(config.TrendingCacheEncryptionKey, out var key)) return null;
        var cached = await storage.TryReadAsync(repository, id, key, cancellationToken);
        if (cached is null) return null;

        var name = string.IsNullOrWhiteSpace(cached.Track.Title)
            ? cached.Track.Artist
            : $"{cached.Track.Artist} - {cached.Track.Title}";
        return new CachedTrackDownload(cached.Content, cached.ContentType, $"{SanitizeFileName(name)}{GetExtension(cached.ContentType)}");
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "radio-track" : sanitized;
    }

    private static string GetExtension(string contentType) => contentType.ToLowerInvariant() switch
    {
        "audio/aac" or "audio/aacp" => ".aac",
        "audio/flac" => ".flac",
        "audio/ogg" => ".ogg",
        _ => ".mp3"
    };
}