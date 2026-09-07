using MusicLibrary.Api.Data;
using MusicLibrary.Api.Services;
using EfCoreRepository.Extensions;
using EfCoreRepository.Interfaces;
using EfCoreRepository.Models;

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

        var stations = await scope.ServiceProvider.GetRequiredService<IEfRepository>().For<Station>().GetAll(
            filterExprs: [station => station.IsProbeEnabled],
            orderBy: Ordering<Station>.Asc(station => station.LastProbedAt),
            maxResults: config.ProbeBatchSize);

        using var gate = new SemaphoreSlim(config.ProbeConcurrency);
        await Task.WhenAll(stations.Select(station => ProbeStationAsync(station, config.ProbeTimeoutSeconds, gate, cancellationToken)));
    }

    private async Task ProbeStationAsync(Station station, int timeoutSeconds, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var probe = scope.ServiceProvider.GetRequiredService<IStreamMetadataProbe>();
            var repository = scope.ServiceProvider.GetRequiredService<IEfRepository>();
            var stations = repository.For<Station>();
            var trackedStation = await stations.Get<Guid>(station.Id);
            if (trackedStation is null || !Uri.TryCreate(trackedStation.StreamUrl, UriKind.Absolute, out var streamUri)) return;

            var result = await probe.ProbeAsync(streamUri, TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
            if (result is null)
            {
                await stations.Update<Guid>(station.Id, tracked =>
                {
                    tracked.LastProbedAt = DateTimeOffset.UtcNow;
                    tracked.ConsecutiveProbeFailures++;
                });
                return;
            }

            await stations.Update<Guid>(station.Id, tracked =>
            {
                tracked.LastProbedAt = DateTimeOffset.UtcNow;
                tracked.ConsecutiveProbeFailures = 0;
                tracked.LastMetadataAt = DateTimeOffset.UtcNow;
            });

            var observation = new PlayObservation
            {
                Id = Guid.NewGuid(), StationId = station.Id, RawMetadata = result.RawMetadata,
                Artist = result.Artist, Title = result.Title, Confidence = string.IsNullOrWhiteSpace(result.Artist) ? 0.2m : 0.9m,
                ObservedAt = DateTimeOffset.UtcNow
            };
            await repository.For<PlayObservation>().Save(observation);

            if (!string.IsNullOrWhiteSpace(result.Artist))
            {
                var normalizedArtist = result.Artist.Trim().ToUpperInvariant();
                var subscriptions = await repository.For<ArtistSubscription>().GetAll(filterExprs: [subscription => subscription.NormalizedArtistName == normalizedArtist]);
                foreach (var subscription in subscriptions)
                {
                    await repository.For<UserAlert>().Save(new UserAlert { Id = Guid.NewGuid(), UserId = subscription.UserId, ArtistSubscriptionId = subscription.Id, PlayObservationId = observation.Id, CreatedAt = DateTimeOffset.UtcNow });
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }
}