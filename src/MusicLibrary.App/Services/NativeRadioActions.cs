namespace MusicLibrary.App.Services;

public sealed record OfflineTrack(string Key, string Name, long SizeBytes, DateTimeOffset SavedAt);

public static class NativeRadioActions
{
    public static bool SupportsStreamRecorder { get; set; }
    public static Func<Uri, Task>? ListenAsync { get; set; }
    public static Func<Guid, Task>? ListenToStationAsync { get; set; }
    public static Func<Uri, TimeSpan, Task<string>>? DownloadAsync { get; set; }
    public static Func<byte[], string, string, Task>? PlayFileAsync { get; set; }
    public static Func<byte[], string, string, CancellationToken, Task>? PlayFileToCompletionAsync { get; set; }
    public static Func<Task<int>>? ToggleFilePlaybackAsync { get; set; }
    public static Func<Task<int>>? GetPlaybackStateAsync { get; set; }
    public static Func<Task>? StopPlaybackAsync { get; set; }
    public static Func<byte[], string, string, Task<string>>? SaveFileAsync { get; set; }
    public static Func<Task<IReadOnlyList<OfflineTrack>>>? ListOfflineTracksAsync { get; set; }
    public static Func<string, Task>? PlayOfflineTrackAsync { get; set; }
    public static Func<string, Task>? DeleteOfflineTrackAsync { get; set; }
}