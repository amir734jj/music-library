using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MusicLibrary.Api.Data.Configurations;

public sealed class ArtistSubscriptionConfiguration : IEntityTypeConfiguration<ArtistSubscription>
{
    public void Configure(EntityTypeBuilder<ArtistSubscription> builder)
    {
        builder.HasIndex(subscription => new { subscription.UserId, subscription.NormalizedArtistName }).IsUnique();
    }
}
