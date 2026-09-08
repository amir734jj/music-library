using System.ComponentModel.DataAnnotations;

namespace MusicLibrary.Api.Data;

public sealed class UserPlaybackActivity
{
    [Key]
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public string PlaybackDescription { get; set; } = string.Empty;
    public bool IsLiveStation { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastHeartbeatAt { get; set; }
}