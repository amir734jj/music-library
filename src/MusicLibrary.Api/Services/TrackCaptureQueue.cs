using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MusicLibrary.Api.Services;

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