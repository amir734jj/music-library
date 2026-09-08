using EfCoreRepository.Interfaces;
using MusicLibrary.Api.Data;
using StreamRipper.Interfaces;
using StreamRipper.Models;

namespace MusicLibrary.Api.Services;

public sealed class EncryptedTrackCacheWorker(
    TrackCaptureQueue queue,
    ITrackCacheStorage storage,
    IServiceScopeFactory scopeFactory,
    IStreamRipperFactory streamRipperFactory,
    ILogger<EncryptedTrackCacheWorker> logger) : BackgroundService
{
    private const int MaximumCaptureBytes = 32 * 1024 * 1024;
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var scope = scopeFactory.CreateScope())
        {
            var config = await scope.ServiceProvider.GetRequiredService<IGlobalConfigService>().GetAsync(stoppingToken);
            var repository = scope.ServiceProvider.GetRequiredService<IEfRepository>();
            var key = TrackCacheCryptography.TryGetKey(config.TrendingCacheEncryptionKey, out var encryptionKey)
                ? encryptionKey
                : null;
            await storage.EnforceLimitAsync(
                repository,
                key,
                config.TrendingCacheMaxSizeMegabytes * 1024L * 1024L,
                stoppingToken);
        }
        await Task.WhenAll(ProcessQueueAsync(stoppingToken), RunMaintenanceAsync(stoppingToken));
    }

    private async Task ProcessQueueAsync(CancellationToken stoppingToken)
    {
        await Parallel.ForEachAsync(
            queue.ReadAllAsync(stoppingToken),
            new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = stoppingToken },
            async (request, cancellationToken) =>
            {
                try
                {
                    await CaptureAsync(request, cancellationToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Could not cache track {Artist} - {Title}.", request.Artist, request.Title);
                }
                finally
                {
                    queue.Complete(request);
                }
            });
    }

    private async Task RunMaintenanceAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(MaintenanceInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var config = await scope.ServiceProvider.GetRequiredService<IGlobalConfigService>().GetAsync(stoppingToken);
                var repository = scope.ServiceProvider.GetRequiredService<IEfRepository>();
                var key = TrackCacheCryptography.TryGetKey(config.TrendingCacheEncryptionKey, out var encryptionKey)
                    ? encryptionKey
                    : null;
                await storage.EnforceLimitAsync(
                    repository,
                    key,
                    config.TrendingCacheMaxSizeMegabytes * 1024L * 1024L,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not complete trending cache maintenance.");
            }
        }
    }

    private async Task CaptureAsync(TrackCaptureRequest request, CancellationToken cancellationToken)
    {
        if (!TrackMetadataValidation.IsMeaningful(request.Artist, request.Title)) return;

        using var scope = scopeFactory.CreateScope();
        var config = await scope.ServiceProvider.GetRequiredService<IGlobalConfigService>().GetAsync(cancellationToken);
        if (!TrackCacheCryptography.TryGetKey(config.TrendingCacheEncryptionKey, out var key)) return;

        var repository = scope.ServiceProvider.GetRequiredService<IEfRepository>();
        var normalizedArtist = request.Artist.Trim().ToUpperInvariant();
        var normalizedTitle = request.Title?.Trim().ToUpperInvariant() ?? string.Empty;
        var cachedTracks = repository.For<CachedTrack>();
        var matchingTracks = (await cachedTracks.GetAll<CachedTrack>(filterExprs: [
            track => track.NormalizedArtist == normalizedArtist
                     && track.NormalizedTitle == normalizedTitle
                     && track.ExpiresAt > DateTimeOffset.UtcNow])).ToList();
        if (matchingTracks.Any(track => storage.Exists(track.FilePath))) return;
        var missingTrackIds = matchingTracks.Select(track => track.Id).ToArray();
        if (missingTrackIds.Length > 0)
        {
            await cachedTracks.Delete([track => missingTrackIds.Contains(track.Id)]);
        }

        var audio = await CaptureSongAsync(
            request,
            TimeSpan.FromSeconds(config.TrendingCacheCaptureTimeoutSeconds),
            cancellationToken);
        if (audio is null) return;
        if (!TrackAudioValidation.TryAnalyze(audio, out var audioInfo))
        {
            logger.LogWarning("Discarding invalid capture for {Artist} - {Title}.", request.Artist, request.Title);
            return;
        }
        var id = Guid.NewGuid();
        var path = storage.CreateEncryptedPath(id, key);
        var encrypted = TrackCacheCryptography.Encrypt(audio, key);
        var createdAt = DateTimeOffset.UtcNow;
        var track = new CachedTrack
        {
            Id = id,
            PlayObservationId = request.PlayObservationId,
            Artist = request.Artist.Trim(),
            Title = request.Title?.Trim(),
            NormalizedArtist = normalizedArtist,
            NormalizedTitle = normalizedTitle,
            FilePath = path,
            ContentType = audioInfo.ContentType,
            PlaintextLength = audio.Length,
            BitrateKbps = audioInfo.BitrateKbps,
            KeyFingerprint = TrackCacheCryptography.GetFingerprint(key),
            CreatedAt = createdAt,
            ExpiresAt = createdAt.AddHours(config.TrendingCacheRetentionHours)
        };
        await storage.TrySaveAsync(
            repository,
            track,
            encrypted,
            config.TrendingCacheMaxSizeMegabytes * 1024L * 1024L,
            cancellationToken);
    }

    private async Task<byte[]?> CaptureSongAsync(
        TrackCaptureRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var ripper = streamRipperFactory.New(new StreamRipperOptions
        {
            Url = request.StreamUri,
            MaxBufferSize = MaximumCaptureBytes,
            MetadataOnly = false
        });
        ripper.SongChangedEventHandlers += (_, eventArgs) =>
        {
            var metadata = eventArgs.SongInfo.SongMetadata;
            if (Matches(metadata.Artist, metadata.Title, request))
            {
                var content = eventArgs.SongInfo.Stream.ToArray();
                completion.TrySetResult(content.Length is > 0 and <= MaximumCaptureBytes ? content : null);
            }
            eventArgs.SongInfo.Dispose();
        };
        ripper.StreamFailedHandlers += (_, _) => completion.TrySetResult(null);
        ripper.StreamEndedEventHandlers += (_, _) => completion.TrySetResult(null);
        await using var cancellationRegistration = timeoutSource.Token.Register(() => completion.TrySetResult(null));
        ripper.Start();
        return await completion.Task;
    }

    private static bool Matches(string? artist, string? title, TrackCaptureRequest request)
    {
        var matchesArtist = string.Equals(artist?.Trim(), request.Artist.Trim(), StringComparison.OrdinalIgnoreCase);
        var matchesTitle = string.IsNullOrWhiteSpace(request.Title)
                           || string.Equals(title?.Trim(), request.Title.Trim(), StringComparison.OrdinalIgnoreCase);
        return matchesArtist && matchesTitle;
    }
}