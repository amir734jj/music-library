using FluentMigrator;

namespace MusicLibrary.Api.Migrations;

[Migration(202609070005)]
public sealed class AddEncryptedTrendingCache : Migration
{
    public override void Up()
    {
        Create.Table("CachedTracks")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("PlayObservationId").AsGuid().NotNullable().ForeignKey("PlayObservations", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("Artist").AsString().NotNullable()
            .WithColumn("Title").AsString().Nullable()
            .WithColumn("NormalizedArtist").AsString().NotNullable()
            .WithColumn("NormalizedTitle").AsString().NotNullable()
            .WithColumn("FilePath").AsString().NotNullable()
            .WithColumn("ContentType").AsString(128).NotNullable()
            .WithColumn("PlaintextLength").AsInt64().NotNullable()
            .WithColumn("KeyFingerprint").AsString(64).NotNullable()
            .WithColumn("CreatedAt").AsCustom("timestamp with time zone").NotNullable()
            .WithColumn("ExpiresAt").AsCustom("timestamp with time zone").NotNullable();

        Create.Index("IX_CachedTracks_PlayObservationId").OnTable("CachedTracks").OnColumn("PlayObservationId").Ascending().WithOptions().Unique();
        Create.Index("IX_CachedTracks_ExpiresAt").OnTable("CachedTracks").OnColumn("ExpiresAt").Ascending();
        Create.Index("IX_CachedTracks_NormalizedArtist_NormalizedTitle_ExpiresAt").OnTable("CachedTracks")
            .OnColumn("NormalizedArtist").Ascending()
            .OnColumn("NormalizedTitle").Ascending()
            .OnColumn("ExpiresAt").Ascending();
    }

    public override void Down()
    {
        Delete.Table("CachedTracks");
    }
}