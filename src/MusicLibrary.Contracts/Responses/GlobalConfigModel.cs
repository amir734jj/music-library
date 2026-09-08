using MusicLibrary.Contracts.Attributes;

namespace MusicLibrary.Contracts.Responses;

public sealed class GlobalConfigModel
{
    [GlobalConfigCol(Name = "DIRECTORY_ARTIFACT_URL")]
    public string DirectoryArtifactUrl { get; init; } = "https://github.com/amir734jj/shoutcast-directory-crawler/releases/download/latest/shoutcast-directory.json";

    [GlobalConfigCol(Name = "PROBING_ENABLED")]
    public bool ProbingEnabled { get; init; } = true;

    [GlobalConfigCol(Name = "PROBE_CONCURRENCY")]
    public int ProbeConcurrency { get; init; } = 5;

    [GlobalConfigCol(Name = "PROBE_TIMEOUT_SECONDS")]
    public int ProbeTimeoutSeconds { get; init; } = 12;

    [GlobalConfigCol(Name = "PROBE_BATCH_SIZE")]
    public int ProbeBatchSize { get; init; } = 100;

    [GlobalConfigCol(Name = "TRENDING_CACHE_ENCRYPTION_KEY")]
    public string TrendingCacheEncryptionKey { get; init; } = string.Empty;

    [GlobalConfigCol(Name = "TRENDING_CACHE_CAPTURE_TIMEOUT_SECONDS")]
    public int TrendingCacheCaptureTimeoutSeconds { get; init; } = 600;

    [GlobalConfigCol(Name = "TRENDING_MINIMUM_DURATION_SECONDS")]
    public int TrendingMinimumDurationSeconds { get; init; } = 60;

    [GlobalConfigCol(Name = "TRENDING_CACHE_RETENTION_HOURS")]
    public int TrendingCacheRetentionHours { get; init; } = 24;

    [GlobalConfigCol(Name = "TRENDING_CACHE_MAX_SIZE_MEGABYTES")]
    public int TrendingCacheMaxSizeMegabytes { get; init; } = 1024;
}