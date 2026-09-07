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

    private async void Login_Click(object? sender, RoutedEventArgs eventArgs)
    {
        await AuthenticateAsync(register: false);
    }

    private async void Register_Click(object? sender, RoutedEventArgs eventArgs)
    {
        await AuthenticateAsync(register: true);
    }

    private async Task AuthenticateAsync(bool register)
    {
        AuthenticationStatus.Text = register ? "Creating account..." : "Signing in...";
        try
        {
            var email = AuthEmailInput.Text?.Trim() ?? string.Empty;
            var password = AuthPasswordInput.Text ?? string.Empty;
            var authentication = register
                ? await MusicLibraryApi.RegisterAsync(new RegisterRequest(email, password, AuthDisplayNameInput.Text?.Trim()))
                : await MusicLibraryApi.LoginAsync(new LoginRequest(email, password));
            ShowAuthenticatedApplication(authentication.User);
        }
        catch (Exception exception)
        {
            AuthenticationStatus.Text = exception.Message;
        }
    }

    private void ShowAuthenticatedApplication(UserSummary user)
    {
        AuthenticationStatus.Text = string.Empty;
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
        AuthPasswordInput.Text = string.Empty;
        ApplicationView.IsVisible = false;
        AuthenticationView.IsVisible = true;
        AuthenticationStatus.Text = string.Empty;
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
        AdministrationStatus.Text = "Loading users...";
        AdminUsersList.ItemsSource = null;
        try
        {
            var users = await MusicLibraryApi.GetAdminUsersAsync();
            AdminUsersList.ItemsSource = users.Select(user => $"{user.DisplayName ?? user.Email}  |  {string.Join(", ", user.Roles)}  |  {(user.IsActive ? "Active" : "Inactive")}");
            AdministrationStatus.Text = users.Count == 0 ? "No accounts found." : $"{users.Count} account(s)";
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