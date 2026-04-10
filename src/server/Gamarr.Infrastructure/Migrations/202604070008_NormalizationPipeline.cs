using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gamarr.Infrastructure.Migrations;

[Migration("202604070008_NormalizationPipeline")]
public partial class NormalizationPipeline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "NormalizedAssetRootPath",
            table: "PackageVersions",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "NormalizedAtUtc",
            table: "PackageVersions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NormalizationDiagnostics",
            table: "PackageVersions",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "ProcessingState",
            table: "PackageVersions",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "Discovered");

        migrationBuilder.CreateTable(
            name: "NormalizationJobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                PackageVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                SourcePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                Summary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NormalizationJobs", x => x.Id);
                table.ForeignKey(
                    name: "FK_NormalizationJobs_PackageVersions_PackageVersionId",
                    column: x => x.PackageVersionId,
                    principalTable: "PackageVersions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_NormalizationJobs_Packages_PackageId",
                    column: x => x.PackageId,
                    principalTable: "Packages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_NormalizationJobs_PackageId",
            table: "NormalizationJobs",
            column: "PackageId");

        migrationBuilder.CreateIndex(
            name: "IX_NormalizationJobs_PackageVersionId_State",
            table: "NormalizationJobs",
            columns: new[] { "PackageVersionId", "State" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "NormalizationJobs");

        migrationBuilder.DropColumn(
            name: "NormalizedAssetRootPath",
            table: "PackageVersions");

        migrationBuilder.DropColumn(
            name: "NormalizedAtUtc",
            table: "PackageVersions");

        migrationBuilder.DropColumn(
            name: "NormalizationDiagnostics",
            table: "PackageVersions");

        migrationBuilder.DropColumn(
            name: "ProcessingState",
            table: "PackageVersions");
    }
}
