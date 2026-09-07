using MusicLibrary.Api.Data;
using MusicLibrary.Api.Services;
using EfCoreRepository.Extensions;
using EfCoreRepository.Interfaces;
using EfCoreRepository.Models;

namespace MusicLibrary.Api.Workers;

public sealed class StationProbeWorker(
    IServiceScopeFactory scopeFactory,
    StationProbeStatusStore statusStore,
    ILogger<StationProbeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var probedStations = false;
            try
            {
                probedStations = await ProbeBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Station metadata probe batch failed.");
            }

            if (!probedStations)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<bool> ProbeBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var config = await scope.ServiceProvider.GetRequiredService<IGlobalConfigService>().GetAsync(cancellationToken);
        if (!config.ProbingEnabled)
        {
            return false;
        }

        var stations = (await scope.ServiceProvider.GetRequiredService<IEfRepository>().For<Station>().GetAll(
                filterExprs: [station => station.IsProbeEnabled],
                orderBy: Ordering<Station>.Asc(station => station.LastProbedAt),
                maxResults: config.ProbeBatchSize)).ToList();
        if (stations.Count == 0)
        {
            return false;
        }

        statusStore.BatchStarted();
        try
        {
            using var gate = new SemaphoreSlim(config.ProbeConcurrency);
            await Task.WhenAll(stations.Select(station => ProbeStationAsync(station, config.ProbeTimeoutSeconds, gate, cancellationToken)));
            return true;
        }
        finally
        {
            statusStore.BatchCompleted();
        }
    }

    private async Task ProbeStationAsync(Station station, int timeoutSeconds, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        statusStore.ProbeStarted(station.Id);
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
            statusStore.ProbeCompleted(station.Id);
            gate.Release();
        }
    }
}