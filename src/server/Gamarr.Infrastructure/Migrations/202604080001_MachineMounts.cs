using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gamarr.Infrastructure.Migrations;

[Migration("202604080001_MachineMounts")]
public partial class MachineMounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MachineMounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MachineId = table.Column<Guid>(type: "uuid", nullable: false),
                IsoPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Pending"),
                DriveLetter = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MachineMounts", x => x.Id);
                table.ForeignKey(
                    name: "FK_MachineMounts_Machines_MachineId",
                    column: x => x.MachineId,
                    principalTable: "Machines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MachineMounts_MachineId_Status",
            table: "MachineMounts",
            columns: new[] { "MachineId", "Status" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MachineMounts");
    }
}
