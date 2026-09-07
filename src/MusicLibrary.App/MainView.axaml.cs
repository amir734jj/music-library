using Avalonia.Controls;
using Avalonia.Interactivity;
using MusicLibrary.Contracts;

namespace MusicLibrary.App;

public sealed partial class MainView : UserControl
{
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
        MusicLibraryApi.SignOut();
        ApplicationView.IsVisible = false;
        AuthenticationView.IsVisible = true;
        AuthenticationView.Reset();
    }

    private void Library_Click(object? sender, RoutedEventArgs eventArgs)
    {
        ShowLibrary();
    }

    private void ShowLibrary()
    {
        AdministrationView.IsVisible = false;
        LibraryView.IsVisible = true;
    }

    private async void Administration_Click(object? sender, RoutedEventArgs eventArgs)
    {
        LibraryView.IsVisible = false;
        AdministrationView.IsVisible = true;
        await LoadAdminUsersAsync();
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
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(new TextBlock
        {
            Text = $"{user.DisplayName ?? user.Email}  |  {string.Join(", ", user.Roles)}  |  {(user.IsActive ? "Active" : "Inactive")}",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        if (!user.IsActive)
        {
            var enableButton = new Button { Content = "Enable" };
            enableButton.Click += async (_, _) => await EnableUserAsync(user);
            row.Children.Add(enableButton);
        }
        return row;
    }

    private async Task EnableUserAsync(UserSummary user)
    {
        AdministrationStatus.Text = $"Enabling {user.DisplayName ?? user.Email}...";
        try
        {
            await MusicLibraryApi.EnableAdminUserAsync(user);
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