using Microsoft.AspNetCore.Identity;

namespace MusicLibrary.Api.Data;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
}