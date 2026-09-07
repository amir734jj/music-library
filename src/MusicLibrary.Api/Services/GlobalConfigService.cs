using System.Globalization;
using MusicLibrary.Api.Data;
using MusicLibrary.Contracts;
using Microsoft.EntityFrameworkCore;

namespace MusicLibrary.Api.Services;

public interface IGlobalConfigService
{
    Task<GlobalConfigModel> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyDictionary<string, string> values, Guid userId, CancellationToken cancellationToken);
}

public sealed class GlobalConfigService(MusicLibraryDbContext dbContext) : IGlobalConfigService
{
    public async Task<GlobalConfigModel> GetAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.GlobalConfigRows.AsNoTracking().ToDictionaryAsync(row => row.Key, row => row.Value, cancellationToken);
        return new GlobalConfigModel
        {
            DirectoryArtifactUrl = Get(rows, "DIRECTORY_ARTIFACT_URL", "https://github.com/amir734jj/shoutcast-directory-crawler/releases/download/latest/shoutcast-directory.json"),
            ProbingEnabled = GetBool(rows, "PROBING_ENABLED"),
            ProbeConcurrency = GetInt(rows, "PROBE_CONCURRENCY", 25, 1, 100),
            ProbeTimeoutSeconds = GetInt(rows, "PROBE_TIMEOUT_SECONDS", 12, 2, 60),
            ProbeBatchSize = GetInt(rows, "PROBE_BATCH_SIZE", 100, 1, 1000)
        };
    }

    public async Task SaveAsync(IReadOnlyDictionary<string, string> values, Guid userId, CancellationToken cancellationToken)
    {
        var supportedKeys = typeof(GlobalConfigModel).GetProperties()
            .Select(property => property.GetCustomAttributes(typeof(GlobalConfigColAttribute), false).OfType<GlobalConfigColAttribute>().SingleOrDefault()?.Name)
            .Where(key => key is not null)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (key, value) in values.Where(pair => supportedKeys.Contains(pair.Key)))
        {
            var row = await dbContext.GlobalConfigRows.FindAsync([key], cancellationToken)
                ?? new GlobalConfigRow { Key = key, Value = string.Empty };
            row.Value = value.Trim();
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedByUserId = userId;
            dbContext.Update(row);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string Get(IReadOnlyDictionary<string, string> rows, string key, string fallback) => rows.GetValueOrDefault(key, fallback);
    private static bool GetBool(IReadOnlyDictionary<string, string> rows, string key) => bool.TryParse(rows.GetValueOrDefault(key), out var value) && value;
    private static int GetInt(IReadOnlyDictionary<string, string> rows, string key, int fallback, int min, int max) => int.TryParse(rows.GetValueOrDefault(key), CultureInfo.InvariantCulture, out var value) ? Math.Clamp(value, min, max) : fallback;
}