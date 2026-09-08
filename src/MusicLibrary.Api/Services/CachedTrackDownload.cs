namespace MusicLibrary.Api.Services;

public sealed record CachedTrackDownload(byte[] Content, string ContentType, string FileName);