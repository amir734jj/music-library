using System.Text.Json;
using MusicLibrary.Api.Data;
using MusicLibrary.Contracts;
using Microsoft.EntityFrameworkCore;

namespace MusicLibrary.Api.Services;

public interface IStationDirectoryImportService
{
    Task<DirectoryImportSummary> ImportAsync(CancellationToken cancellationToken);
}

public sealed class StationDirectoryImportService(HttpClient httpClient, IGlobalConfigService configService, MusicLibraryDbContext dbContext) : IStationDirectoryImportService
{
    public async Task<DirectoryImportSummary> ImportAsync(CancellationToken cancellationToken)
    {
        var config = await configService.GetAsync(cancellationToken);
        if (!Uri.TryCreate(config.DirectoryArtifactUrl, UriKind.Absolute, out var directoryUri) || directoryUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("DIRECTORY_ARTIFACT_URL must be an absolute HTTPS URL.");
        }

        using var response = await httpClient.GetAsync(directoryUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var catalog = await JsonSerializer.DeserializeAsync<Dictionary<string, List<DirectoryStation>>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken) ?? [];
        var existing = await dbContext.Stations.ToDictionaryAsync(station => station.DirectoryId, cancellationToken);
        var created = 0;
        var updated = 0;
        var rejected = 0;

        foreach (var (genre, stations) in catalog)
        {
            foreach (var station in stations)
            {
                if (station.ID <= 0 || string.IsNullOrWhiteSpace(station.Name) || !IsSupportedStreamUrl(station.Url))
                {
                    rejected++;
                    continue;
                }

                if (!existing.TryGetValue(station.ID, out var entity))
                {
                    entity = new Station { Id = Guid.NewGuid(), DirectoryId = station.ID, Name = station.Name.Trim(), Genre = genre, StreamUrl = station.Url.Trim() };
                    dbContext.Stations.Add(entity);
                    existing.Add(station.ID, entity);
                    created++;
                }
                else
                {
                    entity.Name = station.Name.Trim();
                    entity.Genre = genre;
                    entity.StreamUrl = station.Url.Trim();
                    updated++;
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new DirectoryImportSummary(created, updated, rejected);
    }

    private static bool IsSupportedStreamUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private sealed class DirectoryStation
    {
        public long ID { get; init; }
        public string? Name { get; init; }
        public string? Url { get; init; }
    }
}