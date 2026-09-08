using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Threading.Channels;
using EfCoreRepository.Interfaces;
using MusicLibrary.Api.Data;
using MusicLibrary.Contracts;
using StreamRipper.Interfaces;
using StreamRipper.Models;

namespace MusicLibrary.Api.Services;

public sealed record TrackCaptureRequest(
    Guid PlayObservationId,
    Uri StreamUri,
    string Artist,
    string? Title);

public sealed class TrackCaptureQueue
{
    private readonly ConcurrentDictionary<string, byte> _pendingTracks = new(StringComparer.Ordinal);
    private readonly Channel<TrackCaptureRequest> _channel = Channel.CreateBounded<TrackCaptureRequest>(
        new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    public bool TryQueue(TrackCaptureRequest request)
    {
        var key = GetTrackKey(request);
        if (!_pendingTracks.TryAdd(key, 0)) return false;
        if (_channel.Writer.TryWrite(request)) return true;
        _pendingTracks.TryRemove(key, out _);
        return false;
    }

    public IAsyncEnumerable<TrackCaptureRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete(TrackCaptureRequest request) => _pendingTracks.TryRemove(GetTrackKey(request), out _);

    private static string GetTrackKey(TrackCaptureRequest request) =>
        $"{request.Artist.Trim().ToUpperInvariant()}\u001f{request.Title?.Trim().ToUpperInvariant()}";
}

public sealed record CachedTrackDownload(byte[] Content, string ContentType, string FileName);
public sealed record CachedTrackRead(CachedTrack Track, byte[] Content, string ContentType);

public interface IEncryptedTrackCacheService
{
    Task<CachedTrackDownload?> GetAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class EncryptedTrackCacheService(
    IGlobalConfigService configService,
    IEfRepository repository,
    TrackCacheStorage storage) : IEncryptedTrackCacheService
{
    public async Task<CachedTrackDownload?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var config = await configService.GetAsync(cancellationToken);
        if (!TrackCacheCryptography.TryGetKey(config.TrendingCacheEncryptionKey, out var key)) return null;
        var cached = await storage.TryReadAsync(repository, id, key, cancellationToken);
        if (cached is null) return null;

        var name = string.IsNullOrWhiteSpace(cached.Track.Title)
            ? cached.Track.Artist
            : $"{cached.Track.Artist} - {cached.Track.Title}";
        return new CachedTrackDownload(cached.Content, cached.ContentType, $"{SanitizeFileName(name)}{GetExtension(cached.ContentType)}");
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "radio-track" : sanitized;
    }

    private static string GetExtension(string contentType) => contentType.ToLowerInvariant() switch
    {
        "audio/aac" or "audio/aacp" => ".aac",
        "audio/flac" => ".flac",
        "audio/ogg" => ".ogg",
        _ => ".mp3"
    };
}

public sealed class TrackCacheStorage(IConfiguration configuration)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _activeKeyFingerprint;

    public string CacheDirectory { get; } = configuration["TRENDING_CACHE_DIRECTORY"]
        ?? Path.Combine(Path.GetTempPath(), "music-library-trending-cache");

    public async Task<bool> TrySaveAsync(
        IEfRepository repository,
        CachedTrack track,
        byte[] encrypted,
        long maximumCacheBytes,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            if (_activeKeyFingerprint is not null && track.KeyFingerprint != _activeKeyFingerprint) return false;
            await RemoveExpiredAsync(repository, cancellationToken);
            if (!await MakeSpaceAsync(repository, maximumCacheBytes, encrypted.LongLength, cancellationToken)) return false;

            await File.WriteAllBytesAsync(track.FilePath, encrypted, cancellationToken);
            try
            {
                await repository.For<CachedTrack>().Save(track);
                return true;
            }
            catch
            {
                File.Delete(track.FilePath);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CachedTrackRead?> TryReadAsync(
        IEfRepository repository,
        Guid id,
        byte[] encryptionKey,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var tracks = repository.For<CachedTrack>();
            var track = await tracks.Get<Guid>(id);
            if (track is null) return null;
            if (track.KeyFingerprint != TrackCacheCryptography.GetFingerprint(encryptionKey)) return null;
            if (track.ExpiresAt <= DateTimeOffset.UtcNow || !File.Exists(track.FilePath))
            {
                TryDeleteFile(track.FilePath);
                await tracks.Delete([candidate => candidate.Id == id]);
                return null;
            }

            try
            {
                var encrypted = await File.ReadAllBytesAsync(track.FilePath, cancellationToken);
                var content = TrackCacheCryptography.Decrypt(encrypted, encryptionKey);
                if (TrackAudioValidation.TryAnalyze(content, out var audioInfo))
                {
                    if (track.BitrateKbps != audioInfo.BitrateKbps)
                    {
                        await tracks.Update<Guid>(track.Id, candidate => candidate.BitrateKbps = audioInfo.BitrateKbps);
                        track.BitrateKbps = audioInfo.BitrateKbps;
                    }
                    return new CachedTrackRead(track, content, audioInfo.ContentType);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
            {
            }

            TryDeleteFile(track.FilePath);
            await tracks.Delete([candidate => candidate.Id == id]);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(IEfRepository repository, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var tracks = repository.For<CachedTrack>();
            var metadata = (await tracks.GetAll<CachedTrack>(maxResults: 100000)).ToList();
            if (metadata.Count > 0)
            {
                var ids = metadata.Select(track => track.Id).ToArray();
                await tracks.Delete([track => ids.Contains(track.Id)]);
            }

            if (!Directory.Exists(CacheDirectory)) return;
            foreach (var path in Directory.EnumerateFiles(CacheDirectory, "*.cache"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Delete(path);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RotateKeyAsync(
        IEfRepository repository,
        byte[] oldKey,
        byte[] newKey,
        Func<Task> saveConfiguration,
        CancellationToken cancellationToken)
    {
        var oldFingerprint = TrackCacheCryptography.GetFingerprint(oldKey);
        var newFingerprint = TrackCacheCryptography.GetFingerprint(newKey);
        if (oldFingerprint == newFingerprint)
        {
            await saveConfiguration();
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        var stagedPaths = new List<string>();
        var backupPaths = new List<(string Original, string Backup)>();
        var tracks = repository.For<CachedTrack>();
        CachedTrack[] metadata = [];
        try
        {
            metadata = (await tracks.GetAll<CachedTrack>(maxResults: 100000)).ToArray();
            foreach (var track in metadata)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (track.KeyFingerprint != oldFingerprint)
                {
                    throw new CryptographicException($"Cached track {track.Id} is not encrypted with the current key.");
                }

                var encrypted = await File.ReadAllBytesAsync(track.FilePath, cancellationToken);
                var plaintext = TrackCacheCryptography.Decrypt(encrypted, oldKey);
                if (!TrackAudioValidation.TryAnalyze(plaintext, out _))
                {
                    throw new InvalidDataException($"Cached track {track.Id} is not valid audio.");
                }

                var stagedPath = $"{track.FilePath}.{Guid.NewGuid():N}.rotation.cache";
                await File.WriteAllBytesAsync(stagedPath, TrackCacheCryptography.Encrypt(plaintext, newKey), cancellationToken);
                stagedPaths.Add(stagedPath);
            }

            for (var index = 0; index < metadata.Length; index++)
            {
                var track = metadata[index];
                var stagedPath = stagedPaths[index];
                var backupPath = $"{track.FilePath}.{Guid.NewGuid():N}.backup.cache";
                File.Move(track.FilePath, backupPath, true);
                backupPaths.Add((track.FilePath, backupPath));
                File.Move(stagedPath, track.FilePath, true);
            }
            stagedPaths.Clear();

            var ids = metadata.Select(track => track.Id).ToArray();
            if (ids.Length > 0)
            {
                await tracks.BulkUpdate(ids, track => track.KeyFingerprint = newFingerprint);
            }
            await saveConfiguration();
            _activeKeyFingerprint = newFingerprint;
            foreach (var (_, backupPath) in backupPaths) TryDeleteFile(backupPath);
        }
        catch
        {
            foreach (var (originalPath, backupPath) in backupPaths)
            {
                if (File.Exists(backupPath)) File.Move(backupPath, originalPath, true);
            }
            var ids = metadata.Select(track => track.Id).ToArray();
            if (ids.Length > 0)
            {
                await tracks.BulkUpdate(ids, track => track.KeyFingerprint = oldFingerprint);
            }
            throw;
        }
        finally
        {
            foreach (var stagedPath in stagedPaths) TryDeleteFile(stagedPath);
            _gate.Release();
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public async Task RemoveAsync(
        IEfRepository repository,
        CachedTrack track,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                File.Delete(track.FilePath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            await repository.For<CachedTrack>().Delete([candidate => candidate.Id == track.Id]);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EnforceLimitAsync(
        IEfRepository repository,
        byte[]? encryptionKey,
        long maximumCacheBytes,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            await RemoveExpiredAsync(repository, cancellationToken);
            await ReconcileAsync(repository, cancellationToken);
            if (encryptionKey is not null)
            {
                await RemoveInvalidAsync(repository, encryptionKey, cancellationToken);
            }
            _activeKeyFingerprint = encryptionKey is null
                ? null
                : TrackCacheCryptography.GetFingerprint(encryptionKey);
            await MakeSpaceAsync(repository, maximumCacheBytes, 0, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TrendingCacheStatusSummary> GetStatusAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var files = Directory.EnumerateFiles(CacheDirectory, "*.cache")
                .Select(path => new FileInfo(path))
                .ToList();
            return new TrendingCacheStatusSummary(files.Sum(file => file.Length), files.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> MakeSpaceAsync(
        IEfRepository repository,
        long maximumCacheBytes,
        long incomingBytes,
        CancellationToken cancellationToken)
    {
        if (incomingBytes > maximumCacheBytes) return false;

        var files = Directory.EnumerateFiles(CacheDirectory, "*.cache")
            .Select(path => new FileInfo(path))
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToList();
        var cacheBytes = files.Sum(file => file.Length);
        if (cacheBytes + incomingBytes <= maximumCacheBytes) return true;

        var tracks = repository.For<CachedTrack>();
        var metadata = (await tracks.GetAll<CachedTrack>(maxResults: 100000)).ToDictionary(
            track => Path.GetFullPath(track.FilePath),
            track => track.Id,
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var removedTrackIds = new List<Guid>();
        foreach (var file in files)
        {
            if (cacheBytes + incomingBytes <= maximumCacheBytes) break;
            var length = file.Length;
            file.Delete();
            cacheBytes -= length;
            if (metadata.TryGetValue(file.FullName, out var trackId)) removedTrackIds.Add(trackId);
        }
        if (removedTrackIds.Count > 0)
        {
            await tracks.Delete([track => removedTrackIds.Contains(track.Id)]);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return cacheBytes + incomingBytes <= maximumCacheBytes;
    }

    private static async Task RemoveExpiredAsync(IEfRepository repository, CancellationToken cancellationToken)
    {
        var tracks = repository.For<CachedTrack>();
        while (true)
        {
            var now = DateTimeOffset.UtcNow;
            var expired = (await tracks.GetAll<CachedTrack>(
                filterExprs: [track => track.ExpiresAt <= now],
                maxResults: 1000)).ToList();
            if (expired.Count == 0) return;

            foreach (var track in expired)
            {
                File.Delete(track.FilePath);
            }
            var expiredIds = expired.Select(track => track.Id).ToArray();
            await tracks.Delete([track => expiredIds.Contains(track.Id)]);
            cancellationToken.ThrowIfCancellationRequested();
            if (expired.Count < 1000) return;
        }
    }

    private async Task ReconcileAsync(IEfRepository repository, CancellationToken cancellationToken)
    {
        var tracks = repository.For<CachedTrack>();
        var metadata = (await tracks.GetAll<CachedTrack>(maxResults: 100000)).ToList();
        var missingIds = metadata.Where(track => !File.Exists(track.FilePath)).Select(track => track.Id).ToArray();
        var missingIdSet = missingIds.ToHashSet();
        if (missingIds.Length > 0)
        {
            await tracks.Delete([track => missingIds.Contains(track.Id)]);
        }

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var knownPaths = metadata
            .Where(track => !missingIdSet.Contains(track.Id))
            .Select(track => Path.GetFullPath(track.FilePath))
            .ToHashSet(comparer);
        foreach (var path in Directory.EnumerateFiles(CacheDirectory, "*.cache"))
        {
            if (!knownPaths.Contains(Path.GetFullPath(path))) File.Delete(path);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task RemoveInvalidAsync(
        IEfRepository repository,
        byte[] encryptionKey,
        CancellationToken cancellationToken)
    {
        var tracks = repository.For<CachedTrack>();
        var fingerprint = TrackCacheCryptography.GetFingerprint(encryptionKey);
        var metadata = (await tracks.GetAll<CachedTrack>(maxResults: 100000)).ToList();
        var invalidIds = new List<Guid>();
        foreach (var track in metadata)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isValid = track.KeyFingerprint == fingerprint
                && TrackMetadataValidation.IsMeaningful(track.Artist, track.Title);
            try
            {
                if (isValid)
                {
                    var encrypted = await File.ReadAllBytesAsync(track.FilePath, cancellationToken);
                    var content = TrackCacheCryptography.Decrypt(encrypted, encryptionKey);
                    isValid = TrackAudioValidation.TryAnalyze(content, out var audioInfo);
                    if (isValid && track.BitrateKbps != audioInfo.BitrateKbps)
                    {
                        await tracks.Update<Guid>(track.Id, candidate => candidate.BitrateKbps = audioInfo.BitrateKbps);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
            {
                isValid = false;
            }
            if (isValid) continue;

            try
            {
                File.Delete(track.FilePath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            invalidIds.Add(track.Id);
        }
        if (invalidIds.Count > 0)
        {
            await tracks.Delete([track => invalidIds.Contains(track.Id)]);
        }
    }
}

public sealed class EncryptedTrackCacheWorker(
    TrackCaptureQueue queue,
    TrackCacheStorage storage,
    IServiceScopeFactory scopeFactory,
    IStreamRipperFactory streamRipperFactory,
    ILogger<EncryptedTrackCacheWorker> logger) : BackgroundService
{
    private const int MaximumCaptureBytes = 32 * 1024 * 1024;
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(storage.CacheDirectory);
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
        if (matchingTracks.Any(track => File.Exists(track.FilePath))) return;
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
        var path = Path.Combine(storage.CacheDirectory, $"{id:N}.cache");
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
        using var cancellationRegistration = timeoutSource.Token.Register(() => completion.TrySetResult(null));
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

internal sealed record TrackAudioInfo(string ContentType, int BitrateKbps);

internal static class TrackAudioValidation
{
    public static bool TryAnalyze(byte[] content, out TrackAudioInfo audioInfo)
    {
        audioInfo = new TrackAudioInfo(string.Empty, 0);
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            var track = new ATL.Track(stream);
            if (track.DurationMs <= 0 || track.SampleRate <= 0 || track.Bitrate <= 0) return false;

            var detectedType = track.AudioFormat.MimeList.FirstOrDefault()?.ToLowerInvariant();
            var contentType = detectedType switch
            {
                "audio/aac" or "audio/aacp" => "audio/aac",
                "audio/flac" or "audio/x-flac" => "audio/flac",
                "audio/ogg" or "application/ogg" => "audio/ogg",
                "audio/mp3" or "audio/mpeg" => "audio/mpeg",
                _ => string.Empty
            };
            if (contentType.Length == 0) return false;

            audioInfo = new TrackAudioInfo(contentType, Convert.ToInt32(Math.Round(track.Bitrate)));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}