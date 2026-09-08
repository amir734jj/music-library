using MusicLibrary.Contracts.Constants;

namespace MusicLibrary.Contracts.Responses;

public sealed record RegistrationAuthenticationResult(UserSummary User)
    : AuthenticationResult(AuthenticationResultType.Registration, User);