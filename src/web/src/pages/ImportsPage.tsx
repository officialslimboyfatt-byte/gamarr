import { type FormEvent, useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api } from "../api/client";
import { PageHeader } from "../components/PageHeader";
import { PageState } from "../components/PageState";
import { StatusPill } from "../components/StatusPill";
import { useAsyncData } from "../hooks/useAsyncData";
import { usePollingAsyncData } from "../hooks/usePollingAsyncData";
import type { LibraryCandidateRecord } from "../types/api";

function relative(value?: string | null) {
  if (!value) return "Never";
  const diffMinutes = Math.max(0, Math.round((Date.now() - new Date(value).getTime()) / 60000));
  if (diffMinutes < 1) return "Just now";
  if (diffMinutes < 60) return `${diffMinutes}m ago`;
  if (diffMinutes < 24 * 60) return `${Math.round(diffMinutes / 60)}h ago`;
  return `${Math.round(diffMinutes / 1440)}d ago`;
}

function CandidateRowSummary({ candidate }: { candidate: LibraryCandidateRecord }) {
  return (
    <div className="table-title-with-poster">
      {candidate.posterImageUrl ?? candidate.coverImagePath ? (
        <img className="table-poster-thumb" src={candidate.posterImageUrl ?? candidate.coverImagePath ?? undefined} alt={`${candidate.title} poster`} />
      ) : (
        <div className="table-poster-thumb fallback">{candidate.title.slice(0, 3).toUpperCase()}</div>
      )}
      <div className="table-title-cell">
        <strong>{candidate.title}</strong>
        <span>{candidate.primaryPath}</span>
        {candidate.hintFilePresent ? <span>Hint file: `gamarr.json`</span> : null}
        <span>{candidate.matchSummary}</span>
      </div>
    </div>
  );
}

function providerDiagnosticSummary(candidate: LibraryCandidateRecord) {
  if (candidate.providerDiagnostics.length === 0) {
    return candidate.warningSignals[0] ?? candidate.winningSignals[0] ?? "No diagnostics";
  }

  return candidate.providerDiagnostics
    .map((diagnostic) => `${diagnostic.provider}: ${diagnostic.summary}`)
    .join(" | ");
}

export function ImportsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const selectedRootId = searchParams.get("rootId") ?? "";
  const candidateSearch = searchParams.get("search") ?? "";
  const [displayName, setDisplayName] = useState("");
  const [path, setPath] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [scanningRootId, setScanningRootId] = useState<string | null>(null);
  const [cancellingScanId, setCancellingScanId] = useState<string | null>(null);
  const [updatingCandidateId, setUpdatingCandidateId] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [mergeTarget, setMergeTarget] = useState<Record<string, string>>({});
  const [selectedMatchChoice, setSelectedMatchChoice] = useState<Record<string, string>>({});
  const [manualMetadataQuery, setManualMetadataQuery] = useState<Record<string, string>>({});
  const [manualMetadataResult, setManualMetadataResult] = useState<Record<string, Awaited<ReturnType<typeof api.searchLibraryCandidateMetadata>> | null>>({});
  const [searchingMetadataId, setSearchingMetadataId] = useState<string | null>(null);

  const mediaSettingsState = useAsyncData(() => api.getMediaSettings(), []);
  const rootsState = useAsyncData(() => api.listLibraryRoots(), []);
  const scansState = useAsyncData(
    () => api.listLibraryScans({ rootId: selectedRootId || undefined }),
    [selectedRootId]
  );
  const roots = rootsState.data ?? [];
  const scans = scansState.data ?? [];
  const selectedRoot = roots.find((root) => root.id === selectedRootId) ?? null;
  const latestScan = selectedRootId ? scans[0] ?? null : null;
  const isRunning = latestScan?.state === "Running";

  // Poll the latest scan every 2s while it is Running so progress is live.
  const liveScanState = usePollingAsyncData(
    () => latestScan ? api.getLibraryScan(latestScan.id) : Promise.resolve(null),
    [latestScan?.id],
    2000,
    isRunning
  );

  // Merge live scan data into the scans list for display.
  const displayScans = useMemo(() => {
    if (!liveScanState.data || !isRunning) return scans;
    return scans.map((s) => (s.id === liveScanState.data!.id ? liveScanState.data! : s));
  }, [scans, liveScanState.data, isRunning]);

  // When the live scan transitions to Completed/Failed, reload everything.
  useEffect(() => {
    if (liveScanState.data && liveScanState.data.state !== "Running") {
      void reloadAll();
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [liveScanState.data?.state]);

  const candidatesState = useAsyncData(
    () =>
      api.listLibraryCandidates({
        status: "PendingReview",
        rootId: selectedRootId || undefined,
        scanId: latestScan?.id,
        search: candidateSearch || undefined
      }),
    [selectedRootId, latestScan?.id, candidateSearch]
  );

  const mergedCandidatesState = useAsyncData(
    () =>
      api.listLibraryCandidates({
        status: "Merged",
        rootId: selectedRootId || undefined,
        scanId: latestScan?.id,
        search: candidateSearch || undefined
      }),
    [selectedRootId, latestScan?.id, candidateSearch]
  );

  const packagesState = useAsyncData(() => api.listPackages(), []);

  const candidates = candidatesState.data ?? [];
  const mergedCandidates = mergedCandidatesState.data ?? [];
  const packages = packagesState.data ?? [];

  useEffect(() => {
    if (!displayName && !path && mediaSettingsState.data?.defaultLibraryRootPath) {
      setPath(mediaSettingsState.data.defaultLibraryRootPath);
    }
  }, [displayName, path, mediaSettingsState.data]);

  function updateParam(key: string, value: string) {
    const next = new URLSearchParams(searchParams);
    if (value) next.set(key, value);
    else next.delete(key);
    setSearchParams(next);
  }

  async function reloadAll() {
    await Promise.all([rootsState.reload(), scansState.reload(), candidatesState.reload(), mergedCandidatesState.reload()]);
  }

  async function onCreateRoot(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    setFeedback(null);

    try {
      const created = await api.createLibraryRoot({ displayName, path });
      const autoScan = mediaSettingsState.data?.autoScanOnRootCreate ?? false;
      setDisplayName("");
      setPath(mediaSettingsState.data?.defaultLibraryRootPath ?? "");
      updateParam("rootId", created.id);
      if (autoScan) {
        const scan = await api.scanLibraryRoot(created.id);
        setFeedback(`Library root saved and scanned: ${scan.candidatesDetected} candidate(s), ${scan.candidatesImported} imported.`);
      } else {
        setFeedback("Library root saved.");
      }
      await reloadAll();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to create library root.");
    } finally {
      setSubmitting(false);
    }
  }

  async function onCancelScan(scanId: string) {
    setCancellingScanId(scanId);
    setError(null);
    try {
      await api.cancelLibraryScan(scanId);
      await Promise.all([rootsState.reload(), scansState.reload()]);
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to cancel scan.");
    } finally {
      setCancellingScanId(null);
    }
  }

  async function onScanRoot(rootId: string) {
    setScanningRootId(rootId);
    setError(null);
    setFeedback(null);

    try {
      await api.scanLibraryRoot(rootId);
      updateParam("rootId", rootId);
      setFeedback("Scan started. Progress will update automatically.");
      await scansState.reload();
      await rootsState.reload();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to scan library root.");
    } finally {
      setScanningRootId(null);
    }
  }

  async function onApprove(candidateId: string) {
    setUpdatingCandidateId(candidateId);
    setError(null);
    try {
      await api.approveLibraryCandidate(candidateId);
      await reloadAll();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to approve candidate.");
    } finally {
      setUpdatingCandidateId(null);
    }
  }

  async function onReject(candidateId: string) {
    setUpdatingCandidateId(candidateId);
    setError(null);
    try {
      await api.rejectLibraryCandidate(candidateId);
      await reloadAll();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to reject candidate.");
    } finally {
      setUpdatingCandidateId(null);
    }
  }

  async function onMerge(candidateId: string) {
    const packageId = mergeTarget[candidateId];
    if (!packageId) {
      setError("Choose an existing package before merging.");
      return;
    }

    setUpdatingCandidateId(candidateId);
    setError(null);
    try {
      await api.mergeLibraryCandidate(candidateId, { packageId });
      await reloadAll();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to merge candidate.");
    } finally {
      setUpdatingCandidateId(null);
    }
  }

  async function onSelectMatch(candidateId: string) {
    const choice = selectedMatchChoice[candidateId];
    if (!choice) {
      setError("Choose a metadata option or Local Only before applying.");
      return;
    }

    setUpdatingCandidateId(candidateId);
    setError(null);
    try {
      if (choice === "__local__") {
        await api.selectLibraryCandidateMatch(candidateId, {
          matchKey: null,
          localOnly: true
        });
      } else if (manualMetadataResult[candidateId]?.alternativeMatches.some((match) => match.key === choice)) {
        await api.applyLibraryCandidateMetadataSearch(candidateId, {
          query: manualMetadataQuery[candidateId] || "",
          matchKey: choice
        });
      } else {
        await api.selectLibraryCandidateMatch(candidateId, {
          matchKey: choice,
          localOnly: false
        });
      }
      await reloadAll();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to update metadata selection.");
    } finally {
      setUpdatingCandidateId(null);
    }
  }

  async function onSearchCandidateMetadata(candidateId: string, fallbackTitle: string) {
    const query = manualMetadataQuery[candidateId]?.trim() || fallbackTitle;
    setSearchingMetadataId(candidateId);
    setError(null);
    try {
      const result = await api.searchLibraryCandidateMetadata(candidateId, { query });
      setManualMetadataResult((current) => ({ ...current, [candidateId]: result }));
      setManualMetadataQuery((current) => ({ ...current, [candidateId]: query }));
      setSelectedMatchChoice((current) => ({ ...current, [candidateId]: result.alternativeMatches[0]?.key ?? current[candidateId] ?? "" }));
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to search metadata.");
    } finally {
      setSearchingMetadataId(null);
    }
  }

  async function onUnmerge(candidateId: string) {
    setUpdatingCandidateId(candidateId);
    setError(null);
    try {
      await api.unmergeLibraryCandidate(candidateId);
      await reloadAll();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to unmerge candidate.");
    } finally {
      setUpdatingCandidateId(null);
    }
  }

  async function onReplaceTarget(candidateId: string, currentPackageId?: string | null) {
    const packageId = mergeTarget[candidateId] ?? currentPackageId ?? "";
    if (!packageId) {
      setError("Choose a replacement package before updating the merge target.");
      return;
    }

    setUpdatingCandidateId(candidateId);
    setError(null);
    try {
      await api.replaceMergeTarget(candidateId, { packageId });
      await reloadAll();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to replace merge target.");
    } finally {
      setUpdatingCandidateId(null);
    }
  }

  const candidatePackageOptions = useMemo(() => {
    return [...packages].sort((left, right) => left.name.localeCompare(right.name));
  }, [packages]);

  return (
    <div className="page-grid">
      <PageHeader
        eyebrow="Import"
        title="Add New"
        description="Add local folders or UNC shares, run scans, review metadata, and correct bad merges."
        actions={
          <div className="page-actions">
            <button type="button" className="secondary-button inline-button" onClick={() => void reloadAll()}>
              Refresh
            </button>
            <Link className="secondary-button inline-button" to="/settings">
              Import Defaults
            </Link>
            <Link className="secondary-button inline-button" to="/">
              View Library
            </Link>
          </div>
        }
      />

      {error ? <div className="inline-error">{error}</div> : null}
      {feedback ? <div className="inline-success">{feedback}</div> : null}

      <div className="table-card">
        <div className="table-section-header">
          <div>
            <h3>1. Choose Source</h3>
            <p>Register a local path or UNC share, then choose which root to work from.</p>
          </div>
        </div>
        <div className="two-column shell-panels">
          <section className="toolbar-card">
            <h3>Add Root</h3>
            <form className="form compact-form" onSubmit={onCreateRoot}>
              <input value={displayName} onChange={(event) => setDisplayName(event.target.value)} placeholder="Archive Name" />
              <input value={path} onChange={(event) => setPath(event.target.value)} placeholder="\\\\NAS\\Games or D:\\Games" />
              <button type="submit" disabled={submitting || !displayName.trim() || !path.trim()}>
                {submitting ? "Saving..." : "Add Root"}
              </button>
            </form>
          </section>
          <section className="toolbar-card">
            <h3>Current Root</h3>
            {selectedRoot ? (
              <div className="detail-grid">
                <div>
                  <label>Name</label>
                  <p>{selectedRoot.displayName}</p>
                </div>
                <div>
                  <label>Health</label>
                  <p>{selectedRoot.healthSummary}</p>
                </div>
                <div>
                  <label>Kind</label>
                  <p>{selectedRoot.pathKind} / {selectedRoot.contentKind}</p>
                </div>
                <div>
                  <label>Last Scan</label>
                  <p>{selectedRoot.lastScanState ?? "Never scanned"}</p>
                </div>
              </div>
            ) : (
              <PageState title="No root selected" description="Choose a root below to drive scan and review." />
            )}
          </section>
        </div>
        {rootsState.loading ? (
          <PageState title="Loading roots" description="Fetching registered import roots." tone="loading" />
        ) : roots.length === 0 ? (
          <PageState title="No roots configured" description="Add a root above to start importing." />
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Path</th>
                <th>Kind</th>
                <th>Health</th>
                <th>Last Scan</th>
                <th className="actions-column">Actions</th>
              </tr>
            </thead>
            <tbody>
              {roots.map((root) => (
                <tr key={root.id}>
                  <td>{root.displayName}</td>
                  <td>{root.path}</td>
                  <td>{root.pathKind} / {root.contentKind}</td>
                  <td>
                    <StatusPill label={root.isReachable ? "Reachable" : "Unavailable"} tone={root.isReachable ? "success" : "danger"} />
                  </td>
                  <td>{root.lastScanState ?? "Never"} {root.lastScanCompletedAtUtc ? `| ${relative(root.lastScanCompletedAtUtc)}` : ""}</td>
                  <td className="actions-column">
                    <div className="table-actions">
                      <button type="button" className="secondary-button inline-button" onClick={() => updateParam("rootId", root.id)}>
                        {selectedRootId === root.id ? "Selected" : "Select"}
                      </button>
                      <button type="button" onClick={() => void onScanRoot(root.id)} disabled={scanningRootId === root.id}>
                        {scanningRootId === root.id ? "Scanning..." : "Scan"}
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <div className="table-card">
        <div className="table-section-header">
          <div>
            <h3>2. Run Scan</h3>
            <p>
              {isRunning
                ? "Scan in progress — updating every 2 seconds."
                : "Review current and recent scans for the selected root."}
            </p>
          </div>
          {isRunning ? <span className="header-chip">Live</span> : null}
        </div>
        {scansState.loading ? (
          <PageState title="Loading scans" description="Fetching recent scan runs." tone="loading" />
        ) : displayScans.length === 0 ? (
          <PageState title="No scans yet" description="Run your first import scan to populate results." />
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Root</th>
                <th>Status</th>
                <th>Progress</th>
                <th>Started</th>
                <th>Completed</th>
                <th className="actions-column">Actions</th>
              </tr>
            </thead>
            <tbody>
              {displayScans.slice(0, 10).map((scan) => (
                <tr key={scan.id}>
                  <td>{scan.rootDisplayName}</td>
                  <td>
                    <StatusPill label={scan.state} tone={scan.state === "Completed" ? "success" : scan.state === "Failed" ? "danger" : "warning"} />
                  </td>
                  <td>
                    <div className="table-title-cell">
                      <strong>{scan.summary}</strong>
                      {scan.state === "Running" ? (
                        <span>{scan.directoriesScanned} dirs · {scan.filesScanned} files{scan.candidatesDetected > 0 ? ` · ${scan.candidatesDetected} candidates` : ""}</span>
                      ) : (
                        <span>{scan.candidatesDetected} candidate(s) · {scan.candidatesImported} imported · {scan.errorsCount} error(s)</span>
                      )}
                    </div>
                  </td>
                  <td>{new Date(scan.startedAtUtc).toLocaleString()}</td>
                  <td>{scan.completedAtUtc ? new Date(scan.completedAtUtc).toLocaleString() : "—"}</td>
                  <td className="actions-column">
                    {scan.state === "Running" ? (
                      <button
                        type="button"
                        className="secondary-button inline-button"
                        onClick={() => void onCancelScan(scan.id)}
                        disabled={cancellingScanId === scan.id}
                      >
                        {cancellingScanId === scan.id ? "Cancelling..." : "Cancel"}
                      </button>
                    ) : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <div className="table-card">
        <div className="table-section-header">
          <div>
            <h3>3. Review Matches</h3>
            <p>Approve, merge, or reject candidates that still need human confirmation.</p>
          </div>
        </div>
        <div className="toolbar-card compact-toolbar">
          <div className="table-toolbar activity-toolbar">
            <input value={candidateSearch} onChange={(event) => updateParam("search", event.target.value)} placeholder="Search candidates" />
          </div>
        </div>
        {candidatesState.loading ? (
          <PageState title="Loading review queue" description="Fetching discovered candidates." tone="loading" />
        ) : candidates.length === 0 ? (
          <PageState title="No pending candidates" description="Everything discovered is either imported, merged, or cleared." />
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Title</th>
                <th>Strategy</th>
                <th>Metadata</th>
                <th>Root</th>
                <th>Sources</th>
                <th>Match</th>
                <th className="actions-column">Actions</th>
              </tr>
            </thead>
            <tbody>
              {candidates.map((candidate) => (
                <tr key={candidate.id}>
                  <td>
                    <CandidateRowSummary candidate={candidate} />
                  </td>
                  <td>
                    <div className="table-title-cell">
                      <strong>{candidate.installStrategy}</strong>
                      <span>{candidate.isInstallable ? "Installable" : "Review required"}</span>
                      <span>{candidate.sourceConflicts.length ? `${candidate.sourceConflicts.length} source conflict(s)` : "No source conflicts"}</span>
                    </div>
                  </td>
                  <td>
                    <div className="table-title-cell">
                      <strong>{candidate.studio || "Unknown studio"}{candidate.releaseYear ? ` | ${candidate.releaseYear}` : ""}</strong>
                      <span>{candidate.metadataStatus}{candidate.metadataPrimarySource ? ` | ${candidate.metadataPrimarySource}` : ""}</span>
                      <span>{providerDiagnosticSummary(candidate)}</span>
                    </div>
                  </td>
                  <td>
                    <div className="table-title-cell">
                      <strong>{candidate.rootDisplayName}</strong>
                      <span>{candidate.scanStartedAtUtc ? relative(candidate.scanStartedAtUtc) : "Unknown scan"}</span>
                    </div>
                  </td>
                  <td>{candidate.sourceCount}</td>
                  <td>
                    <div className="table-title-cell">
                      <strong>{candidate.matchDecision} | {(candidate.confidenceScore * 100).toFixed(0)}%</strong>
                      <span>{candidate.recipeDiagnostics}</span>
                      <span>{(manualMetadataResult[candidate.id]?.providerDiagnostics[0]?.topTitles ?? candidate.providerDiagnostics[0]?.topTitles)?.join(", ") || "No provider candidates recorded."}</span>
                      <div className="table-actions">
                        <input
                          value={manualMetadataQuery[candidate.id] ?? candidate.title}
                          onChange={(event) => setManualMetadataQuery((current) => ({ ...current, [candidate.id]: event.target.value }))}
                          placeholder="Search providers for a better match"
                        />
                        <button
                          type="button"
                          className="secondary-button inline-button"
                          onClick={() => void onSearchCandidateMetadata(candidate.id, candidate.title)}
                          disabled={searchingMetadataId === candidate.id}
                        >
                          {searchingMetadataId === candidate.id ? "Searching..." : "Search Metadata"}
                        </button>
                      </div>
                      <select
                        value={selectedMatchChoice[candidate.id] ?? candidate.selectedMatchKey ?? ""}
                        onChange={(event) => setSelectedMatchChoice((current) => ({ ...current, [candidate.id]: event.target.value }))}
                      >
                        <option value="">Choose metadata...</option>
                        {(manualMetadataResult[candidate.id]?.alternativeMatches ?? candidate.alternativeMatches).map((match) => (
                          <option key={match.key} value={match.key}>
                            {match.title}{match.releaseYear ? ` (${match.releaseYear})` : ""} | {(match.score * 100).toFixed(0)}%
                          </option>
                        ))}
                        <option value="__local__">Keep Local Only</option>
                      </select>
                      {manualMetadataResult[candidate.id] ? <span>{manualMetadataResult[candidate.id]!.matchSummary}</span> : null}
                    </div>
                  </td>
                  <td className="actions-column">
                    <div className="table-actions import-actions">
                      <button type="button" onClick={() => void onApprove(candidate.id)} disabled={updatingCandidateId === candidate.id}>
                        {updatingCandidateId === candidate.id ? "Working..." : "Approve"}
                      </button>
                      <button
                        type="button"
                        className="secondary-button inline-button"
                        onClick={() => void onSelectMatch(candidate.id)}
                        disabled={updatingCandidateId === candidate.id || !(selectedMatchChoice[candidate.id] ?? candidate.selectedMatchKey)}
                      >
                        Use Match
                      </button>
                      <select
                        value={mergeTarget[candidate.id] ?? ""}
                        onChange={(event) => setMergeTarget((current) => ({ ...current, [candidate.id]: event.target.value }))}
                      >
                        <option value="">Merge into package...</option>
                        {candidatePackageOptions.map((pkg) => (
                          <option key={pkg.id} value={pkg.id}>
                            {pkg.name}
                          </option>
                        ))}
                      </select>
                      <button type="button" className="secondary-button inline-button" onClick={() => void onMerge(candidate.id)} disabled={updatingCandidateId === candidate.id}>
                        Merge
                      </button>
                      <button type="button" className="secondary-button inline-button" onClick={() => void onReject(candidate.id)} disabled={updatingCandidateId === candidate.id}>
                        Reject
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <div className="table-card">
        <div className="table-section-header">
          <div>
            <h3>4. Resolve Merges</h3>
            <p>Detach candidates from the wrong package or redirect them to a better merge target.</p>
          </div>
        </div>
        {mergedCandidatesState.loading ? (
          <PageState title="Loading merged candidates" description="Fetching current merge results for the selected scan." tone="loading" />
        ) : mergedCandidates.length === 0 ? (
          <PageState title="No merged candidates" description="The latest scan has not merged any candidates into existing library titles." />
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Candidate</th>
                <th>Current Target</th>
                <th>Conflicts</th>
                <th>Metadata</th>
                <th className="actions-column">Actions</th>
              </tr>
            </thead>
            <tbody>
              {mergedCandidates.map((candidate) => {
                const currentTarget = candidatePackageOptions.find((pkg) => pkg.id === candidate.packageId);
                return (
                  <tr key={candidate.id}>
                    <td>
                      <CandidateRowSummary candidate={candidate} />
                    </td>
                    <td>
                      <div className="table-title-cell">
                        <strong>{currentTarget?.name ?? "Unknown target"}</strong>
                        <span>{candidate.packageId ?? "No target package id"}</span>
                      </div>
                    </td>
                    <td>
                      <div className="table-title-cell">
                        <strong>{candidate.sourceConflicts.length ? `${candidate.sourceConflicts.length} conflict(s)` : "No conflicts"}</strong>
                        <span>{candidate.sourceConflicts[0] ? `${candidate.sourceConflicts[0].conflictType} | ${candidate.sourceConflicts[0].packageName}` : "No duplicate source paths detected."}</span>
                      </div>
                    </td>
                    <td>
                      <div className="table-title-cell">
                        <strong>{candidate.matchDecision} | {(candidate.confidenceScore * 100).toFixed(0)}%</strong>
                        <span>{providerDiagnosticSummary(candidate)}</span>
                      </div>
                    </td>
                    <td className="actions-column">
                      <div className="table-actions import-actions">
                        {candidate.packageId ? (
                          <Link className="secondary-button inline-button" to={`/library/${candidate.packageId}`}>
                            Open Target
                          </Link>
                        ) : null}
                        <button type="button" className="secondary-button inline-button" onClick={() => void onUnmerge(candidate.id)} disabled={updatingCandidateId === candidate.id}>
                          Unmerge
                        </button>
                        <select
                          value={mergeTarget[candidate.id] ?? candidate.packageId ?? ""}
                          onChange={(event) => setMergeTarget((current) => ({ ...current, [candidate.id]: event.target.value }))}
                        >
                          <option value="">Replace merge target...</option>
                          {candidatePackageOptions.map((pkg) => (
                            <option key={pkg.id} value={pkg.id}>
                              {pkg.name}
                            </option>
                          ))}
                        </select>
                        <button
                          type="button"
                          className="secondary-button inline-button"
                          onClick={() => void onReplaceTarget(candidate.id, candidate.packageId)}
                          disabled={updatingCandidateId === candidate.id || !(mergeTarget[candidate.id] ?? candidate.packageId)}
                        >
                          Replace Target
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
