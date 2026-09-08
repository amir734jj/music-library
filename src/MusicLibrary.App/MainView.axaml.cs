using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MusicLibrary.App.Services;
using MusicLibrary.Contracts.Constants;
using MusicLibrary.Contracts.Responses;
using Projektanker.Icons.Avalonia;

namespace MusicLibrary.App;

public sealed partial class MainView : UserControl
{
    private enum LibraryMode
    {
        NowPlaying,
        Stations,
        Following,
        Trending,
        Offline,
        UserBoard,
        About
    }

    private const int ProbePageSize = 100;
    private static readonly Dictionary<string, Geometry> ActionIconGeometries = [];
    private readonly SemaphoreSlim _nowPlayingLoadGate = new(1, 1);
    private readonly SemaphoreSlim _playbackActivitySyncGate = new(1, 1);
    private readonly SemaphoreSlim _probeStatusLoadGate = new(1, 1);
    private readonly HashSet<string> _subscribedArtists = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Button>> _subscriptionButtons = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _librarySearchCancellation;
    private CancellationTokenSource? _libraryPollingCancellation;
    private CancellationTokenSource? _playbackActivityHeartbeatCancellation;
    private CancellationTokenSource? _trendingBatchDownloadCancellation;
    private CancellationTokenSource? _trendingPlaybackCancellation;
    private CancellationTokenSource? _probeSearchCancellation;
    private CancellationTokenSource? _probeStatusPollingCancellation;
    private CancellationTokenSource? _sessionExpiryCancellation;
    private string? _trendingBatchDownloadStatus;
    private string? _completedTrendingBatchDownloadStatus;
    private bool _probingEnabled;
    private bool _clearTrendingCacheConfirmed;
    private int _enabledStationCount;
    private int _probePage = 1;
    private int _probePageCount = 1;
    private LibraryMode _libraryMode;
    private bool _suppressLibrarySearch;
    private IReadOnlyCollection<NowPlayingSummary> _nowPlayingSnapshot = [];
    private IReadOnlyCollection<ArtistSubscriptionSummary> _followingSnapshot = [];
    private IReadOnlyCollection<TrendingSummary> _trendingSnapshot = [];
    private IReadOnlyCollection<UserPlaybackActivitySummary> _playbackActivitySnapshot = [];
    private Guid? _playingStationId;
    private Button? _playingStationButton;
    private Guid? _playingCachedTrackId;
    private Button? _playingCachedTrackButton;
    private string? _playingOfflineTrackKey;
    private Button? _playingOfflineTrackButton;
    private bool _isCachedTrackPlaying;
    private string? _playbackActivityDescription;
    private bool _playbackActivityIsLiveStation;
    private bool _isPlaybackActivityActive;
    private bool _subscriptionsLoaded;
    private bool? _isCompactLayout;

    public bool IsBrowserHost { get; }
    public bool ShowNativeMedia
    {
        get { return !IsBrowserHost; }
    }
    public bool ShowOfflineMode => NativeRadioActions.ListOfflineTracksAsync is not null;

    public MainView() : this(showAdministration: false)
    {
    }

    public MainView(bool showAdministration = false)
    {
        IsBrowserHost = showAdministration;
        InitializeComponent();
        AuthenticationView.Authenticated += AuthenticationView_Authenticated;
        AuthenticationView.OfflineRequested += AuthenticationView_OfflineRequested;
        MusicLibraryApi.SessionInvalidated += MusicLibraryApi_SessionInvalidated;
        OfflineNavigationButton.IsVisible = ShowOfflineMode;
        AuthenticationView.IsVisible = true;
        ApplicationView.IsVisible = false;
        SignOutButton.IsVisible = true;
    }

    protected override async void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        ApplyResponsiveLayout(Bounds.Width);
        if (!MusicLibraryApi.IsConfigured)
        {
            ApiConnectionStatus.Text = "Browser app";
            return;
        }

        try
        {
            ApiConnectionStatus.Text = await MusicLibraryApi.IsHealthyAsync() ? "API connected" : "API unavailable";
            if (await MusicLibraryApi.RestoreSessionAsync() is { } restoredUser)
            {
                ShowAuthenticatedApplication(restoredUser);
                _ = LoadNowPlayingAsync();
            }
        }
        catch (HttpRequestException)
        {
            ApiConnectionStatus.Text = "API unavailable";
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs eventArgs)
    {
        _librarySearchCancellation?.Cancel();
        _libraryPollingCancellation?.Cancel();
        _playbackActivityHeartbeatCancellation?.Cancel();
        _trendingBatchDownloadCancellation?.Cancel();
        _trendingPlaybackCancellation?.Cancel();
        _probeSearchCancellation?.Cancel();
        _probeStatusPollingCancellation?.Cancel();
        _sessionExpiryCancellation?.Cancel();
        base.OnDetachedFromVisualTree(eventArgs);
    }

    private void MainView_SizeChanged(object? sender, SizeChangedEventArgs eventArgs)
    {
        ApplyResponsiveLayout(eventArgs.NewSize.Width);
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0) return;
        var useCompactLayout = width < 760;
        if (_isCompactLayout == useCompactLayout) return;
        _isCompactLayout = useCompactLayout;

        if (useCompactLayout)
        {
            ApplicationView.Margin = new Avalonia.Thickness(10);
            HeaderGrid.ColumnDefinitions = new ColumnDefinitions("*");
            HeaderGrid.RowDefinitions = new RowDefinitions("Auto,8,Auto");
            Grid.SetColumn(HeaderActions, 0);
            Grid.SetRow(HeaderActions, 2);
            HeaderActions.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            CurrentUserStatus.IsVisible = false;

            MainShell.ColumnDefinitions = new ColumnDefinitions("*");
            MainShell.RowDefinitions = new RowDefinitions("Auto,12,*,12,Auto");
            LibraryNavigationPanel.Orientation = Avalonia.Layout.Orientation.Horizontal;
            LibraryNavigationTitle.IsVisible = false;
            LibraryNavigationScroll.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
            Grid.SetColumn(LibraryView, 0);
            Grid.SetRow(LibraryView, 2);
            Grid.SetColumn(AdministrationView, 0);
            Grid.SetRow(AdministrationView, 2);
            Grid.SetColumn(PlaybackDock, 0);
            Grid.SetColumnSpan(PlaybackDock, 1);
            Grid.SetRow(PlaybackDock, 4);
            AdministrationHeader.ColumnDefinitions = new ColumnDefinitions("*");
            AdministrationHeader.RowDefinitions = new RowDefinitions("Auto,8,Auto");
            Grid.SetColumn(BackToLibraryButton, 0);
            Grid.SetRow(BackToLibraryButton, 2);
            BackToLibraryButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        }
        else
        {
            ApplicationView.Margin = new Avalonia.Thickness(16);
            HeaderGrid.ColumnDefinitions = new ColumnDefinitions("*,Auto");
            HeaderGrid.RowDefinitions = new RowDefinitions("Auto");
            Grid.SetColumn(HeaderActions, 1);
            Grid.SetRow(HeaderActions, 0);
            HeaderActions.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            CurrentUserStatus.IsVisible = true;

            MainShell.ColumnDefinitions = new ColumnDefinitions("240,16,*");
            MainShell.RowDefinitions = new RowDefinitions("*,12,Auto");
            LibraryNavigationPanel.Orientation = Avalonia.Layout.Orientation.Vertical;
            LibraryNavigationTitle.IsVisible = true;
            LibraryNavigationScroll.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
            Grid.SetColumn(LibraryView, 2);
            Grid.SetRow(LibraryView, 0);
            Grid.SetColumn(AdministrationView, 2);
            Grid.SetRow(AdministrationView, 0);
            Grid.SetColumn(PlaybackDock, 0);
            Grid.SetColumnSpan(PlaybackDock, 3);
            Grid.SetRow(PlaybackDock, 2);
            AdministrationHeader.ColumnDefinitions = new ColumnDefinitions("*,Auto");
            AdministrationHeader.RowDefinitions = new RowDefinitions("Auto");
            Grid.SetColumn(BackToLibraryButton, 1);
            Grid.SetRow(BackToLibraryButton, 0);
            BackToLibraryButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        }
    }

    private void AuthenticationView_Authenticated(object? sender, AuthenticatedEventArgs eventArgs)
    {
        ShowAuthenticatedApplication(eventArgs.User);
        _ = LoadNowPlayingAsync();
    }

    private void AuthenticationView_OfflineRequested(object? sender, EventArgs eventArgs)
    {
        AuthenticationView.IsVisible = false;
        ApplicationView.IsVisible = true;
        CurrentUserStatus.Text = "Offline";
        ApiConnectionStatus.Text = "Offline mode";
        SignOutButton.Content = "Sign in";
        SignOutButton.IsVisible = true;
        AdministrationSeparator.IsVisible = false;
        AdministrationButton.IsVisible = false;
        SetOnlineNavigationVisibility(false);
        SetLibraryMode(LibraryMode.Offline);
        ShowLibrary();
        _ = LoadOfflineTracksAsync();
    }

    private void ShowAuthenticatedApplication(UserSummary user)
    {
        _subscribedArtists.Clear();
        _subscriptionsLoaded = false;
        AuthenticationView.IsVisible = false;
        ApplicationView.IsVisible = true;
        SignOutButton.Content = "Sign out";
        ApplyResponsiveLayout(Bounds.Width);
        CurrentUserStatus.Text = user.DisplayName ?? user.Email;
        var isAdmin = user.Roles.Contains(Roles.Admin);
        AdministrationSeparator.IsVisible = isAdmin;
        AdministrationButton.IsVisible = isAdmin;
        SetOnlineNavigationVisibility(true);
        SetLibraryMode(LibraryMode.NowPlaying);
        ShowLibrary();
        ScheduleSessionExpiry();
    }

    private void SetOnlineNavigationVisibility(bool isVisible)
    {
        NowPlayingNavigationButton.IsVisible = isVisible;
        StationsNavigationButton.IsVisible = isVisible;
        FollowingNavigationButton.IsVisible = isVisible;
        TrendingNavigationButton.IsVisible = isVisible;
        UserBoardNavigationButton.IsVisible = isVisible;
    }

    private void SignOut_Click(object? sender, RoutedEventArgs eventArgs)
    {
        SignOutAndShowAuthentication();
    }

    private void MusicLibraryApi_SessionInvalidated()
    {
        SignOutAndShowAuthentication();
    }

    private void SignOutAndShowAuthentication()
    {
        _librarySearchCancellation?.Cancel();
        _libraryPollingCancellation?.Cancel();
        _trendingBatchDownloadCancellation?.Cancel();
        _trendingPlaybackCancellation?.Cancel();
        _probeSearchCancellation?.Cancel();
        _probeStatusPollingCancellation?.Cancel();
        _sessionExpiryCancellation?.Cancel();
        StopPublishingPlaybackActivity();
        MusicLibraryApi.SignOut();
        ApplicationView.IsVisible = false;
        AuthenticationView.IsVisible = true;
        AuthenticationView.Reset();
    }

    private void ScheduleSessionExpiry()
    {
        _sessionExpiryCancellation?.Cancel();
        if (MusicLibraryApi.SessionExpiresAt is not { } expiresAt) return;

        var cancellation = _sessionExpiryCancellation = new CancellationTokenSource();
        _ = ExpireSessionAsync(expiresAt, cancellation.Token);
    }

    private async Task ExpireSessionAsync(DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        try
        {
            var remaining = expiresAt - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken);
            if (!cancellationToken.IsCancellationRequested) SignOutAndShowAuthentication();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Library_Click(object? sender, RoutedEventArgs eventArgs)
    {
        SetLibraryMode(LibraryMode.NowPlaying);
        ShowLibrary();
        _ = LoadCurrentLibraryViewAsync(LibrarySearchInput.Text);
    }

    private void Trending_Click(object? sender, RoutedEventArgs eventArgs)
    {
        SetLibraryMode(LibraryMode.Trending);
        ShowLibrary();
        _ = LoadCurrentLibraryViewAsync(LibrarySearchInput.Text);
    }

    private void Offline_Click(object? sender, RoutedEventArgs eventArgs)
    {
        SetLibraryMode(LibraryMode.Offline);
        ShowLibrary();
        _ = LoadCurrentLibraryViewAsync();
    }

    private async void ChangeOfflineDirectory_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (NativeRadioActions.SetOfflineDirectoryAsync is null) return;
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null) return;

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder for cached recordings",
            AllowMultiple = false
        });
        var selectedPath = folders.Count > 0 ? folders[0].Path.LocalPath : null;
        if (string.IsNullOrWhiteSpace(selectedPath)) return;

        ChangeOfflineDirectoryButton.IsEnabled = false;
        NowPlayingStatus.Text = "Moving cached recordings...";
        try
        {
            await NativeRadioActions.SetOfflineDirectoryAsync(selectedPath);
            await LoadOfflineTracksAsync(LibrarySearchInput.Text);
        }
        catch (Exception exception)
        {
            NowPlayingStatus.Text = exception.Message;
        }
        finally
        {
            ChangeOfflineDirectoryButton.IsEnabled = true;
        }
    }

    private void Stations_Click(object? sender, RoutedEventArgs eventArgs)
    {
        SetLibraryMode(LibraryMode.Stations);
        ShowLibrary();
        _ = LoadCurrentLibraryViewAsync(LibrarySearchInput.Text);
    }

    private void Following_Click(object? sender, RoutedEventArgs eventArgs)
    {
        SetLibraryMode(LibraryMode.Following);
        ShowLibrary();
        _ = LoadCurrentLibraryViewAsync();
    }

    private void UserBoard_Click(object? sender, RoutedEventArgs eventArgs)
    {
        SetLibraryMode(LibraryMode.UserBoard);
        ShowLibrary();
        _ = LoadCurrentLibraryViewAsync();
    }

    private void About_Click(object? sender, RoutedEventArgs eventArgs)
    {
        SetLibraryMode(LibraryMode.About);
        ShowLibrary();
        ShowAbout();
    }

    private void SetLibraryMode(LibraryMode mode)
    {
        _librarySearchCancellation?.Cancel();
        _libraryPollingCancellation?.Cancel();
        _trendingPlaybackCancellation?.Cancel();
        _libraryMode = mode;
        NowPlayingNavigationButton.Classes.Set("active", mode == LibraryMode.NowPlaying);
        StationsNavigationButton.Classes.Set("active", mode == LibraryMode.Stations);
        FollowingNavigationButton.Classes.Set("active", mode == LibraryMode.Following);
        TrendingNavigationButton.Classes.Set("active", mode == LibraryMode.Trending);
        OfflineNavigationButton.Classes.Set("active", mode == LibraryMode.Offline);
        UserBoardNavigationButton.Classes.Set("active", mode == LibraryMode.UserBoard);
        AboutNavigationButton.Classes.Set("active", mode == LibraryMode.About);
        LibrarySearchInput.IsVisible = mode != LibraryMode.About;
        NowPlayingStatus.IsVisible = mode != LibraryMode.About;
        PlayAllTrendingButton.IsVisible = mode == LibraryMode.Trending
            && NativeRadioActions.PlayFileToCompletionAsync is not null;
        DownloadAllTrendingButton.IsVisible = mode == LibraryMode.Trending && ShowNativeMedia;
        ChangeOfflineDirectoryButton.IsVisible = mode == LibraryMode.Offline
            && NativeRadioActions.SetOfflineDirectoryAsync is not null;
        LibraryViewTitle.Text = mode switch
        {
            LibraryMode.Following => "Following",
            LibraryMode.Stations => "Stations",
            LibraryMode.Trending => "Trending Now",
            LibraryMode.Offline => "Cached locally",
            LibraryMode.UserBoard => "User board",
            LibraryMode.About => "About",
            _ => "Now Playing"
        };
        LibrarySearchInput.PlaceholderText = mode switch
        {
            LibraryMode.Following => "Search followed artists",
            LibraryMode.Stations => "Search station, genre, or URL",
            LibraryMode.Trending => "Search trending artist or track",
            LibraryMode.Offline => "Search downloaded recordings",
            LibraryMode.UserBoard => "Search listeners or playback",
            LibraryMode.About => string.Empty,
            _ => "Search artist, track, or station"
        };
        _suppressLibrarySearch = true;
        try
        {
            LibrarySearchInput.Text = string.Empty;
        }
        finally
        {
            _suppressLibrarySearch = false;
        }
        NowPlayingScroll.Offset = default;
        if (mode == LibraryMode.Trending && _trendingBatchDownloadStatus is not null)
        {
            NowPlayingStatus.Text = _trendingBatchDownloadStatus;
        }
    }

    private void ShowLibrary()
    {
        _probeStatusPollingCancellation?.Cancel();
        AdministrationView.IsVisible = false;
        LibraryView.IsVisible = true;
        if (_libraryMode is LibraryMode.About or LibraryMode.Stations or LibraryMode.Offline)
        {
            _libraryPollingCancellation?.Cancel();
        }
        else
        {
            StartNowPlayingPolling();
        }
    }

    private void StartNowPlayingPolling()
    {
        _libraryPollingCancellation?.Cancel();
        if (!MusicLibraryApi.IsAuthenticated) return;

        var cancellation = _libraryPollingCancellation = new CancellationTokenSource();
        _ = PollNowPlayingAsync(cancellation.Token);
    }

    private async Task PollNowPlayingAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                await LoadCurrentLibraryViewAsync(LibrarySearchInput.Text, cancellationToken, showLoading: false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void LibrarySearchInput_TextChanged(object? sender, TextChangedEventArgs eventArgs)
    {
        if (_suppressLibrarySearch) return;
        _librarySearchCancellation?.Cancel();
        var cancellation = _librarySearchCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, cancellation.Token);
            await LoadCurrentLibraryViewAsync(LibrarySearchInput.Text, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Task LoadCurrentLibraryViewAsync(
        string? query = null,
        CancellationToken cancellationToken = default,
        bool showLoading = true)
    {
        return _libraryMode switch
        {
            LibraryMode.Following => LoadFollowingAsync(query, cancellationToken, showLoading),
            LibraryMode.Stations => LoadStationsAsync(query, cancellationToken, showLoading),
            LibraryMode.Trending => LoadTrendingAsync(query, cancellationToken, showLoading),
            LibraryMode.Offline => LoadOfflineTracksAsync(query),
            LibraryMode.UserBoard => LoadPlaybackActivitiesAsync(query, cancellationToken, showLoading),
            LibraryMode.About => Task.CompletedTask,
            _ => LoadNowPlayingAsync(query, cancellationToken, showLoading)
        };
    }

    private async Task LoadOfflineTracksAsync(string? query = null)
    {
        if (NativeRadioActions.ListOfflineTracksAsync is null) return;
        NowPlayingStatus.Text = "Loading downloaded recordings...";
        try
        {
            var tracks = await NativeRadioActions.ListOfflineTracksAsync();
            if (!string.IsNullOrWhiteSpace(query))
            {
                tracks = tracks.Where(track => track.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            if (_libraryMode != LibraryMode.Offline) return;
            NowPlayingList.ItemsSource = tracks.Select(CreateOfflineTrackRow).ToList();
            var locationText = string.IsNullOrWhiteSpace(NativeRadioActions.OfflineDirectoryPath)
                ? string.Empty
                : $" Stored at: {NativeRadioActions.OfflineDirectoryPath}";
            NowPlayingStatus.Text = (tracks.Count == 0
                ? "No locally cached recordings. Download songs from Trending to keep them on this device."
                : $"{tracks.Count} recording(s) cached locally") + locationText;
        }
        catch (Exception exception)
        {
            NowPlayingStatus.Text = exception.Message;
        }
    }

    private Control CreateOfflineTrackRow(OfflineTrack track)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,8,Auto,8,Auto"),
            Margin = new Avalonia.Thickness(0, 0, 0, 10)
        };
        var details = new StackPanel { Spacing = 2 };
        details.Children.Add(new TextBlock { Text = track.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        details.Children.Add(new TextBlock
        {
            Text = $"{FormatFileSize(track.SizeBytes)} | Saved {FormatRelativeTime(track.SavedAt)}",
            Opacity = 0.65
        });
        row.Children.Add(details);

        var playButton = new Button { Content = CreateActionIcon("mdi-play"), Width = 36, Height = 32 };
        ToolTip.SetTip(playButton, "Play offline recording");
        if (_playingOfflineTrackKey == track.Key)
        {
            _playingOfflineTrackButton = playButton;
            SetPlaybackButtonState(playButton, _isCachedTrackPlaying);
        }
        playButton.Click += async (_, _) => await PlayOfflineTrackAsync(track, playButton);
        Grid.SetColumn(playButton, 2);
        row.Children.Add(playButton);

        var deleteButton = new Button { Content = CreateActionIcon("mdi-delete"), Width = 36, Height = 32 };
        ToolTip.SetTip(deleteButton, "Remove offline recording");
        deleteButton.Click += async (_, _) => await DeleteOfflineTrackAsync(track, deleteButton);
        Grid.SetColumn(deleteButton, 4);
        row.Children.Add(deleteButton);
        return row;
    }

    private async Task PlayOfflineTrackAsync(OfflineTrack track, Button playButton)
    {
        if (NativeRadioActions.PlayOfflineTrackAsync is null) return;
        playButton.IsEnabled = false;
        try
        {
            if (NativeRadioActions.ToggleFilePlaybackAsync is null)
            {
                await NativeRadioActions.PlayOfflineTrackAsync(track.Key);
                NowPlayingStatus.Text = $"Opened {track.Name} in the desktop audio player.";
                return;
            }
            if (_playingOfflineTrackKey == track.Key && NativeRadioActions.ToggleFilePlaybackAsync is not null)
            {
                _isCachedTrackPlaying = await NativeRadioActions.ToggleFilePlaybackAsync() == 1;
            }
            else
            {
                ClearPlaybackState();
                await NativeRadioActions.PlayOfflineTrackAsync(track.Key);
                _playingOfflineTrackKey = track.Key;
                _playingOfflineTrackButton = playButton;
                _isCachedTrackPlaying = true;
            }
            SetPlaybackButtonState(playButton, _isCachedTrackPlaying);
            ShowPlaybackDock(track.Name, "Available offline", _isCachedTrackPlaying, isLiveStation: false);
            NowPlayingStatus.Text = _isCachedTrackPlaying ? $"Playing {track.Name}" : "Playback paused.";
        }
        catch (Exception exception)
        {
            ClearPlaybackState();
            NowPlayingStatus.Text = exception.Message;
        }
        finally
        {
            playButton.IsEnabled = true;
        }
    }

    private async Task DeleteOfflineTrackAsync(OfflineTrack track, Button deleteButton)
    {
        if (NativeRadioActions.DeleteOfflineTrackAsync is null) return;
        deleteButton.IsEnabled = false;
        try
        {
            if (_playingOfflineTrackKey == track.Key)
            {
                if (NativeRadioActions.StopPlaybackAsync is not null) await NativeRadioActions.StopPlaybackAsync();
                ClearPlaybackState();
            }
            await NativeRadioActions.DeleteOfflineTrackAsync(track.Key);
            await LoadOfflineTracksAsync(LibrarySearchInput.Text);
        }
        catch (Exception exception)
        {
            NowPlayingStatus.Text = exception.Message;
            deleteButton.IsEnabled = true;
        }
    }

    private static string FormatFileSize(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / (1024d * 1024d):0.0} MB"
        : $"{Math.Max(1, bytes / 1024d):0} KB";

    private void ShowAbout()
    {
        if (_libraryMode != LibraryMode.About) return;
        var version = typeof(MainView).Assembly.GetName().Version?.ToString(3) ?? "Development";
        var content = new StackPanel
        {
            Spacing = 12,
            MaxWidth = 640,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        content.Children.Add(new TextBlock
        {
            Text = "Music Library",
            FontSize = 22,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = "Discover what live radio stations are playing, follow artists, listen to trending recordings, and see what other listeners are enjoying.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = $"Version {version}",
            Opacity = 0.65
        });
        NowPlayingList.ItemsSource = new[] { content };
    }

    private async Task LoadStationsAsync(
        string? query = null,
        CancellationToken cancellationToken = default,
        bool showLoading = true)
    {
        await _nowPlayingLoadGate.WaitAsync(cancellationToken);
        try
        {
            if (showLoading)
            {
                NowPlayingStatus.Text = "Loading stations...";
                NowPlayingList.ItemsSource = null;
            }
            var stations = await MusicLibraryApi.GetStationsAsync(query, cancellationToken);
            if (_libraryMode != LibraryMode.Stations) return;
            NowPlayingList.ItemsSource = stations.Select(CreateStationRow).ToList();
            NowPlayingStatus.Text = stations.Count switch
            {
                0 => string.IsNullOrWhiteSpace(query) ? "No stations are available." : "No matching stations found.",
                200 => "Showing the first 200 matching stations. Refine your search to narrow the results.",
                _ => $"{stations.Count} station(s)."
            };
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            NowPlayingStatus.Text = exception.Message;
        }
        finally
        {
            _nowPlayingLoadGate.Release();
        }
    }

    private Control CreateStationRow(StationSummary station)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,Auto"),
            Margin = new Avalonia.Thickness(0, 0, 0, 12)
        };
        var details = new StackPanel { Spacing = 2 };
        details.Children.Add(new TextBlock
        {
            Text = station.Name,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        details.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(station.Genre) ? station.StreamUrl : station.Genre,
            Opacity = 0.7,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        row.Children.Add(details);

        var hasDirectStream = Uri.TryCreate(station.StreamUrl, UriKind.Absolute, out var streamUri)
            && (streamUri.Scheme == Uri.UriSchemeHttp || streamUri.Scheme == Uri.UriSchemeHttps)
            && NativeRadioActions.ListenAsync is not null;
        if (hasDirectStream || NativeRadioActions.ListenToStationAsync is not null)
        {
            var listenButton = new Button
            {
                Content = CreateActionIcon("mdi-radio-tower"),
                Width = 36,
                Height = 32,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            listenButton.Classes.Add("playback");
            var isPlaying = _playingStationId == station.Id;
            if (isPlaying) _playingStationButton = listenButton;
            SetStationButtonState(listenButton, isPlaying, station.Name);
            listenButton.Click += async (_, _) => await ListenToNowPlayingStationAsync(
                station.Id,
                streamUri,
                station.Name,
                listenButton);
            Grid.SetColumn(listenButton, 2);
            row.Children.Add(listenButton);
        }
        return row;
    }

    private async Task LoadPlaybackActivitiesAsync(
        string? query = null,
        CancellationToken cancellationToken = default,
        bool showLoading = true)
    {
        if (showLoading)
        {
            await _nowPlayingLoadGate.WaitAsync(cancellationToken);
        }
        else if (!await _nowPlayingLoadGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            if (showLoading)
            {
                NowPlayingStatus.Text = "Loading active listeners...";
                NowPlayingList.ItemsSource = null;
            }
            try
            {
                var activities = await MusicLibraryApi.GetPlaybackActivitiesAsync(cancellationToken);
                if (_libraryMode != LibraryMode.UserBoard) return;
                if (!string.IsNullOrWhiteSpace(query))
                {
                    var value = query.Trim();
                    activities =
                    [
                        .. activities.Where(activity =>
                            activity.UserDisplayName.Contains(value, StringComparison.OrdinalIgnoreCase)
                            || activity.PlaybackDescription.Contains(value, StringComparison.OrdinalIgnoreCase))
                    ];
                }
                var hasChanges = !_playbackActivitySnapshot.SequenceEqual(activities);
                if (showLoading || hasChanges)
                {
                    _playbackActivitySnapshot = [.. activities];
                    NowPlayingList.ItemsSource = activities.Select(CreatePlaybackActivityRow).ToList();
                }
                NowPlayingStatus.Text = activities.Count == 0
                    ? "Nobody is listening right now."
                    : $"{activities.Count} active listener(s) | Updated {DateTime.Now:T}";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                NowPlayingStatus.Text = exception.Message;
            }
        }
        finally
        {
            _nowPlayingLoadGate.Release();
        }
    }

    private Control CreatePlaybackActivityRow(UserPlaybackActivitySummary activity)
    {
        var details = new StackPanel
        {
            Spacing = 3,
            Margin = new Avalonia.Thickness(0, 0, 0, 12)
        };
        details.Children.Add(new TextBlock
        {
            Text = $"{activity.UserDisplayName} is currently playing {activity.PlaybackDescription}",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        details.Children.Add(new TextBlock
        {
            Text = $"{(activity.IsLiveStation ? "Live station" : "Trending recording")} | Started {FormatRelativeTime(activity.StartedAt)}",
            Opacity = 0.65
        });
        return details;
    }

    private async Task LoadNowPlayingAsync(
        string? query = null,
        CancellationToken cancellationToken = default,
        bool showLoading = true)
    {
        if (showLoading)
        {
            await _nowPlayingLoadGate.WaitAsync(cancellationToken);
        }
        else if (!await _nowPlayingLoadGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            if (showLoading)
            {
                NowPlayingStatus.Text = string.IsNullOrWhiteSpace(query) ? "Loading live observations..." : "Searching...";
                NowPlayingList.ItemsSource = null;
            }
            try
            {
                await EnsureSubscriptionsLoadedAsync(cancellationToken);
                var observations = await MusicLibraryApi.GetNowPlayingAsync(query, cancellationToken);
                if (_libraryMode != LibraryMode.NowPlaying) return;
                var hasChanges = !_nowPlayingSnapshot.SequenceEqual(observations);
                var canReplaceRows = showLoading || NowPlayingScroll.Offset.Y <= 1;
                if ((showLoading || hasChanges) && canReplaceRows)
                {
                    _nowPlayingSnapshot = [.. observations];
                    _subscriptionButtons.Clear();
                    NowPlayingList.ItemsSource = observations.Select(CreateNowPlayingRow).ToList();
                }
                NowPlayingStatus.Text = observations.Count == 0
                    ? (string.IsNullOrWhiteSpace(query)
                        ? "No live observations yet. An administrator must import stations and enable probing first."
                        : "No matching artists, tracks, or stations found.")
                    : hasChanges && !canReplaceRows
                        ? $"{observations.Count} live station(s) | Updates available"
                        : $"{observations.Count} live station(s) | Updated {DateTime.Now:T}";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                NowPlayingStatus.Text = exception.Message;
            }
        }
        finally
        {
            _nowPlayingLoadGate.Release();
        }
    }

    private Control CreateNowPlayingRow(NowPlayingSummary observation)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,8,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Avalonia.Thickness(0, 0, 0, 10)
        };
        var details = new StackPanel { Spacing = 2 };
        details.Children.Add(new TextBlock
        {
            Text = observation.Artist is null
                ? observation.Title ?? observation.RawMetadata
                : $"{observation.Artist} - {observation.Title ?? observation.RawMetadata}",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        details.Children.Add(new TextBlock
        {
            Text = observation.StationName,
            Opacity = 0.7,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        Grid.SetColumnSpan(details, 4);
        row.Children.Add(details);

        var observedAt = new TextBlock
        {
            Text = $"Played {FormatRelativeTime(observation.ObservedAt)}",
            Opacity = 0.65,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetColumn(observedAt, 1);
        Grid.SetRow(observedAt, 1);
        row.Children.Add(observedAt);

        var hasDirectStream = Uri.TryCreate(observation.StreamUrl, UriKind.Absolute, out var streamUri)
            && (streamUri.Scheme == Uri.UriSchemeHttp || streamUri.Scheme == Uri.UriSchemeHttps)
            && NativeRadioActions.ListenAsync is not null;
        if (hasDirectStream || NativeRadioActions.ListenToStationAsync is not null)
        {
            var listenButton = new Button
            {
                Content = CreateActionIcon("mdi-radio-tower"),
                Width = 36,
                Height = 32,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            listenButton.Classes.Add("playback");
            var isPlaying = _playingStationId == observation.StationId;
            if (isPlaying) _playingStationButton = listenButton;
            SetStationButtonState(listenButton, isPlaying, observation.StationName);
            listenButton.Click += async (_, _) => await ListenToNowPlayingStationAsync(
                observation.StationId,
                streamUri,
                observation.StationName,
                listenButton);
            Grid.SetColumn(listenButton, 3);
            Grid.SetRow(listenButton, 1);
            row.Children.Add(listenButton);
        }

        if (!string.IsNullOrWhiteSpace(observation.Artist))
        {
            var artist = observation.Artist.Trim();
            var isSubscribed = _subscribedArtists.Contains(artist);
            var subscribeButton = new Button
            {
                Content = isSubscribed ? "Following" : "Follow artist",
                IsEnabled = !isSubscribed,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Margin = new Avalonia.Thickness(0, 6, 0, 0)
            };
            subscribeButton.Click += async (_, _) => await SubscribeToArtistAsync(artist);
            if (!_subscriptionButtons.TryGetValue(artist, out var buttons))
            {
                buttons = [];
                _subscriptionButtons.Add(artist, buttons);
            }
            buttons.Add(subscribeButton);
            Grid.SetRow(subscribeButton, 1);
            row.Children.Add(subscribeButton);
        }

        return row;
    }

    private async Task ListenToNowPlayingStationAsync(
        Guid stationId,
        Uri? streamUri,
        string stationName,
        Button listenButton)
    {
        listenButton.IsEnabled = false;
        try
        {
            if (_playingStationId == stationId)
            {
                if (NativeRadioActions.StopPlaybackAsync is null)
                {
                    throw new InvalidOperationException("Stopping live playback is not available on this platform.");
                }
                await NativeRadioActions.StopPlaybackAsync();
                ClearPlaybackState();
                StopPublishingPlaybackActivity();
                NowPlayingStatus.Text = $"Stopped listening to {stationName}.";
                return;
            }

            ClearPlaybackState();
            PausePublishingPlaybackActivity();
            if (NativeRadioActions.ListenToStationAsync is not null)
            {
                await NativeRadioActions.ListenToStationAsync(stationId);
            }
            else if (streamUri is not null && NativeRadioActions.ListenAsync is not null)
            {
                await NativeRadioActions.ListenAsync(streamUri);
            }
            else
            {
                throw new InvalidOperationException("Live listening is not available on this platform.");
            }
            var current = await MusicLibraryApi.GetNowPlayingStationAsync(stationId);
            var currentTrack = current is null
                ? null
                : string.IsNullOrWhiteSpace(current.Artist)
                    ? current.Title ?? current.RawMetadata
                    : $"{current.Artist} - {current.Title ?? current.RawMetadata}";
            _playingStationId = stationId;
            _playingStationButton = listenButton;
            SetStationButtonState(listenButton, true, stationName);
            ShowPlaybackDock(stationName, currentTrack ?? "Live radio", isPlaying: true, isLiveStation: true);
            PublishPlaybackActivity(string.IsNullOrWhiteSpace(currentTrack) ? stationName : $"{stationName}: {currentTrack}", isLiveStation: true);
            NowPlayingStatus.Text = string.IsNullOrWhiteSpace(currentTrack)
                ? $"Listening live to {stationName}."
                : $"Listening live to {stationName}: {currentTrack}";
        }
        catch (Exception exception)
        {
            NowPlayingStatus.Text = exception.Message;
        }
        finally
        {
            listenButton.IsEnabled = true;
        }
    }

    private async Task EnsureSubscriptionsLoadedAsync(CancellationToken cancellationToken)
    {
        if (_subscriptionsLoaded) return;

        try
        {
            var subscriptions = await MusicLibraryApi.GetSubscriptionsAsync(cancellationToken);
            foreach (var subscription in subscriptions)
            {
                _subscribedArtists.Add(subscription.ArtistName);
            }
            _subscriptionsLoaded = true;
        }
        catch (HttpRequestException) when (MusicLibraryApi.IsAuthenticated)
        {
            // Now Playing remains usable while subscription state retries on the next refresh.
        }
    }

    private async Task SubscribeToArtistAsync(string artist)
    {
        SetSubscriptionButtons(artist, "Following...", false);
        try
        {
            await MusicLibraryApi.CreateSubscriptionAsync(artist);
            _subscribedArtists.Add(artist);
            SetSubscriptionButtons(artist, "Following", false);
            NowPlayingStatus.Text = $"Following {artist}.";
        }
        catch (Exception exception)
        {
            SetSubscriptionButtons(artist, "Follow artist", true);
            NowPlayingStatus.Text = exception.Message;
        }
    }

    private void SetSubscriptionButtons(string artist, string content, bool isEnabled)
    {
        if (!_subscriptionButtons.TryGetValue(artist, out var buttons)) return;
        foreach (var subscriptionButton in buttons)
        {
            subscriptionButton.Content = content;
            subscriptionButton.IsEnabled = isEnabled;
        }
    }

    private async Task LoadFollowingAsync(
        string? query = null,
        CancellationToken cancellationToken = default,
        bool showLoading = true)
    {
        if (_libraryMode != LibraryMode.Following) return;

        if (showLoading)
        {
            await _nowPlayingLoadGate.WaitAsync(cancellationToken);
        }
        else if (!await _nowPlayingLoadGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            if (showLoading)
            {
                NowPlayingStatus.Text = "Loading followed artists...";
                NowPlayingList.ItemsSource = null;
            }
            try
            {
                var subscriptions = await MusicLibraryApi.GetSubscriptionsAsync(cancellationToken);
                if (_libraryMode != LibraryMode.Following) return;
                if (!string.IsNullOrWhiteSpace(query))
                {
                    subscriptions =
                    [
                        .. subscriptions
                            .Where(subscription =>
                                subscription.ArtistName.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
                    ];
                }
                var hasChanges = !_followingSnapshot.SequenceEqual(subscriptions);
                var canReplaceRows = showLoading || NowPlayingScroll.Offset.Y <= 1;
                if ((showLoading || hasChanges) && canReplaceRows)
                {
                    _followingSnapshot = [.. subscriptions];
                    NowPlayingList.ItemsSource = subscriptions.Select(CreateFollowingRow).ToList();
                }
                NowPlayingStatus.Text = subscriptions.Count == 0
                    ? (string.IsNullOrWhiteSpace(query) ? "You are not following any artists yet." : "No followed artists match your search.")
                    : $"Following {subscriptions.Count} artist(s) | Updated {DateTime.Now:T}";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                NowPlayingStatus.Text = exception.Message;
            }
        }
        finally
        {
            _nowPlayingLoadGate.Release();
        }
    }

    private Control CreateFollowingRow(ArtistSubscriptionSummary subscription)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Avalonia.Thickness(0, 0, 0, 10)
        };
        var details = new StackPanel { Spacing = 2 };
        details.Children.Add(new TextBlock
        {
            Text = subscription.ArtistName,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });
        details.Children.Add(new TextBlock
        {
            Text = $"Following since {subscription.CreatedAt.LocalDateTime:g}",
            Opacity = 0.7
        });
        row.Children.Add(details);

        var removeButton = new Button { Content = "Unfollow" };
        removeButton.Click += async (_, _) =>
        {
            removeButton.IsEnabled = false;
            try
            {
                await MusicLibraryApi.DeleteSubscriptionAsync(subscription.Id);
                _subscribedArtists.Remove(subscription.ArtistName);
                if (_libraryMode == LibraryMode.Following)
                {
                    await LoadFollowingAsync(LibrarySearchInput.Text);
                }
            }
            catch (Exception exception)
            {
                removeButton.IsEnabled = true;
                NowPlayingStatus.Text = exception.Message;
            }
        };
        Grid.SetColumn(removeButton, 1);
        row.Children.Add(removeButton);
        return row;
    }

    private async Task LoadTrendingAsync(
        string? query = null,
        CancellationToken cancellationToken = default,
        bool showLoading = true)
    {
        if (showLoading)
        {
            await _nowPlayingLoadGate.WaitAsync(cancellationToken);
        }
        else if (!await _nowPlayingLoadGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            if (showLoading)
            {
                NowPlayingStatus.Text = string.IsNullOrWhiteSpace(query) ? "Loading 24-hour trends..." : "Searching trends...";
                NowPlayingList.ItemsSource = null;
            }
            try
            {
                var trends = (await MusicLibraryApi.GetTrendingAsync(query, cancellationToken))
                    .Where(trend => trend.CachedTrackId is not null)
                    .ToList();
                if (_libraryMode != LibraryMode.Trending) return;
                var hasChanges = !_trendingSnapshot.SequenceEqual(trends);
                var currentTrackIds = _trendingSnapshot.Select(trend => trend.CachedTrackId).ToHashSet();
                var updatedTrackIds = trends.Select(trend => trend.CachedTrackId).ToHashSet();
                var hasRemovedTracks = currentTrackIds.Except(updatedTrackIds).Any();
                var canReplaceRows = showLoading || NowPlayingScroll.Offset.Y <= 1 || hasRemovedTracks;
                if ((showLoading || hasChanges) && canReplaceRows)
                {
                    _trendingSnapshot = [.. trends];
                    NowPlayingList.ItemsSource = trends.Select((trend, index) => CreateTrendingRow(trend, index + 1)).ToList();
                }
                if (_trendingBatchDownloadCancellation is null && _trendingPlaybackCancellation is null)
                {
                    if (_completedTrendingBatchDownloadStatus is not null)
                    {
                        NowPlayingStatus.Text = _completedTrendingBatchDownloadStatus;
                        _completedTrendingBatchDownloadStatus = null;
                    }
                    else
                    {
                        NowPlayingStatus.Text = trends.Count == 0
                            ? (string.IsNullOrWhiteSpace(query) ? "No trends detected in the past 24 hours." : "No matching trends found.")
                            : hasChanges && !canReplaceRows
                                ? $"{trends.Count} trend(s) | Rankings updated"
                                : $"{trends.Count} trend(s) from the past 24 hours | Updated {DateTime.Now:T}";
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                NowPlayingStatus.Text = exception.Message;
            }
        }
        finally
        {
            _nowPlayingLoadGate.Release();
        }
    }

    private Control CreateTrendingRow(TrendingSummary trend, int rank)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,12,*,12,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Avalonia.Thickness(0, 0, 0, 12)
        };
        row.Children.Add(new TextBlock
        {
            Text = $"#{rank}",
            FontSize = 16,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        var details = new StackPanel { Spacing = 2 };
        details.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(trend.Title) ? trend.Artist : $"{trend.Artist} - {trend.Title}",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        details.Children.Add(new TextBlock
        {
            Text = GetTrendingDetails(trend),
            Opacity = 0.7
        });
        Grid.SetColumn(details, 2);
        row.Children.Add(details);
        var lastObserved = new TextBlock
        {
            Text = $"Played {FormatRelativeTime(trend.LastObservedAt)}",
            Opacity = 0.65,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetColumn(lastObserved, 2);
        Grid.SetRow(lastObserved, 1);
        row.Children.Add(lastObserved);
        var actions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        var cachedUntilText = trend.CachedUntil?.LocalDateTime.ToString("g");
        var playButton = new Button
        {
            Content = CreateActionIcon("mdi-play"),
            Width = 36,
            Height = 32,
            IsEnabled = trend.CachedTrackId is not null
        };
        ToolTip.SetTip(playButton, cachedUntilText is not null
            ? $"Play cached recording (available until {cachedUntilText})"
            : "Recording is not ready yet");
        var hasDirectStream = Uri.TryCreate(trend.LastStationStreamUrl, UriKind.Absolute, out var streamUri)
            && (streamUri.Scheme == Uri.UriSchemeHttp || streamUri.Scheme == Uri.UriSchemeHttps)
            && NativeRadioActions.ListenAsync is not null;
        Button? listenButton = null;
        if (hasDirectStream || NativeRadioActions.ListenToStationAsync is not null)
        {
            listenButton = new Button
            {
                Content = CreateActionIcon("mdi-radio-tower"),
                Width = 36,
                Height = 32
            };
            listenButton.Classes.Add("playback");
            var isPlaying = _playingStationId == trend.LastStationId;
            if (isPlaying) _playingStationButton = listenButton;
            SetStationButtonState(listenButton, isPlaying, trend.LastStationName);
            listenButton.Click += async (_, _) => await ListenToNowPlayingStationAsync(
                trend.LastStationId,
                streamUri,
                trend.LastStationName,
                listenButton);
        }
        var downloadButton = new Button
        {
            Content = CreateActionIcon("mdi-download"),
            Width = 36,
            Height = 32,
            IsEnabled = trend.CachedTrackId is not null
        };
        ToolTip.SetTip(downloadButton, cachedUntilText is not null
            ? $"Download cached recording (available until {cachedUntilText})"
            : "Recording is not ready yet");
        if (trend.CachedTrackId is { } cachedTrackId)
        {
            if (_playingCachedTrackId == cachedTrackId && NativeRadioActions.ToggleFilePlaybackAsync is not null)
            {
                _playingCachedTrackButton = playButton;
                SetPlaybackButtonState(playButton, _isCachedTrackPlaying);
            }
            playButton.Click += async (_, _) => await PlayTrendingTrackAsync(trend, cachedTrackId, playButton);
            downloadButton.Click += async (_, _) => await DownloadTrendingTrackAsync(cachedTrackId, downloadButton);
        }
        actions.Children.Add(playButton);
        if (listenButton is not null) actions.Children.Add(listenButton);
        actions.Children.Add(downloadButton);
        Grid.SetColumn(actions, 4);
        Grid.SetRowSpan(actions, 2);
        row.Children.Add(actions);
        return row;
    }

    private async Task PlayTrendingTrackAsync(TrendingSummary trend, Guid cachedTrackId, Button playButton)
    {
        if (_trendingBatchDownloadCancellation is not null)
        {
            NowPlayingStatus.Text = "Wait for the batch download to finish before starting playback.";
            return;
        }
        _trendingPlaybackCancellation?.Cancel();
        if (NativeRadioActions.PlayFileAsync is null)
        {
            NowPlayingStatus.Text = "Playback is not available on this platform.";
            return;
        }

        playButton.IsEnabled = false;
        try
        {
            if (_playingCachedTrackId == cachedTrackId && NativeRadioActions.ToggleFilePlaybackAsync is not null)
            {
                var playbackState = await NativeRadioActions.ToggleFilePlaybackAsync();
                if (playbackState >= 0)
                {
                    _isCachedTrackPlaying = playbackState == 1;
                    SetPlaybackButtonState(playButton, _isCachedTrackPlaying);
                    ShowPlaybackDock(GetTrackDisplayName(trend), GetTrendingPlaybackSubtitle(trend), _isCachedTrackPlaying, isLiveStation: false);
                    if (_isCachedTrackPlaying)
                    {
                        PublishPlaybackActivity(GetTrackDisplayName(trend), isLiveStation: false);
                    }
                    else
                    {
                        PausePublishingPlaybackActivity();
                    }
                    NowPlayingStatus.Text = _isCachedTrackPlaying ? "Playback resumed." : "Playback paused.";
                    return;
                }
            }

            NowPlayingStatus.Text = "Preparing encrypted recording...";
            var download = await MusicLibraryApi.DownloadTrendingTrackAsync(cachedTrackId);
            ClearPlaybackState();
            PausePublishingPlaybackActivity();
            await NativeRadioActions.PlayFileAsync(download.Content, download.ContentType, download.FileName);
            _playingCachedTrackId = cachedTrackId;
            _playingCachedTrackButton = playButton;
            _isCachedTrackPlaying = NativeRadioActions.ToggleFilePlaybackAsync is not null;
            SetPlaybackButtonState(playButton, _isCachedTrackPlaying);
            ShowPlaybackDock(GetTrackDisplayName(trend), GetTrendingPlaybackSubtitle(trend), _isCachedTrackPlaying, isLiveStation: false);
            PublishPlaybackActivity(GetTrackDisplayName(trend), isLiveStation: false);
            NowPlayingStatus.Text = $"Playing {download.FileName}";
        }
        catch (Exception exception)
        {
            NowPlayingStatus.Text = exception.Message;
        }
        finally
        {
            playButton.IsEnabled = true;
        }
    }

    private static void SetPlaybackButtonState(Button button, bool isPlaying)
    {
        button.Content = CreateActionIcon(isPlaying ? "mdi-pause" : "mdi-play");
        ToolTip.SetTip(button, isPlaying ? "Pause cached recording" : "Play cached recording");
    }

    private static void SetStationButtonState(Button button, bool isPlaying, string stationName)
    {
        button.Content = CreateActionIcon(isPlaying ? "mdi-stop" : "mdi-radio-tower");
        button.Classes.Set("active", isPlaying);
        ToolTip.SetTip(button, isPlaying ? $"Stop listening to {stationName}" : $"Listen live to {stationName}");
    }

    private async void PlaybackDockButton_Click(object? sender, RoutedEventArgs eventArgs)
    {
        PlaybackDockButton.IsEnabled = false;
        try
        {
            if (_playingStationId is not null)
            {
                if (NativeRadioActions.StopPlaybackAsync is null) return;
                await NativeRadioActions.StopPlaybackAsync();
                ClearPlaybackState();
                StopPublishingPlaybackActivity();
                NowPlayingStatus.Text = "Live playback stopped.";
                return;
            }

            if (_trendingPlaybackCancellation is not null)
            {
                _trendingPlaybackCancellation.Cancel();
                return;
            }

            if ((_playingCachedTrackId is null && _playingOfflineTrackKey is null)
                || NativeRadioActions.ToggleFilePlaybackAsync is null) return;
            var playbackState = await NativeRadioActions.ToggleFilePlaybackAsync();
            if (playbackState < 0)
            {
                ClearPlaybackState();
                StopPublishingPlaybackActivity();
                return;
            }
            _isCachedTrackPlaying = playbackState == 1;
            if (_playingCachedTrackButton is not null) SetPlaybackButtonState(_playingCachedTrackButton, _isCachedTrackPlaying);
            if (_playingOfflineTrackButton is not null) SetPlaybackButtonState(_playingOfflineTrackButton, _isCachedTrackPlaying);
            SetPlaybackDockButtonState(_isCachedTrackPlaying, isLiveStation: false);
            if (_isCachedTrackPlaying)
            {
                if (_playbackActivityDescription is not null)
                {
                    PublishPlaybackActivity(_playbackActivityDescription, _playbackActivityIsLiveStation);
                }
            }
            else
            {
                PausePublishingPlaybackActivity();
            }
        }
        finally
        {
            PlaybackDockButton.IsEnabled = true;
        }
    }

    private void ShowPlaybackDock(string title, string subtitle, bool isPlaying, bool isLiveStation)
    {
        PlaybackDockTitle.Text = title;
        PlaybackDockSubtitle.Text = subtitle;
        PlaybackDockIcon.Data = ActionIconGeometries.TryGetValue(isLiveStation ? "mdi-radio-tower" : "mdi-music", out var geometry)
            ? geometry
            : CreateActionIcon(isLiveStation ? "mdi-radio-tower" : "mdi-music").Data;
        PlaybackDock.IsVisible = true;
        SetPlaybackDockButtonState(isPlaying, isLiveStation);
    }

    private void SetPlaybackDockButtonState(bool isPlaying, bool isLiveStation)
    {
        PlaybackDockButton.Content = CreateActionIcon(isLiveStation ? "mdi-stop" : isPlaying ? "mdi-pause" : "mdi-play");
        ToolTip.SetTip(PlaybackDockButton, isLiveStation ? "Stop live playback" : isPlaying ? "Pause playback" : "Resume playback");
    }

    private void ClearPlaybackState()
    {
        if (_playingStationButton is not null)
        {
            SetStationButtonState(_playingStationButton, false, PlaybackDockTitle.Text ?? "station");
        }
        if (_playingCachedTrackButton is not null) SetPlaybackButtonState(_playingCachedTrackButton, false);
        if (_playingOfflineTrackButton is not null) SetPlaybackButtonState(_playingOfflineTrackButton, false);
        _playingStationId = null;
        _playingStationButton = null;
        _playingCachedTrackId = null;
        _playingCachedTrackButton = null;
        _playingOfflineTrackKey = null;
        _playingOfflineTrackButton = null;
        _isCachedTrackPlaying = false;
        PlaybackDock.IsVisible = false;
    }

    private void PublishPlaybackActivity(string description, bool isLiveStation)
    {
        _playbackActivityDescription = description;
        _playbackActivityIsLiveStation = isLiveStation;
        _isPlaybackActivityActive = true;
        _ = SyncPlaybackActivityAsync(description, isLiveStation, clear: false);
        StartPlaybackActivityHeartbeat();
    }

    private void PausePublishingPlaybackActivity()
    {
        _isPlaybackActivityActive = false;
        _playbackActivityHeartbeatCancellation?.Cancel();
        _ = SyncPlaybackActivityAsync(null, isLiveStation: false, clear: true);
    }

    private void StopPublishingPlaybackActivity()
    {
        _playbackActivityDescription = null;
        _playbackActivityIsLiveStation = false;
        _isPlaybackActivityActive = false;
        _playbackActivityHeartbeatCancellation?.Cancel();
        _ = SyncPlaybackActivityAsync(null, isLiveStation: false, clear: true);
    }

    private void StartPlaybackActivityHeartbeat()
    {
        var getPlaybackStateAsync = NativeRadioActions.GetPlaybackStateAsync;
        if (getPlaybackStateAsync is null) return;
        _playbackActivityHeartbeatCancellation?.Cancel();
        var cancellation = _playbackActivityHeartbeatCancellation = new CancellationTokenSource();
        _ = HeartbeatPlaybackActivityAsync(getPlaybackStateAsync, cancellation.Token);
    }

    private async Task HeartbeatPlaybackActivityAsync(
        Func<Task<int>> getPlaybackStateAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                if (!_isPlaybackActivityActive || _playbackActivityDescription is null) return;
                var playbackState = await getPlaybackStateAsync();
                if (playbackState <= 0)
                {
                    if (playbackState < 0) ClearPlaybackState();
                    PausePublishingPlaybackActivity();
                    return;
                }

                if (_playbackActivityIsLiveStation && _playingStationId is { } stationId)
                {
                    var current = await MusicLibraryApi.GetNowPlayingStationAsync(stationId, cancellationToken);
                    var currentTrack = current is null
                        ? null
                        : string.IsNullOrWhiteSpace(current.Artist)
                            ? current.Title ?? current.RawMetadata
                            : $"{current.Artist} - {current.Title ?? current.RawMetadata}";
                    var stationName = PlaybackDockTitle.Text ?? "Live station";
                    _playbackActivityDescription = string.IsNullOrWhiteSpace(currentTrack)
                        ? stationName
                        : $"{stationName}: {currentTrack}";
                    PlaybackDockSubtitle.Text = currentTrack ?? "Live radio";
                }

                await SyncPlaybackActivityAsync(
                    _playbackActivityDescription,
                    _playbackActivityIsLiveStation,
                    clear: false,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SyncPlaybackActivityAsync(
        string? description,
        bool isLiveStation,
        bool clear,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _playbackActivitySyncGate.WaitAsync(cancellationToken);
            try
            {
                if (clear)
                {
                    await MusicLibraryApi.ClearPlaybackActivityAsync(cancellationToken);
                }
                else if (description is not null)
                {
                    await MusicLibraryApi.UpdatePlaybackActivityAsync(description, isLiveStation, cancellationToken);
                }
            }
            finally
            {
                _playbackActivitySyncGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Playback activity sync failed: {exception}");
        }
    }

    private static string GetTrackDisplayName(TrendingSummary trend) =>
        string.IsNullOrWhiteSpace(trend.Title) ? trend.Artist : $"{trend.Artist} - {trend.Title}";

    private static string GetTrendingDetails(TrendingSummary trend)
    {
        var details = $"{trend.ObservationCount} detection(s) across {trend.StationCount} station(s)";
        if (trend.BitrateKbps is > 0) details += $" | {trend.BitrateKbps} kbps";
        var duration = FormatTrackDuration(trend.DurationMs);
        return duration is null ? details : $"{details} | {duration}";
    }

    private static string GetTrendingPlaybackSubtitle(TrendingSummary trend)
    {
        var formatted = FormatTrackDuration(trend.DurationMs);
        return formatted is null ? "Trending recording" : $"Trending recording | {formatted}";
    }

    private static string? FormatTrackDuration(int? durationMs)
    {
        if (durationMs is not > 0) return null;
        var duration = TimeSpan.FromMilliseconds(durationMs.Value);
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"m\:ss");
    }

    private static string FormatRelativeTime(DateTimeOffset timestamp)
    {
        var elapsed = DateTimeOffset.UtcNow - timestamp.ToUniversalTime();
        if (elapsed < TimeSpan.Zero || elapsed < TimeSpan.FromMinutes(1)) return "just now";
        if (elapsed < TimeSpan.FromHours(1)) return FormatElapsedUnit((int)elapsed.TotalMinutes, "minute");
        if (elapsed < TimeSpan.FromDays(1)) return FormatElapsedUnit((int)elapsed.TotalHours, "hour");
        return FormatElapsedUnit((int)elapsed.TotalDays, "day");
    }

    private static string FormatElapsedUnit(int value, string unit) => $"{value} {unit}{(value == 1 ? string.Empty : "s")} ago";

    private static PathIcon CreateActionIcon(string value)
    {
        if (!ActionIconGeometries.TryGetValue(value, out var geometry))
        {
            geometry = StreamGeometry.Parse(IconProvider.Current.GetIcon(value).Path.ToString());
            ActionIconGeometries.Add(value, geometry);
        }

        return new PathIcon
        {
            Data = geometry,
            Width = 16,
            Height = 16
        };
    }

    private async Task DownloadTrendingTrackAsync(Guid cachedTrackId, Button downloadButton)
    {
        if (NativeRadioActions.SaveFileAsync is null)
        {
            NowPlayingStatus.Text = "Downloads are not available on this platform.";
            return;
        }

        downloadButton.IsEnabled = false;
        NowPlayingStatus.Text = "Preparing encrypted recording...";
        try
        {
            var download = await MusicLibraryApi.DownloadTrendingTrackAsync(cachedTrackId);
            var destination = await NativeRadioActions.SaveFileAsync(download.Content, download.ContentType, download.FileName);
            NowPlayingStatus.Text = $"Downloaded {destination}";
        }
        catch (Exception exception)
        {
            NowPlayingStatus.Text = exception.Message;
        }
        finally
        {
            downloadButton.IsEnabled = true;
        }
    }

    private async void DownloadAllTrending_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (_trendingPlaybackCancellation is not null)
        {
            NowPlayingStatus.Text = "Stop trending playback before downloading all recordings.";
            return;
        }
        if (NativeRadioActions.SaveFileAsync is null)
        {
            NowPlayingStatus.Text = "Downloads are not available on this platform.";
            return;
        }

        var cachedTrackIds = _trendingSnapshot
            .Where(trend => trend.CachedTrackId is not null)
            .Select(trend => trend.CachedTrackId!.Value)
            .Distinct()
            .ToList();
        if (cachedTrackIds.Count == 0)
        {
            NowPlayingStatus.Text = "No cached trending recordings are ready to download.";
            return;
        }

        _trendingBatchDownloadCancellation?.Cancel();
        var cancellation = _trendingBatchDownloadCancellation = new CancellationTokenSource();
        _completedTrendingBatchDownloadStatus = null;
        DownloadAllTrendingButton.IsEnabled = false;
        PlayAllTrendingButton.IsEnabled = false;
        var downloadedCount = 0;
        var failedCount = 0;
        try
        {
            for (var index = 0; index < cachedTrackIds.Count; index++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                SetTrendingBatchDownloadStatus($"Downloading trending recording {index + 1} of {cachedTrackIds.Count}...");
                try
                {
                    var download = await MusicLibraryApi.DownloadTrendingTrackAsync(cachedTrackIds[index], cancellation.Token);
                    await NativeRadioActions.SaveFileAsync(download.Content, download.ContentType, download.FileName);
                    downloadedCount++;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    failedCount++;
                }
            }

            _completedTrendingBatchDownloadStatus = failedCount == 0
                ? $"Downloaded {downloadedCount} trending recording(s)."
                : $"Downloaded {downloadedCount} trending recording(s); {failedCount} failed or expired.";
            SetTrendingBatchDownloadStatus(null);
            if (_libraryMode == LibraryMode.Trending && LibraryView.IsVisible)
            {
                NowPlayingStatus.Text = _completedTrendingBatchDownloadStatus;
                _completedTrendingBatchDownloadStatus = null;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_trendingBatchDownloadCancellation == cancellation)
            {
                _trendingBatchDownloadCancellation = null;
                DownloadAllTrendingButton.IsEnabled = true;
                PlayAllTrendingButton.IsEnabled = true;
            }
            cancellation.Dispose();
        }
    }

    private void SetTrendingBatchDownloadStatus(string? status)
    {
        _trendingBatchDownloadStatus = status;
        if (status is not null && _libraryMode == LibraryMode.Trending && LibraryView.IsVisible)
        {
            NowPlayingStatus.Text = status;
        }
    }

    private async void PlayAllTrending_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (_trendingPlaybackCancellation is not null)
        {
            _trendingPlaybackCancellation.Cancel();
            return;
        }
        if (_trendingBatchDownloadCancellation is not null)
        {
            NowPlayingStatus.Text = "Wait for the batch download to finish before starting playback.";
            return;
        }
        if (NativeRadioActions.PlayFileToCompletionAsync is null)
        {
            NowPlayingStatus.Text = "Continuous playback is not available on this platform.";
            return;
        }

        var cachedTracks = _trendingSnapshot
            .Where(trend => trend.CachedTrackId is not null)
            .DistinctBy(trend => trend.CachedTrackId)
            .ToList();
        if (cachedTracks.Count == 0)
        {
            NowPlayingStatus.Text = "No cached trending recordings are ready to play.";
            return;
        }

        var cancellation = _trendingPlaybackCancellation = new CancellationTokenSource();
        PlayAllTrendingButtonText.Text = "Stop";
        DownloadAllTrendingButton.IsEnabled = false;
        var cycle = 1;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var playedCount = 0;
                for (var index = 0; index < cachedTracks.Count; index++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var trend = cachedTracks[index];
                    NowPlayingStatus.Text = $"Playing trending recording {index + 1} of {cachedTracks.Count} (loop {cycle})...";
                    try
                    {
                        var download = await MusicLibraryApi.DownloadTrendingTrackAsync(trend.CachedTrackId!.Value, cancellation.Token);
                        ClearPlaybackState();
                        var duration = FormatTrackDuration(trend.DurationMs);
                        var subtitle = $"Trending playlist | Track {index + 1} of {cachedTracks.Count}";
                        if (duration is not null) subtitle += $" | {duration}";
                        ShowPlaybackDock(GetTrackDisplayName(trend), subtitle, isPlaying: true, isLiveStation: false);
                        PlaybackDockButton.Content = CreateActionIcon("mdi-stop");
                        ToolTip.SetTip(PlaybackDockButton, "Stop trending playback");
                        PublishPlaybackActivity(GetTrackDisplayName(trend), isLiveStation: false);
                        await NativeRadioActions.PlayFileToCompletionAsync(
                            download.Content,
                            download.ContentType,
                            download.FileName,
                            cancellation.Token);
                        playedCount++;
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch
                    {
                    }
                }

                if (playedCount == 0)
                {
                    NowPlayingStatus.Text = "None of the trending recordings could be played.";
                    break;
                }
                cycle++;
            }
        }
        catch (OperationCanceledException)
        {
            if (_libraryMode == LibraryMode.Trending && LibraryView.IsVisible)
            {
                NowPlayingStatus.Text = "Trending playback stopped.";
            }
        }
        finally
        {
            if (_trendingPlaybackCancellation == cancellation)
            {
                _trendingPlaybackCancellation = null;
                PlayAllTrendingButtonText.Text = "Play all";
                DownloadAllTrendingButton.IsEnabled = true;
                ClearPlaybackState();
                StopPublishingPlaybackActivity();
            }
            cancellation.Dispose();
        }
    }

    private async void Administration_Click(object? sender, RoutedEventArgs eventArgs)
    {
        _libraryPollingCancellation?.Cancel();
        _trendingPlaybackCancellation?.Cancel();
        _probeStatusPollingCancellation?.Cancel();
        NowPlayingNavigationButton.Classes.Set("active", false);
        FollowingNavigationButton.Classes.Set("active", false);
        TrendingNavigationButton.Classes.Set("active", false);
        UserBoardNavigationButton.Classes.Set("active", false);
        AboutNavigationButton.Classes.Set("active", false);
        LibraryView.IsVisible = false;
        AdministrationView.IsVisible = true;
        await Task.WhenAll(LoadAdminUsersAsync(), LoadProbeStatusAsync(), LoadGlobalConfigAsync());
        if (AdministrationView.IsVisible && MusicLibraryApi.IsAuthenticated)
        {
            StartProbeStatusPolling();
        }
    }

    private void StartProbeStatusPolling()
    {
        _probeStatusPollingCancellation?.Cancel();
        var cancellation = _probeStatusPollingCancellation = new CancellationTokenSource();
        _ = PollProbeStatusAsync(cancellation.Token);
    }

    private async Task PollProbeStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                await LoadProbeStatusAsync(
                    ProbeStationSearchInput.Text,
                    _probePage,
                    cancellationToken,
                    showLoading: false,
                    updateStations: false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void RefreshProbeStatus_Click(object? sender, RoutedEventArgs eventArgs)
    {
        RefreshProbeStatusButton.IsEnabled = false;
        try
        {
            await LoadProbeStatusAsync(ProbeStationSearchInput.Text, _probePage);
        }
        finally
        {
            RefreshProbeStatusButton.IsEnabled = true;
        }
    }

    private async void ProbeStationSearchInput_TextChanged(object? sender, TextChangedEventArgs eventArgs)
    {
        _probeSearchCancellation?.Cancel();
        var cancellation = _probeSearchCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, cancellation.Token);
            _probePage = 1;
            await LoadProbeStatusAsync(ProbeStationSearchInput.Text, _probePage, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void ImportStations_Click(object? sender, RoutedEventArgs eventArgs)
    {
        ImportStationsButton.IsEnabled = false;
        _probeStatusPollingCancellation?.Cancel();
        ProbeStatus.Text = "Importing stations...";
        try
        {
            var result = await MusicLibraryApi.ImportStationsAsync();
            _probePage = 1;
            await LoadProbeStatusAsync(page: _probePage);
            ProbeStatus.Text = $"Import complete | {result.Created} created | {result.Updated} updated | {result.Rejected} rejected";
        }
        catch (Exception exception)
        {
            ProbeStatus.Text = exception.Message;
        }
        finally
        {
            ImportStationsButton.IsEnabled = true;
            if (AdministrationView.IsVisible && MusicLibraryApi.IsAuthenticated) StartProbeStatusPolling();
        }
    }

    private async void ToggleProbeWorker_Click(object? sender, RoutedEventArgs eventArgs)
    {
        ProbeWorkerToggleButton.IsEnabled = false;
        ProbeStatus.Text = _probingEnabled ? "Disabling probe worker..." : "Enabling probe worker...";
        try
        {
            if (!_probingEnabled && _enabledStationCount == 0)
            {
                await MusicLibraryApi.SetAllStationProbingEnabledAsync(true);
            }
            await MusicLibraryApi.SetProbingEnabledAsync(!_probingEnabled);
            await Task.WhenAll(LoadProbeStatusAsync(), LoadGlobalConfigAsync());
        }
        catch (Exception exception)
        {
            ProbeStatus.Text = exception.Message;
        }
        finally
        {
            ProbeWorkerToggleButton.IsEnabled = true;
        }
    }

    private async void ToggleAllStationProbes_Click(object? sender, RoutedEventArgs eventArgs)
    {
        var enable = _enabledStationCount == 0;
        StationProbesToggleButton.IsEnabled = false;
        ProbeStatus.Text = $"{(enable ? "Enabling" : "Disabling")} all stations...";
        try
        {
            await MusicLibraryApi.SetAllStationProbingEnabledAsync(enable);
            await LoadProbeStatusAsync(ProbeStationSearchInput.Text, _probePage);
        }
        catch (Exception exception)
        {
            ProbeStatus.Text = exception.Message;
        }
        finally
        {
            StationProbesToggleButton.IsEnabled = true;
        }
    }

    private async void ReloadGlobalConfig_Click(object? sender, RoutedEventArgs eventArgs)
    {
        ReloadGlobalConfigButton.IsEnabled = false;
        try
        {
            await LoadGlobalConfigAsync();
        }
        finally
        {
            ReloadGlobalConfigButton.IsEnabled = true;
        }
    }

    private void ShowTrendingCacheEncryptionKey_Click(object? sender, RoutedEventArgs eventArgs)
    {
        ConfigTrendingCacheEncryptionKeyInput.PasswordChar = ShowTrendingCacheEncryptionKeyInput.IsChecked == true
            ? '\0'
            : '*';
    }

    private async void ClearTrendingCache_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (!_clearTrendingCacheConfirmed)
        {
            _clearTrendingCacheConfirmed = true;
            ClearTrendingCacheButton.Content = "Confirm clear cache";
            GlobalConfigStatus.Text = "Click Confirm clear cache to permanently remove all cached recordings.";
            return;
        }

        ClearTrendingCacheButton.IsEnabled = false;
        SaveGlobalConfigButton.IsEnabled = false;
        ReloadGlobalConfigButton.IsEnabled = false;
        GlobalConfigStatus.Text = "Clearing cached recordings...";
        try
        {
            await MusicLibraryApi.ClearTrendingCacheAsync();
            await LoadGlobalConfigAsync();
            GlobalConfigStatus.Text = "Trending cache cleared.";
        }
        catch (Exception exception)
        {
            GlobalConfigStatus.Text = exception.Message;
        }
        finally
        {
            _clearTrendingCacheConfirmed = false;
            ClearTrendingCacheButton.Content = "Clear cache";
            ClearTrendingCacheButton.IsEnabled = true;
            SaveGlobalConfigButton.IsEnabled = true;
            ReloadGlobalConfigButton.IsEnabled = true;
        }
    }

    private async void SaveGlobalConfig_Click(object? sender, RoutedEventArgs eventArgs)
    {
        SaveGlobalConfigButton.IsEnabled = false;
        ReloadGlobalConfigButton.IsEnabled = false;
        ClearTrendingCacheButton.IsEnabled = false;
        GlobalConfigStatus.Text = "Saving configuration...";
        try
        {
            var directoryArtifactUrl = ConfigDirectoryArtifactUrlInput.Text?.Trim() ?? string.Empty;
            if (!Uri.TryCreate(directoryArtifactUrl, UriKind.Absolute, out var directoryUri)
                || directoryUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("Directory artifact URL must be an absolute HTTPS URL.");
            }
            var probeConcurrency = GetDisplayedInteger(ConfigProbeConcurrencyInput, 1, 100, "Probe concurrency");
            var probeTimeout = GetDisplayedInteger(ConfigProbeTimeoutInput, 2, 60, "Probe timeout");
            var probeBatchSize = GetDisplayedInteger(ConfigProbeBatchSizeInput, 1, 1000, "Stations per batch");
            var cacheTimeout = GetDisplayedInteger(ConfigTrendingCacheCaptureTimeoutInput, 60, 1800, "Trending cache capture timeout");
            var minimumTrendingDuration = GetDisplayedInteger(ConfigTrendingMinimumDurationInput, 15, 600, "Minimum trending duration");
            var cacheRetention = GetDisplayedInteger(ConfigTrendingCacheRetentionInput, 1, 168, "Trending cache retention");
            var cacheMaxSize = GetDisplayedInteger(ConfigTrendingCacheMaxSizeInput, 32, 4096, "Trending cache maximum size");
            var cacheKey = ConfigTrendingCacheEncryptionKeyInput.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(cacheKey)
                && (!TryDecodeCacheKey(cacheKey, out var decodedKey) || decodedKey.Length != 32))
            {
                throw new InvalidOperationException("Trending cache encryption key must be a Base64-encoded 32-byte key.");
            }

            var config = new GlobalConfigModel
            {
                DirectoryArtifactUrl = directoryArtifactUrl,
                ProbingEnabled = ConfigProbingEnabledInput.IsChecked == true,
                ProbeConcurrency = probeConcurrency,
                ProbeTimeoutSeconds = probeTimeout,
                ProbeBatchSize = probeBatchSize,
                TrendingCacheEncryptionKey = cacheKey,
                TrendingCacheCaptureTimeoutSeconds = cacheTimeout,
                TrendingMinimumDurationSeconds = minimumTrendingDuration,
                TrendingCacheRetentionHours = cacheRetention,
                TrendingCacheMaxSizeMegabytes = cacheMaxSize
            };
            await MusicLibraryApi.SaveGlobalConfigAsync(config);
            await Task.WhenAll(LoadGlobalConfigAsync(), LoadProbeStatusAsync());
            GlobalConfigStatus.Text = "Configuration saved.";
        }
        catch (Exception exception)
        {
            GlobalConfigStatus.Text = exception.Message;
        }
        finally
        {
            SaveGlobalConfigButton.IsEnabled = true;
            ReloadGlobalConfigButton.IsEnabled = true;
            ClearTrendingCacheButton.IsEnabled = true;
        }
    }

    private static int GetDisplayedInteger(NumericUpDown input, int minimum, int maximum, string name)
    {
        if (!int.TryParse(input.Text, out var value) || value < minimum || value > maximum)
        {
            throw new InvalidOperationException($"{name} must be a whole number between {minimum} and {maximum}.");
        }
        return value;
    }

    private async Task LoadGlobalConfigAsync()
    {
        _clearTrendingCacheConfirmed = false;
        ClearTrendingCacheButton.Content = "Clear cache";
        GlobalConfigStatus.Text = "Loading configuration...";
        try
        {
            var configTask = MusicLibraryApi.GetGlobalConfigAsync();
            var cacheStatusTask = MusicLibraryApi.GetTrendingCacheStatusAsync();
            await Task.WhenAll(configTask, cacheStatusTask);
            var config = await configTask;
            var cacheStatus = await cacheStatusTask;
            ConfigDirectoryArtifactUrlInput.Text = config.DirectoryArtifactUrl;
            ConfigProbingEnabledInput.IsChecked = config.ProbingEnabled;
            SetProbingEnabledState(config.ProbingEnabled);
            ConfigProbeConcurrencyInput.Value = config.ProbeConcurrency;
            ConfigProbeTimeoutInput.Value = config.ProbeTimeoutSeconds;
            ConfigProbeBatchSizeInput.Value = config.ProbeBatchSize;
            ConfigTrendingCacheEncryptionKeyInput.Text = config.TrendingCacheEncryptionKey;
            ConfigTrendingCacheCaptureTimeoutInput.Value = config.TrendingCacheCaptureTimeoutSeconds;
            ConfigTrendingMinimumDurationInput.Value = config.TrendingMinimumDurationSeconds;
            ConfigTrendingCacheRetentionInput.Value = config.TrendingCacheRetentionHours;
            ConfigTrendingCacheMaxSizeInput.Value = config.TrendingCacheMaxSizeMegabytes;
            var cacheSizeMegabytes = cacheStatus.SizeBytes / (1024d * 1024d);
            ConfigTrendingCacheStatus.Text = $"Current cache: {cacheStatus.SongCount:N0} song(s), {cacheSizeMegabytes:N1} MiB used";
            GlobalConfigStatus.Text = "Configuration loaded.";
        }
        catch (Exception exception)
        {
            GlobalConfigStatus.Text = exception.Message;
        }
    }

    private static bool TryDecodeCacheKey(string value, out byte[] key)
    {
        try
        {
            key = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            key = [];
            return false;
        }
    }

    private async void PreviousProbePage_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (_probePage <= 1) return;
        PreviousProbePageButton.IsEnabled = false;
        NextProbePageButton.IsEnabled = false;
        try
        {
            await LoadProbeStatusAsync(ProbeStationSearchInput.Text, _probePage - 1);
        }
        finally
        {
            PreviousProbePageButton.IsEnabled = _probePage > 1;
            NextProbePageButton.IsEnabled = _probePage < _probePageCount;
        }
    }

    private async void NextProbePage_Click(object? sender, RoutedEventArgs eventArgs)
    {
        PreviousProbePageButton.IsEnabled = false;
        NextProbePageButton.IsEnabled = false;
        try
        {
            await LoadProbeStatusAsync(ProbeStationSearchInput.Text, _probePage + 1);
        }
        finally
        {
            PreviousProbePageButton.IsEnabled = _probePage > 1;
            NextProbePageButton.IsEnabled = _probePage < _probePageCount;
        }
    }

    private async Task LoadProbeStatusAsync(
        string? query = null,
        int page = 1,
        CancellationToken cancellationToken = default,
        bool showLoading = true,
        bool updateStations = true)
    {
        if (showLoading)
        {
            await _probeStatusLoadGate.WaitAsync(cancellationToken);
        }
        else if (!await _probeStatusLoadGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            if (showLoading)
            {
                ProbeStatus.Text = "Loading probe status...";
                ProbeStationsList.ItemsSource = null;
            }
            try
            {
                var status = await MusicLibraryApi.GetAdminProbeStatusAsync(query, page, ProbePageSize, cancellationToken);
                _probePage = status.Page;
                SetProbingEnabledState(status.ProbingEnabled);
                _enabledStationCount = status.EnabledStationCount;
                StationProbesToggleButton.Content = status.EnabledStationCount == 0 ? "Enable all stations" : "Disable all stations";
                StationProbesToggleButton.IsEnabled = status.MatchingStationCount > 0;
                if (updateStations)
                {
                    ProbeStationsList.ItemsSource = status.Stations.Select(CreateProbeStatusRow).ToList();
                }
                var pageCount = Math.Max(1, (int)Math.Ceiling(status.MatchingStationCount / (double)status.PageSize));
                _probePageCount = pageCount;
                ProbePageStatus.Text = $"Page {status.Page} of {pageCount}";
                PreviousProbePageButton.IsEnabled = status.Page > 1;
                NextProbePageButton.IsEnabled = status.Page < pageCount;
                var workerState = status.ProbingEnabled ? "enabled" : "disabled";
                var batchState = status.LastBatchStartedAt is null
                    ? "No probe batch has run since the API started."
                    : status.LastBatchCompletedAt is null || status.LastBatchCompletedAt < status.LastBatchStartedAt
                        ? $"Batch running since {status.LastBatchStartedAt.Value.LocalDateTime:g}."
                        : $"Last batch completed {status.LastBatchCompletedAt.Value.LocalDateTime:g}.";
                var stationState = status.MatchingStationCount == 0
                    ? (string.IsNullOrWhiteSpace(query) ? "No stations imported." : "No matching stations.")
                    : status.MatchingStationCount > status.Stations.Count
                        ? $"Showing {status.Stations.Count} of {status.MatchingStationCount} matching stations."
                        : $"{status.MatchingStationCount} station(s).";
                ProbeStatus.Text = $"Worker {workerState} | {status.ActiveProbeCount} querying now | {status.EnabledStationCount} enabled | {stationState} {batchState} Updated {DateTime.Now:T}";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                ProbeStatus.Text = exception.Message;
            }
        }
        finally
        {
            _probeStatusLoadGate.Release();
        }
    }

    private void SetProbingEnabledState(bool enabled)
    {
        _probingEnabled = enabled;
        ProbeWorkerToggleButton.Content = enabled ? "Disable worker" : "Enable worker";
        ProbeWorkerToggleButton.IsEnabled = true;
    }

    private Control CreateProbeStatusRow(StationProbeStatusSummary station)
    {
        var state = station.IsProbing
            ? $"Querying since {station.ProbeStartedAt!.Value.LocalDateTime:t}"
            : !station.IsProbeEnabled
                ? "Disabled"
                : station.ConsecutiveProbeFailures > 0
                    ? "Failing"
                    : "Waiting";
        var lastAttempt = station.LastProbedAt?.LocalDateTime.ToString("g") ?? "never";
        var lastSuccessfulProbe = station.LastMetadataAt?.LocalDateTime.ToString("g") ?? "never";
        var category = string.IsNullOrWhiteSpace(station.Genre) ? "Uncategorized" : station.Genre;
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Avalonia.Thickness(0, 0, 0, 10)
        };
        var details = new StackPanel { Spacing = 2 };
        details.Children.Add(new TextBlock
        {
            Text = station.Name,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });
        details.Children.Add(new TextBlock
        {
            Text = $"Category: {category}",
            Opacity = 0.7,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        details.Children.Add(new TextBlock
        {
            Text = $"Last attempt: {lastAttempt} | Last successful probe: {lastSuccessfulProbe} | Failures: {station.ConsecutiveProbeFailures}",
            Opacity = 0.7,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        details.Children.Add(new TextBlock
        {
            Text = station.StreamUrl,
            Opacity = 0.55,
            FontSize = 11,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        Grid.SetColumnSpan(details, 2);
        row.Children.Add(details);

        var actions = new WrapPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        actions.Children.Add(new TextBlock
        {
            Text = state,
            FontWeight = station.IsProbing ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        var toggleButton = new Button
        {
            Content = station.IsProbeEnabled ? "Disable" : "Enable",
            Margin = new Avalonia.Thickness(8, 0, 0, 8)
        };
        toggleButton.Click += async (_, _) => await UpdateStationProbeAsync(station);
        actions.Children.Add(toggleButton);
        Grid.SetColumnSpan(actions, 2);
        Grid.SetRow(actions, 1);
        actions.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        actions.Margin = new Avalonia.Thickness(0, 6, 0, 0);
        row.Children.Add(actions);
        return row;
    }

    private async Task UpdateStationProbeAsync(StationProbeStatusSummary station)
    {
        ProbeStatus.Text = $"{(station.IsProbeEnabled ? "Disabling" : "Enabling")} {station.Name}...";
        try
        {
            await MusicLibraryApi.SetStationProbingEnabledAsync(station.Id, !station.IsProbeEnabled);
            await LoadProbeStatusAsync(ProbeStationSearchInput.Text, _probePage);
        }
        catch (Exception exception)
        {
            ProbeStatus.Text = exception.Message;
        }
    }

    private async Task LoadAdminUsersAsync()
    {
        AdministrationStatus.Text = "Loading users...";
        AdminUsersList.ItemsSource = null;
        try
        {
            var users = await MusicLibraryApi.GetAdminUsersAsync();
            AdminUsersList.ItemsSource = users.Select(CreateAdminUserRow).ToList();
            AdministrationStatus.Text = users.Count == 0 ? "No accounts found." : $"{users.Count} account(s)";
        }
        catch (Exception exception)
        {
            AdministrationStatus.Text = exception.Message;
        }
    }

    private Control CreateAdminUserRow(UserSummary user)
    {
        var isCurrentUser = MusicLibraryApi.CurrentUser?.Id == user.Id;
        var isAdmin = user.Roles.Contains(Roles.Admin);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Avalonia.Thickness(0, 0, 0, 8)
        };
        var identity = new StackPanel { Spacing = 2 };
        identity.Children.Add(new TextBlock
        {
            Text = $"{user.DisplayName ?? user.Email}{(isCurrentUser ? " (you)" : string.Empty)}",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        identity.Children.Add(new TextBlock
        {
            Text = $"{user.Email}  |  {(isAdmin ? "Administrator" : "User")}  |  {(user.IsActive ? "Enabled" : "Disabled")}",
            Opacity = 0.7,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        row.Children.Add(identity);

        var actions = new WrapPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetRow(actions, 1);
        actions.Margin = new Avalonia.Thickness(0, 6, 0, 0);

        var activationButton = new Button
        {
            Content = user.IsActive ? "Disable" : "Enable",
            IsEnabled = !isCurrentUser,
            Margin = new Avalonia.Thickness(0, 0, 8, 8)
        };
        activationButton.Click += async (_, _) => await UpdateUserAsync(
            user,
            isActive: !user.IsActive,
            role: null,
            $"{(user.IsActive ? "Disabling" : "Enabling")} {user.DisplayName ?? user.Email}...");
        actions.Children.Add(activationButton);

        var roleButton = new Button
        {
            Content = isAdmin ? "Make user" : "Make admin",
            IsEnabled = !isCurrentUser,
            Margin = new Avalonia.Thickness(0, 0, 8, 8)
        };
        roleButton.Click += async (_, _) => await UpdateUserAsync(
            user,
            user.IsActive,
            isAdmin ? Roles.User : Roles.Admin,
            $"Updating {user.DisplayName ?? user.Email}'s role...");
        actions.Children.Add(roleButton);

        var deleteButton = new Button
        {
            Content = isCurrentUser ? "Current account" : "Delete",
            IsEnabled = !isCurrentUser,
            Margin = new Avalonia.Thickness(0, 0, 8, 8)
        };
        var deleteConfirmed = false;
        deleteButton.Click += async (_, _) =>
        {
            if (!deleteConfirmed)
            {
                deleteConfirmed = true;
                deleteButton.Content = "Confirm delete";
                AdministrationStatus.Text = $"Click Confirm delete to permanently remove {user.DisplayName ?? user.Email}.";
                return;
            }

            await DeleteUserAsync(user);
        };
        actions.Children.Add(deleteButton);
        row.Children.Add(actions);
        return row;
    }

    private async Task UpdateUserAsync(UserSummary user, bool isActive, string? role, string progress)
    {
        AdministrationStatus.Text = progress;
        try
        {
            await MusicLibraryApi.UpdateAdminUserAsync(user, isActive, role);
            await LoadAdminUsersAsync();
        }
        catch (Exception exception)
        {
            AdministrationStatus.Text = exception.Message;
        }
    }

    private async Task DeleteUserAsync(UserSummary user)
    {
        AdministrationStatus.Text = $"Deleting {user.DisplayName ?? user.Email}...";
        try
        {
            await MusicLibraryApi.DeleteAdminUserAsync(user.Id);
            await LoadAdminUsersAsync();
        }
        catch (Exception exception)
        {
            AdministrationStatus.Text = exception.Message;
        }
    }

}