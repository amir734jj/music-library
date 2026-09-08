using System.ComponentModel.DataAnnotations;

namespace MusicLibrary.Contracts.Requests;

public sealed record UpdatePlaybackActivityRequest(
    [param: Required, MaxLength(300)] string PlaybackDescription,
    bool IsLiveStation);