namespace MusicLibrary.Contracts.Requests;

public sealed record UpdateUserRequest(string? DisplayName, bool IsActive, string? Role);