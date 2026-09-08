using System.Linq.Expressions;
using EfCoreRepository.Interfaces;
using EfCoreRepository.Models;
using MusicLibrary.Api.Data;
using MusicLibrary.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using MusicLibrary.Contracts.Requests;
using MusicLibrary.Contracts.Responses;

namespace MusicLibrary.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public sealed class LibraryController(
    IEfRepository repository,
    IGlobalConfigService configService,
    IEncryptedTrackCacheService encryptedTrackCacheService,
    ITrackCacheStorage trackCacheStorage,
    LiveStreamTicketStore liveStreamTickets,
    IHttpClientFactory httpClientFactory) : MusicLibraryControllerBase
{
    [AllowAnonymous]
    [HttpGet("now-playing")]
    public async Task<IReadOnlyCollection<NowPlayingSummary>> NowPlaying([FromQuery] string? query)
    {
        var filters = string.IsNullOrWhiteSpace(query)
            ? new[] { (Expression<Func<Station, bool>>)(station => station.LastMetadataAt != null) }
            : new[]
            {
                (Expression<Func<Station, bool>>)(station => station.LastMetadataAt != null),
                Filter<Station>.LikeAny(
                    $"%{query.Trim().ToLowerInvariant()}%",
                    station => station.CurrentArtist!.ToLower(),
                    station => station.CurrentTitle!.ToLower(),
                    station => station.Name.ToLower())
            };

        return
        [
            .. await repository.For<Station>().GetAll(
                filterExprs: filters,
                orderBy: Ordering<Station>.Desc(station => station.LastMetadataAt),
                project: station => new NowPlayingSummary(
                    station.Id,
                    station.Name,
                    station.CurrentArtist,
                    station.CurrentTitle,
                    station.CurrentRawMetadata!,
                    station.LastMetadataAt!.Value,
                    station.CurrentConfidence,
                    station.StreamUrl),
                maxResults: 100)
        ];
    }

    [AllowAnonymous]
    [HttpGet("stations")]
    public async Task<IReadOnlyCollection<StationSummary>> Stations([FromQuery] string? query)
    {
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
        return
        [
            .. await repository.For<Station>().GetAll(
                filterExprs: filters,
                orderBy: Ordering<Station>.Asc(station => station.Name),
                project: station => new StationSummary(
                    station.Id,
                    station.Name,
                    station.Genre,
                    station.StreamUrl,
                    station.IsProbeEnabled,
                    station.LastProbedAt),
                maxResults: 200)
        ];
    }

    [AllowAnonymous]
    [HttpGet("now-playing/{stationId:guid}")]
    public async Task<ActionResult<NowPlayingSummary>> NowPlayingStation(Guid stationId)
    {
        var station = await repository.For<Station>().Get<Guid>(stationId);
        if (station?.LastMetadataAt is null) return NotFound();

        return Ok(new NowPlayingSummary(
            station.Id,
            station.Name,
            station.CurrentArtist,
            station.CurrentTitle,
            station.CurrentRawMetadata ?? string.Empty,
            station.LastMetadataAt.Value,
            station.CurrentConfidence,
            station.StreamUrl));
    }

    [AllowAnonymous]
    [HttpPost("now-playing/{stationId:guid}/stream-ticket")]
    public async Task<ActionResult<LiveStreamTicket>> CreateLiveStreamTicket(Guid stationId)
    {
        var station = await repository.For<Station>().Get<Guid>(stationId);
        if (station is null) return NotFound();
        if (!Uri.TryCreate(station.StreamUrl, UriKind.Absolute, out var streamUri)
            || (!streamUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !streamUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest("The station does not have a valid stream URL.");
        }

        var ticket = liveStreamTickets.Issue(stationId);
        return Ok(new LiveStreamTicket($"/api/live-stream/{ticket}"));
    }

    [AllowAnonymous]
    [HttpGet("live-stream/{ticket}")]
    [HttpHead("live-stream/{ticket}")]
    public async Task ProxyLiveStream(string ticket, CancellationToken cancellationToken)
    {
        if (!liveStreamTickets.TryResolve(ticket, out var stationId))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var station = await repository.For<Station>().Get<Guid>(stationId);
        if (station is null
            || !Uri.TryCreate(station.StreamUrl, UriKind.Absolute, out var streamUri)
            || (!streamUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !streamUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, streamUri);
            request.Headers.TryAddWithoutValidation("Icy-MetaData", "0");
            request.Headers.UserAgent.ParseAdd("MusicLibrary/1.0");
            using var upstream = await httpClientFactory.CreateClient("LiveStreamProxy").SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!upstream.IsSuccessStatusCode)
            {
                Response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }

            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "audio/mpeg";
            Response.Headers.CacheControl = "no-store";
            Response.Headers["X-Accel-Buffering"] = "no";
            if (HttpMethods.IsHead(Request.Method)) return;

            HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            await using var stream = await upstream.Content.ReadAsStreamAsync(cancellationToken);
            await stream.CopyToAsync(Response.Body, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (HttpRequestException)
        {
            if (!Response.HasStarted) Response.StatusCode = StatusCodes.Status502BadGateway;
        }
    }

    [AllowAnonymous]
    [HttpGet("trending")]
    public async Task<IReadOnlyCollection<TrendingSummary>> Trending([FromQuery] string? query, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
        var filters = string.IsNullOrWhiteSpace(query)
            ? new[] { (Expression<Func<PlayObservation, bool>>)(play => play.ObservedAt >= cutoff && play.Artist != null) }
            : new[]
            {
                (Expression<Func<PlayObservation, bool>>)(play => play.ObservedAt >= cutoff && play.Artist != null),
                Filter<PlayObservation>.LikeAny(
                    $"%{query.Trim().ToLowerInvariant()}%",
                    play => play.Artist!.ToLower(),
                    play => play.Title!.ToLower())
            };

        var observations = await repository.For<PlayObservation>().GetAll(
            filterExprs: filters,
            orderBy: Ordering<PlayObservation>.Desc(play => play.ObservedAt),
            project: play => new NowPlayingSummary(play.StationId, play.Station.Name, play.Artist, play.Title, play.RawMetadata, play.ObservedAt, play.Confidence, play.Station.StreamUrl),
            maxResults: 10000);
        var config = await configService.GetAsync(cancellationToken);
        IEnumerable<CachedTrack> cachedTracks = [];
        if (TrackCacheCryptography.TryGetKey(config.TrendingCacheEncryptionKey, out var cacheKey))
        {
            var keyFingerprint = TrackCacheCryptography.GetFingerprint(cacheKey);
            cachedTracks = await repository.For<CachedTrack>().GetAll<CachedTrack>(
                filterExprs: [track => track.ExpiresAt > DateTimeOffset.UtcNow
                    && track.KeyFingerprint == keyFingerprint],
                orderBy: Ordering<CachedTrack>.Desc(track => track.CreatedAt),
                maxResults: 10000);
        }
        var cacheByTrack = cachedTracks
            .Where(track => trackCacheStorage.Exists(track.FilePath))
            .GroupBy(track => (track.NormalizedArtist, track.NormalizedTitle))
            .ToDictionary(group => group.Key, group => group.First());

        return
        [
            .. observations
                .Where(play => !string.IsNullOrWhiteSpace(play.Artist)
                               && TrackMetadataValidation.IsMeaningful(play.Artist, play.Title))
                .GroupBy(play => new
                {
                    Artist = play.Artist!.Trim().ToUpperInvariant(),
                    Title = play.Title?.Trim().ToUpperInvariant()
                })
                .Select(group =>
                {
                    var latest = group.MaxBy(play => play.ObservedAt)!;
                    cacheByTrack.TryGetValue((group.Key.Artist, group.Key.Title ?? string.Empty), out var cachedTrack);
                    return new TrendingSummary(
                        latest.Artist!.Trim(),
                        latest.Title?.Trim(),
                        group.Count(),
                        group.Select(play => play.StationId).Distinct().Count(),
                        latest.ObservedAt,
                        latest.StationId,
                        latest.StationName,
                        latest.StreamUrl,
                        cachedTrack?.BitrateKbps,
                        cachedTrack?.DurationMs ?? EstimateDurationMs(cachedTrack),
                        cachedTrack?.Id,
                        cachedTrack?.ExpiresAt);
                })
                .Where(trend => trend.CachedTrackId is not null
                                && trend.DurationMs is { } durationMs
                                && durationMs >= config.TrendingMinimumDurationSeconds * 1000)
                .OrderByDescending(trend => trend.ObservationCount)
                .ThenByDescending(trend => trend.StationCount)
                .ThenByDescending(trend => trend.LastObservedAt)
                .Take(100)
        ];
    }

    private static int? EstimateDurationMs(CachedTrack? track)
    {
        if (track?.BitrateKbps is not > 0 || track.PlaintextLength <= 0) return null;
        return Convert.ToInt32(Math.Round(track.PlaintextLength * 8d / track.BitrateKbps.Value));
    }

    [AllowAnonymous]
    [HttpGet("trending/{cachedTrackId:guid}/download")]
    public async Task<IActionResult> DownloadTrendingTrack(Guid cachedTrackId, CancellationToken cancellationToken)
    {
        var download = await encryptedTrackCacheService.GetAsync(cachedTrackId, cancellationToken);
        if (download is null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return File(download.Content, download.ContentType, download.FileName);
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
            (Expression<Func<ArtistSubscription, bool>>)(subscription =>
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

