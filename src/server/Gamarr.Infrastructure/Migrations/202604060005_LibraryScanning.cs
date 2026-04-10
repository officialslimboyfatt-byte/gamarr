using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Gamarr.Infrastructure.Migrations;

[DbContext(typeof(GamarrDbContext))]
[Migration("202604060005_LibraryScanning")]
public partial class LibraryScanning : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LibraryRoots",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                PathKind = table.Column<int>(type: "integer", nullable: false),
                ContentKind = table.Column<int>(type: "integer", nullable: false),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LastScanStartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastScanCompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastScanState = table.Column<int>(type: "integer", nullable: true),
                LastScanError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_LibraryRoots", x => x.Id));

        migrationBuilder.CreateTable(
            name: "LibraryScans",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                LibraryRootId = table.Column<Guid>(type: "uuid", nullable: false),
                State = table.Column<int>(type: "integer", nullable: false),
                DirectoriesScanned = table.Column<int>(type: "integer", nullable: false),
                FilesScanned = table.Column<int>(type: "integer", nullable: false),
                CandidatesDetected = table.Column<int>(type: "integer", nullable: false),
                CandidatesImported = table.Column<int>(type: "integer", nullable: false),
                ErrorsCount = table.Column<int>(type: "integer", nullable: false),
                Summary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LibraryScans", x => x.Id);
                table.ForeignKey(
                    name: "FK_LibraryScans_LibraryRoots_LibraryRootId",
                    column: x => x.LibraryRootId,
                    principalTable: "LibraryRoots",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "LibraryCandidates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                LibraryRootId = table.Column<Guid>(type: "uuid", nullable: false),
                LibraryScanId = table.Column<Guid>(type: "uuid", nullable: false),
                PackageId = table.Column<Guid>(type: "uuid", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                NormalizedTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                Studio = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ReleaseYear = table.Column<int>(type: "integer", nullable: true),
                CoverImagePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                GenresSerialized = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                MetadataProvider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                MetadataSourceUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                ConfidenceScore = table.Column<double>(type: "double precision", nullable: false),
                PrimaryPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                SourcesJson = table.Column<string>(type: "text", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LibraryCandidates", x => x.Id);
                table.ForeignKey(
                    name: "FK_LibraryCandidates_LibraryRoots_LibraryRootId",
                    column: x => x.LibraryRootId,
                    principalTable: "LibraryRoots",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_LibraryCandidates_LibraryScans_LibraryScanId",
                    column: x => x.LibraryScanId,
                    principalTable: "LibraryScans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_LibraryCandidates_Packages_PackageId",
                    column: x => x.PackageId,
                    principalTable: "Packages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(name: "IX_LibraryRoots_Path", table: "LibraryRoots", column: "Path", unique: true);
        migrationBuilder.CreateIndex(name: "IX_LibraryScans_LibraryRootId_StartedAtUtc", table: "LibraryScans", columns: new[] { "LibraryRootId", "StartedAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_LibraryCandidates_LibraryRootId_Status", table: "LibraryCandidates", columns: new[] { "LibraryRootId", "Status" });
        migrationBuilder.CreateIndex(name: "IX_LibraryCandidates_LibraryScanId", table: "LibraryCandidates", column: "LibraryScanId");
        migrationBuilder.CreateIndex(name: "IX_LibraryCandidates_PackageId", table: "LibraryCandidates", column: "PackageId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "LibraryCandidates");
        migrationBuilder.DropTable(name: "LibraryScans");
        migrationBuilder.DropTable(name: "LibraryRoots");
    }
}
