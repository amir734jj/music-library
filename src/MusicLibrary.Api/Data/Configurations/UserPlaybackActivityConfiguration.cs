using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MusicLibrary.Api.Data.Configurations;

public sealed class UserPlaybackActivityConfiguration : IEntityTypeConfiguration<UserPlaybackActivity>
{
    public void Configure(EntityTypeBuilder<UserPlaybackActivity> builder)
    {
        builder.HasKey(activity => activity.UserId);
        builder.Property(activity => activity.UserId).ValueGeneratedNever();
        builder.Property(activity => activity.PlaybackDescription).HasMaxLength(300);
        builder.HasIndex(activity => activity.LastHeartbeatAt);
        builder.HasOne(activity => activity.User)
            .WithOne()
            .HasForeignKey<UserPlaybackActivity>(activity => activity.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}