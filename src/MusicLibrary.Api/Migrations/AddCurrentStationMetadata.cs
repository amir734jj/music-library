using FluentMigrator;

namespace MusicLibrary.Api.Migrations;

[Migration(202609070003)]
public sealed class AddCurrentStationMetadata : Migration
{
    public override void Up()
    {
        Alter.Table("Stations")
            .AddColumn("CurrentRawMetadata").AsString().Nullable()
            .AddColumn("CurrentArtist").AsString().Nullable()
            .AddColumn("CurrentTitle").AsString().Nullable()
            .AddColumn("CurrentConfidence").AsDecimal(4, 3).NotNullable().WithDefaultValue(0);

        Create.Index("IX_Stations_LastMetadataAt")
            .OnTable("Stations")
            .OnColumn("LastMetadataAt").Ascending();

        Execute.Sql("""
            UPDATE "Stations" AS station
            SET "CurrentRawMetadata" = latest."RawMetadata",
                "CurrentArtist" = latest."Artist",
                "CurrentTitle" = latest."Title",
                "CurrentConfidence" = latest."Confidence",
                "LastMetadataAt" = latest."ObservedAt"
            FROM (
                SELECT DISTINCT ON ("StationId")
                    "StationId", "RawMetadata", "Artist", "Title", "Confidence", "ObservedAt"
                FROM "PlayObservations"
                ORDER BY "StationId", "ObservedAt" DESC
            ) AS latest
            WHERE station."Id" = latest."StationId";
            """);
    }

    public override void Down()
    {
        Delete.Index("IX_Stations_LastMetadataAt").OnTable("Stations");
        Delete.Column("CurrentConfidence").FromTable("Stations");
        Delete.Column("CurrentTitle").FromTable("Stations");
        Delete.Column("CurrentArtist").FromTable("Stations");
        Delete.Column("CurrentRawMetadata").FromTable("Stations");
    }
}