using MusicLibrary.Contracts.Responses;
using Serilog;

namespace MusicLibrary.App.Services;

public static class AppLogging
{
    public static async Task<bool> ConfigureFromApiAsync(string platform, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(5));
            var settings = await MusicLibraryApi.GetClientLoggingConfigurationAsync(timeoutSource.Token);
            Configure(platform, settings);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Configure(string platform, ClientLoggingConfiguration settings)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("Application", "music-library-app")
            .Enrich.WithProperty("Platform", platform)
            .WriteTo.BetterStack(
                sourceToken: settings.SourceToken,
                betterStackEndpoint: settings.Endpoint)
            .CreateLogger();
    }
}