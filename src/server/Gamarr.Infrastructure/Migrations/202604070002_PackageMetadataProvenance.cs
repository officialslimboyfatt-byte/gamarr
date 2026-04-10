using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Gamarr.Infrastructure.Migrations;

[DbContext(typeof(GamarrDbContext))]
[Migration("202604070002_PackageMetadataProvenance")]
public partial class PackageMetadataProvenance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "MetadataProvider",
            table: "Packages",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MetadataSelectionKind",
            table: "Packages",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "Unknown");

        migrationBuilder.AddColumn<string>(
            name: "MetadataSourceUrl",
            table: "Packages",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "MetadataProvider", table: "Packages");
        migrationBuilder.DropColumn(name: "MetadataSelectionKind", table: "Packages");
        migrationBuilder.DropColumn(name: "MetadataSourceUrl", table: "Packages");
    }
}
