using MusicLibrary.Api.Data;
using MusicLibrary.Api.Services;
using MusicLibrary.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MusicLibrary.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(UserManager<ApplicationUser> userManager, IJwtTokenService tokenService) : MusicLibraryControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        // The first registered account bootstraps itself as the administrator.
        var isFirstUser = !await userManager.Users.AnyAsync(cancellationToken);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = request.Email, Email = request.Email, DisplayName = request.DisplayName };
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded) return IdentityFailure(result.Errors);

        var role = isFirstUser ? Roles.Admin : Roles.User;
        await userManager.AddToRoleAsync(user, role);
        return Created($"/api/admin/users/{user.Id}", new UserSummary(user.Id, user.Email!, user.DisplayName, [role], user.IsActive));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive || !await userManager.CheckPasswordAsync(user, request.Password)) return Unauthorized();

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(user);
        var roles = await userManager.GetRolesAsync(user);
        var (token, expiresAt) = tokenService.CreateToken(user, roles.ToList());
        return Ok(new AuthResponse(token, expiresAt, new UserSummary(user.Id, user.Email!, user.DisplayName, roles.ToList(), user.IsActive)));
    }
}
