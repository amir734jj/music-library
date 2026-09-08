using MusicLibrary.Api.Data;

namespace MusicLibrary.Api.Services;

public interface IJwtTokenService
{
    (string Token, DateTimeOffset ExpiresAt) CreateToken(ApplicationUser user, IReadOnlyCollection<string> roles);
}