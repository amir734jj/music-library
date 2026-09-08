namespace MusicLibrary.App.Desktop;

internal static class DesktopSettingsStorage
{
    private static readonly string OfflineDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Music Library",
        "offline-directory.txt");

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
}
