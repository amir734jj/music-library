using System.Text.Json;
using MusicLibrary.Api.Data;
using MusicLibrary.Contracts;
using EfCoreRepository.Interfaces;

namespace MusicLibrary.Api.Services;

public interface IStationDirectoryImportService
{
    Task<DirectoryImportSummary> ImportAsync(CancellationToken cancellationToken);
}

public sealed class StationDirectoryImportService(HttpClient httpClient, IGlobalConfigService configService, IEfRepository repository) : IStationDirectoryImportService
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
        var stations = repository.For<Station>();
        var existing = (await stations.GetAll()).ToDictionary(station => station.DirectoryId);
        var created = 0;
        var updated = 0;
        var rejected = 0;

        foreach (var (genre, list) in catalog)
        {
            foreach (var station in list)
            {
                if (station.ID <= 0 || string.IsNullOrWhiteSpace(station.Name) || !IsSupportedStreamUrl(station.Url))
                {
                    rejected++;
                    continue;
                }

                if (!existing.TryGetValue(station.ID, out var entity))
                {
                    var saved = await stations.Save(new Station { Id = Guid.NewGuid(), DirectoryId = station.ID, Name = station.Name!.Trim(), Genre = genre, StreamUrl = station.Url!.Trim() });
                    existing.Add(station.ID, saved);
                    created++;
                }
                else
                {
                    await stations.Update(entity.Id, tracked =>
                    {
                        tracked.Name = station.Name!.Trim();
                        tracked.Genre = genre;
                        tracked.StreamUrl = station.Url!.Trim();
                    });
                    updated++;
                }
            }
        }

        return new DirectoryImportSummary(created, updated, rejected);
    }

    private static bool IsSupportedStreamUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private sealed class DirectoryStation
    {
        public long ID { get; init; }
        public string? Name { get; init; }
        public string? Url { get; init; }
    }
}