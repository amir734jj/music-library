namespace MusicLibrary.App.Services;

public static class AuthenticationSessionStorage
{
    public static Func<string?>? Load { get; set; }
    public static Action<string?>? Save { get; set; }
}