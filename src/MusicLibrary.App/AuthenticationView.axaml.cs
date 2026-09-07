using Avalonia.Controls;
using Avalonia.Interactivity;
using MusicLibrary.Contracts;

namespace MusicLibrary.App;

public sealed partial class AuthenticationView : UserControl
{
    private bool _isRegistrationMode;

    public AuthenticationView()
    {
        InitializeComponent();
    }

    public event EventHandler<AuthenticatedEventArgs>? Authenticated;

    public void Reset()
    {
        AuthPasswordInput.Text = string.Empty;
        SetAuthenticationMode(false);
    }

    private async void AuthenticationSubmit_Click(object? sender, RoutedEventArgs eventArgs)
    {
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
    }

    private void AuthenticationMode_Click(object? sender, RoutedEventArgs eventArgs)
    {
        SetAuthenticationMode(!_isRegistrationMode);
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

public sealed class AuthenticatedEventArgs(UserSummary user) : EventArgs
{
    public UserSummary User { get; } = user;
}