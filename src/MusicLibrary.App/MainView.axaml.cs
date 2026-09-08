using Avalonia.Controls;
using Avalonia.Interactivity;
using MusicLibrary.Contracts;

namespace MusicLibrary.App;

public sealed partial class MainView : UserControl
{
    private enum LibraryMode
    {
        NowPlaying,
        Following,
        Trending
    }

    private const int ProbePageSize = 100;
    private readonly SemaphoreSlim _nowPlayingLoadGate = new(1, 1);
    private readonly SemaphoreSlim _probeStatusLoadGate = new(1, 1);
    private readonly HashSet<string> _subscribedArtists = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Button>> _subscriptionButtons = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _librarySearchCancellation;
    private CancellationTokenSource? _libraryPollingCancellation;
    private CancellationTokenSource? _probeSearchCancellation;
    private CancellationTokenSource? _probeStatusPollingCancellation;
    private CancellationTokenSource? _sessionExpiryCancellation;
    private bool _probingEnabled;
    private int _enabledStationCount;
    private int _probePage = 1;
    private int _probePageCount = 1;
    private LibraryMode _libraryMode;
    private bool _suppressLibrarySearch;
    private IReadOnlyCollection<NowPlayingSummary> _nowPlayingSnapshot = [];
    private IReadOnlyCollection<ArtistSubscriptionSummary> _followingSnapshot = [];
    private IReadOnlyCollection<TrendingSummary> _trendingSnapshot = [];
    private bool _subscriptionsLoaded;
    private bool? _isCompactLayout;

    public bool IsBrowserHost { get; }
    public bool ShowNativeMedia
    {
        get { return !IsBrowserHost; }
    }

    public MainView() : this(showAdministration: false)
    {
    }

    public MainView(bool showAdministration = false)
    {
        IsBrowserHost = showAdministration;
        InitializeComponent();
        AuthenticationView.Authenticated += AuthenticationView_Authenticated;
        MusicLibraryApi.SessionInvalidated += MusicLibraryApi_SessionInvalidated;
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
            MainShell.RowDefinitions = new RowDefinitions("Auto,12,*");
            LibraryNavigationPanel.Orientation = Avalonia.Layout.Orientation.Horizontal;
            LibraryNavigationTitle.IsVisible = false;
            LibraryNavigationScroll.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            Grid.SetColumn(LibraryView, 0);
            Grid.SetRow(LibraryView, 2);
            Grid.SetColumn(AdministrationView, 0);
            Grid.SetRow(AdministrationView, 2);
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
            MainShell.RowDefinitions = new RowDefinitions("*,Auto");
            LibraryNavigationPanel.Orientation = Avalonia.Layout.Orientation.Vertical;
            LibraryNavigationTitle.IsVisible = true;
            LibraryNavigationScroll.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
            Grid.SetColumn(LibraryView, 2);
            Grid.SetRow(LibraryView, 0);
            Grid.SetColumn(AdministrationView, 2);
            Grid.SetRow(AdministrationView, 0);
            AdministrationHeader.ColumnDefinitions = new ColumnDefinitions("*,Auto");
            AdministrationHeader.RowDefinitions = new RowDefinitions("Auto");
            Grid.SetColumn(BackToLibraryButton, 1);
            Grid.SetRow(BackToLibraryButton, 0);
            BackToLibraryButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        }
    }

    private async void Listen_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (NativeRadioActions.ListenAsync is null || !TryGetStreamUri(out var streamUri))
        {
            NativeMediaStatus.Text = "Enter a valid HTTP or HTTPS station stream URL.";
            return;
        }
        ListenButton.IsEnabled = false;
        try
        {
            await NativeRadioActions.ListenAsync(streamUri);
            NativeMediaStatus.Text = "Opening the station in the native audio player.";
        }
        catch (Exception exception)
        {
            NativeMediaStatus.Text = exception.Message;
        }
        finally
        {
            ListenButton.IsEnabled = true;
        }
    }

    private void AuthenticationView_Authenticated(object? sender, AuthenticatedEventArgs eventArgs)
    {
        ShowAuthenticatedApplication(eventArgs.User);
        _ = LoadNowPlayingAsync();
    }

    private void ShowAuthenticatedApplication(UserSummary user)
    {
        _subscribedArtists.Clear();
        _subscriptionsLoaded = false;
        AuthenticationView.IsVisible = false;
        ApplicationView.IsVisible = true;
        ApplyResponsiveLayout(Bounds.Width);
        CurrentUserStatus.Text = user.DisplayName ?? user.Email;
        var isAdmin = user.Roles.Contains(Roles.Admin);
        AdministrationSeparator.IsVisible = isAdmin;
        AdministrationButton.IsVisible = isAdmin;
        SetLibraryMode(LibraryMode.NowPlaying);
        ShowLibrary();
        ScheduleSessionExpiry();
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
        _probeSearchCancellation?.Cancel();
        _probeStatusPollingCancellation?.Cancel();
        _sessionExpiryCancellation?.Cancel();
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

    private void Following_Click(object? sender, RoutedEventArgs eventArgs)
    {
        SetLibraryMode(LibraryMode.Following);
        ShowLibrary();
        _ = LoadCurrentLibraryViewAsync();
    }

    private void SetLibraryMode(LibraryMode mode)
    {
        _librarySearchCancellation?.Cancel();
        _libraryPollingCancellation?.Cancel();
        _libraryMode = mode;
        NowPlayingNavigationButton.Classes.Set("active", mode == LibraryMode.NowPlaying);
        FollowingNavigationButton.Classes.Set("active", mode == LibraryMode.Following);
        TrendingNavigationButton.Classes.Set("active", mode == LibraryMode.Trending);
        LibraryViewTitle.Text = mode switch
        {
            LibraryMode.Following => "Following",
            LibraryMode.Trending => "Trending Now",
            _ => "Now Playing"
        };
        LibrarySearchInput.PlaceholderText = mode switch
        {
            LibraryMode.Following => "Search followed artists",
            LibraryMode.Trending => "Search trending artist or track",
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
    }

    private void ShowLibrary()
    {
        _probeStatusPollingCancellation?.Cancel();
        AdministrationView.IsVisible = false;
        LibraryView.IsVisible = true;
        StartNowPlayingPolling();
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
            LibraryMode.Trending => LoadTrendingAsync(query, cancellationToken, showLoading),
            _ => LoadNowPlayingAsync(query, cancellationToken, showLoading)
        };
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
                    _nowPlayingSnapshot = observations.ToList();
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
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
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
        Grid.SetColumnSpan(details, 2);
        row.Children.Add(details);

        var observedAt = new TextBlock
        {
            Text = observation.ObservedAt.LocalDateTime.ToString("g"),
            Opacity = 0.65,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetColumn(observedAt, 1);
        Grid.SetRow(observedAt, 1);
        row.Children.Add(observedAt);

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
                    subscriptions = subscriptions
                        .Where(subscription => subscription.ArtistName.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }
                var hasChanges = !_followingSnapshot.SequenceEqual(subscriptions);
                var canReplaceRows = showLoading || NowPlayingScroll.Offset.Y <= 1;
                if ((showLoading || hasChanges) && canReplaceRows)
                {
                    _followingSnapshot = subscriptions.ToList();
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
                await LoadFollowingAsync(LibrarySearchInput.Text);
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
                var trends = await MusicLibraryApi.GetTrendingAsync(query, cancellationToken);
                if (_libraryMode != LibraryMode.Trending) return;
                var hasChanges = !_trendingSnapshot.SequenceEqual(trends);
                var canReplaceRows = showLoading || NowPlayingScroll.Offset.Y <= 1;
                if ((showLoading || hasChanges) && canReplaceRows)
                {
                    _trendingSnapshot = trends.ToList();
                    NowPlayingList.ItemsSource = trends.Select((trend, index) => CreateTrendingRow(trend, index + 1)).ToList();
                }
                NowPlayingStatus.Text = trends.Count == 0
                    ? (string.IsNullOrWhiteSpace(query) ? "No trends detected in the past 24 hours." : "No matching trends found.")
                    : hasChanges && !canReplaceRows
                        ? $"{trends.Count} trend(s) | Rankings updated"
                        : $"{trends.Count} trend(s) from the past 24 hours | Updated {DateTime.Now:T}";
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
            Text = $"{trend.ObservationCount} detection(s) across {trend.StationCount} station(s)",
            Opacity = 0.7
        });
        Grid.SetColumn(details, 2);
        row.Children.Add(details);
        var lastObserved = new TextBlock
        {
            Text = trend.LastObservedAt.LocalDateTime.ToString("g"),
            Opacity = 0.65,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetColumn(lastObserved, 2);
        Grid.SetRow(lastObserved, 1);
        row.Children.Add(lastObserved);
        if (trend.CachedTrackId is { } cachedTrackId)
        {
            var actions = new StackPanel
            {
                Spacing = 4,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            var cachedUntilText = trend.CachedUntil?.LocalDateTime.ToString("g");
            var playButton = new Button { Content = "Play" };
            ToolTip.SetTip(playButton, cachedUntilText is not null
                ? $"Play cached recording (available until {cachedUntilText})"
                : "Play cached recording");
            playButton.Click += async (_, _) => await PlayTrendingTrackAsync(cachedTrackId, playButton);
            actions.Children.Add(playButton);
            var downloadButton = new Button
            {
                Content = "Download"
            };
            ToolTip.SetTip(downloadButton, trend.CachedUntil is { } downloadCachedUntil
                ? $"Cached until {downloadCachedUntil.LocalDateTime:g}"
                : "Download cached recording");
            downloadButton.Click += async (_, _) => await DownloadTrendingTrackAsync(cachedTrackId, downloadButton);
            actions.Children.Add(downloadButton);
            Grid.SetColumn(actions, 4);
            Grid.SetRowSpan(actions, 2);
            row.Children.Add(actions);
        }
        return row;
    }

    private async Task PlayTrendingTrackAsync(Guid cachedTrackId, Button playButton)
    {
        if (NativeRadioActions.PlayFileAsync is null)
        {
            NowPlayingStatus.Text = "Playback is not available on this platform.";
            return;
        }

        playButton.IsEnabled = false;
        NowPlayingStatus.Text = "Preparing encrypted recording...";
        try
        {
            var download = await MusicLibraryApi.DownloadTrendingTrackAsync(cachedTrackId);
            await NativeRadioActions.PlayFileAsync(download.Content, download.ContentType, download.FileName);
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

    private async void Administration_Click(object? sender, RoutedEventArgs eventArgs)
    {
        _libraryPollingCancellation?.Cancel();
        _probeStatusPollingCancellation?.Cancel();
        NowPlayingNavigationButton.Classes.Set("active", false);
        FollowingNavigationButton.Classes.Set("active", false);
        TrendingNavigationButton.Classes.Set("active", false);
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

    private async void SaveGlobalConfig_Click(object? sender, RoutedEventArgs eventArgs)
    {
        SaveGlobalConfigButton.IsEnabled = false;
        GlobalConfigStatus.Text = "Saving configuration...";
        try
        {
            var directoryArtifactUrl = ConfigDirectoryArtifactUrlInput.Text?.Trim() ?? string.Empty;
            if (!Uri.TryCreate(directoryArtifactUrl, UriKind.Absolute, out var directoryUri)
                || directoryUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("Directory artifact URL must be an absolute HTTPS URL.");
            }
            if (ConfigProbeConcurrencyInput.Value is not { } probeConcurrency
                || ConfigProbeTimeoutInput.Value is not { } probeTimeout
                || ConfigProbeBatchSizeInput.Value is not { } probeBatchSize
                || ConfigTrendingCacheCaptureTimeoutInput.Value is not { } cacheTimeout
                || ConfigTrendingCacheRetentionInput.Value is not { } cacheRetention
                || ConfigTrendingCacheMaxSizeInput.Value is not { } cacheMaxSize)
            {
                throw new InvalidOperationException("All numeric configuration values are required.");
            }
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
                ProbeConcurrency = Convert.ToInt32(probeConcurrency),
                ProbeTimeoutSeconds = Convert.ToInt32(probeTimeout),
                ProbeBatchSize = Convert.ToInt32(probeBatchSize),
                TrendingCacheEncryptionKey = cacheKey,
                TrendingCacheCaptureTimeoutSeconds = Convert.ToInt32(cacheTimeout),
                TrendingCacheRetentionHours = Convert.ToInt32(cacheRetention),
                TrendingCacheMaxSizeMegabytes = Convert.ToInt32(cacheMaxSize)
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
        }
    }

    private async Task LoadGlobalConfigAsync()
    {
        GlobalConfigStatus.Text = "Loading configuration...";
        try
        {
            var config = await MusicLibraryApi.GetGlobalConfigAsync();
            ConfigDirectoryArtifactUrlInput.Text = config.DirectoryArtifactUrl;
            ConfigProbingEnabledInput.IsChecked = config.ProbingEnabled;
            SetProbingEnabledState(config.ProbingEnabled);
            ConfigProbeConcurrencyInput.Value = config.ProbeConcurrency;
            ConfigProbeTimeoutInput.Value = config.ProbeTimeoutSeconds;
            ConfigProbeBatchSizeInput.Value = config.ProbeBatchSize;
            ConfigTrendingCacheEncryptionKeyInput.Text = config.TrendingCacheEncryptionKey;
            ConfigTrendingCacheCaptureTimeoutInput.Value = config.TrendingCacheCaptureTimeoutSeconds;
            ConfigTrendingCacheRetentionInput.Value = config.TrendingCacheRetentionHours;
            ConfigTrendingCacheMaxSizeInput.Value = config.TrendingCacheMaxSizeMegabytes;
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
        var lastMetadata = station.LastMetadataAt?.LocalDateTime.ToString("g") ?? "never";
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
            Text = $"Last attempt: {lastAttempt} | Metadata: {lastMetadata} | Failures: {station.ConsecutiveProbeFailures}",
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

        var actions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        actions.Children.Add(new TextBlock
        {
            Text = state,
            FontWeight = station.IsProbing ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        var toggleButton = new Button { Content = station.IsProbeEnabled ? "Disable" : "Enable" };
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
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
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
        Grid.SetColumnSpan(identity, 2);
        row.Children.Add(identity);

        var actions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetColumnSpan(actions, 2);
        Grid.SetRow(actions, 1);
        actions.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        actions.Margin = new Avalonia.Thickness(0, 6, 0, 0);

        var activationButton = new Button { Content = user.IsActive ? "Disable" : "Enable", IsEnabled = !isCurrentUser };
        activationButton.Click += async (_, _) => await UpdateUserAsync(
            user,
            isActive: !user.IsActive,
            role: null,
            $"{(user.IsActive ? "Disabling" : "Enabling")} {user.DisplayName ?? user.Email}...");
        actions.Children.Add(activationButton);

        var roleButton = new Button { Content = isAdmin ? "Make user" : "Make admin", IsEnabled = !isCurrentUser };
        roleButton.Click += async (_, _) => await UpdateUserAsync(
            user,
            user.IsActive,
            isAdmin ? Roles.User : Roles.Admin,
            $"Updating {user.DisplayName ?? user.Email}'s role...");
        actions.Children.Add(roleButton);

        var deleteButton = new Button { Content = isCurrentUser ? "Current account" : "Delete", IsEnabled = !isCurrentUser };
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

    private async void Download_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (NativeRadioActions.DownloadAsync is null || !TryGetStreamUri(out var streamUri))
        {
            NativeMediaStatus.Text = "Enter a valid HTTP or HTTPS station stream URL.";
            return;
        }
        DownloadButton.IsEnabled = false;
        var seconds = int.TryParse(RecordingSecondsInput.Text, out var parsedSeconds) ? Math.Clamp(parsedSeconds, 10, 1800) : 300;
        try
        {
            var path = await NativeRadioActions.DownloadAsync(streamUri, TimeSpan.FromSeconds(seconds));
            NativeMediaStatus.Text = $"Saved recording: {path}";
        }
        catch (Exception exception)
        {
            NativeMediaStatus.Text = exception.Message;
        }
        finally
        {
            DownloadButton.IsEnabled = true;
        }
    }

    private bool TryGetStreamUri(out Uri streamUri)
    {
        if (Uri.TryCreate(StreamUrlInput.Text?.Trim(), UriKind.Absolute, out var parsedUri)
            && (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps))
        {
            streamUri = parsedUri;
            return true;
        }
        streamUri = null!;
        return false;
    }
}