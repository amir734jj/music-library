namespace MusicLibrary.Contracts;

public sealed class GlobalConfigModel
{
    [GlobalConfigCol(Name = "DIRECTORY_ARTIFACT_URL")]
    public string DirectoryArtifactUrl { get; init; } = "https://github.com/amir734jj/shoutcast-directory-crawler/releases/download/latest/shoutcast-directory.json";

    [GlobalConfigCol(Name = "PROBING_ENABLED")]
    public bool ProbingEnabled { get; init; } = false;

    [GlobalConfigCol(Name = "PROBE_CONCURRENCY")]
    public int ProbeConcurrency { get; init; } = 25;

    [GlobalConfigCol(Name = "PROBE_TIMEOUT_SECONDS")]
    public int ProbeTimeoutSeconds { get; init; } = 12;

    [GlobalConfigCol(Name = "PROBE_BATCH_SIZE")]
    public int ProbeBatchSize { get; init; } = 100;
}