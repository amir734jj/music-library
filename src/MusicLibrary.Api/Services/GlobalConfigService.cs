using System.Globalization;
using System.Security.Cryptography;
using MusicLibrary.Api.Data;
using MusicLibrary.Contracts;
using EfCoreRepository.Interfaces;

namespace MusicLibrary.Api.Services;

public interface IGlobalConfigService
{
    Task<GlobalConfigModel> GetAsync(CancellationToken cancellationToken);
    Task EnsureTrendingCacheEncryptionKeyAsync(CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyDictionary<string, string> values, Guid userId, CancellationToken cancellationToken);
}

public sealed class GlobalConfigService(IEfRepository repository) : IGlobalConfigService
{
    private const string TrendingCacheEncryptionKeyName = "TRENDING_CACHE_ENCRYPTION_KEY";

    public async Task<GlobalConfigModel> GetAsync(CancellationToken cancellationToken)
    {
        var rows = (await repository.For<GlobalConfigRow>().GetAll()).ToDictionary(row => row.Key, row => row.Value);
        return new GlobalConfigModel
        {
            DirectoryArtifactUrl = Get(rows, "DIRECTORY_ARTIFACT_URL", "https://github.com/amir734jj/shoutcast-directory-crawler/releases/download/latest/shoutcast-directory.json"),
            ProbingEnabled = GetBool(rows, "PROBING_ENABLED", true),
            ProbeConcurrency = GetInt(rows, "PROBE_CONCURRENCY", 5, 1, 100),
            ProbeTimeoutSeconds = GetInt(rows, "PROBE_TIMEOUT_SECONDS", 12, 2, 60),
            ProbeBatchSize = GetInt(rows, "PROBE_BATCH_SIZE", 100, 1, 1000),
            TrendingCacheEncryptionKey = Get(rows, TrendingCacheEncryptionKeyName, string.Empty),
            TrendingCacheCaptureTimeoutSeconds = GetInt(rows, "TRENDING_CACHE_CAPTURE_TIMEOUT_SECONDS", 600, 60, 1800),
            TrendingCacheRetentionHours = GetInt(rows, "TRENDING_CACHE_RETENTION_HOURS", 24, 1, 168),
            TrendingCacheMaxSizeMegabytes = GetInt(rows, "TRENDING_CACHE_MAX_SIZE_MEGABYTES", 1024, 32, 1024)
        };
    }

    public async Task EnsureTrendingCacheEncryptionKeyAsync(CancellationToken cancellationToken)
    {
        var rows = repository.For<GlobalConfigRow>();
        var existing = await rows.Get([row => row.Key == TrendingCacheEncryptionKeyName]);
        if (existing is not null && TrackCacheCryptography.TryGetKey(existing.Value, out _)) return;

        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        if (existing is null)
        {
            await rows.Save(new GlobalConfigRow
            {
                Key = TrendingCacheEncryptionKeyName,
                Value = value,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            await rows.Update([row => row.Key == TrendingCacheEncryptionKeyName], row =>
            {
                row.Value = value;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            });
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task SaveAsync(IReadOnlyDictionary<string, string> values, Guid userId, CancellationToken cancellationToken)
    {
        var supportedKeys = typeof(GlobalConfigModel).GetProperties()
            .Select(property => property.GetCustomAttributes(typeof(GlobalConfigColAttribute), false).OfType<GlobalConfigColAttribute>().SingleOrDefault()?.Name)
            .Where(key => key is not null)
            .ToHashSet(StringComparer.Ordinal);

        var rows = repository.For<GlobalConfigRow>();
        foreach (var (key, value) in values.Where(pair => supportedKeys.Contains(pair.Key)))
        {
            var trimmedValue = value.Trim();
            var existing = await rows.Get([row => row.Key == key]);
            if (existing is null)
            {
                await rows.Save(new GlobalConfigRow { Key = key, Value = trimmedValue, UpdatedAt = DateTimeOffset.UtcNow, UpdatedByUserId = userId });
            }
            else
            {
                await rows.Update([row => row.Key == key], row =>
                {
                    row.Value = trimmedValue;
                    row.UpdatedAt = DateTimeOffset.UtcNow;
                    row.UpdatedByUserId = userId;
                });
            }
        }
    }

    private static string Get(IReadOnlyDictionary<string, string> rows, string key, string fallback)
    {
        return rows.GetValueOrDefault(key, fallback);
    }

    private static bool GetBool(IReadOnlyDictionary<string, string> rows, string key, bool fallback)
    {
        return bool.TryParse(rows.GetValueOrDefault(key), out var value) ? value : fallback;
    }

    private static int GetInt(IReadOnlyDictionary<string, string> rows, string key, int fallback, int min, int max)
    {
        return int.TryParse(rows.GetValueOrDefault(key), CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;
    }
}