using Avalonia;
using MusicLibrary.App;
using System.Diagnostics;

namespace MusicLibrary.App.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        MusicLibraryApi.Configure(new Uri("https://music-library.coolify.hesamian.com/"));
        AuthenticationSessionStorage.Load = DesktopAuthenticationSessionStorage.Load;
        AuthenticationSessionStorage.Save = DesktopAuthenticationSessionStorage.Save;
        NativeRadioActions.ListenAsync = streamUri =>
        {
            Process.Start(new ProcessStartInfo(streamUri.AbsoluteUri) { UseShellExecute = true });
            return Task.CompletedTask;
        };
        NativeRadioActions.DownloadAsync = (streamUri, duration) => NativeStreamDownloader.DownloadAsync(streamUri,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Music Library"), duration);
        NativeRadioActions.PlayFileAsync = async (content, _, fileName) =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "Music Library");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}{Path.GetExtension(Path.GetFileName(fileName))}");
            await File.WriteAllBytesAsync(temporaryPath, content);
            Process.Start(new ProcessStartInfo(temporaryPath) { UseShellExecute = true });
        };
        NativeRadioActions.SaveFileAsync = (content, _, fileName) => NativeStreamDownloader.SaveAsync(content, fileName,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Music Library"));
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
    }
}

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