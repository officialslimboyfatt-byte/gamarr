using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Gamarr.Infrastructure.Migrations;

[DbContext(typeof(GamarrDbContext))]
[Migration("202604070004_BackfillPackageVersionInstallPlan")]
public partial class BackfillPackageVersionInstallPlan : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
UPDATE "PackageVersions"
SET "InstallStrategy" = CASE
    WHEN "InstallScriptPath" = 'builtin:portable-copy' OR "InstallScriptPath" = 'builtin:library-import' THEN 'PortableCopy'
    WHEN "InstallScriptPath" = 'builtin:auto-install' THEN 'AutoInstall'
    WHEN "InstallScriptPath" = 'builtin:needs-review' THEN 'NeedsReview'
    ELSE "InstallStrategy"
END,
"InstallerFamily" = CASE
    WHEN "InstallScriptPath" = 'builtin:portable-copy' OR "InstallScriptPath" = 'builtin:library-import' THEN 'Portable'
    WHEN "InstallScriptPath" = 'builtin:auto-install' THEN 'Unknown'
    ELSE "InstallerFamily"
END,
"InstallDiagnostics" = CASE
    WHEN COALESCE("InstallDiagnostics", '') = '' THEN COALESCE("Notes", '')
    ELSE "InstallDiagnostics"
END;
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
