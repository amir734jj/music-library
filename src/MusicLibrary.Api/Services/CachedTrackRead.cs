using MusicLibrary.Api.Data;

namespace MusicLibrary.Api.Services;

public sealed record CachedTrackRead(CachedTrack Track, byte[] Content, string ContentType);