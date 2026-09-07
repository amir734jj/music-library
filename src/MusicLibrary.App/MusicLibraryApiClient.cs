using MusicLibrary.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Refit;

namespace MusicLibrary.App;

public interface IMusicLibraryApiClient
{
    [Get("/api/health")]
    Task<IApiResponse> GetHealthAsync(CancellationToken cancellationToken = default);

    [Post("/api/auth/register")]
    Task<ApiResponse<AuthenticationResult>> RegisterAsync([Body] RegisterRequest request, CancellationToken cancellationToken = default);

    [Post("/api/auth/login")]
    Task<ApiResponse<AuthenticationResult>> LoginAsync([Body] LoginRequest request, CancellationToken cancellationToken = default);

    [Get("/api/auth/me")]
    Task<ApiResponse<UserSummary>> GetCurrentUserAsync([Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Get("/api/now-playing")]
    Task<ApiResponse<List<NowPlayingSummary>>> GetNowPlayingAsync([Query] string? query, [Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Get("/api/admin/users")]
    Task<ApiResponse<List<UserSummary>>> GetAdminUsersAsync([Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Get("/api/admin/probes/status")]
    Task<ApiResponse<ProbeStatusSummary>> GetAdminProbeStatusAsync([Query] string? query, [Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Get("/api/admin/config")]
    Task<ApiResponse<GlobalConfigModel>> GetAdminConfigAsync([Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Put("/api/admin/config")]
    Task<IApiResponse> SaveAdminConfigAsync([Body] UpdateGlobalConfigRequest request, [Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Post("/api/admin/stations/import")]
    Task<ApiResponse<DirectoryImportSummary>> ImportAdminStationsAsync([Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Put("/api/admin/stations/{id}/probe")]
    Task<IApiResponse> UpdateAdminStationProbeAsync(Guid id, [Body] UpdateStationProbeRequest request, [Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Put("/api/admin/stations/probe")]
    Task<ApiResponse<int>> UpdateAllAdminStationProbesAsync([Body] UpdateStationProbeRequest request, [Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Put("/api/admin/users/{id}")]
    Task<IApiResponse> UpdateAdminUserAsync(Guid id, [Body] UpdateUserRequest request, [Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Delete("/api/admin/users/{id}")]
    Task<IApiResponse> DeleteAdminUserAsync(Guid id, [Authorize] string accessToken, CancellationToken cancellationToken = default);
}

public static class MusicLibraryApi
{
    private static readonly Newtonsoft.Json.JsonSerializerSettings SerializerSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Converters = { new StringEnumConverter() }
    };
    private static Uri? _baseAddress;
    private static IMusicLibraryApiClient? _client;
    private static LoginAuthenticationResult? _authentication;

    public static event Action? SessionInvalidated;

    public static bool IsConfigured => _client is not null;
    public static bool IsAuthenticated => _authentication is not null && _authentication.ExpiresAt > DateTimeOffset.UtcNow;
    public static UserSummary? CurrentUser => IsAuthenticated ? _authentication!.User : null;
    public static DateTimeOffset? SessionExpiresAt => _authentication?.ExpiresAt;

    public static Uri BaseAddress => _baseAddress ?? throw new InvalidOperationException("The Music Library API endpoint has not been configured.");

    public static void Configure(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        var isSecure = baseAddress.Scheme == Uri.UriSchemeHttps;
        var isLocalDevelopment = baseAddress.Scheme == Uri.UriSchemeHttp && baseAddress.IsLoopback;
        if (!baseAddress.IsAbsoluteUri || (!isSecure && !isLocalDevelopment))
        {
            throw new ArgumentException("The Music Library API endpoint must use HTTPS, except on loopback addresses.", nameof(baseAddress));
        }

        _baseAddress = baseAddress.AbsoluteUri.EndsWith('/') ? baseAddress : new Uri($"{baseAddress.AbsoluteUri}/");
        var refitSettings = new RefitSettings(new NewtonsoftJsonContentSerializer(SerializerSettings));
        _client = RestService.ForGenerated<IMusicLibraryApiClient>(new HttpClient { BaseAddress = BaseAddress }, refitSettings);
    }

    public static async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetHealthAsync(cancellationToken);
        return response.IsSuccessful;
    }

    public static async Task<RegistrationAuthenticationResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await Client.RegisterAsync(request, cancellationToken);
        EnsureSuccess(response);
        return GetContent(response) as RegistrationAuthenticationResult
            ?? throw new HttpRequestException("The API returned an unexpected registration response.");
    }

    public static async Task<LoginAuthenticationResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await Client.LoginAsync(request, cancellationToken);
        EnsureSuccess(response, isLoginRequest: true);
        var result = GetContent(response) as LoginAuthenticationResult
            ?? throw new HttpRequestException("The API returned an unexpected login response.");
        _authentication = result;
        SaveAuthenticationSession();
        return result;
    }

    public static void SignOut()
    {
        _authentication = null;
        AuthenticationSessionStorage.Save?.Invoke(null);
    }

    public static async Task<UserSummary?> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        var serialized = AuthenticationSessionStorage.Load?.Invoke();
        if (string.IsNullOrWhiteSpace(serialized)) return null;

        try
        {
            var restored = JsonConvert.DeserializeObject<LoginAuthenticationResult>(serialized, SerializerSettings);
            if (restored is null || restored.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                SignOut();
                return null;
            }

            _authentication = restored;
            using var response = await Client.GetCurrentUserAsync(restored.AccessToken, cancellationToken);
            EnsureSuccess(response);
            var user = GetContent(response);
            _authentication = restored with { User = user };
            SaveAuthenticationSession();
            return user;
        }
        catch
        {
            SignOut();
            return null;
        }
    }

    public static async Task<IReadOnlyCollection<UserSummary>> GetAdminUsersAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        using var response = await Client.GetAdminUsersAsync(_authentication!.AccessToken, cancellationToken);
        EnsureSuccess(response);
        return GetContent(response);
    }

    public static async Task<IReadOnlyCollection<NowPlayingSummary>> GetNowPlayingAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        using var response = await Client.GetNowPlayingAsync(query, _authentication!.AccessToken, cancellationToken);
        EnsureSuccess(response);
        return GetContent(response);
    }

    public static async Task<ProbeStatusSummary> GetAdminProbeStatusAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        using var response = await Client.GetAdminProbeStatusAsync(query, _authentication!.AccessToken, cancellationToken);
        EnsureSuccess(response);
        return GetContent(response);
    }

    public static async Task<GlobalConfigModel> GetGlobalConfigAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        using var response = await Client.GetAdminConfigAsync(_authentication!.AccessToken, cancellationToken);
        EnsureSuccess(response);
        return GetContent(response);
    }

    public static async Task SaveGlobalConfigAsync(GlobalConfigModel config, CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        var values = new Dictionary<string, string>
        {
            ["DIRECTORY_ARTIFACT_URL"] = config.DirectoryArtifactUrl,
            ["PROBING_ENABLED"] = config.ProbingEnabled.ToString(),
            ["PROBE_CONCURRENCY"] = config.ProbeConcurrency.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["PROBE_TIMEOUT_SECONDS"] = config.ProbeTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["PROBE_BATCH_SIZE"] = config.ProbeBatchSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        using var response = await Client.SaveAdminConfigAsync(
            new UpdateGlobalConfigRequest(values),
            _authentication!.AccessToken,
            cancellationToken);
        EnsureSuccess(response);
    }

    public static async Task SetProbingEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        using var response = await Client.SaveAdminConfigAsync(
            new UpdateGlobalConfigRequest(new Dictionary<string, string> { ["PROBING_ENABLED"] = enabled.ToString() }),
            _authentication!.AccessToken,
            cancellationToken);
        EnsureSuccess(response);
    }

    public static async Task<DirectoryImportSummary> ImportStationsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        using var response = await Client.ImportAdminStationsAsync(_authentication!.AccessToken, cancellationToken);
        EnsureSuccess(response);
        return GetContent(response);
    }

    public static async Task SetStationProbingEnabledAsync(Guid stationId, bool enabled, CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        using var response = await Client.UpdateAdminStationProbeAsync(
            stationId,
            new UpdateStationProbeRequest(enabled),
            _authentication!.AccessToken,
            cancellationToken);
        EnsureSuccess(response);
    }

    public static async Task<int> SetAllStationProbingEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        using var response = await Client.UpdateAllAdminStationProbesAsync(
            new UpdateStationProbeRequest(enabled),
            _authentication!.AccessToken,
            cancellationToken);
        EnsureSuccess(response);
        return GetContent(response);
    }

    public static async Task UpdateAdminUserAsync(UserSummary user, bool isActive, string? role = null, CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        using var response = await Client.UpdateAdminUserAsync(
            user.Id,
            new UpdateUserRequest(user.DisplayName, isActive, role),
            _authentication!.AccessToken,
            cancellationToken);
        EnsureSuccess(response);
    }

    public static async Task DeleteAdminUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        using var response = await Client.DeleteAdminUserAsync(userId, _authentication!.AccessToken, cancellationToken);
        EnsureSuccess(response);
    }

    private static IMusicLibraryApiClient Client => _client ?? throw new InvalidOperationException("The Music Library API endpoint has not been configured.");

    private static void SaveAuthenticationSession()
    {
        if (_authentication is not null)
        {
            AuthenticationSessionStorage.Save?.Invoke(JsonConvert.SerializeObject(_authentication, SerializerSettings));
        }
    }

    private static T GetContent<T>(ApiResponse<T> response)
    {
        return response.Content
            ?? throw new HttpRequestException("The API returned an empty JSON response.");
    }

    private static void EnsureSuccess(IApiResponse response, bool isLoginRequest = false)
    {
        if (response.IsSuccessful) return;
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            if (!isLoginRequest)
            {
                SignOut();
                SessionInvalidated?.Invoke();
            }
            throw new HttpRequestException(isLoginRequest
                ? "The email or password is incorrect."
                : "Your session is no longer valid. Sign in again.");
        }

        var status = response.StatusCode is { } statusCode ? ((int)statusCode).ToString() : "unknown";
        var body = (response.Error as ApiException)?.Content;
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new HttpRequestException($"The API request failed with status {status}.", response.Error);
        }

        try
        {
            var document = JObject.Parse(body);
            if (document["errors"] is JObject errors)
            {
                var messages = errors.Properties()
                    .SelectMany(error => error.Value.Values<string>())
                    .Where(message => !string.IsNullOrWhiteSpace(message));
                throw new HttpRequestException(string.Join(" ", messages));
            }
            if (document.Value<string>("title") is { } title) throw new HttpRequestException(title);
        }
        catch (JsonReaderException)
        {
        }

        throw new HttpRequestException($"The API request failed with status {status}.", response.Error);
    }
}

public static class AuthenticationSessionStorage
{
    public static Func<string?>? Load { get; set; }
    public static Action<string?>? Save { get; set; }
}