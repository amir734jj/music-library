namespace MusicLibrary.Contracts.Responses;

public sealed record UserSummary(Guid Id, string Email, string? DisplayName, IReadOnlyCollection<string> Roles, bool IsActive);