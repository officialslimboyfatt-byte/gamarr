using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Gamarr.Infrastructure.Migrations;

[DbContext(typeof(GamarrDbContext))]
[Migration("202604070003_PackageVersionInstallPlan")]
public partial class PackageVersionInstallPlan : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "InstallDiagnostics",
            table: "PackageVersions",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "InstallerFamily",
            table: "PackageVersions",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "Unknown");

        migrationBuilder.AddColumn<string>(
            name: "InstallerPath",
            table: "PackageVersions",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "InstallStrategy",
            table: "PackageVersions",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "NeedsReview");

        migrationBuilder.AddColumn<string>(
            name: "SilentArguments",
            table: "PackageVersions",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "InstallDiagnostics", table: "PackageVersions");
        migrationBuilder.DropColumn(name: "InstallerFamily", table: "PackageVersions");
        migrationBuilder.DropColumn(name: "InstallerPath", table: "PackageVersions");
        migrationBuilder.DropColumn(name: "InstallStrategy", table: "PackageVersions");
        migrationBuilder.DropColumn(name: "SilentArguments", table: "PackageVersions");
    }
}
