namespace MusicLibrary.Contracts.Requests;

public sealed record RegisterRequest(string Email, string Password, string PasswordConfirmation, string? DisplayName);