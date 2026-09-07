using System.Runtime.InteropServices.JavaScript;

namespace MusicLibrary.App.Browser;

internal static partial class BrowserAuthenticationSessionStorage
{
    [JSImport("loadAuthenticationSession", "./authenticationSession.js")]
    internal static partial string? Load();

    [JSImport("saveAuthenticationSession", "./authenticationSession.js")]
    internal static partial void Save(string? value);
}
