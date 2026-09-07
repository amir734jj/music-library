using Avalonia.Controls;
using Avalonia.Interactivity;
using MusicLibrary.Contracts;

namespace MusicLibrary.App;

public sealed partial class MainView : UserControl
{
    private CancellationTokenSource? _librarySearchCancellation;

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
        AuthenticationView.IsVisible = IsBrowserHost;
        ApplicationView.IsVisible = !IsBrowserHost;
        SignOutButton.IsVisible = IsBrowserHost;
    }

    protected override async void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        if (!MusicLibraryApi.IsConfigured)
        {
            ApiConnectionStatus.Text = "Browser app";
            return;
        }

        try
        {
            ApiConnectionStatus.Text = await MusicLibraryApi.IsHealthyAsync() ? "API connected" : "API unavailable";
        }
        catch (HttpRequestException)
        {
            ApiConnectionStatus.Text = "API unavailable";
        }
    }

    private async void Listen_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (NativeRadioActions.ListenAsync is null || !TryGetStreamUri(out var streamUri))
        {
            NativeMediaStatus.Text = "Enter a valid HTTP or HTTPS station stream URL.";
            return;
        }
        try
        {
            await NativeRadioActions.ListenAsync(streamUri);
            NativeMediaStatus.Text = "Opening the station in the native audio player.";
        }
        catch (Exception exception)
        {
            NativeMediaStatus.Text = exception.Message;
        }
    }

    private void AuthenticationView_Authenticated(object? sender, AuthenticatedEventArgs eventArgs)
    {
        ShowAuthenticatedApplication(eventArgs.User);
        _ = LoadNowPlayingAsync();
    }

    private void ShowAuthenticatedApplication(UserSummary user)
    {
        AuthenticationView.IsVisible = false;
        ApplicationView.IsVisible = true;
        CurrentUserStatus.Text = user.DisplayName ?? user.Email;
        var isAdmin = user.Roles.Contains(Roles.Admin);
        AdministrationSeparator.IsVisible = isAdmin;
        AdministrationButton.IsVisible = isAdmin;
        ShowLibrary();
    }

    private void SignOut_Click(object? sender, RoutedEventArgs eventArgs)
    {
        _librarySearchCancellation?.Cancel();
        MusicLibraryApi.SignOut();
        ApplicationView.IsVisible = false;
        AuthenticationView.IsVisible = true;
        AuthenticationView.Reset();
    }

    private void Library_Click(object? sender, RoutedEventArgs eventArgs)
    {
        ShowLibrary();
        _ = LoadNowPlayingAsync(LibrarySearchInput.Text);
    }

    private void ShowLibrary()
    {
        AdministrationView.IsVisible = false;
        LibraryView.IsVisible = true;
    }

    private async void LibrarySearchInput_TextChanged(object? sender, TextChangedEventArgs eventArgs)
    {
        _librarySearchCancellation?.Cancel();
        var cancellation = _librarySearchCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, cancellation.Token);
            await LoadNowPlayingAsync(LibrarySearchInput.Text, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task LoadNowPlayingAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        NowPlayingStatus.Text = string.IsNullOrWhiteSpace(query) ? "Loading live observations..." : "Searching...";
        NowPlayingList.ItemsSource = null;
        try
        {
            var observations = await MusicLibraryApi.GetNowPlayingAsync(query, cancellationToken);
            NowPlayingList.ItemsSource = observations.Select(CreateNowPlayingRow).ToList();
            NowPlayingStatus.Text = observations.Count == 0
                ? (string.IsNullOrWhiteSpace(query)
                    ? "No live observations yet. An administrator must import stations and enable probing first."
                    : "No matching artists, tracks, or stations found.")
                : $"{observations.Count} observation(s)";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            NowPlayingStatus.Text = exception.Message;
        }
    }

    private static Control CreateNowPlayingRow(NowPlayingSummary observation)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Avalonia.Thickness(0, 0, 0, 10)
        };
        var details = new StackPanel { Spacing = 2 };
        details.Children.Add(new TextBlock
        {
            Text = observation.Artist is null
                ? observation.Title ?? observation.RawMetadata
                : $"{observation.Artist} - {observation.Title ?? observation.RawMetadata}",
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });
        details.Children.Add(new TextBlock { Text = observation.StationName, Opacity = 0.7 });
        row.Children.Add(details);

        var observedAt = new TextBlock
        {
            Text = observation.ObservedAt.LocalDateTime.ToString("g"),
            Opacity = 0.65,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetColumn(observedAt, 1);
        row.Children.Add(observedAt);
        return row;
    }

    private async void Administration_Click(object? sender, RoutedEventArgs eventArgs)
    {
        LibraryView.IsVisible = false;
        AdministrationView.IsVisible = true;
        await Task.WhenAll(LoadAdminUsersAsync(), LoadProbeStatusAsync());
    }

    private async void RefreshProbeStatus_Click(object? sender, RoutedEventArgs eventArgs)
    {
        await LoadProbeStatusAsync();
    }

    private async Task LoadProbeStatusAsync()
    {
        ProbeStatus.Text = "Loading probe status...";
        ProbeStationsList.ItemsSource = null;
        try
        {
            var status = await MusicLibraryApi.GetAdminProbeStatusAsync();
            ProbeStationsList.ItemsSource = status.Stations.Select(CreateProbeStatusRow).ToList();
            var workerState = status.ProbingEnabled ? "enabled" : "disabled";
            var batchState = status.LastBatchStartedAt is null
                ? "No probe batch has run since the API started."
                : status.LastBatchCompletedAt is null || status.LastBatchCompletedAt < status.LastBatchStartedAt
                    ? $"Batch running since {status.LastBatchStartedAt.Value.LocalDateTime:g}."
                    : $"Last batch completed {status.LastBatchCompletedAt.Value.LocalDateTime:g}.";
            ProbeStatus.Text = $"Worker {workerState} | {status.ActiveProbeCount} querying now | {batchState}";
        }
        catch (Exception exception)
        {
            ProbeStatus.Text = exception.Message;
        }
    }

    private static Control CreateProbeStatusRow(StationProbeStatusSummary station)
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
        row.Children.Add(details);

        var stateText = new TextBlock
        {
            Text = state,
            FontWeight = station.IsProbing ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetColumn(stateText, 1);
        row.Children.Add(stateText);
        return row;
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
            Margin = new Avalonia.Thickness(0, 0, 0, 8)
        };
        var identity = new StackPanel { Spacing = 2 };
        identity.Children.Add(new TextBlock
        {
            Text = $"{user.DisplayName ?? user.Email}{(isCurrentUser ? " (you)" : string.Empty)}",
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });
        identity.Children.Add(new TextBlock
        {
            Text = $"{user.Email}  |  {(isAdmin ? "Administrator" : "User")}  |  {(user.IsActive ? "Enabled" : "Disabled")}",
            Opacity = 0.7
        });
        row.Children.Add(identity);

        var actions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetColumn(actions, 1);

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