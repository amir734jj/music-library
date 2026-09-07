using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MusicLibrary.Api.Data;
using MusicLibrary.Api.Services;
using MusicLibrary.Contracts;
using EfCoreRepository.Interfaces;
using EfCoreRepository.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MusicLibrary.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminController(
    UserManager<ApplicationUser> userManager,
    IGlobalConfigService configService,
    IStationDirectoryImportService importService,
    IEfRepository repository) : MusicLibraryControllerBase
{
    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyCollection<UserSummary>>> GetUsers(CancellationToken cancellationToken)
    {
        var accounts = await userManager.Users.AsNoTracking().OrderBy(user => user.Email).ToListAsync(cancellationToken);
        var summaries = await Task.WhenAll(accounts.Select(async user => new UserSummary(user.Id, user.Email!, user.DisplayName, (await userManager.GetRolesAsync(user)).ToList(), user.IsActive)));
        return Ok(summaries);
    }

    [HttpGet("config")]
    public Task<GlobalConfigModel> GetConfig(CancellationToken cancellationToken)
    {
        return configService.GetAsync(cancellationToken);
    }

    [HttpPut("config")]
    public async Task<IActionResult> SaveConfig(UpdateGlobalConfigRequest request, CancellationToken cancellationToken)
    {
        await configService.SaveAsync(request.Values, CurrentUserId, cancellationToken);
        return NoContent();
    }

    [HttpPost("stations/import")]
    public async Task<ActionResult<DirectoryImportSummary>> ImportStations(CancellationToken cancellationToken)
    {
        return Ok(await importService.ImportAsync(cancellationToken));
    }

    [HttpGet("stations")]
    public Task<IEnumerable<StationSummary>> GetStations() =>
        repository.For<Station>().GetAll(
            orderBy: Ordering<Station>.Asc(station => station.Name),
            project: station => new StationSummary(station.Id, station.Name, station.Genre, station.StreamUrl, station.IsProbeEnabled, station.LastProbedAt));

    [HttpPut("users/{id:guid}")]
    public async Task<IActionResult> UpdateUser(Guid id, UpdateUserRequest request)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        user.DisplayName = request.DisplayName?.Trim();
        user.IsActive = request.IsActive;
        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded) return IdentityFailure(update.Errors);

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            if (request.Role is not Roles.Admin and not Roles.User)
            {
                ModelState.AddModelError("role", "Role must be Admin or User.");
                return ValidationProblem(ModelState);
            }

            var existingRoles = await userManager.GetRolesAsync(user);
            await userManager.RemoveFromRolesAsync(user, existingRoles);
            await userManager.AddToRoleAsync(user, request.Role);
        }

        return NoContent();
    }

    private Guid CurrentUserId
    {
        get { return Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!); }
    }
}
