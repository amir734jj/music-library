namespace MusicLibrary.Contracts;

public sealed record RegisterRequest(string Email, string Password, string? DisplayName);
public sealed record LoginRequest(string Email, string Password);
public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserSummary User);
public sealed record UserSummary(Guid Id, string Email, string? DisplayName, IReadOnlyCollection<string> Roles, bool IsActive);
public sealed record StationSummary(Guid Id, string Name, string Genre, string StreamUrl, bool IsProbeEnabled, DateTimeOffset? LastProbedAt);
public sealed record NowPlayingSummary(Guid StationId, string StationName, string? Artist, string? Title, string RawMetadata, DateTimeOffset ObservedAt, decimal Confidence);
public sealed record ArtistSubscriptionSummary(Guid Id, string ArtistName, DateTimeOffset CreatedAt, bool CaptureEnabled);
public sealed record CreateSubscriptionRequest(string ArtistName, bool CaptureEnabled);
public sealed record UpdateGlobalConfigRequest(IReadOnlyDictionary<string, string> Values);
public sealed record DirectoryImportSummary(int Created, int Updated, int Rejected);
public sealed record UpdateUserRequest(string? DisplayName, bool IsActive, string? Role);
public sealed record UserAlertSummary(Guid Id, string ArtistName, string StationName, string? TrackTitle, DateTimeOffset ObservedAt);