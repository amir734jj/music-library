using MusicLibrary.Contracts.Constants;

namespace MusicLibrary.Contracts.Responses;

public sealed record LoginAuthenticationResult(string AccessToken, DateTimeOffset ExpiresAt, UserSummary User)
    : AuthenticationResult(AuthenticationResultType.Login, User);