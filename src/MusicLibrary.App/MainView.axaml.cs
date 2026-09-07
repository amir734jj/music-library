using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MusicLibrary.App;

public sealed partial class MainView : UserControl
{
    public bool ShowAdministration { get; }
    public bool ShowNativeMedia => !ShowAdministration;

    public MainView(bool showAdministration = false)
    {
        ShowAdministration = showAdministration;
        InitializeComponent();
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

    private bool TryGetStreamUri(out Uri streamUri) => Uri.TryCreate(StreamUrlInput.Text?.Trim(), UriKind.Absolute, out streamUri)
        && (streamUri.Scheme == Uri.UriSchemeHttp || streamUri.Scheme == Uri.UriSchemeHttps);
}