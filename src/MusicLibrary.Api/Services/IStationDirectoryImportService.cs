using MusicLibrary.Contracts.Responses;

namespace MusicLibrary.Api.Services;

public interface IStationDirectoryImportService
{
    Task<DirectoryImportSummary> ImportAsync(CancellationToken cancellationToken);
}