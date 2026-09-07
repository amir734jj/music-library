using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using MusicLibrary.App;

namespace MusicLibrary.App.Android;

[Activity(Label = "Music Library", Theme = "@style/MyTheme.NoActionBar", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        MusicLibraryApi.Configure(new Uri("https://music-library.coolify.hesamian.com/"));
        NativeRadioActions.ListenAsync = streamUri =>
        {
            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(global::Android.Net.Uri.Parse(streamUri.AbsoluteUri), "audio/*");
            StartActivity(Intent.CreateChooser(intent, "Listen live"));
            return Task.CompletedTask;
        };
        NativeRadioActions.DownloadAsync = (streamUri, duration) =>
        {
            var directory = GetExternalFilesDir(global::Android.OS.Environment.DirectoryMusic)?.AbsolutePath ?? FilesDir!.AbsolutePath;
            return NativeStreamDownloader.DownloadAsync(streamUri, directory, duration);
        };
        base.OnCreate(savedInstanceState);
    }
}