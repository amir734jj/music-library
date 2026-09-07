namespace MusicLibrary.App;

public static class MusicLibraryApi
{
    private static Uri? _baseAddress;

    public static bool IsConfigured => _baseAddress is not null;

    public static Uri BaseAddress => _baseAddress ?? throw new InvalidOperationException("The Music Library API endpoint has not been configured.");

    public static void Configure(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        if (!baseAddress.IsAbsoluteUri || baseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The Music Library API endpoint must be an absolute HTTPS URL.", nameof(baseAddress));
        }

        _baseAddress = baseAddress.AbsoluteUri.EndsWith('/') ? baseAddress : new Uri($"{baseAddress.AbsoluteUri}/");
    }

    public static HttpClient CreateClient() => new() { BaseAddress = BaseAddress };

    public static async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("api/health", cancellationToken);
        return response.IsSuccessStatusCode;
    }
}