using MusicLibrary.Contracts.Requests;
using MusicLibrary.Contracts.Responses;
using Refit;

namespace MusicLibrary.App.Services;

public interface IMusicLibraryApiClient
{
    [Get("/api/health")]
    Task<IApiResponse> GetHealthAsync(CancellationToken cancellationToken = default);

    [Get("/api/client-logging")]
    Task<ApiResponse<ClientLoggingConfiguration>> GetClientLoggingConfigurationAsync(CancellationToken cancellationToken = default);

    [Post("/api/auth/register")]
    Task<ApiResponse<AuthenticationResult>> RegisterAsync([Body] RegisterRequest request, CancellationToken cancellationToken = default);

    [Post("/api/auth/login")]
    Task<ApiResponse<AuthenticationResult>> LoginAsync([Body] LoginRequest request, CancellationToken cancellationToken = default);

    [Get("/api/auth/me")]
    Task<ApiResponse<UserSummary>> GetCurrentUserAsync([Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Get("/api/now-playing")]
    Task<ApiResponse<List<NowPlayingSummary>>> GetNowPlayingAsync([Query] string? query, [Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Get("/api/stations")]
    Task<ApiResponse<List<StationSummary>>> GetStationsAsync([Query] string? query, [Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Get("/api/now-playing/{stationId}")]
    Task<ApiResponse<NowPlayingSummary>> GetNowPlayingStationAsync(Guid stationId, [Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Post("/api/now-playing/{stationId}/stream-ticket")]
    Task<ApiResponse<LiveStreamTicket>> CreateLiveStreamTicketAsync(Guid stationId, [Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Get("/api/trending")]
    Task<ApiResponse<List<TrendingSummary>>> GetTrendingAsync([Query] string? query, [Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Get("/api/trending/{cachedTrackId}/download")]
    Task<HttpResponseMessage> DownloadTrendingTrackAsync(Guid cachedTrackId, [Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Get("/api/playback-activity")]
    Task<ApiResponse<List<UserPlaybackActivitySummary>>> GetPlaybackActivitiesAsync([Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Put("/api/playback-activity")]
    Task<IApiResponse> UpdatePlaybackActivityAsync([Body] UpdatePlaybackActivityRequest request, [Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Delete("/api/playback-activity")]
    Task<IApiResponse> ClearPlaybackActivityAsync([Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Get("/api/subscriptions")]
    Task<ApiResponse<List<ArtistSubscriptionSummary>>> GetSubscriptionsAsync([Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Post("/api/subscriptions")]
    Task<ApiResponse<ArtistSubscriptionSummary>> CreateSubscriptionAsync([Body] CreateSubscriptionRequest request, [Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Delete("/api/subscriptions/{id}")]
    Task<IApiResponse> DeleteSubscriptionAsync(Guid id, [Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Get("/api/admin/users")]
    Task<ApiResponse<List<UserSummary>>> GetAdminUsersAsync([Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Get("/api/admin/probes/status")]
    Task<ApiResponse<ProbeStatusSummary>> GetAdminProbeStatusAsync(
        [Query] string? query,
        [Query] int page,
        [Query] int pageSize,
        [Authorize] string accessToken,
        CancellationToken cancellationToken = default);

    [Get("/api/admin/config")]
    Task<ApiResponse<GlobalConfigModel>> GetAdminConfigAsync([Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Get("/api/admin/cache/status")]
    Task<ApiResponse<TrendingCacheStatusSummary>> GetAdminCacheStatusAsync([Authorize] string accessToken, CancellationToken cancellationToken = default);

    [Delete("/api/admin/cache")]
    Task<IApiResponse> ClearAdminCacheAsync([Authorize] string accessToken, CancellationToken cancellationToken = default);

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