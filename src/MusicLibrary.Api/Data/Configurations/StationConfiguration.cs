using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MusicLibrary.Api.Data.Configurations;

public sealed class StationConfiguration : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> builder)
    {
        builder.HasIndex(station => station.DirectoryId).IsUnique();
        builder.HasIndex(station => station.IsProbeEnabled);
    }
}
