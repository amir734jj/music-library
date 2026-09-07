using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MusicLibrary.Api.Data.Configurations;

public sealed class PlayObservationConfiguration : IEntityTypeConfiguration<PlayObservation>
{
    public void Configure(EntityTypeBuilder<PlayObservation> builder)
    {
        builder.HasIndex(play => new { play.Artist, play.ObservedAt });
        builder.Property(play => play.Confidence).HasPrecision(4, 3);
    }
}
