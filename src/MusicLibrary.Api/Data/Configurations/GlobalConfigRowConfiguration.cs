using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MusicLibrary.Api.Data.Configurations;

public sealed class GlobalConfigRowConfiguration : IEntityTypeConfiguration<GlobalConfigRow>
{
    public void Configure(EntityTypeBuilder<GlobalConfigRow> builder)
    {
        builder.HasKey(row => row.Key);
        builder.Property(row => row.Key).HasMaxLength(128);
        builder.Property(row => row.Value).HasMaxLength(2048);
    }
}
