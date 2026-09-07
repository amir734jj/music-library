using FluentMigrator;

namespace MusicLibrary.Api.Migrations;

[Migration(202609070002)]
public sealed class EnableProbingByDefault : Migration
{
    public override void Up()
    {
        Alter.Column("IsProbeEnabled")
            .OnTable("Stations")
            .AsBoolean()
            .NotNullable()
            .WithDefaultValue(true);

        Update.Table("Stations")
            .Set(new { IsProbeEnabled = true })
            .AllRows();

        Update.Table("GlobalConfigRows")
            .Set(new { Value = "True" })
            .Where(new { Key = "PROBING_ENABLED" });
    }

    public override void Down()
    {
        Alter.Column("IsProbeEnabled")
            .OnTable("Stations")
            .AsBoolean()
            .NotNullable();
    }
}