using System.Net.Http.Headers;

namespace MusicLibrary.App;

public static class NativeRadioActions
{
    public static Func<Uri, Task>? ListenAsync { get; set; }
    public static Func<Uri, TimeSpan, Task<string>>? DownloadAsync { get; set; }
    public static Func<byte[], string, string, Task>? PlayFileAsync { get; set; }
    public static Func<byte[], string, string, CancellationToken, Task>? PlayFileToCompletionAsync { get; set; }
    public static Func<Task<int>>? ToggleFilePlaybackAsync { get; set; }
    public static Func<byte[], string, string, Task<string>>? SaveFileAsync { get; set; }
}

public static class NativeStreamDownloader
{
    private static readonly HttpClient HttpClient = new();

    public static async Task<string> DownloadAsync(Uri streamUri, string destinationDirectory, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        Directory.CreateDirectory(destinationDirectory);
        var filename = $"radio-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.mp3";
        var destinationPath = Path.Combine(destinationDirectory, filename);
        using var request = new HttpRequestMessage(HttpMethod.Get, streamUri);
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.IsNullOrWhiteSpace(mediaType) && !mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected URL did not return an audio stream.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destinationPath);
        using var timeLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeLimit.CancelAfter(duration);
        var buffer = new byte[64 * 1024];
        try
        {
            while (true)
            {
                var read = await input.ReadAsync(buffer, timeLimit.Token);
                if (read == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), timeLimit.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The configured duration completes a bounded live recording.
        }

        return destinationPath;
    }

    public static async Task<string> SaveAsync(
        byte[] content,
        string fileName,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(fileName));
        await File.WriteAllBytesAsync(destinationPath, content, cancellationToken);
        return destinationPath;
    }
}