using Gamarr.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamarr.Infrastructure.Persistence;

public sealed class GamarrDbContext(DbContextOptions<GamarrDbContext> options) : DbContext(options)
{
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<PackageVersion> PackageVersions => Set<PackageVersion>();
    public DbSet<PackageMedia> PackageMedia => Set<PackageMedia>();
    public DbSet<InstallDetectionRule> InstallDetectionRules => Set<InstallDetectionRule>();
    public DbSet<PackagePrerequisite> PackagePrerequisites => Set<PackagePrerequisite>();
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<MachineCapability> MachineCapabilities => Set<MachineCapability>();
    public DbSet<MachinePackageInstall> MachinePackageInstalls => Set<MachinePackageInstall>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobEvent> JobEvents => Set<JobEvent>();
    public DbSet<PackageActionLog> PackageActionLogs => Set<PackageActionLog>();
    public DbSet<LibraryRoot> LibraryRoots => Set<LibraryRoot>();
    public DbSet<LibraryScan> LibraryScans => Set<LibraryScan>();
    public DbSet<LibraryCandidate> LibraryCandidates => Set<LibraryCandidate>();
    public DbSet<NormalizationJob> NormalizationJobs => Set<NormalizationJob>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<MachineMount> MachineMounts => Set<MachineMount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Package>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Slug).HasMaxLength(128);
            entity.Property(x => x.Name).HasMaxLength(256);
            entity.Property(x => x.Description).HasMaxLength(2048);
            entity.Property(x => x.Notes).HasMaxLength(4096);
            entity.Property(x => x.TagsSerialized).HasMaxLength(1024);
            entity.Property(x => x.GenresSerialized).HasMaxLength(1024);
            entity.Property(x => x.Studio).HasMaxLength(256);
            entity.Property(x => x.CoverImagePath).HasMaxLength(1024);
            entity.Property(x => x.MetadataProvider).HasMaxLength(128);
            entity.Property(x => x.MetadataSourceUrl).HasMaxLength(1024);
            entity.Property(x => x.MetadataSelectionKind).HasMaxLength(64);
            entity.Property(x => x.ArchivedReason).HasMaxLength(512);
        });

        modelBuilder.Entity<PackageVersion>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Package).WithMany(x => x.Versions).HasForeignKey(x => x.PackageId);
            entity.Property(x => x.VersionLabel).HasMaxLength(64);
            entity.Property(x => x.SupportedOs).HasMaxLength(128);
            entity.Property(x => x.ManifestFormatVersion).HasMaxLength(64);
            entity.Property(x => x.InstallScriptPath).HasMaxLength(512);
            entity.Property(x => x.UninstallScriptPath).HasMaxLength(512);
            entity.Property(x => x.UninstallArguments).HasMaxLength(1024);
            entity.Property(x => x.InstallStrategy).HasMaxLength(64);
            entity.Property(x => x.InstallerFamily).HasMaxLength(64);
            entity.Property(x => x.InstallerPath).HasMaxLength(1024);
            entity.Property(x => x.SilentArguments).HasMaxLength(1024);
            entity.Property(x => x.InstallDiagnostics).HasMaxLength(2048);
            entity.Property(x => x.LaunchExecutablePath).HasMaxLength(1024);
            entity.Property(x => x.ProcessingState).HasMaxLength(64);
            entity.Property(x => x.NormalizedAssetRootPath).HasMaxLength(1024);
            entity.Property(x => x.NormalizationDiagnostics).HasMaxLength(2048);
        });

        modelBuilder.Entity<PackageMedia>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.PackageVersion).WithMany(x => x.Media).HasForeignKey(x => x.PackageVersionId);
            entity.Property(x => x.Label).HasMaxLength(128);
            entity.Property(x => x.Path).HasMaxLength(1024);
            entity.Property(x => x.EntrypointHint).HasMaxLength(1024);
        });

        modelBuilder.Entity<InstallDetectionRule>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.PackageVersion).WithMany(x => x.DetectionRules).HasForeignKey(x => x.PackageVersionId);
            entity.Property(x => x.RuleType).HasMaxLength(64);
            entity.Property(x => x.Value).HasMaxLength(1024);
        });

        modelBuilder.Entity<PackagePrerequisite>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.PackageVersion).WithMany(x => x.Prerequisites).HasForeignKey(x => x.PackageVersionId);
            entity.Property(x => x.Name).HasMaxLength(256);
        });

        modelBuilder.Entity<Machine>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.StableKey).IsUnique();
            entity.Property(x => x.StableKey).HasMaxLength(256);
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.Hostname).HasMaxLength(128);
            entity.Property(x => x.OperatingSystem).HasMaxLength(128);
            entity.Property(x => x.AgentVersion).HasMaxLength(64);
        });

        modelBuilder.Entity<MachineCapability>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Machine).WithMany(x => x.Capabilities).HasForeignKey(x => x.MachineId);
            entity.Property(x => x.Capability).HasMaxLength(128);
        });

        modelBuilder.Entity<MachinePackageInstall>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId);
            entity.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId);
            entity.HasOne(x => x.PackageVersion).WithMany().HasForeignKey(x => x.PackageVersionId);
            entity.Property(x => x.State).HasMaxLength(32);
            entity.Property(x => x.ValidationSummary).HasMaxLength(2048);
            entity.Property(x => x.LastKnownLaunchPath).HasMaxLength(1024);
            entity.Property(x => x.LastKnownInstallLocation).HasMaxLength(1024);
            entity.Property(x => x.ResolvedUninstallCommand).HasMaxLength(2048);
            entity.HasIndex(x => new { x.MachineId, x.PackageVersionId }).IsUnique();
            entity.HasIndex(x => new { x.MachineId, x.PackageId });
        });

        modelBuilder.Entity<Job>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId);
            entity.HasOne(x => x.PackageVersion).WithMany().HasForeignKey(x => x.PackageVersionId);
            entity.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId);
            entity.HasIndex(x => new { x.MachineId, x.State });
        });

        modelBuilder.Entity<JobEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Job).WithMany(x => x.Events).HasForeignKey(x => x.JobId);
            entity.HasIndex(x => new { x.JobId, x.SequenceNumber }).IsUnique();
            entity.Property(x => x.Message).HasMaxLength(2048);
        });

        modelBuilder.Entity<PackageActionLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Job).WithMany(x => x.Logs).HasForeignKey(x => x.JobId);
            entity.Property(x => x.Source).HasMaxLength(64);
            entity.Property(x => x.Message).HasMaxLength(4000);
        });

        modelBuilder.Entity<LibraryRoot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Path).IsUnique();
            entity.Property(x => x.DisplayName).HasMaxLength(256);
            entity.Property(x => x.Path).HasMaxLength(1024);
            entity.Property(x => x.LastScanError).HasMaxLength(2048);
        });

        modelBuilder.Entity<LibraryScan>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.LibraryRoot).WithMany(x => x.Scans).HasForeignKey(x => x.LibraryRootId);
            entity.Property(x => x.Summary).HasMaxLength(1024);
            entity.Property(x => x.ErrorMessage).HasMaxLength(2048);
            entity.HasIndex(x => new { x.LibraryRootId, x.StartedAtUtc });
        });

        modelBuilder.Entity<LibraryCandidate>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.LibraryRoot).WithMany(x => x.Candidates).HasForeignKey(x => x.LibraryRootId);
            entity.HasOne(x => x.LibraryScan).WithMany(x => x.Candidates).HasForeignKey(x => x.LibraryScanId);
            entity.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.SetNull);
            entity.Property(x => x.LocalTitle).HasMaxLength(256);
            entity.Property(x => x.LocalNormalizedTitle).HasMaxLength(256);
            entity.Property(x => x.LocalDescription).HasMaxLength(2048);
            entity.Property(x => x.Title).HasMaxLength(256);
            entity.Property(x => x.NormalizedTitle).HasMaxLength(256);
            entity.Property(x => x.Studio).HasMaxLength(256);
            entity.Property(x => x.CoverImagePath).HasMaxLength(1024);
            entity.Property(x => x.MetadataProvider).HasMaxLength(128);
            entity.Property(x => x.MetadataSourceUrl).HasMaxLength(1024);
            entity.Property(x => x.MatchDecision).HasMaxLength(64);
            entity.Property(x => x.MatchSummary).HasMaxLength(2048);
            entity.Property(x => x.SelectedMatchKey).HasMaxLength(256);
            entity.Property(x => x.PrimaryPath).HasMaxLength(1024);
            entity.Property(x => x.GenresSerialized).HasMaxLength(1024);
            entity.Property(x => x.Description).HasMaxLength(2048);
            entity.Property(x => x.SourcesJson).HasColumnType("text");
            entity.Property(x => x.WinningSignalsJson).HasColumnType("text");
            entity.Property(x => x.WarningSignalsJson).HasColumnType("text");
            entity.Property(x => x.ProviderDiagnosticsJson).HasColumnType("text");
            entity.Property(x => x.AlternativeMatchesJson).HasColumnType("text");
            entity.HasIndex(x => new { x.LibraryRootId, x.Status });
        });

        modelBuilder.Entity<NormalizationJob>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId);
            entity.HasOne(x => x.PackageVersion).WithMany().HasForeignKey(x => x.PackageVersionId);
            entity.Property(x => x.State).HasMaxLength(64);
            entity.Property(x => x.SourcePath).HasMaxLength(1024);
            entity.Property(x => x.Summary).HasMaxLength(1024);
            entity.Property(x => x.ErrorMessage).HasMaxLength(2048);
            entity.HasIndex(x => new { x.PackageVersionId, x.State });
        });

        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(128);
            entity.Property(x => x.JsonValue).HasColumnType("text");
        });

        modelBuilder.Entity<MachineMount>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Machine).WithMany().HasForeignKey(x => x.MachineId);
            entity.Property(x => x.IsoPath).HasMaxLength(1024);
            entity.Property(x => x.Status).HasMaxLength(32);
            entity.Property(x => x.DriveLetter).HasMaxLength(8);
            entity.Property(x => x.ErrorMessage).HasMaxLength(2048);
            entity.HasIndex(x => new { x.MachineId, x.Status });
        });
    }
}
