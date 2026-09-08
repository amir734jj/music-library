namespace MusicLibrary.App.Services;

public sealed record DownloadedTrack(byte[] Content, string ContentType, string FileName);