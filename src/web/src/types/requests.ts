import type { ArchitectureKind, InstallScriptKind, MediaType, PackageSourceKind, ScratchPolicy } from "./api";

export interface CreatePackageInput {
  slug: string;
  name: string;
  description: string;
  notes: string;
  tags: string[];
  genres: string[];
  studio: string;
  releaseYear?: number | null;
  coverImagePath?: string | null;
  version: {
    versionLabel: string;
    supportedOs: string;
    architecture: ArchitectureKind;
    installScriptKind: InstallScriptKind;
    installScriptPath: string;
    uninstallScriptPath?: string | null;
    uninstallArguments?: string | null;
    timeoutSeconds: number;
    notes: string;
    installStrategy: string;
    installerFamily: string;
    installerPath?: string | null;
    silentArguments?: string | null;
    installDiagnostics: string;
    launchExecutablePath?: string | null;
    media: Array<{
      mediaType: MediaType;
      label: string;
      path: string;
      discNumber?: number | null;
      entrypointHint?: string | null;
      sourceKind: PackageSourceKind;
      scratchPolicy: ScratchPolicy;
    }>;
    detectionRules: Array<{
      ruleType: string;
      value: string;
    }>;
    prerequisites: Array<{
      name: string;
      notes: string;
    }>;
  };
}

export interface UpdatePackageMetadataInput {
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
}

export interface UpdatePackageInstallPlanInput {
  installStrategy: string;
  installerFamily: string;
  installerPath?: string | null;
  silentArguments?: string | null;
  installDiagnostics: string;
  launchExecutablePath?: string | null;
  uninstallScriptPath?: string | null;
  uninstallArguments?: string | null;
  detectionRules: Array<{
    ruleType: string;
    value: string;
  }>;
}

export interface CreateJobInput {
  packageId: string;
  machineId: string;
  actionType: "Install" | "Launch" | "Validate" | "Uninstall";
  requestedBy: string;
}

export interface CreateLibraryRootInput {
  displayName: string;
  path: string;
}

export interface MergeLibraryCandidateInput {
  packageId: string;
}

export interface SelectLibraryCandidateMatchInput {
  matchKey?: string | null;
  localOnly: boolean;
}

export interface ApplyLibraryReconcileInput {
  matchKey?: string | null;
  localOnly: boolean;
}

export interface ManualMetadataSearchInput {
  query: string;
}

export interface ApplyManualMetadataMatchInput {
  query: string;
  matchKey: string;
}

export interface ReplaceMergeTargetInput {
  packageId: string;
}

export interface UpdateMetadataSettingsInput {
  preferIgdb: boolean;
  igdbEnabled: boolean;
  igdbClientId?: string | null;
  igdbClientSecret?: string | null;
  clearIgdbClientSecret: boolean;
  useSteamFallback: boolean;
  autoImportThreshold: number;
  reviewThreshold: number;
}

export interface UpdateMediaManagementSettingsInput {
  defaultLibraryRootPath?: string | null;
  normalizedAssetRootPath?: string | null;
  autoScanOnRootCreate: boolean;
  autoNormalizeOnImport: boolean;
  autoImportHighConfidenceMatches: boolean;
  includePatterns: string[];
  excludePatterns: string[];
}
