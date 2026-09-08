using JsonSubTypes;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MusicLibrary.Contracts;

public sealed record RegisterRequest(string Email, string Password, string PasswordConfirmation, string? DisplayName);
public sealed record LoginRequest(string Email, string Password);

[JsonConverter(typeof(StringEnumConverter))]
public enum AuthenticationResultType
{
	Login,
	Registration
}

[JsonConverter(typeof(JsonSubtypes), nameof(Type))]
[JsonSubtypes.KnownSubType(typeof(LoginAuthenticationResult), AuthenticationResultType.Login)]
[JsonSubtypes.KnownSubType(typeof(RegistrationAuthenticationResult), AuthenticationResultType.Registration)]
public abstract record AuthenticationResult(AuthenticationResultType Type, UserSummary User);

public sealed record LoginAuthenticationResult(string AccessToken, DateTimeOffset ExpiresAt, UserSummary User)
	: AuthenticationResult(AuthenticationResultType.Login, User);

public sealed record RegistrationAuthenticationResult(UserSummary User)
	: AuthenticationResult(AuthenticationResultType.Registration, User);

public sealed record UserSummary(Guid Id, string Email, string? DisplayName, IReadOnlyCollection<string> Roles, bool IsActive);
public sealed record StationSummary(Guid Id, string Name, string Genre, string StreamUrl, bool IsProbeEnabled, DateTimeOffset? LastProbedAt);
public sealed record StationProbeStatusSummary(
	Guid Id,
	string Name,
	string Genre,
	string StreamUrl,
	bool IsProbeEnabled,
	bool IsProbing,
	DateTimeOffset? ProbeStartedAt,
	DateTimeOffset? LastProbedAt,
	DateTimeOffset? LastMetadataAt,
	int ConsecutiveProbeFailures);
public sealed record ProbeStatusSummary(
	bool ProbingEnabled,
	DateTimeOffset? LastBatchStartedAt,
	DateTimeOffset? LastBatchCompletedAt,
	int ActiveProbeCount,
	int EnabledStationCount,
	int MatchingStationCount,
	int Page,
	int PageSize,
	IReadOnlyCollection<StationProbeStatusSummary> Stations);
public sealed record NowPlayingSummary(Guid StationId, string StationName, string? Artist, string? Title, string RawMetadata, DateTimeOffset ObservedAt, decimal Confidence);
public sealed record TrendingSummary(
	string Artist,
	string? Title,
	int ObservationCount,
	int StationCount,
	DateTimeOffset LastObservedAt,
	Guid? CachedTrackId,
	DateTimeOffset? CachedUntil);
public sealed record ArtistSubscriptionSummary(Guid Id, string ArtistName, DateTimeOffset CreatedAt, bool CaptureEnabled);
public sealed record CreateSubscriptionRequest(string ArtistName, bool CaptureEnabled);
public sealed record UpdateGlobalConfigRequest(IReadOnlyDictionary<string, string> Values);
public sealed record DirectoryImportSummary(int Created, int Updated, int Rejected);
public sealed record UpdateStationProbeRequest(bool IsProbeEnabled);
public sealed record UpdateUserRequest(string? DisplayName, bool IsActive, string? Role);
public sealed record UserAlertSummary(Guid Id, string ArtistName, string StationName, string? TrackTitle, DateTimeOffset ObservedAt);