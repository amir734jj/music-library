using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MusicLibrary.Api.Data.Configurations;

public sealed class UserAlertConfiguration : IEntityTypeConfiguration<UserAlert>
{
    public void Configure(EntityTypeBuilder<UserAlert> builder)
    {
        builder.HasIndex(alert => new { alert.ArtistSubscriptionId, alert.PlayObservationId }).IsUnique();
    }
}
