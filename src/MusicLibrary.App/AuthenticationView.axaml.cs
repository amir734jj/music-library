using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MusicLibrary.App.Services;
using MusicLibrary.Contracts.Requests;

namespace MusicLibrary.App;

public sealed partial class AuthenticationView : UserControl
{
    private bool _isRegistrationMode;

    public AuthenticationView()
    {
        InitializeComponent();
        OfflineButton.IsVisible = NativeRadioActions.ListOfflineTracksAsync is not null;
    }

    public event EventHandler<AuthenticatedEventArgs>? Authenticated;
    public event EventHandler? OfflineRequested;

    public void Reset()
    {
        AuthPasswordInput.Text = string.Empty;
        SetAuthenticationMode(false);
    }

    private void AuthenticationSubmit_Click(object? sender, RoutedEventArgs eventArgs)
    {
        _ = AuthenticationSubmitAsync();
    }

    private void AuthenticationView_KeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Enter || !AuthenticationSubmitButton.IsEnabled) return;
        eventArgs.Handled = true;
        _ = AuthenticationSubmitAsync();
    }

    private async Task AuthenticationSubmitAsync()
    {
        AuthenticationSubmitButton.IsEnabled = false;
        AuthenticationModeButton.IsEnabled = false;
        AuthenticationStatus.Text = _isRegistrationMode ? "Creating account..." : "Signing in...";
        try
        {
            var email = AuthEmailInput.Text?.Trim() ?? string.Empty;
            var password = AuthPasswordInput.Text ?? string.Empty;
            if (_isRegistrationMode)
            {
                var passwordConfirmation = AuthPasswordConfirmationInput.Text ?? string.Empty;
                if (password != passwordConfirmation) throw new InvalidOperationException("Passwords do not match.");

                var registration = await MusicLibraryApi.RegisterAsync(
                    new RegisterRequest(email, password, passwordConfirmation, AuthDisplayNameInput.Text?.Trim()));
                AuthPasswordInput.Text = string.Empty;
                AuthPasswordConfirmationInput.Text = string.Empty;
                AuthDisplayNameInput.Text = string.Empty;
                AuthPasswordInput.Focus();
                SetAuthenticationMode(false, registration.User.IsActive
                    ? "Account created. Sign in to continue."
                    : "Account created. An administrator must enable it before you can sign in.");
                return;
            }

            var authentication = await MusicLibraryApi.LoginAsync(new LoginRequest(email, password));
            AuthenticationStatus.Text = string.Empty;
            Authenticated?.Invoke(this, new AuthenticatedEventArgs(authentication.User));
        }
        catch (Exception exception)
        {
            AuthenticationStatus.Text = exception.Message;
        }
        finally
        {
            AuthenticationSubmitButton.IsEnabled = true;
            AuthenticationModeButton.IsEnabled = true;
        }
    }

    private void AuthenticationMode_Click(object? sender, RoutedEventArgs eventArgs)
    {
        SetAuthenticationMode(!_isRegistrationMode);
    }

    private void Offline_Click(object? sender, RoutedEventArgs eventArgs)
    {
        OfflineRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetAuthenticationMode(bool registration, string? status = null)
    {
        _isRegistrationMode = registration;
        AuthenticationSubtitle.Text = registration ? "Create an account" : "Sign in to your account";
        AuthDisplayNameInput.IsVisible = registration;
        AuthPasswordConfirmationInput.IsVisible = registration;
        AuthenticationSubmitButton.Content = registration ? "Register" : "Sign in";
        AuthenticationModeButton.Content = registration ? "Back to sign in" : "Register";
        AuthenticationStatus.Text = status ?? string.Empty;
        if (!registration)
        {
            AuthDisplayNameInput.Text = string.Empty;
            AuthPasswordConfirmationInput.Text = string.Empty;
        }
    }
}