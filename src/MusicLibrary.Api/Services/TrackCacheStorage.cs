using System.Security.Cryptography;
using EfCoreRepository.Interfaces;
using MusicLibrary.Api.Data;
using MusicLibrary.Contracts.Responses;

namespace MusicLibrary.Api.Services;

public sealed class TrackCacheStorage(IConfiguration configuration) : ITrackCacheStorage
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _activeKeyFingerprint;

    private string CacheDirectory { get; } = configuration["TRENDING_CACHE_DIRECTORY"]
        ?? Path.Combine(Path.GetTempPath(), "music-library-trending-cache");

    public string CreateEncryptedPath(Guid trackId, byte[] encryptionKey)
    {
        var encryptedName = TrackCacheCryptography.Encrypt(trackId.ToByteArray(), encryptionKey);
        var encodedName = Convert.ToBase64String(encryptedName)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return Path.Combine(CacheDirectory, $"enc-{encodedName}.cache");
    }

    public bool Exists(string path) => File.Exists(path);

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
            metadata = [.. await tracks.GetAll<CachedTrack>(maxResults: 100000)];
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
                await EncryptLegacyFileNamesAsync(repository, encryptionKey, cancellationToken);
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

    private async Task EncryptLegacyFileNamesAsync(
        IEfRepository repository,
        byte[] encryptionKey,
        CancellationToken cancellationToken)
    {
        var tracks = repository.For<CachedTrack>();
        var metadata = await tracks.GetAll<CachedTrack>(maxResults: 100000);
        foreach (var track in metadata)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.GetFileName(track.FilePath).StartsWith("enc-", StringComparison.Ordinal)) continue;

            var encryptedPath = CreateEncryptedPath(track.Id, encryptionKey);
            try
            {
                File.Move(track.FilePath, encryptedPath);
                await tracks.Update<Guid>(track.Id, candidate => candidate.FilePath = encryptedPath);
            }
            catch
            {
                if (File.Exists(encryptedPath) && !File.Exists(track.FilePath))
                {
                    File.Move(encryptedPath, track.FilePath);
                }
                throw;
            }
        }
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