export type ArchitectureKind = "X86" | "X64";
export type MediaType = "Iso" | "InstallerFolder" | "Executable" | "Patch" | "SupportFiles" | "DiskImage";
export type MachineStatus = "Unknown" | "Online" | "Offline" | "Busy";
export type InstallScriptKind = "MockRecipe" | "PowerShell";
export type PackageSourceKind = "Auto" | "DirectFolder" | "MountedVolume" | "ExtractedWorkspace" | "MultiDisc";
export type ScratchPolicy = "Temporary" | "Persistent" | "Prompt";
export type LibraryRootPathKind = "Local" | "Unc";
export type LibraryRootContentKind = "Unknown" | "InstalledLibrary" | "MediaArchive" | "Mixed";
export type LibraryScanState = "Queued" | "Running" | "Completed" | "Failed";
export type LibraryCandidateStatus = "PendingReview" | "AutoImported" | "Approved" | "Rejected" | "Merged";
export type JobState =
  | "Queued"
  | "Assigned"
  | "Preparing"
  | "Mounting"
  | "Installing"
  | "Validating"
  | "Completed"
  | "Failed"
  | "Cancelled";

export interface PackageMedia {
  id: string;
  mediaType: MediaType;
  label: string;
  path: string;
  discNumber?: number | null;
  entrypointHint?: string | null;
  sourceKind: PackageSourceKind;
  scratchPolicy: ScratchPolicy;
}

export interface DetectionRule {
  id: string;
  ruleType: string;
  value: string;
}

export interface Prerequisite {
  id: string;
  name: string;
  notes: string;
}

export interface PackageVersion {
  id: string;
  versionLabel: string;
  supportedOs: string;
  architecture: ArchitectureKind;
  installScriptKind: InstallScriptKind;
  installScriptPath: string;
  uninstallScriptPath?: string | null;
  uninstallArguments?: string | null;
  manifestFormatVersion: string;
  manifestJson: string;
  timeoutSeconds: number;
  notes: string;
  installStrategy: string;
  installerFamily: string;
  installerPath?: string | null;
  silentArguments?: string | null;
  installDiagnostics: string;
  launchExecutablePath?: string | null;
  processingState: string;
  normalizedAssetRootPath?: string | null;
  normalizedAtUtc?: string | null;
  normalizationDiagnostics: string;
  isActive: boolean;
  media: PackageMedia[];
  detectionRules: DetectionRule[];
  prerequisites: Prerequisite[];
}

export interface PackageRecord {
  id: string;
  slug: string;
  name: string;
  description: string;
  notes: string;
  tags: string[];
  genres: string[];
  studio: string;
  releaseYear?: number | null;
  coverImagePath?: string | null;
  metadataProvider?: string | null;
  metadataSourceUrl?: string | null;
  metadataSelectionKind: string;
  isArchived: boolean;
  archivedReason?: string | null;
  archivedAtUtc?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  versions: PackageVersion[];
}

export interface MachineRecord {
  id: string;
  stableKey: string;
  name: string;
  hostname: string;
  operatingSystem: string;
  architecture: ArchitectureKind;
  agentVersion: string;
  status: MachineStatus;
  registeredAtUtc: string;
  lastHeartbeatUtc: string;
  capabilities: string[];
  isStale: boolean;
  canRemove: boolean;
  removeBlockedReason?: string | null;
  hasActiveJobs: boolean;
}

export type MachineMountStatus = "Pending" | "Mounted" | "DismountRequested" | "Dismounted" | "Failed";

export interface MachineMountRecord {
  id: string;
  machineId: string;
  isoPath: string;
  status: MachineMountStatus;
  driveLetter?: string | null;
  errorMessage?: string | null;
  createdAtUtc: string;
  completedAtUtc?: string | null;
}

export interface JobEvent {
  id: string;
  sequenceNumber: number;
  state: JobState;
  message: string;
  createdAtUtc: string;
}

export interface JobLog {
  id: string;
  level: string;
  source: string;
  message: string;
  payloadJson?: string | null;
  createdAtUtc: string;
}

export interface JobRecord {
  id: string;
  packageId: string;
  packageVersionId: string;
  machineId: string;
  packageName: string;
  packageVersionLabel: string;
  machineName: string;
  actionType: string;
  state: JobState;
  requestedBy: string;
  createdAtUtc: string;
  claimedAtUtc?: string | null;
  completedAtUtc?: string | null;
  updatedAtUtc: string;
  durationSeconds?: number | null;
  latestEventMessage?: string | null;
  outcomeSummary?: string | null;
  events: JobEvent[];
  logs: JobLog[];
}

export type LibraryInstallState = "NotInstalled" | "Installing" | "Installed" | "Failed" | "Uninstalling";

export interface LibraryTitleRecord {
  id: string;
  slug: string;
  name: string;
  description: string;
  notes: string;
  tags: string[];
  genres: string[];
  studio: string;
  releaseYear?: number | null;
  coverImagePath?: string | null;
  versionLabel: string;
  installScriptKind: InstallScriptKind;
  launchExecutablePath?: string | null;
  installState: LibraryInstallState;
  lastValidatedAtUtc?: string | null;
  isInstallStateStale: boolean;
  validationSummary?: string | null;
  canValidate: boolean;
  canUninstall: boolean;
  lastJobId?: string | null;
  latestJobState?: JobState | null;
  latestJobActionType?: "Install" | "Launch" | "Validate" | "Uninstall" | null;
  latestJobCreatedAtUtc?: string | null;
  sourceSummary: string;
  sourceHealth: string;
  sourceConflictCount: number;
  installStrategy: string;
  processingState: string;
  supportedInstallPath: string;
  installReadiness: string;
  playReadiness: string;
  isInstallable: boolean;
  canInstall: boolean;
  canPlay: boolean;
  reviewRequiredReason?: string | null;
  recipeDiagnostics: string;
  normalizationDiagnostics: string;
  normalizedAssetRootPath?: string | null;
  normalizedAtUtc?: string | null;
  metadataProvider?: string | null;
  metadataSourceUrl?: string | null;
  metadataSelectionKind: string;
  metadataStatus: string;
  metadataPrimarySource?: string | null;
  metadataConfidence?: number | null;
  posterImageUrl?: string | null;
  backdropImageUrl?: string | null;
  storeDescription: string;
  isArchived: boolean;
  archivedReason?: string | null;
  archivedAtUtc?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface LibrarySourceConflictRecord {
  conflictType: string;
  path: string;
  packageId: string;
  packageName: string;
}

export interface LibraryMetadataSnapshotRecord {
  title: string;
  description: string;
  studio: string;
  releaseYear?: number | null;
  coverImagePath?: string | null;
  genres: string[];
  metadataProvider?: string | null;
  metadataSourceUrl?: string | null;
  metadataSelectionKind: string;
}

export interface LibraryTitleDetailRecord {
  title: LibraryTitleRecord;
  sources: LibraryCandidateSourceRecord[];
  detectionRules: DetectionRule[];
  installScriptPath: string;
  uninstallScriptPath?: string | null;
  uninstallArguments?: string | null;
  notes: string;
  sourceConflicts: LibrarySourceConflictRecord[];
  latestJob?: JobRecord | null;
}

export interface LibraryReconcilePreviewRecord {
  packageId: string;
  localTitle: string;
  localDescription: string;
  current: LibraryMetadataSnapshotRecord;
  localOnly: LibraryMetadataSnapshotRecord;
  matchSummary: string;
  winningSignals: string[];
  warningSignals: string[];
  providerDiagnostics: ProviderDiagnosticRecord[];
  alternativeMatches: MetadataMatchOptionRecord[];
  sourceConflicts: LibrarySourceConflictRecord[];
  installStrategy: string;
  recipeDiagnostics: string;
}

export interface ManualMetadataSearchRecord {
  query: string;
  localTitle: string;
  matchSummary: string;
  winningSignals: string[];
  warningSignals: string[];
  providerDiagnostics: ProviderDiagnosticRecord[];
  alternativeMatches: MetadataMatchOptionRecord[];
}

export interface PlayLibraryTitleResult {
  packageId: string;
  machineId: string;
  actionType: "Install" | "Launch" | "Validate" | "Uninstall";
  previousInstallState: LibraryInstallState;
  job: JobRecord;
}

export interface LibraryRootRecord {
  id: string;
  displayName: string;
  path: string;
  pathKind: LibraryRootPathKind;
  contentKind: LibraryRootContentKind;
  isEnabled: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  lastScanStartedAtUtc?: string | null;
  lastScanCompletedAtUtc?: string | null;
  lastScanState?: LibraryScanState | null;
  lastScanError?: string | null;
  isReachable: boolean;
  healthSummary: string;
}

export interface MetadataSettingsRecord {
  preferIgdb: boolean;
  igdbEnabled: boolean;
  igdbClientId?: string | null;
  hasIgdbClientSecret: boolean;
  igdbConfigured: boolean;
  useSteamFallback: boolean;
  autoImportThreshold: number;
  reviewThreshold: number;
  providerStatus: string;
}

export interface MediaManagementSettingsRecord {
  defaultLibraryRootPath?: string | null;
  normalizedAssetRootPath?: string | null;
  autoScanOnRootCreate: boolean;
  autoNormalizeOnImport: boolean;
  autoImportHighConfidenceMatches: boolean;
  includePatterns: string[];
  excludePatterns: string[];
  supportedInstallPathSummary: string;
}

export interface NormalizationJobRecord {
  id: string;
  packageId: string;
  packageVersionId: string;
  packageName: string;
  packageVersionLabel: string;
  state: string;
  sourcePath: string;
  summary: string;
  errorMessage?: string | null;
  createdAtUtc: string;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  updatedAtUtc: string;
}

export interface SettingsRecord {
  metadata: MetadataSettingsRecord;
  media: MediaManagementSettingsRecord;
  network: NetworkSettingsRecord;
}

export interface NetworkSettingsRecord {
  publicServerUrl: string;
  agentServerUrl: string;
  apiListenUrls: string;
  webListenHost: string;
  lanEnabled: boolean;
  summary: string;
}

export interface SystemHealthMetricsRecord {
  totalMachines: number;
  onlineMachines: number;
  activeJobs: number;
  failedJobsLast24Hours: number;
  runningScans: number;
  pendingNormalizationJobs: number;
  failedMounts: number;
}

export interface SystemHealthCheckRecord {
  key: string;
  name: string;
  status: "Healthy" | "Warning" | "Error";
  summary: string;
  actionPath?: string | null;
}

export interface SystemHealthRecord {
  generatedAtUtc: string;
  overallStatus: "Healthy" | "Warning" | "Error";
  summary: string;
  metrics: SystemHealthMetricsRecord;
  checks: SystemHealthCheckRecord[];
}

export interface SystemEventRecord {
  id: string;
  createdAtUtc: string;
  category: string;
  severity: "Info" | "Warning" | "Error";
  title: string;
  message: string;
  jobId?: string | null;
  packageId?: string | null;
  packageName?: string | null;
  machineId?: string | null;
  machineName?: string | null;
  actionPath?: string | null;
}

export interface SystemLogRecord {
  id: string;
  createdAtUtc: string;
  level: "Trace" | "Information" | "Warning" | "Error";
  source: string;
  message: string;
  payloadJson?: string | null;
  jobId: string;
  packageName?: string | null;
  machineName?: string | null;
  actionPath?: string | null;
}

export interface SystemLogFileRecord {
  id: string;
  name: string;
  displayName: string;
  sizeBytes: number;
  updatedAtUtc: string;
}

export interface SystemLogFileContentRecord {
  id: string;
  name: string;
  displayName: string;
  sizeBytes: number;
  updatedAtUtc: string;
  truncated: boolean;
  content: string;
}

export interface LibraryScanRecord {
  id: string;
  libraryRootId: string;
  rootDisplayName: string;
  rootPath: string;
  state: LibraryScanState;
  directoriesScanned: number;
  filesScanned: number;
  candidatesDetected: number;
  candidatesImported: number;
  errorsCount: number;
  summary: string;
  errorMessage?: string | null;
  startedAtUtc: string;
  completedAtUtc?: string | null;
}

export interface LibraryCandidateSourceRecord {
  label: string;
  path: string;
  mediaType: MediaType;
  sourceKind: PackageSourceKind;
  scratchPolicy: ScratchPolicy;
  discNumber?: number | null;
  entrypointHint?: string | null;
  hintFilePresent: boolean;
}

export interface MetadataMatchOptionRecord {
  key: string;
  provider: string;
  title: string;
  description: string;
  releaseYear?: number | null;
  studio: string;
  coverImagePath?: string | null;
  backdropImageUrl?: string | null;
  screenshotImageUrls: string[];
  sourceUrl?: string | null;
  genres: string[];
  themes: string[];
  platforms: string[];
  score: number;
  reasonSummary: string;
}

export interface ProviderDiagnosticRecord {
  provider: string;
  searchStatus: string;
  candidateCount: number;
  topScore?: number | null;
  isWinner: boolean;
  summary: string;
  topTitles: string[];
}

export interface LibraryCandidateRecord {
  id: string;
  libraryRootId: string;
  libraryScanId: string;
  packageId?: string | null;
  rootDisplayName: string;
  scanStartedAtUtc?: string | null;
  status: LibraryCandidateStatus;
  title: string;
  description: string;
  studio: string;
  releaseYear?: number | null;
  coverImagePath?: string | null;
  genres: string[];
  metadataProvider?: string | null;
  metadataSourceUrl?: string | null;
  confidenceScore: number;
  metadataStatus: string;
  metadataPrimarySource?: string | null;
  posterImageUrl?: string | null;
  backdropImageUrl?: string | null;
  storeDescription: string;
  primaryPath: string;
  sourceCount: number;
  hintFilePresent: boolean;
  installStrategy: string;
  isInstallable: boolean;
  recipeDiagnostics: string;
  matchDecision: string;
  matchSummary: string;
  winningSignals: string[];
  warningSignals: string[];
  providerDiagnostics: ProviderDiagnosticRecord[];
  alternativeMatches: MetadataMatchOptionRecord[];
  selectedMatchKey?: string | null;
  sourceConflicts: LibrarySourceConflictRecord[];
  sources: LibraryCandidateSourceRecord[];
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface BulkReMatchRecord {
  processedCount: number;
  autoImportedCount: number;
  nowReviewableCount: number;
  stillUnmatchedCount: number;
}

export interface ResetLibraryResult {
  packagesDeleted: number;
  candidatesDeleted: number;
  scansDeleted: number;
  rootsDeleted: number;
  jobsDeleted: number;
  normalizationJobsDeleted: number;
  mountsDeleted: number;
  normalizedAssetsDeleted: boolean;
  normalizedAssetRootPath?: string | null;
}

export interface ApiProblem {
  title?: string;
  detail?: string;
  status?: number;
}
