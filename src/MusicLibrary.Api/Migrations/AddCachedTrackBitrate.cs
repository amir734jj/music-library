using FluentMigrator;

namespace MusicLibrary.Api.Migrations;

[Migration(202609070006)]
public sealed class AddCachedTrackBitrate : Migration
{
    public override void Up()
    {
        Alter.Table("CachedTracks")
            .AddColumn("BitrateKbps").AsInt32().Nullable();
    }

    public override void Down()
    {
        Delete.Column("BitrateKbps").FromTable("CachedTracks");
    }
}