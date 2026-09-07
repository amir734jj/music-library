using EfCoreRepository.Interfaces;
using EfCoreRepository.Models;
using MusicLibrary.Api.Data;
using MusicLibrary.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MusicLibrary.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public sealed class LibraryController(IEfRepository repository) : MusicLibraryControllerBase
{
    [HttpGet("now-playing")]
    public async Task<IReadOnlyCollection<NowPlayingSummary>> NowPlaying([FromQuery] string? query)
    {
        var filters = string.IsNullOrWhiteSpace(query)
            ? new[] { (System.Linq.Expressions.Expression<Func<Station, bool>>)(station => station.LastMetadataAt != null) }
            : new[]
            {
                (System.Linq.Expressions.Expression<Func<Station, bool>>)(station => station.LastMetadataAt != null),
                Filter<Station>.LikeAny(
                    $"%{query.Trim().ToLowerInvariant()}%",
                    station => station.CurrentArtist!.ToLower(),
                    station => station.CurrentTitle!.ToLower(),
                    station => station.Name.ToLower())
            };

        return (await repository.For<Station>().GetAll(
            filterExprs: filters,
            orderBy: Ordering<Station>.Desc(station => station.LastMetadataAt),
            project: station => new NowPlayingSummary(
                station.Id,
                station.Name,
                station.CurrentArtist,
                station.CurrentTitle,
                station.CurrentRawMetadata!,
                station.LastMetadataAt!.Value,
                station.CurrentConfidence),
            maxResults: 100)).ToList();
    }

    [HttpGet("trending")]
    public async Task<IReadOnlyCollection<TrendingSummary>> Trending([FromQuery] string? query)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
        var filters = string.IsNullOrWhiteSpace(query)
            ? new[] { (System.Linq.Expressions.Expression<Func<PlayObservation, bool>>)(play => play.ObservedAt >= cutoff && play.Artist != null) }
            : new[]
            {
                (System.Linq.Expressions.Expression<Func<PlayObservation, bool>>)(play => play.ObservedAt >= cutoff && play.Artist != null),
                Filter<PlayObservation>.LikeAny(
                    $"%{query.Trim().ToLowerInvariant()}%",
                    play => play.Artist!.ToLower(),
                    play => play.Title!.ToLower())
            };

        var observations = await repository.For<PlayObservation>().GetAll(
            filterExprs: filters,
            orderBy: Ordering<PlayObservation>.Desc(play => play.ObservedAt),
            project: play => new NowPlayingSummary(play.StationId, play.Station.Name, play.Artist, play.Title, play.RawMetadata, play.ObservedAt, play.Confidence),
            maxResults: 10000);

        return observations
            .Where(play => !string.IsNullOrWhiteSpace(play.Artist))
            .GroupBy(play => new
            {
                Artist = play.Artist!.Trim().ToUpperInvariant(),
                Title = play.Title?.Trim().ToUpperInvariant()
            })
            .Select(group =>
            {
                var latest = group.MaxBy(play => play.ObservedAt)!;
                return new TrendingSummary(
                    latest.Artist!.Trim(),
                    latest.Title?.Trim(),
                    group.Count(),
                    group.Select(play => play.StationId).Distinct().Count(),
                    latest.ObservedAt);
            })
            .OrderByDescending(trend => trend.ObservationCount)
            .ThenByDescending(trend => trend.StationCount)
            .ThenByDescending(trend => trend.LastObservedAt)
            .Take(100)
            .ToList();
    }

    [HttpGet("subscriptions")]
    public Task<IEnumerable<ArtistSubscriptionSummary>> GetSubscriptions() =>
        repository.For<ArtistSubscription>().GetAll(
            filterExprs: [subscription => subscription.UserId == CurrentUserId],
            orderBy: Ordering<ArtistSubscription>.Asc(subscription => subscription.ArtistName),
            project: subscription => new ArtistSubscriptionSummary(subscription.Id, subscription.ArtistName, subscription.CreatedAt, subscription.CaptureEnabled));

    [HttpPost("subscriptions")]
    public async Task<IActionResult> CreateSubscription(CreateSubscriptionRequest request)
    {
        var artist = request.ArtistName.Trim();
        if (artist.Length is < 2 or > 200)
        {
            ModelState.AddModelError("artistName", "Artist name must be between 2 and 200 characters.");
            return ValidationProblem(ModelState);
        }

        var subscriptions = repository.For<ArtistSubscription>();
        var normalizedArtist = artist.ToUpperInvariant();
        if (await subscriptions.Any([subscription => subscription.UserId == CurrentUserId && subscription.NormalizedArtistName == normalizedArtist])) return Conflict();

        var subscription = await subscriptions.Save(new ArtistSubscription { Id = Guid.NewGuid(), UserId = CurrentUserId, ArtistName = artist, NormalizedArtistName = normalizedArtist, CaptureEnabled = request.CaptureEnabled, CreatedAt = DateTimeOffset.UtcNow });
        return Created($"/api/subscriptions/{subscription.Id}", new ArtistSubscriptionSummary(subscription.Id, subscription.ArtistName, subscription.CreatedAt, subscription.CaptureEnabled));
    }

    [HttpDelete("subscriptions/{id:guid}")]
    public async Task<IActionResult> DeleteSubscription(Guid id)
    {
        var subscriptions = repository.For<ArtistSubscription>();
        var filters = new[]
        {
            (System.Linq.Expressions.Expression<Func<ArtistSubscription, bool>>)(subscription =>
                subscription.Id == id && subscription.UserId == CurrentUserId)
        };
        if (!await subscriptions.Any(filters)) return NotFound();

        await subscriptions.Delete(filters);
        return NoContent();
    }

    [HttpGet("alerts")]
    public Task<IEnumerable<UserAlertSummary>> GetAlerts() =>
        repository.For<UserAlert>().GetAll(
            filterExprs: [alert => alert.UserId == CurrentUserId],
            orderBy: Ordering<UserAlert>.Desc(alert => alert.CreatedAt),
            project: alert => new UserAlertSummary(alert.Id, alert.ArtistSubscription.ArtistName, alert.PlayObservation.Station.Name, alert.PlayObservation.Title, alert.PlayObservation.ObservedAt),
            maxResults: 100);

}

