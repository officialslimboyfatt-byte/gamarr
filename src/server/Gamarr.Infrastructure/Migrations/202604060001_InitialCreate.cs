using System;
using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Gamarr.Infrastructure.Migrations;

[DbContext(typeof(GamarrDbContext))]
[Migration("202604060001_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Machines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StableKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Hostname = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                OperatingSystem = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Architecture = table.Column<int>(type: "integer", nullable: false),
                AgentVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                RegisteredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LastHeartbeatUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Machines", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Packages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                Notes = table.Column<string>(type: "text", nullable: false),
                TagsSerialized = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Packages", x => x.Id));

        migrationBuilder.CreateTable(
            name: "MachineCapabilities",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MachineId = table.Column<Guid>(type: "uuid", nullable: false),
                Capability = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MachineCapabilities", x => x.Id);
                table.ForeignKey(
                    name: "FK_MachineCapabilities_Machines_MachineId",
                    column: x => x.MachineId,
                    principalTable: "Machines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PackageVersions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                VersionLabel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                SupportedOs = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Architecture = table.Column<int>(type: "integer", nullable: false),
                InstallScriptPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                UninstallScriptPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                Notes = table.Column<string>(type: "text", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PackageVersions", x => x.Id);
                table.ForeignKey(
                    name: "FK_PackageVersions_Packages_PackageId",
                    column: x => x.PackageId,
                    principalTable: "Packages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "InstallDetectionRules",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PackageVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                RuleType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Value = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InstallDetectionRules", x => x.Id);
                table.ForeignKey(
                    name: "FK_InstallDetectionRules_PackageVersions_PackageVersionId",
                    column: x => x.PackageVersionId,
                    principalTable: "PackageVersions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PackageMedia",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PackageVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                MediaType = table.Column<int>(type: "integer", nullable: false),
                Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PackageMedia", x => x.Id);
                table.ForeignKey(
                    name: "FK_PackageMedia_PackageVersions_PackageVersionId",
                    column: x => x.PackageVersionId,
                    principalTable: "PackageVersions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PackagePrerequisites",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PackageVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Notes = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PackagePrerequisites", x => x.Id);
                table.ForeignKey(
                    name: "FK_PackagePrerequisites_PackageVersions_PackageVersionId",
                    column: x => x.PackageVersionId,
                    principalTable: "PackageVersions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Jobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                PackageVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                MachineId = table.Column<Guid>(type: "uuid", nullable: false),
                ActionType = table.Column<int>(type: "integer", nullable: false),
                State = table.Column<int>(type: "integer", nullable: false),
                RequestedBy = table.Column<string>(type: "text", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ClaimedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                OutcomeSummary = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Jobs", x => x.Id);
                table.ForeignKey(
                    name: "FK_Jobs_Machines_MachineId",
                    column: x => x.MachineId,
                    principalTable: "Machines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_Jobs_Packages_PackageId",
                    column: x => x.PackageId,
                    principalTable: "Packages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_Jobs_PackageVersions_PackageVersionId",
                    column: x => x.PackageVersionId,
                    principalTable: "PackageVersions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "JobEvents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                JobId = table.Column<Guid>(type: "uuid", nullable: false),
                SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                State = table.Column<int>(type: "integer", nullable: false),
                Message = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JobEvents", x => x.Id);
                table.ForeignKey(
                    name: "FK_JobEvents_Jobs_JobId",
                    column: x => x.JobId,
                    principalTable: "Jobs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PackageActionLogs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                JobId = table.Column<Guid>(type: "uuid", nullable: false),
                Level = table.Column<int>(type: "integer", nullable: false),
                Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                PayloadJson = table.Column<string>(type: "text", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PackageActionLogs", x => x.Id);
                table.ForeignKey(
                    name: "FK_PackageActionLogs_Jobs_JobId",
                    column: x => x.JobId,
                    principalTable: "Jobs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_InstallDetectionRules_PackageVersionId", table: "InstallDetectionRules", column: "PackageVersionId");
        migrationBuilder.CreateIndex(name: "IX_JobEvents_JobId_SequenceNumber", table: "JobEvents", columns: new[] { "JobId", "SequenceNumber" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_Jobs_MachineId_State", table: "Jobs", columns: new[] { "MachineId", "State" });
        migrationBuilder.CreateIndex(name: "IX_Jobs_PackageId", table: "Jobs", column: "PackageId");
        migrationBuilder.CreateIndex(name: "IX_Jobs_PackageVersionId", table: "Jobs", column: "PackageVersionId");
        migrationBuilder.CreateIndex(name: "IX_MachineCapabilities_MachineId", table: "MachineCapabilities", column: "MachineId");
        migrationBuilder.CreateIndex(name: "IX_Machines_StableKey", table: "Machines", column: "StableKey", unique: true);
        migrationBuilder.CreateIndex(name: "IX_PackageActionLogs_JobId", table: "PackageActionLogs", column: "JobId");
        migrationBuilder.CreateIndex(name: "IX_PackageMedia_PackageVersionId", table: "PackageMedia", column: "PackageVersionId");
        migrationBuilder.CreateIndex(name: "IX_PackagePrerequisites_PackageVersionId", table: "PackagePrerequisites", column: "PackageVersionId");
        migrationBuilder.CreateIndex(name: "IX_Packages_Slug", table: "Packages", column: "Slug", unique: true);
        migrationBuilder.CreateIndex(name: "IX_PackageVersions_PackageId", table: "PackageVersions", column: "PackageId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "InstallDetectionRules");
        migrationBuilder.DropTable(name: "JobEvents");
        migrationBuilder.DropTable(name: "MachineCapabilities");
        migrationBuilder.DropTable(name: "PackageActionLogs");
        migrationBuilder.DropTable(name: "PackageMedia");
        migrationBuilder.DropTable(name: "PackagePrerequisites");
        migrationBuilder.DropTable(name: "Jobs");
        migrationBuilder.DropTable(name: "PackageVersions");
        migrationBuilder.DropTable(name: "Machines");
        migrationBuilder.DropTable(name: "Packages");
    }
}
