namespace MusicLibrary.App.Desktop;

internal static class DesktopAuthenticationSessionStorage
{
    private static readonly string SessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Music Library",
        "authentication.json");

    public static string? Load()
    {
        try
        {
            return File.Exists(SessionPath) ? File.ReadAllText(SessionPath) : null;
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

    public static void Save(string? value)
    {
        var temporaryPath = $"{SessionPath}.tmp";
        try
        {
            if (value is null)
            {
                File.Delete(SessionPath);
                return;
            }

            var directory = Path.GetDirectoryName(SessionPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(temporaryPath, value);
            File.Move(temporaryPath, SessionPath, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
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
}