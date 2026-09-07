using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MusicLibrary.Api.Data;
using Microsoft.IdentityModel.Tokens;

namespace MusicLibrary.Api.Services;

public interface IJwtTokenService
{
    (string Token, DateTimeOffset ExpiresAt) CreateToken(ApplicationUser user, IReadOnlyCollection<string> roles);
}

public sealed class JwtTokenService(IConfiguration configuration) : IJwtTokenService
{
    private readonly string _key = configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is required.");
    private readonly string _issuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is required.");
    private readonly string _audience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is required.");

    public (string Token, DateTimeOffset ExpiresAt) CreateToken(ApplicationUser user, IReadOnlyCollection<string> roles)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(12);
        var token = new JwtSecurityToken(_issuer, _audience,
            [new(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new(ClaimTypes.Name, user.Email!), .. roles.Select(role => new Claim(ClaimTypes.Role, role))],
            expires: expiresAt.UtcDateTime, signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key)), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
