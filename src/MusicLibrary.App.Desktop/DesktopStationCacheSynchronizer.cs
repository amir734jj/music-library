using MusicLibrary.App.Services;
using Serilog;

namespace MusicLibrary.App.Desktop;

internal sealed class DesktopStationCacheSynchronizer : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private readonly Dictionary<Guid, LocalStationSubscription> _subscriptions;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly object _gate = new();
    private string _cacheDirectory;

    public DesktopStationCacheSynchronizer(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
        _subscriptions = DesktopSettingsStorage.LoadStationSubscriptions()
            .GroupBy(subscription => subscription.StationId)
            .ToDictionary(group => group.Key, group => group.Last());
        _ = Task.Run(() => RunAsync(_cancellation.Token));
    }

    public IReadOnlyList<LocalStationSubscription> ListSubscriptions()
    {
        lock (_gate)
        {
            return [.. _subscriptions.Values.OrderBy(subscription => subscription.StationName)];
        }
    }

    public async Task SubscribeAsync(LocalStationSubscription subscription)
    {
        lock (_gate)
        {
            _subscriptions[subscription.StationId] = subscription;
            DesktopSettingsStorage.SaveStationSubscriptions(_subscriptions.Values);
        }
        await SynchronizeAsync(_cancellation.Token);
    }

    public Task UnsubscribeAsync(Guid stationId)
    {
        lock (_gate)
        {
            if (_subscriptions.Remove(stationId))
            {
                DesktopSettingsStorage.SaveStationSubscriptions(_subscriptions.Values);
            }
        }
        return Task.CompletedTask;
    }

    public void SetCacheDirectory(string cacheDirectory)
    {
        lock (_gate)
        {
            _cacheDirectory = cacheDirectory;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await SynchronizeAsync(cancellationToken);
                await Task.Delay(PollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            LocalStationSubscription[] subscriptions;
            lock (_gate)
            {
                subscriptions = [.. _subscriptions.Values];
            }

            foreach (var subscription in subscriptions)
            {
                try
                {
                    await SynchronizeSubscriptionAsync(subscription, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Log.Warning(exception, "Could not synchronize cached tracks for {StationName}", subscription.StationName);
                }
            }
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task SynchronizeSubscriptionAsync(
        LocalStationSubscription subscription,
        CancellationToken cancellationToken)
    {
        string cacheDirectory;
        lock (_gate)
        {
            if (!_subscriptions.ContainsKey(subscription.StationId)) return;
            cacheDirectory = _cacheDirectory;
        }

        var stationDirectory = Path.Combine(
            cacheDirectory,
            SanitizeFileName(subscription.StationName, subscription.StationId.ToString("N")));
        Directory.CreateDirectory(stationDirectory);
        var existingNames = Directory.EnumerateFiles(stationDirectory)
            .Select(Path.GetFileName)
            .ToArray();
        var cachedTracks = await MusicLibraryApi.GetStationCachedTracksAsync(subscription.StationId, cancellationToken);
        foreach (var track in cachedTracks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var idToken = track.CachedTrackId.ToString("N");
            if (existingNames.Any(name => name?.Contains(idToken, StringComparison.OrdinalIgnoreCase) == true)) continue;

            var download = await MusicLibraryApi.DownloadTrendingTrackAsync(track.CachedTrackId, cancellationToken);
            var displayName = string.Join(" - ", new[] { track.Artist, track.Title }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            var extension = Path.GetExtension(download.FileName);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".mp3";
            var fileName = $"{SanitizeFileName(displayName, "radio-track")} [{idToken}]{extension}";
            await NativeStreamDownloader.SaveAsync(download.Content, fileName, stationDirectory, cancellationToken);
        }
    }

    private static string SanitizeFileName(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(character =>
            invalidCharacters.Contains(character) ? '_' : character).ToArray()).Trim(' ', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    public void Dispose()
    {
        _cancellation.Cancel();
    }
}
