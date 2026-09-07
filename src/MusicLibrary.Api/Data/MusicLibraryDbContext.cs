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
        builder.ApplyConfigurationsFromAssembly(typeof(MusicLibraryDbContext).Assembly);
    }
}