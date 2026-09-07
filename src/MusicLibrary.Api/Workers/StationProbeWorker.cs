using MusicLibrary.Api.Data;
using MusicLibrary.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace MusicLibrary.Api.Workers;

public sealed class StationProbeWorker(IServiceScopeFactory scopeFactory, ILogger<StationProbeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProbeBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Station metadata probe batch failed.");
            }
        }
    }

    private async Task ProbeBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var config = await scope.ServiceProvider.GetRequiredService<IGlobalConfigService>().GetAsync(cancellationToken);
        if (!config.ProbingEnabled)
        {
            return;
        }

        var dbContext = scope.ServiceProvider.GetRequiredService<MusicLibraryDbContext>();
        var stations = await dbContext.Stations.Where(station => station.IsProbeEnabled)
            .OrderBy(station => station.LastProbedAt)
            .Take(config.ProbeBatchSize)
            .ToListAsync(cancellationToken);

        using var gate = new SemaphoreSlim(config.ProbeConcurrency);
        await Task.WhenAll(stations.Select(station => ProbeStationAsync(station, config.ProbeTimeoutSeconds, gate, cancellationToken)));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ProbeStationAsync(Station station, int timeoutSeconds, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var probe = scope.ServiceProvider.GetRequiredService<IStreamMetadataProbe>();
            var dbContext = scope.ServiceProvider.GetRequiredService<MusicLibraryDbContext>();
            var trackedStation = await dbContext.Stations.FindAsync([station.Id], cancellationToken);
            if (trackedStation is null || !Uri.TryCreate(trackedStation.StreamUrl, UriKind.Absolute, out var streamUri)) return;

            var result = await probe.ProbeAsync(streamUri, TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
            trackedStation.LastProbedAt = DateTimeOffset.UtcNow;
            if (result is null)
            {
                trackedStation.ConsecutiveProbeFailures++;
            }
            else
            {
                trackedStation.ConsecutiveProbeFailures = 0;
                trackedStation.LastMetadataAt = DateTimeOffset.UtcNow;
                var observation = new PlayObservation
                {
                    Id = Guid.NewGuid(), StationId = trackedStation.Id, RawMetadata = result.RawMetadata,
                    Artist = result.Artist, Title = result.Title, Confidence = string.IsNullOrWhiteSpace(result.Artist) ? 0.2m : 0.9m,
                    ObservedAt = DateTimeOffset.UtcNow
                };
                dbContext.PlayObservations.Add(observation);
                if (!string.IsNullOrWhiteSpace(result.Artist))
                {
                    var normalizedArtist = result.Artist.Trim().ToUpperInvariant();
                    var subscriptions = await dbContext.ArtistSubscriptions.Where(subscription => subscription.NormalizedArtistName == normalizedArtist).ToListAsync(cancellationToken);
                    foreach (var subscription in subscriptions)
                    {
                        dbContext.UserAlerts.Add(new UserAlert { Id = Guid.NewGuid(), UserId = subscription.UserId, ArtistSubscriptionId = subscription.Id, PlayObservationId = observation.Id, CreatedAt = DateTimeOffset.UtcNow });
                    }
                }
            }
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}