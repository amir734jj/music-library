namespace MusicLibrary.App.Services;

public sealed record OfflineTrack(
    string Key,
    string Name,
    long SizeBytes,
    DateTimeOffset SavedAt,
    string? StationName = null);

public sealed record LocalStationSubscription(Guid StationId, string StationName);

public static class NativeRadioActions
{
    public static string? OfflineDirectoryPath { get; set; }
    public static Func<string, Task>? SetOfflineDirectoryAsync { get; set; }
    public static Func<Uri, Task>? ListenAsync { get; set; }
    public static Func<Guid, Task>? ListenToStationAsync { get; set; }
    public static Func<byte[], string, string, Task>? PlayFileAsync { get; set; }
    public static Func<byte[], string, string, CancellationToken, Task>? PlayFileToCompletionAsync { get; set; }
    public static Func<Task<int>>? ToggleFilePlaybackAsync { get; set; }
    public static Func<Task<int>>? GetPlaybackStateAsync { get; set; }
    public static Func<Task>? StopPlaybackAsync { get; set; }
    public static Func<byte[], string, string, Task<string>>? SaveFileAsync { get; set; }
    public static Func<Task<IReadOnlyList<OfflineTrack>>>? ListOfflineTracksAsync { get; set; }
    public static Func<string, Task>? PlayOfflineTrackAsync { get; set; }
    public static Func<string, CancellationToken, Task>? PlayOfflineTrackToCompletionAsync { get; set; }
    public static Func<string, Task>? DeleteOfflineTrackAsync { get; set; }
    public static Func<Task<IReadOnlyList<LocalStationSubscription>>>? ListStationSubscriptionsAsync { get; set; }
    public static Func<LocalStationSubscription, Task>? SubscribeToStationAsync { get; set; }
    public static Func<Guid, Task>? UnsubscribeFromStationAsync { get; set; }
}