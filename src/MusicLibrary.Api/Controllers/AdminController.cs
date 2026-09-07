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
    StationProbeStatusStore probeStatusStore,
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

    [HttpPut("stations/{id:guid}/probe")]
    public async Task<IActionResult> UpdateStationProbe(Guid id, UpdateStationProbeRequest request)
    {
        var stations = repository.For<Station>();
        if (!await stations.Any([station => station.Id == id])) return NotFound();

        await stations.Update<Guid>(id, station => station.IsProbeEnabled = request.IsProbeEnabled);
        return NoContent();
    }

    [HttpPut("stations/probe")]
    public async Task<ActionResult<int>> UpdateAllStationProbes(UpdateStationProbeRequest request, CancellationToken cancellationToken)
    {
        var stations = repository.For<Station>();
        var stationIds = (await stations.GetAll<Station>(project: station => new Station { Id = station.Id }))
            .Select(station => station.Id)
            .ToArray();
        await stations.BulkUpdate(stationIds, station => station.IsProbeEnabled = request.IsProbeEnabled);
        return Ok(stationIds.Length);
    }

    [HttpGet("probes/status")]
    public async Task<ActionResult<ProbeStatusSummary>> GetProbeStatus(
        [FromQuery] string? query,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var config = await configService.GetAsync(cancellationToken);
        var runtime = probeStatusStore.GetSnapshot();
        var filters = string.IsNullOrWhiteSpace(query)
            ? []
            : new[]
            {
                Filter<Station>.LikeAny(
                    $"%{query.Trim().ToLowerInvariant()}%",
                    station => station.Name.ToLower(),
                    station => station.Genre.ToLower(),
                    station => station.StreamUrl.ToLower())
            };
        var stationRepository = repository.For<Station>();
        var enabledStationCount = await stationRepository.Count([station => station.IsProbeEnabled]);
        var matchingStationCount = await stationRepository.Count(filters);
        var stations = await stationRepository.GetAll<Station>(
            filterExprs: filters,
            orderBy: Ordering<Station>.Desc(station => station.IsProbeEnabled).ThenAsc(station => station.Name),
            skip: (page - 1) * pageSize,
            maxResults: pageSize);
        var stationStatuses = stations.Select(station =>
        {
            var isProbing = runtime.ActiveProbes.TryGetValue(station.Id, out var probeStartedAt);
            return new StationProbeStatusSummary(
                station.Id,
                station.Name,
                station.Genre,
                station.StreamUrl,
                station.IsProbeEnabled,
                isProbing,
                probeStartedAt,
                station.LastProbedAt,
                station.LastMetadataAt,
                station.ConsecutiveProbeFailures);
        }).ToList();

        return Ok(new ProbeStatusSummary(
            config.ProbingEnabled,
            runtime.LastBatchStartedAt,
            runtime.LastBatchCompletedAt,
            runtime.ActiveProbes.Count,
            enabledStationCount,
            matchingStationCount,
            page,
            pageSize,
            stationStatuses));
    }

    [HttpPut("users/{id:guid}")]
    public async Task<IActionResult> UpdateUser(Guid id, UpdateUserRequest request)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        var requestedRole = string.IsNullOrWhiteSpace(request.Role) ? null : request.Role;
        if (requestedRole is not null and not Roles.Admin and not Roles.User)
        {
            ModelState.AddModelError("role", "Role must be Admin or User.");
            return ValidationProblem(ModelState);
        }

        var isSelf = id == CurrentUserId;
        var removesAdminAccess = !request.IsActive || requestedRole == Roles.User;
        if (isSelf && removesAdminAccess) return Problem(statusCode: StatusCodes.Status409Conflict, title: "You cannot disable or demote your own account.");
        if (removesAdminAccess && await IsLastActiveAdminAsync(user))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "The last active administrator cannot be disabled or demoted.");
        }

        user.DisplayName = request.DisplayName?.Trim();
        user.IsActive = request.IsActive;
        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded) return IdentityFailure(update.Errors);

        if (requestedRole is not null)
        {
            var existingRoles = await userManager.GetRolesAsync(user);
            var removeRoles = existingRoles.Where(role => role != requestedRole).ToList();
            if (removeRoles.Count > 0)
            {
                var removeResult = await userManager.RemoveFromRolesAsync(user, removeRoles);
                if (!removeResult.Succeeded) return IdentityFailure(removeResult.Errors);
            }
            if (!existingRoles.Contains(requestedRole))
            {
                var addResult = await userManager.AddToRoleAsync(user, requestedRole);
                if (!addResult.Succeeded) return IdentityFailure(addResult.Errors);
            }
        }

        return NoContent();
    }

    [HttpDelete("users/{id:guid}")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();
        if (id == CurrentUserId) return Problem(statusCode: StatusCodes.Status409Conflict, title: "You cannot delete your own account.");
        if (await IsLastActiveAdminAsync(user))
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "The last active administrator cannot be deleted.");
        }

        var result = await userManager.DeleteAsync(user);
        return result.Succeeded ? NoContent() : IdentityFailure(result.Errors);
    }

    private async Task<bool> IsLastActiveAdminAsync(ApplicationUser user)
    {
        if (!user.IsActive || !await userManager.IsInRoleAsync(user, Roles.Admin)) return false;
        var administrators = await userManager.GetUsersInRoleAsync(Roles.Admin);
        return administrators.All(administrator => administrator.Id == user.Id || !administrator.IsActive);
    }
}
