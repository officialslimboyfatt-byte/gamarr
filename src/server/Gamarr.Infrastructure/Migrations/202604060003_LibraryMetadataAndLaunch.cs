using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Gamarr.Infrastructure.Migrations;

[DbContext(typeof(GamarrDbContext))]
[Migration("202604060003_LibraryMetadataAndLaunch")]
public partial class LibraryMetadataAndLaunch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CoverImagePath",
            table: "Packages",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "GenresSerialized",
            table: "Packages",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "ReleaseYear",
            table: "Packages",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Studio",
            table: "Packages",
            type: "character varying(256)",
            maxLength: 256,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "LaunchExecutablePath",
            table: "PackageVersions",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CoverImagePath", table: "Packages");
        migrationBuilder.DropColumn(name: "GenresSerialized", table: "Packages");
        migrationBuilder.DropColumn(name: "ReleaseYear", table: "Packages");
        migrationBuilder.DropColumn(name: "Studio", table: "Packages");
        migrationBuilder.DropColumn(name: "LaunchExecutablePath", table: "PackageVersions");
    }
}
