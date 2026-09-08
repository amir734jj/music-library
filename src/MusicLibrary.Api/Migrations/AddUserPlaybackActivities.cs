using FluentMigrator;

namespace MusicLibrary.Api.Migrations;

[Migration(202609070007)]
public sealed class AddUserPlaybackActivities : Migration
{
    public override void Up()
    {
        Create.Table("UserPlaybackActivities")
            .WithColumn("UserId").AsGuid().PrimaryKey()
                .ForeignKey("AspNetUsers", "Id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("PlaybackDescription").AsString(300).NotNullable()
            .WithColumn("IsLiveStation").AsBoolean().NotNullable()
            .WithColumn("StartedAt").AsCustom("timestamp with time zone").NotNullable()
            .WithColumn("LastHeartbeatAt").AsCustom("timestamp with time zone").NotNullable();

        Create.Index("IX_UserPlaybackActivities_LastHeartbeatAt")
            .OnTable("UserPlaybackActivities")
            .OnColumn("LastHeartbeatAt").Ascending();
    }

    public override void Down()
    {
        Delete.Table("UserPlaybackActivities");
    }
}