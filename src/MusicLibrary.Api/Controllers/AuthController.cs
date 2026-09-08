using MusicLibrary.Api.Data;
using MusicLibrary.Api.Services;
using EfCoreRepository.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MusicLibrary.Contracts.Constants;
using MusicLibrary.Contracts.Requests;
using MusicLibrary.Contracts.Responses;

namespace MusicLibrary.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(UserManager<ApplicationUser> userManager, IJwtTokenService tokenService, IEfRepository repository) : MusicLibraryControllerBase
{
    private static readonly SemaphoreSlim RegistrationGate = new(1, 1);

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserSummary>> GetCurrentUser()
    {
        var user = await userManager.FindByIdAsync(CurrentUserId.ToString());
        if (user is null) return Unauthorized();

        var roles = await userManager.GetRolesAsync(user);
        return Ok(new UserSummary(user.Id, user.Email!, user.DisplayName, [.. roles], user.IsActive));
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthenticationResult>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (request.Password != request.PasswordConfirmation)
        {
            ModelState.AddModelError(nameof(request.PasswordConfirmation), "Passwords do not match.");
            return ValidationProblem(ModelState);
        }

        await RegistrationGate.WaitAsync(cancellationToken);
        try
        {
            // The first registered account bootstraps itself as the administrator.
            var isFirstUser = !await repository.For<ApplicationUser>().Any();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = request.Email,
                Email = request.Email,
                DisplayName = request.DisplayName,
                IsActive = isFirstUser
            };
            var result = await userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded) return IdentityFailure(result.Errors);

            var role = isFirstUser ? Roles.Admin : Roles.User;
            var roleResult = await userManager.AddToRoleAsync(user, role);
            if (!roleResult.Succeeded)
            {
                await userManager.DeleteAsync(user);
                return IdentityFailure(roleResult.Errors);
            }

            var summary = new UserSummary(user.Id, user.Email!, user.DisplayName, [role], user.IsActive);
            return Created($"/api/admin/users/{user.Id}", new RegistrationAuthenticationResult(summary));
        }
        finally
        {
            RegistrationGate.Release();
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthenticationResult>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password)) return Unauthorized();
        if (!user.IsActive) return Problem(statusCode: StatusCodes.Status403Forbidden, title: "This account has not been enabled by an administrator.");

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(user);
        var roles = await userManager.GetRolesAsync(user);
        var (token, expiresAt) = tokenService.CreateToken(user, [.. roles]);
        return Ok(new LoginAuthenticationResult(token, expiresAt, new UserSummary(user.Id, user.Email!, user.DisplayName,
            [.. roles], user.IsActive)));
    }
}
