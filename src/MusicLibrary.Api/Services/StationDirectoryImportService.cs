using MusicLibrary.Api.Data;
using MusicLibrary.Contracts;
using EfCoreRepository.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MusicLibrary.Api.Services;

public interface IStationDirectoryImportService
{
    Task<DirectoryImportSummary> ImportAsync(CancellationToken cancellationToken);
}

public sealed class StationDirectoryImportService(
    HttpClient httpClient,
    IGlobalConfigService configService,
    IEfRepository repository) : IStationDirectoryImportService
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
        using var streamReader = new StreamReader(stream);
        using var jsonReader = new JsonTextReader(streamReader);
        var serializer = JsonSerializer.CreateDefault(new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() });
        var catalog = serializer.Deserialize<Dictionary<string, List<DirectoryStation>>>(jsonReader) ?? [];
        await using var stations = repository.For<Station>().Delayed();
        var existing = (await stations.GetAll()).ToDictionary(station => station.DirectoryId);
        var pendingUpdates = new Dictionary<Guid, DirectoryStation>();
        var pendingCreates = new List<Station>();
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
                    entity = new Station
                    {
                        Id = Guid.NewGuid(),
                        DirectoryId = station.ID,
                        Name = station.Name!.Trim(),
                        Genre = genre,
                        StreamUrl = station.Url!.Trim(),
                        IsProbeEnabled = true
                    };
                    pendingCreates.Add(entity);
                    existing.Add(station.ID, entity);
                    created++;
                }
                else
                {
                    pendingUpdates[entity.Id] = new DirectoryStation
                    {
                        ID = station.ID,
                        Name = station.Name!.Trim(),
                        Url = station.Url!.Trim(),
                        Genre = genre
                    };
                    updated++;
                }
            }
        }

        if (pendingCreates.Count > 0)
        {
            await stations.SaveMany([.. pendingCreates]);
        }
        if (pendingUpdates.Count > 0)
        {
            await stations.BulkUpdate([.. pendingUpdates.Keys], entity =>
            {
                var update = pendingUpdates[entity.Id];
                entity.Name = update.Name!;
                entity.Genre = update.Genre!;
                entity.StreamUrl = update.Url!;
            });
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
        public string? Genre { get; init; }
    }
}