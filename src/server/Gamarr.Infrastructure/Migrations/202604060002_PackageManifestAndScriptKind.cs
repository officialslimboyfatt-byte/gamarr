using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Gamarr.Infrastructure.Migrations;

[DbContext(typeof(GamarrDbContext))]
[Migration("202604060002_PackageManifestAndScriptKind")]
public partial class PackageManifestAndScriptKind : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "InstallScriptKind",
            table: "PackageVersions",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<string>(
            name: "ManifestFormatVersion",
            table: "PackageVersions",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "gamarr.package/v1");

        migrationBuilder.AddColumn<string>(
            name: "ManifestJson",
            table: "PackageVersions",
            type: "text",
            nullable: false,
            defaultValue: "");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "InstallScriptKind", table: "PackageVersions");
        migrationBuilder.DropColumn(name: "ManifestFormatVersion", table: "PackageVersions");
        migrationBuilder.DropColumn(name: "ManifestJson", table: "PackageVersions");
    }
}
