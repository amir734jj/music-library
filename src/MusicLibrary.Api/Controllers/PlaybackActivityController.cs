using EfCoreRepository.Interfaces;
using EfCoreRepository.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicLibrary.Api.Data;
using MusicLibrary.Contracts.Requests;
using MusicLibrary.Contracts.Responses;

namespace MusicLibrary.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/playback-activity")]
public sealed class PlaybackActivityController(IEfRepository repository) : MusicLibraryControllerBase
{
    private static readonly TimeSpan ActivityLifetime = TimeSpan.FromSeconds(90);

    [HttpGet]
    public async Task<IReadOnlyCollection<UserPlaybackActivitySummary>> GetActive()
    {
        var cutoff = DateTimeOffset.UtcNow - ActivityLifetime;
        return
        [
            .. await repository.For<UserPlaybackActivity>().GetAll(
                filterExprs: [activity => activity.LastHeartbeatAt >= cutoff],
                orderBy: Ordering<UserPlaybackActivity>.Desc(activity => activity.StartedAt),
                project: activity => new UserPlaybackActivitySummary(
                    activity.UserId,
                    activity.User.DisplayName ?? activity.User.UserName ?? "Listener",
                    activity.PlaybackDescription,
                    activity.IsLiveStation,
                    activity.StartedAt),
                maxResults: 100)
        ];
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdatePlaybackActivityRequest request)
    {
        var description = request.PlaybackDescription.Trim();
        var activities = repository.For<UserPlaybackActivity>();
        var existing = await activities.Get<Guid>(CurrentUserId);
        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            await activities.Save(new UserPlaybackActivity
            {
                UserId = CurrentUserId,
                PlaybackDescription = description,
                IsLiveStation = request.IsLiveStation,
                StartedAt = now,
                LastHeartbeatAt = now
            });
        }
        else
        {
            await activities.Update<Guid>(CurrentUserId, activity =>
            {
                var playbackChanged = activity.IsLiveStation != request.IsLiveStation
                    || activity.PlaybackDescription != description;
                activity.PlaybackDescription = description;
                activity.IsLiveStation = request.IsLiveStation;
                activity.LastHeartbeatAt = now;
                if (playbackChanged) activity.StartedAt = now;
            });
        }

        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Clear()
    {
        await repository.For<UserPlaybackActivity>().Delete([activity => activity.UserId == CurrentUserId]);
        return NoContent();
    }
}