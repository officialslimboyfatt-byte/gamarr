import type {
  ApiProblem,
  BulkReMatchRecord,
  ResetLibraryResult,
  NormalizationJobRecord,
  JobRecord,
  LibraryCandidateRecord,
  LibraryRootRecord,
  LibraryScanRecord,
  LibraryReconcilePreviewRecord,
  ManualMetadataSearchRecord,
  MetadataSettingsRecord,
  MediaManagementSettingsRecord,
  SettingsRecord,
  NetworkSettingsRecord,
  SystemEventRecord,
  SystemHealthRecord,
  SystemLogFileContentRecord,
  SystemLogFileRecord,
  SystemLogRecord,
  LibraryTitleDetailRecord,
  LibraryTitleRecord,
  MachineMountRecord,
  MachineRecord,
  PackageRecord,
  PlayLibraryTitleResult
} from "../types/api";
import type { ApplyLibraryReconcileInput, ApplyManualMetadataMatchInput, CreateJobInput, CreateLibraryRootInput, CreatePackageInput, ManualMetadataSearchInput, MergeLibraryCandidateInput, ReplaceMergeTargetInput, SelectLibraryCandidateMatchInput, UpdateMediaManagementSettingsInput, UpdateMetadataSettingsInput, UpdatePackageInstallPlanInput, UpdatePackageMetadataInput } from "../types/requests";

const baseUrl = import.meta.env.VITE_API_BASE_URL ?? "";

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${baseUrl}${path}`, {
    headers: {
      "Content-Type": "application/json"
    },
    ...init
  });

  if (!response.ok) {
    let problem: ApiProblem | null = null;
    try {
      problem = (await response.json()) as ApiProblem;
    } catch {
      problem = null;
    }

    throw new Error(problem?.detail ?? problem?.title ?? `Request failed: ${response.status}`);
  }

  if (response.status === 204) {
    return null as T;
  }

  return (await response.json()) as T;
}

export const api = {
  getSettings: () => request<SettingsRecord>("/api/settings"),
  getMetadataSettings: () => request<MetadataSettingsRecord>("/api/settings/metadata"),
  updateMetadataSettings: (body: UpdateMetadataSettingsInput) =>
    request<MetadataSettingsRecord>("/api/settings/metadata", {
      method: "PUT",
      body: JSON.stringify(body)
    }),
  getMediaSettings: () => request<MediaManagementSettingsRecord>("/api/settings/media"),
  getNetworkSettings: () => request<NetworkSettingsRecord>("/api/settings/network"),
  updateMediaSettings: (body: UpdateMediaManagementSettingsInput) =>
    request<MediaManagementSettingsRecord>("/api/settings/media", {
      method: "PUT",
      body: JSON.stringify(body)
    }),
  listLibrary: (params?: { machineId?: string; genre?: string; studio?: string; year?: number; sortBy?: string }) => {
    const search = new URLSearchParams();
    if (params?.machineId) search.set("machineId", params.machineId);
    if (params?.genre) search.set("genre", params.genre);
    if (params?.studio) search.set("studio", params.studio);
    if (params?.year) search.set("year", params.year.toString());
    if (params?.sortBy) search.set("sortBy", params.sortBy);
    const query = search.toString();
    return request<LibraryTitleRecord[]>(`/api/library${query ? `?${query}` : ""}`);
  },
  getLibraryTitle: (id: string, machineId?: string) =>
    request<LibraryTitleDetailRecord>(`/api/library/${id}${machineId ? `?machineId=${encodeURIComponent(machineId)}` : ""}`),
  previewLibraryReconcile: (id: string) =>
    request<LibraryReconcilePreviewRecord>(`/api/library/${id}/re-match`, {
      method: "POST"
    }),
  applyLibraryReconcile: (id: string, body: ApplyLibraryReconcileInput, machineId?: string) =>
    request<LibraryTitleDetailRecord>(`/api/library/${id}/reconcile${machineId ? `?machineId=${encodeURIComponent(machineId)}` : ""}`, {
      method: "POST",
      body: JSON.stringify(body)
    }),
  searchLibraryMetadata: (id: string, body: ManualMetadataSearchInput) =>
    request<ManualMetadataSearchRecord>(`/api/library/${id}/search-metadata`, {
      method: "POST",
      body: JSON.stringify(body)
    }),
  applyLibraryMetadataSearch: (id: string, body: ApplyManualMetadataMatchInput, machineId?: string) =>
    request<LibraryTitleDetailRecord>(`/api/library/${id}/apply-metadata-search${machineId ? `?machineId=${encodeURIComponent(machineId)}` : ""}`, {
      method: "POST",
      body: JSON.stringify(body)
    }),
  archiveLibraryTitle: (id: string, reason?: string, machineId?: string) =>
    request<LibraryTitleDetailRecord>(`/api/library/${id}/archive${machineId ? `?machineId=${encodeURIComponent(machineId)}` : ""}`, {
      method: "POST",
      body: JSON.stringify({ reason: reason ?? null })
    }),
  restoreLibraryTitle: (id: string, machineId?: string) =>
    request<LibraryTitleDetailRecord>(`/api/library/${id}/restore${machineId ? `?machineId=${encodeURIComponent(machineId)}` : ""}`, {
      method: "POST"
    }),
  playLibraryTitle: (id: string, machineId: string) =>
    request<PlayLibraryTitleResult>(`/api/library/${id}/play`, {
      method: "POST",
      body: JSON.stringify({ machineId })
    }),
  validateLibraryTitle: (id: string, machineId: string) =>
    request<PlayLibraryTitleResult>(`/api/library/${id}/validate-install`, {
      method: "POST",
      body: JSON.stringify({ machineId })
    }),
  uninstallLibraryTitle: (id: string, machineId: string) =>
    request<PlayLibraryTitleResult>(`/api/library/${id}/uninstall`, {
      method: "POST",
      body: JSON.stringify({ machineId })
    }),
  markLibraryTitleNotInstalled: (id: string, machineId: string) =>
    request<LibraryTitleDetailRecord>(`/api/library/${id}/mark-not-installed`, {
      method: "POST",
      body: JSON.stringify({ machineId })
    }),
  listLibraryRoots: () => request<LibraryRootRecord[]>("/api/library-roots"),
  createLibraryRoot: (body: CreateLibraryRootInput) =>
    request<LibraryRootRecord>("/api/library-roots", {
      method: "POST",
      body: JSON.stringify(body)
    }),
  scanLibraryRoot: (id: string) =>
    request<LibraryScanRecord>(`/api/library-roots/${id}/scan`, {
      method: "POST"
    }),
  getLibraryScan: (id: string) => request<LibraryScanRecord>(`/api/library-scans/${id}`),
  cancelLibraryScan: (id: string) =>
    request<LibraryScanRecord>(`/api/library-scans/${id}/cancel`, {
      method: "POST"
    }),
  listLibraryScans: (params?: { rootId?: string; state?: string }) => {
    const search = new URLSearchParams();
    if (params?.rootId) search.set("rootId", params.rootId);
    if (params?.state) search.set("state", params.state);
    const query = search.toString();
    return request<LibraryScanRecord[]>(`/api/library-scans${query ? `?${query}` : ""}`);
  },
  listLibraryCandidates: (params?: { status?: string; rootId?: string; scanId?: string; search?: string }) => {
    const search = new URLSearchParams();
    if (params?.status) search.set("status", params.status);
    if (params?.rootId) search.set("rootId", params.rootId);
    if (params?.scanId) search.set("scanId", params.scanId);
    if (params?.search) search.set("search", params.search);
    const query = search.toString();
    return request<LibraryCandidateRecord[]>(`/api/library-candidates${query ? `?${query}` : ""}`);
  },
  approveLibraryCandidate: (id: string) =>
    request<LibraryCandidateRecord>(`/api/library-candidates/${id}/approve`, {
      method: "POST"
    }),
  rejectLibraryCandidate: (id: string) =>
    request<LibraryCandidateRecord>(`/api/library-candidates/${id}/reject`, {
      method: "POST"
    }),
  mergeLibraryCandidate: (id: string, body: MergeLibraryCandidateInput) =>
    request<LibraryCandidateRecord>(`/api/library-candidates/${id}/merge`, {
      method: "POST",
      body: JSON.stringify(body)
    }),
  unmergeLibraryCandidate: (id: string) =>
    request<LibraryCandidateRecord>(`/api/library-candidates/${id}/unmerge`, {
      method: "POST"
    }),
  replaceMergeTarget: (id: string, body: ReplaceMergeTargetInput) =>
    request<LibraryCandidateRecord>(`/api/library-candidates/${id}/replace-merge-target`, {
      method: "POST",
      body: JSON.stringify(body)
    }),
  selectLibraryCandidateMatch: (id: string, body: SelectLibraryCandidateMatchInput) =>
    request<LibraryCandidateRecord>(`/api/library-candidates/${id}/select-match`, {
      method: "POST",
      body: JSON.stringify(body)
    }),
  searchLibraryCandidateMetadata: (id: string, body: ManualMetadataSearchInput) =>
    request<ManualMetadataSearchRecord>(`/api/library-candidates/${id}/search-metadata`, {
      method: "POST",
      body: JSON.stringify(body)
    }),
  applyLibraryCandidateMetadataSearch: (id: string, body: ApplyManualMetadataMatchInput) =>
    request<LibraryCandidateRecord>(`/api/library-candidates/${id}/apply-metadata-search`, {
      method: "POST",
      body: JSON.stringify(body)
    }),
  listPackages: () => request<PackageRecord[]>("/api/packages"),
  getPackage: (id: string) => request<PackageRecord>(`/api/packages/${id}`),
  createPackage: (body: CreatePackageInput) =>
    request<PackageRecord>("/api/packages", {
      method: "POST",
      body: JSON.stringify(body)
    }),
  updatePackageMetadata: (id: string, body: UpdatePackageMetadataInput) =>
    request<PackageRecord>(`/api/packages/${id}/metadata`, {
      method: "PUT",
      body: JSON.stringify(body)
    }),
  updatePackageInstallPlan: (id: string, body: UpdatePackageInstallPlanInput) =>
    request<PackageRecord>(`/api/packages/${id}/install-plan`, {
      method: "PUT",
      body: JSON.stringify(body)
    }),
  listMachines: () => request<MachineRecord[]>("/api/machines"),
  getMachine: (id: string) => request<MachineRecord>(`/api/machines/${id}`),
  removeMachine: (id: string) =>
    request<void>(`/api/machines/${id}`, { method: "DELETE" }),
  getSystemHealth: () => request<SystemHealthRecord>("/api/system/health"),
  listSystemEvents: (params?: { category?: string; severity?: string; machineId?: string; search?: string; limit?: number }) => {
    const search = new URLSearchParams();
    if (params?.category) search.set("category", params.category);
    if (params?.severity) search.set("severity", params.severity);
    if (params?.machineId) search.set("machineId", params.machineId);
    if (params?.search) search.set("search", params.search);
    if (params?.limit) search.set("limit", params.limit.toString());
    const query = search.toString();
    return request<SystemEventRecord[]>(`/api/system/events${query ? `?${query}` : ""}`);
  },
  listSystemLogs: (params?: { level?: string; source?: string; machineId?: string; search?: string; limit?: number }) => {
    const search = new URLSearchParams();
    if (params?.level) search.set("level", params.level);
    if (params?.source) search.set("source", params.source);
    if (params?.machineId) search.set("machineId", params.machineId);
    if (params?.search) search.set("search", params.search);
    if (params?.limit) search.set("limit", params.limit.toString());
    const query = search.toString();
    return request<SystemLogRecord[]>(`/api/system/logs${query ? `?${query}` : ""}`);
  },
  listSystemLogFiles: () => request<SystemLogFileRecord[]>("/api/system/log-files"),
  getSystemLogFile: (id: string, tailLines = 200) =>
    request<SystemLogFileContentRecord>(`/api/system/log-files/${encodeURIComponent(id)}?tailLines=${tailLines}`),
  installWinCDEmu: (machineId: string) =>
    request<JobRecord>(`/api/machines/${machineId}/install-wincdemu`, { method: "POST" }),
  listMounts: (machineId: string) => request<MachineMountRecord[]>(`/api/machines/${machineId}/mounts`),
  listJobs: (params?: { machineId?: string; state?: string; actionType?: string; search?: string; scope?: string }) => {
    const search = new URLSearchParams();
    if (params?.machineId) search.set("machineId", params.machineId);
    if (params?.state) search.set("state", params.state);
    if (params?.actionType) search.set("actionType", params.actionType);
    if (params?.search) search.set("search", params.search);
    if (params?.scope) search.set("scope", params.scope);
    const query = search.toString();
    return request<JobRecord[]>(`/api/jobs${query ? `?${query}` : ""}`);
  },
  getJob: (id: string) => request<JobRecord>(`/api/jobs/${id}`),
  createJob: (body: CreateJobInput) =>
    request<JobRecord>("/api/jobs", {
      method: "POST",
      body: JSON.stringify(body)
    }),
  cancelJob: (id: string) =>
    request<JobRecord>(`/api/jobs/${id}/cancel`, { method: "POST" }),
  bulkReMatchLibrary: () =>
    request<BulkReMatchRecord>("/api/library/re-match-unmatched", { method: "POST" }),
  resetLibrary: (body?: { preserveRoots?: boolean; deleteNormalizedAssets?: boolean }) =>
    request<ResetLibraryResult>("/api/library/reset", {
      method: "POST",
      body: JSON.stringify({
        preserveRoots: body?.preserveRoots ?? true,
        deleteNormalizedAssets: body?.deleteNormalizedAssets ?? true
      })
    }),
  reNormalizePackage: (id: string) =>
    request<PackageRecord>(`/api/packages/${id}/normalize`, { method: "POST" }),
  bulkReNormalizeNeedsReview: () =>
    request<number>("/api/packages/normalize-needs-review", { method: "POST" }),
  listNormalizationJobs: (params?: { packageId?: string; state?: string }) => {
    const search = new URLSearchParams();
    if (params?.packageId) search.set("packageId", params.packageId);
    if (params?.state) search.set("state", params.state);
    const query = search.toString();
    return request<NormalizationJobRecord[]>(`/api/packages/normalization-jobs${query ? `?${query}` : ""}`);
  },
  quickInstall: (body: { isoPath: string; machineId: string; label?: string }) =>
    request<{ jobId: string; packageId: string }>("/api/jobs/quick-install", {
      method: "POST",
      body: JSON.stringify(body)
    }),
  createMount: (machineId: string, isoPath: string) =>
    request<MachineMountRecord>(`/api/machines/${machineId}/mounts`, {
      method: "POST",
      body: JSON.stringify({ isoPath })
    }),
  getMount: (machineId: string, mountId: string) =>
    request<MachineMountRecord>(`/api/machines/${machineId}/mounts/${mountId}`),
  requestDismount: (machineId: string, mountId: string) =>
    request<MachineMountRecord>(`/api/machines/${machineId}/mounts/${mountId}/dismount`, { method: "POST" })
};
