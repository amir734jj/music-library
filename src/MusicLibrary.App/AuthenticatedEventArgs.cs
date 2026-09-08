using MusicLibrary.Contracts.Responses;

namespace MusicLibrary.App;

public sealed class AuthenticatedEventArgs(UserSummary user) : EventArgs
{
    public UserSummary User { get; } = user;
}