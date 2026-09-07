using FluentMigrator;

namespace MusicLibrary.Api.Migrations;

[Migration(202609070001)]
public sealed class InitialDatabase : Migration
{
    public override void Up()
    {
        var expectedTables = new[]
        {
            "AspNetRoles", "AspNetUsers", "AspNetRoleClaims", "AspNetUserClaims", "AspNetUserLogins", "AspNetUserRoles",
            "AspNetUserTokens", "GlobalConfigRows", "Stations", "PlayObservations", "ArtistSubscriptions", "UserAlerts"
        };
        var existingTables = expectedTables.Where(table => Schema.Table(table).Exists()).ToArray();
        if (existingTables.Length == expectedTables.Length) return;
        if (existingTables.Length > 0)
        {
            throw new InvalidOperationException($"Cannot apply the initial migration to a partial schema. Existing tables: {string.Join(", ", existingTables)}.");
        }

        Create.Table("AspNetRoles")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("Name").AsString(256).Nullable()
            .WithColumn("NormalizedName").AsString(256).Nullable()
            .WithColumn("ConcurrencyStamp").AsString().Nullable();

        Create.Table("AspNetUsers")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("DisplayName").AsString().Nullable()
            .WithColumn("IsActive").AsBoolean().NotNullable()
            .WithColumn("LastLoginAt").AsCustom("timestamp with time zone").Nullable()
            .WithColumn("UserName").AsString(256).Nullable()
            .WithColumn("NormalizedUserName").AsString(256).Nullable()
            .WithColumn("Email").AsString(256).Nullable()
            .WithColumn("NormalizedEmail").AsString(256).Nullable()
            .WithColumn("EmailConfirmed").AsBoolean().NotNullable()
            .WithColumn("PasswordHash").AsString().Nullable()
            .WithColumn("SecurityStamp").AsString().Nullable()
            .WithColumn("ConcurrencyStamp").AsString().Nullable()
            .WithColumn("PhoneNumber").AsString().Nullable()
            .WithColumn("PhoneNumberConfirmed").AsBoolean().NotNullable()
            .WithColumn("TwoFactorEnabled").AsBoolean().NotNullable()
            .WithColumn("LockoutEnd").AsCustom("timestamp with time zone").Nullable()
            .WithColumn("LockoutEnabled").AsBoolean().NotNullable()
            .WithColumn("AccessFailedCount").AsInt32().NotNullable();

        Create.Table("AspNetRoleClaims")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("RoleId").AsGuid().NotNullable().ForeignKey("AspNetRoles", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("ClaimType").AsString().Nullable()
            .WithColumn("ClaimValue").AsString().Nullable();

        Create.Table("AspNetUserClaims")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("UserId").AsGuid().NotNullable().ForeignKey("AspNetUsers", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("ClaimType").AsString().Nullable()
            .WithColumn("ClaimValue").AsString().Nullable();

        Create.Table("AspNetUserLogins")
            .WithColumn("LoginProvider").AsString(128).NotNullable()
            .WithColumn("ProviderKey").AsString(128).NotNullable()
            .WithColumn("ProviderDisplayName").AsString().Nullable()
            .WithColumn("UserId").AsGuid().NotNullable().ForeignKey("AspNetUsers", "Id").OnDelete(System.Data.Rule.Cascade);
        Create.PrimaryKey("PK_AspNetUserLogins").OnTable("AspNetUserLogins").Columns("LoginProvider", "ProviderKey");

        Create.Table("AspNetUserRoles")
            .WithColumn("UserId").AsGuid().NotNullable().ForeignKey("AspNetUsers", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("RoleId").AsGuid().NotNullable().ForeignKey("AspNetRoles", "Id").OnDelete(System.Data.Rule.Cascade);
        Create.PrimaryKey("PK_AspNetUserRoles").OnTable("AspNetUserRoles").Columns("UserId", "RoleId");

        Create.Table("AspNetUserTokens")
            .WithColumn("UserId").AsGuid().NotNullable().ForeignKey("AspNetUsers", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("LoginProvider").AsString(128).NotNullable()
            .WithColumn("Name").AsString(128).NotNullable()
            .WithColumn("Value").AsString().Nullable();
        Create.PrimaryKey("PK_AspNetUserTokens").OnTable("AspNetUserTokens").Columns("UserId", "LoginProvider", "Name");

        Create.Table("GlobalConfigRows")
            .WithColumn("Key").AsString(128).PrimaryKey()
            .WithColumn("Value").AsString(2048).NotNullable()
            .WithColumn("UpdatedAt").AsCustom("timestamp with time zone").NotNullable()
            .WithColumn("UpdatedByUserId").AsGuid().Nullable();

        Create.Table("Stations")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("DirectoryId").AsInt64().NotNullable()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("Genre").AsString().NotNullable()
            .WithColumn("StreamUrl").AsString().NotNullable()
            .WithColumn("IsProbeEnabled").AsBoolean().NotNullable()
            .WithColumn("LastProbedAt").AsCustom("timestamp with time zone").Nullable()
            .WithColumn("LastMetadataAt").AsCustom("timestamp with time zone").Nullable()
            .WithColumn("ConsecutiveProbeFailures").AsInt32().NotNullable();

        Create.Table("PlayObservations")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("StationId").AsGuid().NotNullable().ForeignKey("Stations", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("RawMetadata").AsString().NotNullable()
            .WithColumn("Artist").AsString().Nullable()
            .WithColumn("Title").AsString().Nullable()
            .WithColumn("Confidence").AsDecimal(4, 3).NotNullable()
            .WithColumn("ObservedAt").AsCustom("timestamp with time zone").NotNullable();

        Create.Table("ArtistSubscriptions")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("UserId").AsGuid().NotNullable().ForeignKey("AspNetUsers", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("ArtistName").AsString().NotNullable()
            .WithColumn("NormalizedArtistName").AsString().NotNullable()
            .WithColumn("CaptureEnabled").AsBoolean().NotNullable()
            .WithColumn("CreatedAt").AsCustom("timestamp with time zone").NotNullable();

        Create.Table("UserAlerts")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("UserId").AsGuid().NotNullable().ForeignKey("AspNetUsers", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("ArtistSubscriptionId").AsGuid().NotNullable().ForeignKey("ArtistSubscriptions", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("PlayObservationId").AsGuid().NotNullable().ForeignKey("PlayObservations", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("CreatedAt").AsCustom("timestamp with time zone").NotNullable();

        Create.Index("RoleNameIndex").OnTable("AspNetRoles").OnColumn("NormalizedName").Ascending().WithOptions().Unique();
        Create.Index("EmailIndex").OnTable("AspNetUsers").OnColumn("NormalizedEmail").Ascending();
        Create.Index("UserNameIndex").OnTable("AspNetUsers").OnColumn("NormalizedUserName").Ascending().WithOptions().Unique();
        Create.Index("IX_AspNetRoleClaims_RoleId").OnTable("AspNetRoleClaims").OnColumn("RoleId").Ascending();
        Create.Index("IX_AspNetUserClaims_UserId").OnTable("AspNetUserClaims").OnColumn("UserId").Ascending();
        Create.Index("IX_AspNetUserLogins_UserId").OnTable("AspNetUserLogins").OnColumn("UserId").Ascending();
        Create.Index("IX_AspNetUserRoles_RoleId").OnTable("AspNetUserRoles").OnColumn("RoleId").Ascending();
        Create.Index("IX_Stations_DirectoryId").OnTable("Stations").OnColumn("DirectoryId").Ascending().WithOptions().Unique();
        Create.Index("IX_Stations_IsProbeEnabled").OnTable("Stations").OnColumn("IsProbeEnabled").Ascending();
        Create.Index("IX_PlayObservations_Artist_ObservedAt").OnTable("PlayObservations").OnColumn("Artist").Ascending().OnColumn("ObservedAt").Ascending();
        Create.Index("IX_PlayObservations_StationId").OnTable("PlayObservations").OnColumn("StationId").Ascending();
        Create.Index("IX_ArtistSubscriptions_UserId_NormalizedArtistName").OnTable("ArtistSubscriptions").OnColumn("UserId").Ascending().OnColumn("NormalizedArtistName").Ascending().WithOptions().Unique();
        Create.Index("IX_UserAlerts_ArtistSubscriptionId_PlayObservationId").OnTable("UserAlerts").OnColumn("ArtistSubscriptionId").Ascending().OnColumn("PlayObservationId").Ascending().WithOptions().Unique();
        Create.Index("IX_UserAlerts_PlayObservationId").OnTable("UserAlerts").OnColumn("PlayObservationId").Ascending();
        Create.Index("IX_UserAlerts_UserId").OnTable("UserAlerts").OnColumn("UserId").Ascending();
    }

    public override void Down()
    {
        Delete.Table("UserAlerts");
        Delete.Table("ArtistSubscriptions");
        Delete.Table("PlayObservations");
        Delete.Table("Stations");
        Delete.Table("GlobalConfigRows");
        Delete.Table("AspNetUserTokens");
        Delete.Table("AspNetUserRoles");
        Delete.Table("AspNetUserLogins");
        Delete.Table("AspNetUserClaims");
        Delete.Table("AspNetRoleClaims");
        Delete.Table("AspNetUsers");
        Delete.Table("AspNetRoles");
    }
}