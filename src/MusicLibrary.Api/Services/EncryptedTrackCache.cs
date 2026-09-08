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

public interface IEncryptedTrackCacheService
{
    Task<CachedTrackDownload?> GetAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class EncryptedTrackCacheService(
    IGlobalConfigService configService,
    IEfRepository repository) : IEncryptedTrackCacheService
{
    public async Task<CachedTrackDownload?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var cachedTrack = await repository.For<CachedTrack>().Get<Guid>(id);
        if (cachedTrack is null || cachedTrack.ExpiresAt <= DateTimeOffset.UtcNow || !File.Exists(cachedTrack.FilePath)) return null;

        var config = await configService.GetAsync(cancellationToken);
        if (!TrackCacheCryptography.TryGetKey(config.TrendingCacheEncryptionKey, out var key)
            || cachedTrack.KeyFingerprint != TrackCacheCryptography.GetFingerprint(key)) return null;

        var encrypted = await File.ReadAllBytesAsync(cachedTrack.FilePath, cancellationToken);
        var content = TrackCacheCryptography.Decrypt(encrypted, key);
        var name = string.IsNullOrWhiteSpace(cachedTrack.Title)
            ? cachedTrack.Artist
            : $"{cachedTrack.Artist} - {cachedTrack.Title}";
        return new CachedTrackDownload(content, cachedTrack.ContentType, $"{SanitizeFileName(name)}{GetExtension(cachedTrack.ContentType)}");
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
        "audio/ogg" => ".ogg",
        _ => ".mp3"
    };
}

public sealed class TrackCacheStorage
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string CacheDirectory { get; } = Path.Combine(Path.GetTempPath(), "music-library-trending-cache");

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

    public async Task EnforceLimitAsync(
        IEfRepository repository,
        long maximumCacheBytes,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            await RemoveExpiredAsync(repository, cancellationToken);
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
        var now = DateTimeOffset.UtcNow;
        var tracks = repository.For<CachedTrack>();
        var expired = await tracks.GetAll<CachedTrack>(filterExprs: [track => track.ExpiresAt <= now], maxResults: 1000);
        foreach (var track in expired)
        {
            File.Delete(track.FilePath);
        }
        var expiredIds = expired.Select(track => track.Id).ToArray();
        if (expiredIds.Length > 0) await tracks.Delete([track => expiredIds.Contains(track.Id)]);
        cancellationToken.ThrowIfCancellationRequested();
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(storage.CacheDirectory);
        using (var scope = scopeFactory.CreateScope())
        {
            var config = await scope.ServiceProvider.GetRequiredService<IGlobalConfigService>().GetAsync(stoppingToken);
            var repository = scope.ServiceProvider.GetRequiredService<IEfRepository>();
            await storage.EnforceLimitAsync(
                repository,
                config.TrendingCacheMaxSizeMegabytes * 1024L * 1024L,
                stoppingToken);
        }
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

    private async Task CaptureAsync(TrackCaptureRequest request, CancellationToken cancellationToken)
    {
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
        if (audio is null || audio.Length == 0) return;
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
            ContentType = "audio/mpeg",
            PlaintextLength = audio.Length,
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
        using var song = new MemoryStream();
        var captureStarted = false;
        using var ripper = streamRipperFactory.New(new StreamRipperOptions
        {
            Url = request.StreamUri,
            MaxBufferSize = MaximumCaptureBytes,
            MetadataOnly = false
        });
        ripper.SongChangedEventHandlers += (_, eventArgs) =>
        {
            var metadata = eventArgs.SongInfo.SongMetadata;
            if (captureStarted && Matches(metadata.Artist, metadata.Title, request))
            {
                var content = eventArgs.SongInfo.Stream.ToArray();
                if (song.Length + content.Length <= MaximumCaptureBytes)
                {
                    song.Write(content);
                }
                else
                {
                    captureStarted = false;
                    completion.TrySetResult(null);
                }
            }
            eventArgs.SongInfo.Dispose();
        };
        ripper.MetadataChangedHandlers += (_, eventArgs) =>
        {
            var metadata = eventArgs.SongMetadata;
            if (Matches(metadata.Artist, metadata.Title, request))
            {
                captureStarted = true;
                return;
            }
            if (captureStarted)
            {
                captureStarted = false;
                completion.TrySetResult(song.Length == 0 ? null : song.ToArray());
            }
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

internal static class TrackCacheCryptography
{
    private static readonly byte[] Header = "MLTC1"u8.ToArray();

    public static bool TryGetKey(string value, out byte[] key)
    {
        try
        {
            key = Convert.FromBase64String(value.Trim());
            return key.Length == 32;
        }
        catch (FormatException)
        {
            key = [];
            return false;
        }
    }

    public static string GetFingerprint(byte[] key) => Convert.ToHexString(SHA256.HashData(key));

    public static byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = new byte[plaintext.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return [.. Header, .. nonce, .. tag, .. ciphertext];
    }

    public static byte[] Decrypt(byte[] encrypted, byte[] key)
    {
        const int nonceLength = 12;
        const int tagLength = 16;
        if (encrypted.Length <= Header.Length + nonceLength + tagLength
            || !encrypted.AsSpan(0, Header.Length).SequenceEqual(Header)) throw new CryptographicException("The cached track is invalid.");

        var nonce = encrypted.AsSpan(Header.Length, nonceLength);
        var tag = encrypted.AsSpan(Header.Length + nonceLength, tagLength);
        var ciphertext = encrypted.AsSpan(Header.Length + nonceLength + tagLength);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, tagLength);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}