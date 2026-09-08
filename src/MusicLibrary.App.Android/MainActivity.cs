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
    private global::Android.Media.MediaPlayer? _cachedTrackPlayer;
    private string? _cachedTrackPath;

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
        NativeRadioActions.PlayFileAsync = async (content, _, fileName) =>
        {
            ReleaseCachedTrackPlayer();
            var directory = Path.Combine(CacheDir!.AbsolutePath, "Music Library");
            Directory.CreateDirectory(directory);
            _cachedTrackPath = Path.Combine(directory, $"{Guid.NewGuid():N}{Path.GetExtension(Path.GetFileName(fileName))}");
            await File.WriteAllBytesAsync(_cachedTrackPath, content);

            var player = new global::Android.Media.MediaPlayer();
            _cachedTrackPlayer = player;
            try
            {
                player.SetDataSource(_cachedTrackPath);
                player.Prepare();
                player.Completion += (_, _) => ReleaseCachedTrackPlayer();
                player.Start();
            }
            catch
            {
                ReleaseCachedTrackPlayer();
                throw;
            }
        };
        NativeRadioActions.ToggleFilePlaybackAsync = () =>
        {
            if (_cachedTrackPlayer is null) return Task.FromResult(-1);
            if (_cachedTrackPlayer.IsPlaying)
            {
                _cachedTrackPlayer.Pause();
                return Task.FromResult(0);
            }

            _cachedTrackPlayer.Start();
            return Task.FromResult(1);
        };
        NativeRadioActions.SaveFileAsync = (content, _, fileName) =>
        {
            var directory = GetExternalFilesDir(global::Android.OS.Environment.DirectoryMusic)?.AbsolutePath ?? FilesDir!.AbsolutePath;
            return NativeStreamDownloader.SaveAsync(content, fileName, directory);
        };
        base.OnCreate(savedInstanceState);
    }

    protected override void OnDestroy()
    {
        ReleaseCachedTrackPlayer();
        base.OnDestroy();
    }

    private void ReleaseCachedTrackPlayer()
    {
        _cachedTrackPlayer?.Release();
        _cachedTrackPlayer?.Dispose();
        _cachedTrackPlayer = null;
        if (_cachedTrackPath is not null) File.Delete(_cachedTrackPath);
        _cachedTrackPath = null;
    }
}