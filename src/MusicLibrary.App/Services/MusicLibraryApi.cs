using MusicLibrary.Contracts.Requests;
using MusicLibrary.Contracts.Responses;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Refit;

namespace MusicLibrary.App.Services;

public static class MusicLibraryApi
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver
        {
            NamingStrategy = new CamelCaseNamingStrategy
            {
                ProcessDictionaryKeys = false,
                OverrideSpecifiedNames = true
            }
        },
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
        _client = RestService.ForGenerated<IMusicLibraryApiClient>(new HttpClient
        {
            BaseAddress = BaseAddress,
            Timeout = TimeSpan.FromMinutes(30)
        }, refitSettings);
    }

    public static async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetHealthAsync(cancellationToken);
        return response.IsSuccessful;
    }

    public static async Task<ClientLoggingConfiguration> GetClientLoggingConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetClientLoggingConfigurationAsync(cancellationToken);
        EnsureSuccess(response);
        return response.Content
            ?? throw new HttpRequestException("The API returned an unexpected client logging response.");
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
        using var response = await Client.GetNowPlayingAsync(query, cancellationToken);
        EnsureSuccess(response);
        return GetContent(response);
    }

    public static async Task<IReadOnlyCollection<StationSummary>> GetStationsAsync(
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetStationsAsync(query, cancellationToken);
        EnsureSuccess(response);
        return GetContent(response);
    }

    public static async Task<NowPlayingSummary?> GetNowPlayingStationAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetNowPlayingStationAsync(stationId, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        EnsureSuccess(response);
        return GetContent(response);
    }

    public static async Task<Uri> CreateLiveStreamUriAsync(Guid stationId, CancellationToken cancellationToken = default)
    {
        using var response = await Client.CreateLiveStreamTicketAsync(stationId, cancellationToken);
        EnsureSuccess(response);
        return new Uri(BaseAddress, GetContent(response).Path);
    }

    public static async Task<IReadOnlyCollection<TrendingSummary>> GetTrendingAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetTrendingAsync(query, cancellationToken);
        EnsureSuccess(response);
        return GetContent(response);
    }

    public static async Task<DownloadedTrack> DownloadTrendingTrackAsync(Guid cachedTrackId, CancellationToken cancellationToken = default)
    {
        using var response = await Client.DownloadTrendingTrackAsync(cachedTrackId, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            SignOut();
            SessionInvalidated?.Invoke();
            throw new HttpRequestException("Your session is no longer valid. Sign in again.");
        }
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("This cached recording has expired.");
        }
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? "radio-track.mp3";
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
        return new DownloadedTrack(content, contentType, fileName);
    }

    public static async Task<IReadOnlyCollection<UserPlaybackActivitySummary>> GetPlaybackActivitiesAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetPlaybackActivitiesAsync(cancellationToken);
        EnsureSuccess(response);
        return GetContent(response);
    }

    public static async Task UpdatePlaybackActivityAsync(
        string playbackDescription,
        bool isLiveStation,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        using var response = await Client.UpdatePlaybackActivityAsync(
            new UpdatePlaybackActivityRequest(playbackDescription, isLiveStation),
            _authentication!.AccessToken,
            cancellationToken);
        EnsureSuccess(response);
    }

    public static async Task ClearPlaybackActivityAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) return;
        using var response = await Client.ClearPlaybackActivityAsync(_authentication!.AccessToken, cancellationToken);
        EnsureSuccess(response);
    }

    public static async Task<IReadOnlyCollection<ArtistSubscriptionSummary>> GetSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        using var response = await Client.GetSubscriptionsAsync(_authentication!.AccessToken, cancellationToken);
        EnsureSuccess(response);
        return GetContent(response);
    }

    public static async Task<ArtistSubscriptionSummary?> CreateSubscriptionAsync(
        string artistName,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        using var response = await Client.CreateSubscriptionAsync(
            new CreateSubscriptionRequest(artistName, true),
            _authentication!.AccessToken,
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict) return null;
        EnsureSuccess(response);
        return GetContent(response);
    }

    public static async Task DeleteSubscriptionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        using var response = await Client.DeleteSubscriptionAsync(id, _authentication!.AccessToken, cancellationToken);
        EnsureSuccess(response);
    }

    public static async Task<ProbeStatusSummary> GetAdminProbeStatusAsync(
        string? query = null,
        int page = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        using var response = await Client.GetAdminProbeStatusAsync(query, page, pageSize, _authentication!.AccessToken, cancellationToken);
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

    public static async Task<TrendingCacheStatusSummary> GetTrendingCacheStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        using var response = await Client.GetAdminCacheStatusAsync(_authentication!.AccessToken, cancellationToken);
        EnsureSuccess(response);
        return GetContent(response);
    }

    public static async Task ClearTrendingCacheAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated) throw new InvalidOperationException("An authenticated session is required.");
        using var response = await Client.ClearAdminCacheAsync(_authentication!.AccessToken, cancellationToken);
        EnsureSuccess(response);
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
            ["PROBE_BATCH_SIZE"] = config.ProbeBatchSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["TRENDING_CACHE_ENCRYPTION_KEY"] = config.TrendingCacheEncryptionKey,
            ["TRENDING_CACHE_CAPTURE_TIMEOUT_SECONDS"] = config.TrendingCacheCaptureTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["TRENDING_MINIMUM_DURATION_SECONDS"] = config.TrendingMinimumDurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["TRENDING_CACHE_RETENTION_HOURS"] = config.TrendingCacheRetentionHours.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["TRENDING_CACHE_MAX_SIZE_MEGABYTES"] = config.TrendingCacheMaxSizeMegabytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
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