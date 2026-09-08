namespace MusicLibrary.Contracts.Responses;

public sealed record StationSummary(Guid Id, string Name, string Genre, string StreamUrl, bool IsProbeEnabled, DateTimeOffset? LastProbedAt);