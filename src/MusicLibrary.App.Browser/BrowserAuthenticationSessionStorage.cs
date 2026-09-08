using System.Runtime.InteropServices.JavaScript;

namespace MusicLibrary.App.Browser;

internal static partial class BrowserAuthenticationSessionStorage
{
    internal const string ModuleName = "authenticationSession";

    [JSImport("loadAuthenticationSession", ModuleName)]
    internal static partial string? Load();

    [JSImport("saveAuthenticationSession", ModuleName)]
    internal static partial void Save(string? value);

    [JSImport("downloadFile", ModuleName)]
    internal static partial void DownloadFile(byte[] content, string contentType, string fileName);

    [JSImport("playFile", ModuleName)]
    internal static partial Task PlayFileAsync(byte[] content, string contentType);

    [JSImport("listenLive", ModuleName)]
    internal static partial Task ListenLiveAsync(string streamUrl);

    [JSImport("toggleFilePlayback", ModuleName)]
    internal static partial Task<int> ToggleFilePlaybackAsync();
}
