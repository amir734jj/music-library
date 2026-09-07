using FluentMigrator;

namespace MusicLibrary.Api.Migrations;

[Migration(202609070004)]
public sealed class OptimizeStationProbeSelection : Migration
{
    public override void Up()
    {
        Delete.Index("IX_Stations_IsProbeEnabled").OnTable("Stations");
        Create.Index("IX_Stations_IsProbeEnabled_LastProbedAt")
            .OnTable("Stations")
            .OnColumn("IsProbeEnabled").Ascending()
            .OnColumn("LastProbedAt").Ascending();
    }

    public override void Down()
    {
        Delete.Index("IX_Stations_IsProbeEnabled_LastProbedAt").OnTable("Stations");
        Create.Index("IX_Stations_IsProbeEnabled")
            .OnTable("Stations")
            .OnColumn("IsProbeEnabled").Ascending();
    }
}