namespace MusicLibrary.App.Services;

public static class NativeStreamDownloader
{
    private static readonly HttpClient HttpClient = new();

    public static async Task<string> DownloadAsync(Uri streamUri, string destinationDirectory, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        Directory.CreateDirectory(destinationDirectory);
        var filename = $"radio-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.mp3";
        var temporaryPath = Path.Combine(destinationDirectory, $".{Guid.NewGuid():N}.download");
        using var request = new HttpRequestMessage(HttpMethod.Get, streamUri);
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.IsNullOrWhiteSpace(mediaType) && !mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected URL did not return an audio stream.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            await using (var output = File.Create(temporaryPath))
            {
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
            }

            var destinationPath = GetAvailableDestinationPath(destinationDirectory, filename);
            File.Move(temporaryPath, destinationPath);
            return destinationPath;
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public static async Task<string> SaveAsync(
        byte[] content,
        string fileName,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName)) safeFileName = "radio-track.mp3";
        var temporaryPath = Path.Combine(destinationDirectory, $".{Guid.NewGuid():N}.download");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken);
            var destinationPath = GetAvailableDestinationPath(destinationDirectory, safeFileName);
            File.Move(temporaryPath, destinationPath);
            return destinationPath;
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string GetAvailableDestinationPath(string directory, string fileName)
    {
        var destinationPath = Path.Combine(directory, fileName);
        if (!File.Exists(destinationPath)) return destinationPath;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 2; ; suffix++)
        {
            destinationPath = Path.Combine(directory, $"{name} ({suffix}){extension}");
            if (!File.Exists(destinationPath)) return destinationPath;
        }
    }
}