namespace MusicLibrary.Api.Services;

public sealed record TrackCaptureRequest(
    Guid PlayObservationId,
    Uri StreamUri,
    string Artist,
    string? Title);