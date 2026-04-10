using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gamarr.Infrastructure.Migrations;

[Migration("202604090001_MachineInstallStateAndUninstall")]
public partial class MachineInstallStateAndUninstall : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "UninstallArguments",
            table: "PackageVersions",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "MachinePackageInstalls",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MachineId = table.Column<Guid>(type: "uuid", nullable: false),
                PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                PackageVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                InstalledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastValidatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ValidationSummary = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                LastKnownLaunchPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                LastKnownInstallLocation = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                ResolvedUninstallCommand = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MachinePackageInstalls", x => x.Id);
                table.ForeignKey(
                    name: "FK_MachinePackageInstalls_Machines_MachineId",
                    column: x => x.MachineId,
                    principalTable: "Machines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_MachinePackageInstalls_PackageVersions_PackageVersionId",
                    column: x => x.PackageVersionId,
                    principalTable: "PackageVersions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_MachinePackageInstalls_Packages_PackageId",
                    column: x => x.PackageId,
                    principalTable: "Packages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MachinePackageInstalls_MachineId_PackageId",
            table: "MachinePackageInstalls",
            columns: new[] { "MachineId", "PackageId" });

        migrationBuilder.CreateIndex(
            name: "IX_MachinePackageInstalls_MachineId_PackageVersionId",
            table: "MachinePackageInstalls",
            columns: new[] { "MachineId", "PackageVersionId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MachinePackageInstalls_PackageId",
            table: "MachinePackageInstalls",
            column: "PackageId");

        migrationBuilder.CreateIndex(
            name: "IX_MachinePackageInstalls_PackageVersionId",
            table: "MachinePackageInstalls",
            column: "PackageVersionId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MachinePackageInstalls");

        migrationBuilder.DropColumn(
            name: "UninstallArguments",
            table: "PackageVersions");
    }
}
