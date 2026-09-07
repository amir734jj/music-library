using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MusicLibrary.Api.Data;

public sealed class MusicLibraryDbContext(DbContextOptions<MusicLibraryDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public DbSet<GlobalConfigRow> GlobalConfigRows => Set<GlobalConfigRow>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<PlayObservation> PlayObservations => Set<PlayObservation>();
    public DbSet<ArtistSubscription> ArtistSubscriptions => Set<ArtistSubscription>();
    public DbSet<UserAlert> UserAlerts => Set<UserAlert>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<GlobalConfigRow>().HasKey(row => row.Key);
        builder.Entity<GlobalConfigRow>().Property(row => row.Key).HasMaxLength(128);
        builder.Entity<GlobalConfigRow>().Property(row => row.Value).HasMaxLength(2048);
        builder.Entity<Station>().HasIndex(station => station.DirectoryId).IsUnique();
        builder.Entity<Station>().HasIndex(station => station.IsProbeEnabled);
        builder.Entity<PlayObservation>().HasIndex(play => new { play.Artist, play.ObservedAt });
        builder.Entity<PlayObservation>().Property(play => play.Confidence).HasPrecision(4, 3);
        builder.Entity<ArtistSubscription>().HasIndex(subscription => new { subscription.UserId, subscription.NormalizedArtistName }).IsUnique();
        builder.Entity<UserAlert>().HasIndex(alert => new { alert.ArtistSubscriptionId, alert.PlayObservationId }).IsUnique();
    }
}