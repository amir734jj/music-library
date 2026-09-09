using System.Text.Json;
using MusicLibrary.App.Services;

namespace MusicLibrary.App.Desktop;

internal static class DesktopSettingsStorage
{
    private static readonly string OfflineDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Music Library",
        "offline-directory.txt");
    private static readonly string StationSubscriptionsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Music Library",
        "station-subscriptions.json");

    public static string? LoadOfflineDirectory()
    {
        try
        {
            return File.Exists(OfflineDirectoryPath) ? File.ReadAllText(OfflineDirectoryPath).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void SaveOfflineDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OfflineDirectoryPath)!);
            File.WriteAllText(OfflineDirectoryPath, path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static IReadOnlyList<LocalStationSubscription> LoadStationSubscriptions()
    {
        try
        {
            if (!File.Exists(StationSubscriptionsPath)) return [];
            return JsonSerializer.Deserialize<List<LocalStationSubscription>>(
                File.ReadAllText(StationSubscriptionsPath)) ?? [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public static void SaveStationSubscriptions(IEnumerable<LocalStationSubscription> subscriptions)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StationSubscriptionsPath)!);
            File.WriteAllText(StationSubscriptionsPath, JsonSerializer.Serialize(subscriptions));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
