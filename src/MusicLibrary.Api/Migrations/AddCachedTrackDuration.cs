using FluentMigrator;

namespace MusicLibrary.Api.Migrations;

[Migration(202609080008)]
public sealed class AddCachedTrackDuration : Migration
{
    public override void Up()
    {
        Alter.Table("CachedTracks")
            .AddColumn("DurationMs").AsInt32().Nullable();
    }

    public override void Down()
    {
        Delete.Column("DurationMs").FromTable("CachedTracks");
    }
}