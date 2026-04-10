using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Gamarr.Infrastructure.Migrations;

[DbContext(typeof(GamarrDbContext))]
[Migration("202604060004_PackageMediaSourceMetadata")]
public partial class PackageMediaSourceMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "DiscNumber",
            table: "PackageMedia",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "EntrypointHint",
            table: "PackageMedia",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ScratchPolicy",
            table: "PackageMedia",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<int>(
            name: "SourceKind",
            table: "PackageMedia",
            type: "integer",
            nullable: false,
            defaultValue: 1);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DiscNumber", table: "PackageMedia");
        migrationBuilder.DropColumn(name: "EntrypointHint", table: "PackageMedia");
        migrationBuilder.DropColumn(name: "ScratchPolicy", table: "PackageMedia");
        migrationBuilder.DropColumn(name: "SourceKind", table: "PackageMedia");
    }
}
