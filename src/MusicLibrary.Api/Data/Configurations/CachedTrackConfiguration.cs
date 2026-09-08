using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MusicLibrary.Api.Data.Configurations;

public sealed class CachedTrackConfiguration : IEntityTypeConfiguration<CachedTrack>
{
    public void Configure(EntityTypeBuilder<CachedTrack> builder)
    {
        builder.HasIndex(track => track.PlayObservationId).IsUnique();
        builder.HasIndex(track => track.ExpiresAt);
        builder.HasIndex(track => new { track.NormalizedArtist, track.NormalizedTitle, track.ExpiresAt });
    }
}