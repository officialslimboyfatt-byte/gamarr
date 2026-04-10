using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Gamarr.Infrastructure.Migrations;

[DbContext(typeof(GamarrDbContext))]
[Migration("202604070001_LibraryCandidateMetadataDecisions")]
public partial class LibraryCandidateMetadataDecisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AlternativeMatchesJson",
            table: "LibraryCandidates",
            type: "text",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.AddColumn<string>(
            name: "LocalDescription",
            table: "LibraryCandidates",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "LocalNormalizedTitle",
            table: "LibraryCandidates",
            type: "character varying(256)",
            maxLength: 256,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "LocalTitle",
            table: "LibraryCandidates",
            type: "character varying(256)",
            maxLength: 256,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "MatchDecision",
            table: "LibraryCandidates",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "Review");

        migrationBuilder.AddColumn<string>(
            name: "MatchSummary",
            table: "LibraryCandidates",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "SelectedMatchKey",
            table: "LibraryCandidates",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "WarningSignalsJson",
            table: "LibraryCandidates",
            type: "text",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.AddColumn<string>(
            name: "WinningSignalsJson",
            table: "LibraryCandidates",
            type: "text",
            nullable: false,
            defaultValue: "[]");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AlternativeMatchesJson", table: "LibraryCandidates");
        migrationBuilder.DropColumn(name: "LocalDescription", table: "LibraryCandidates");
        migrationBuilder.DropColumn(name: "LocalNormalizedTitle", table: "LibraryCandidates");
        migrationBuilder.DropColumn(name: "LocalTitle", table: "LibraryCandidates");
        migrationBuilder.DropColumn(name: "MatchDecision", table: "LibraryCandidates");
        migrationBuilder.DropColumn(name: "MatchSummary", table: "LibraryCandidates");
        migrationBuilder.DropColumn(name: "SelectedMatchKey", table: "LibraryCandidates");
        migrationBuilder.DropColumn(name: "WarningSignalsJson", table: "LibraryCandidates");
        migrationBuilder.DropColumn(name: "WinningSignalsJson", table: "LibraryCandidates");
    }
}
