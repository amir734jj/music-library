namespace MusicLibrary.Contracts.Requests;

public sealed record UpdateGlobalConfigRequest(IReadOnlyDictionary<string, string> Values);