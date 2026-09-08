using Android.Content;
using Android.Content.PM;
using Avalonia.Android;
using MusicLibrary.App.Services;
using Serilog;

[assembly: UsesPermission(global::Android.Manifest.Permission.Internet)]

namespace MusicLibrary.App.Android;

[Activity(Label = "Music Library", Theme = "@style/MyTheme.NoActionBar", Icon = "@drawable/app_icon",
    MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
    private global::Android.Media.MediaPlayer? _cachedTrackPlayer;
    private string? _cachedTrackPath;
    private bool _deleteCachedTrackOnRelease;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        var authenticationPreferences = GetSharedPreferences("authentication", FileCreationMode.Private);
        AuthenticationSessionStorage.Load = () => authenticationPreferences?.GetString("session", null);
        AuthenticationSessionStorage.Save = value =>
        {
            using var editor = authenticationPreferences?.Edit();
            if (value is null) editor?.Remove("session");
            else editor?.PutString("session", value);
            editor?.Apply();
        };
        NativeRadioActions.ListenAsync = async streamUri =>
        {
            ReleaseCachedTrackPlayer();
            var player = CreateAudioPlayer();
            _cachedTrackPlayer = player;
            try
            {
                await player.SetDataSourceAsync(streamUri.AbsoluteUri);
                await PreparePlayerAsync(player);
                player.Start();
            }
            catch
            {
                ReleaseCachedTrackPlayer();
                throw;
            }
        };
        NativeRadioActions.PlayFileAsync = async (content, _, fileName) =>
        {
            ReleaseCachedTrackPlayer();
            var directory = Path.Combine(CacheDir!.AbsolutePath, "Music Library");
            Directory.CreateDirectory(directory);
            _cachedTrackPath = Path.Combine(directory, $"{Guid.NewGuid():N}{Path.GetExtension(Path.GetFileName(fileName))}");
            _deleteCachedTrackOnRelease = true;
            await File.WriteAllBytesAsync(_cachedTrackPath, content);

            var player = CreateAudioPlayer();
            _cachedTrackPlayer = player;
            try
            {
                await player.SetDataSourceAsync(_cachedTrackPath);
                await PreparePlayerAsync(player);
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
        NativeRadioActions.GetPlaybackStateAsync = () => Task.FromResult(
            _cachedTrackPlayer is null ? -1 : _cachedTrackPlayer.IsPlaying ? 1 : 0);
        NativeRadioActions.PlayFileToCompletionAsync = PlayCachedTrackToCompletionAsync;
        NativeRadioActions.StopPlaybackAsync = () =>
        {
            ReleaseCachedTrackPlayer();
            return Task.CompletedTask;
        };
        NativeRadioActions.SaveFileAsync = (content, _, fileName) =>
        {
            return NativeStreamDownloader.SaveAsync(content, fileName, GetOfflineDirectory());
        };
        NativeRadioActions.ListOfflineTracksAsync = ListOfflineTracksAsync;
        NativeRadioActions.PlayOfflineTrackAsync = PlayOfflineTrackAsync;
        NativeRadioActions.DeleteOfflineTrackAsync = DeleteOfflineTrackAsync;
        NativeRadioActions.OfflineDirectoryPath = GetOfflineDirectory();
        base.OnCreate(savedInstanceState);
    }

    protected override void OnDestroy()
    {
        Log.Information("Stopping Music Library Android activity");
        ReleaseCachedTrackPlayer();
        base.OnDestroy();
    }

    private async Task PlayCachedTrackToCompletionAsync(
        byte[] content,
        string contentType,
        string fileName,
        CancellationToken cancellationToken)
    {
        ReleaseCachedTrackPlayer();
        var directory = Path.Combine(CacheDir!.AbsolutePath, "Music Library");
        Directory.CreateDirectory(directory);
        _cachedTrackPath = Path.Combine(directory, $"{Guid.NewGuid():N}{Path.GetExtension(Path.GetFileName(fileName))}");
        _deleteCachedTrackOnRelease = true;
        await File.WriteAllBytesAsync(_cachedTrackPath, content, cancellationToken);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = CreateAudioPlayer();
        _cachedTrackPlayer = player;
        player.Completion += (_, _) =>
        {
            if (_cachedTrackPlayer == player) ReleaseCachedTrackPlayer();
            completion.TrySetResult();
        };
        await using var cancellationRegistration = cancellationToken.Register(() =>
        {
            if (_cachedTrackPlayer == player) ReleaseCachedTrackPlayer();
            completion.TrySetCanceled(cancellationToken);
        });
        try
        {
            await player.SetDataSourceAsync(_cachedTrackPath);
            await PreparePlayerAsync(player, cancellationToken);
            player.Start();
            await completion.Task;
        }
        catch
        {
            if (_cachedTrackPlayer == player) ReleaseCachedTrackPlayer();
            throw;
        }
    }

    private Task<IReadOnlyList<OfflineTrack>> ListOfflineTracksAsync()
    {
        var directory = GetOfflineDirectory();
        Directory.CreateDirectory(directory);
        IReadOnlyList<OfflineTrack> tracks = new DirectoryInfo(directory)
            .EnumerateFiles()
            .Where(file => IsAudioFile(file.Extension))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => new OfflineTrack(
                file.Name,
                Path.GetFileNameWithoutExtension(file.Name),
                file.Length,
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)))
            .ToList();
        return Task.FromResult(tracks);
    }

    private async Task PlayOfflineTrackAsync(string key)
    {
        var path = ResolveOfflineTrackPath(key);
        ReleaseCachedTrackPlayer();
        _cachedTrackPath = path;
        _deleteCachedTrackOnRelease = false;
        var player = CreateAudioPlayer();
        _cachedTrackPlayer = player;
        try
        {
            await player.SetDataSourceAsync(path);
            await PreparePlayerAsync(player);
            player.Completion += (_, _) => ReleaseCachedTrackPlayer();
            player.Start();
        }
        catch
        {
            ReleaseCachedTrackPlayer();
            throw;
        }
    }

    private Task DeleteOfflineTrackAsync(string key)
    {
        File.Delete(ResolveOfflineTrackPath(key));
        return Task.CompletedTask;
    }

    private string GetOfflineDirectory() =>
        GetExternalFilesDir(global::Android.OS.Environment.DirectoryMusic)?.AbsolutePath ?? FilesDir!.AbsolutePath;

    private string ResolveOfflineTrackPath(string key)
    {
        if (key != Path.GetFileName(key)) throw new InvalidOperationException("Invalid offline recording.");
        var path = Path.Combine(GetOfflineDirectory(), key);
        if (!File.Exists(path)) throw new FileNotFoundException("The offline recording no longer exists.", key);
        return path;
    }

    private static bool IsAudioFile(string extension) => extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".aac", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".flac", StringComparison.OrdinalIgnoreCase);

    private static global::Android.Media.MediaPlayer CreateAudioPlayer()
    {
        var player = new global::Android.Media.MediaPlayer();
        using var builder = new global::Android.Media.AudioAttributes.Builder();
        builder.SetUsage(global::Android.Media.AudioUsageKind.Media);
        builder.SetContentType(global::Android.Media.AudioContentType.Music);
        using var audioAttributes = builder.Build()
            ?? throw new InvalidOperationException("Android audio attributes could not be created.");
        player.SetAudioAttributes(audioAttributes);
        return player;
    }

    private static async Task PreparePlayerAsync(
        global::Android.Media.MediaPlayer player,
        CancellationToken cancellationToken = default)
    {
        var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? preparedHandler = null;
        EventHandler<global::Android.Media.MediaPlayer.ErrorEventArgs>? errorHandler = null;
        preparedHandler = (_, _) => prepared.TrySetResult();
        errorHandler = (_, eventArgs) =>
        {
            eventArgs.Handled = true;
            prepared.TrySetException(new InvalidOperationException($"Android audio preparation failed ({eventArgs.What}, {eventArgs.Extra})."));
        };
        player.Prepared += preparedHandler;
        player.Error += errorHandler;
        using var registration = cancellationToken.Register(() => prepared.TrySetCanceled(cancellationToken));
        try
        {
            player.PrepareAsync();
            await prepared.Task;
        }
        finally
        {
            player.Prepared -= preparedHandler;
            player.Error -= errorHandler;
        }
    }

    private void ReleaseCachedTrackPlayer()
    {
        _cachedTrackPlayer?.Release();
        _cachedTrackPlayer?.Dispose();
        _cachedTrackPlayer = null;
        if (_deleteCachedTrackOnRelease && _cachedTrackPath is not null) File.Delete(_cachedTrackPath);
        _cachedTrackPath = null;
        _deleteCachedTrackOnRelease = false;
    }
}