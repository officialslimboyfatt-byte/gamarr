import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api } from "../api/client";
import { InstallProgress } from "../components/InstallProgress";
import { PageHeader } from "../components/PageHeader";
import { PageState } from "../components/PageState";
import { StatusPill } from "../components/StatusPill";
import { useSelectedMachine } from "../context/SelectedMachineContext";
import { usePollingAsyncData } from "../hooks/usePollingAsyncData";
import type { JobRecord, LibraryTitleRecord } from "../types/api";

interface EditForm {
  name: string;
  slug: string;
  description: string;
  notes: string;
  studio: string;
  releaseYear: string;
  genres: string;
  coverImagePath: string;
  metadataProvider: string;
  metadataSourceUrl: string;
  metadataSelectionKind: string;
}

function titleToForm(title: LibraryTitleRecord): EditForm {
  return {
    name: title.name,
    slug: title.slug,
    description: title.description,
    notes: title.notes,
    studio: title.studio,
    releaseYear: title.releaseYear?.toString() ?? "",
    genres: title.genres.join(", "),
    coverImagePath: title.coverImagePath ?? "",
    metadataProvider: title.metadataProvider ?? "",
    metadataSourceUrl: title.metadataSourceUrl ?? "",
    metadataSelectionKind: title.metadataSelectionKind,
  };
}

function relative(value?: string | null) {
  if (!value) return "Unknown";
  const diffMinutes = Math.max(0, Math.round((Date.now() - new Date(value).getTime()) / 60000));
  if (diffMinutes < 1) return "Just now";
  if (diffMinutes < 60) return `${diffMinutes}m ago`;
  if (diffMinutes < 24 * 60) return `${Math.round(diffMinutes / 60)}h ago`;
  return `${Math.round(diffMinutes / 1440)}d ago`;
}

export function LibraryTitleDetailPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const { selectedMachineId, selectedMachine } = useSelectedMachine();
  const [reconcileError, setReconcileError] = useState<string | null>(null);
  const [reconcilePreview, setReconcilePreview] = useState<Awaited<ReturnType<typeof api.previewLibraryReconcile>> | null>(null);
  const [selectedMatchKey, setSelectedMatchKey] = useState<string>("");
  const [manualSearchQuery, setManualSearchQuery] = useState("");
  const [manualSearchResult, setManualSearchResult] = useState<Awaited<ReturnType<typeof api.searchLibraryMetadata>> | null>(null);
  const [reconcileBusy, setReconcileBusy] = useState(false);
  const [editForm, setEditForm] = useState<EditForm | null>(null);
  const [editSaving, setEditSaving] = useState(false);
  const [editError, setEditError] = useState<string | null>(null);
  const [activeJobId, setActiveJobId] = useState<string | null>(null);
  const [activeJob, setActiveJob] = useState<JobRecord | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const state = usePollingAsyncData(
    () => api.getLibraryTitle(id ?? "", selectedMachineId || undefined),
    [id, selectedMachineId],
    10000,
    Boolean(id)
  );

  useEffect(() => {
    if (!activeJobId) return;
    const interval = setInterval(async () => {
      try {
        const job = await api.getJob(activeJobId);
        setActiveJob(job);
        if (job.state === "Completed" || job.state === "Failed" || job.state === "Cancelled") {
          clearInterval(interval);
          void state.reload();
          if (job.state === "Completed") {
            setTimeout(() => { setActiveJob(null); setActiveJobId(null); }, 2500);
          }
        }
      } catch { /* ignore poll errors */ }
    }, 3000);
    return () => clearInterval(interval);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeJobId, state.reload]);

  useEffect(() => {
    if (!state.data || !selectedMachineId || activeJobId) {
      return;
    }

    if (state.data.title.installState !== "Installed" || !state.data.title.isInstallStateStale) {
      return;
    }

    void (async () => {
      try {
        const result = await api.validateLibraryTitle(state.data!.title.id, selectedMachineId);
        setActiveJobId(result.job.id);
        setActiveJob(result.job);
      } catch {
        // Leave stale state visible if validation could not be queued.
      }
    })();
  }, [activeJobId, selectedMachineId, state.data]);

  if (!id) {
    return <PageState title="Title not found" description="The requested library title id is missing." tone="error" />;
  }

  if (state.loading) {
    return <PageState title="Loading title" description="Fetching title metadata, sources, and latest activity." tone="loading" />;
  }

  if (state.error || !state.data) {
    return <PageState title="Title unavailable" description={state.error ?? "The requested title could not be loaded."} actionLabel="Retry" onAction={() => void state.reload()} tone="error" />;
  }

  const { title, sources, detectionRules, installScriptPath, notes, latestJob } = state.data;
  const isBusy = title.installState === "Installing" || title.installState === "Uninstalling";

  async function onPreviewReconcile() {
    setReconcileBusy(true);
    setReconcileError(null);
    try {
      const preview = await api.previewLibraryReconcile(title.id);
      setReconcilePreview(preview);
      setManualSearchResult(null);
      setManualSearchQuery(preview.localTitle);
      setSelectedMatchKey(preview.alternativeMatches[0]?.key ?? "");
    } catch (requestError) {
      setReconcileError(requestError instanceof Error ? requestError.message : "Unable to load reconcile preview.");
    } finally {
      setReconcileBusy(false);
    }
  }

  async function onApplyReconcile(localOnly: boolean) {
    setReconcileBusy(true);
    setReconcileError(null);
    try {
      await api.applyLibraryReconcile(title.id, { matchKey: localOnly ? null : selectedMatchKey, localOnly }, selectedMachineId || undefined);
      await state.reload();
      await onPreviewReconcile();
    } catch (requestError) {
      setReconcileError(requestError instanceof Error ? requestError.message : "Unable to apply reconciliation.");
    } finally {
      setReconcileBusy(false);
    }
  }

  async function onSearchMetadata() {
    setReconcileBusy(true);
    setReconcileError(null);
    try {
      const result = await api.searchLibraryMetadata(title.id, { query: manualSearchQuery || title.name });
      setManualSearchResult(result);
      setSelectedMatchKey(result.alternativeMatches[0]?.key ?? "");
    } catch (requestError) {
      setReconcileError(requestError instanceof Error ? requestError.message : "Unable to search metadata.");
    } finally {
      setReconcileBusy(false);
    }
  }

  async function onApplyManualSearch() {
    if (!selectedMatchKey) {
      return;
    }

    setReconcileBusy(true);
    setReconcileError(null);
    try {
      await api.applyLibraryMetadataSearch(title.id, { query: manualSearchQuery || title.name, matchKey: selectedMatchKey }, selectedMachineId || undefined);
      await state.reload();
      await onPreviewReconcile();
    } catch (requestError) {
      setReconcileError(requestError instanceof Error ? requestError.message : "Unable to apply metadata search result.");
    } finally {
      setReconcileBusy(false);
    }
  }

  async function onSaveEdit() {
    if (!editForm || !id) return;
    setEditSaving(true);
    setEditError(null);
    try {
      await api.updatePackageMetadata(id, {
        slug: editForm.slug.trim(),
        name: editForm.name.trim(),
        description: editForm.description.trim(),
        notes: editForm.notes.trim(),
        tags: title.tags,
        genres: editForm.genres.split(",").map((g) => g.trim()).filter(Boolean),
        studio: editForm.studio.trim(),
        releaseYear: editForm.releaseYear ? parseInt(editForm.releaseYear, 10) : null,
        coverImagePath: editForm.coverImagePath.trim() || null,
        metadataProvider: editForm.metadataProvider.trim() || null,
        metadataSourceUrl: editForm.metadataSourceUrl.trim() || null,
        metadataSelectionKind: editForm.metadataSelectionKind || "Manual",
      });
      await state.reload();
      setEditForm(null);
    } catch (err) {
      setEditError(err instanceof Error ? err.message : "Failed to save changes.");
    } finally {
      setEditSaving(false);
    }
  }

  async function onPlay() {
    if (!selectedMachineId) {
      return;
    }

    setActionError(null);

    if (!title.canInstall && !title.canPlay) {
      void navigate(`/packages/${title.id}`);
      return;
    }

    try {
      const result = await api.playLibraryTitle(title.id, selectedMachineId);
      setActiveJobId(result.job.id);
      setActiveJob(result.job);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Unable to start the requested action.");
    }
  }

  async function onValidate() {
    if (!selectedMachineId) {
      return;
    }

    setActionError(null);
    try {
      const result = await api.validateLibraryTitle(title.id, selectedMachineId);
      setActiveJobId(result.job.id);
      setActiveJob(result.job);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Unable to validate install state.");
    }
  }

  async function onUninstall() {
    if (!selectedMachineId) {
      return;
    }

    setActionError(null);
    try {
      const result = await api.uninstallLibraryTitle(title.id, selectedMachineId);
      setActiveJobId(result.job.id);
      setActiveJob(result.job);
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Unable to start uninstall.");
    }
  }

  async function onMarkNotInstalled() {
    if (!selectedMachineId) {
      return;
    }

    setActionError(null);
    try {
      await api.markLibraryTitleNotInstalled(title.id, selectedMachineId);
      await state.reload();
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Unable to mark title as not installed.");
    }
  }

  async function onToggleArchive() {
    setReconcileBusy(true);
    setReconcileError(null);
    try {
      if (title.isArchived) {
        await api.restoreLibraryTitle(title.id, selectedMachineId || undefined);
      } else {
        await api.archiveLibraryTitle(title.id, "Archived from library cleanup.", selectedMachineId || undefined);
      }

      await state.reload();
      setReconcilePreview(null);
    } catch (requestError) {
      setReconcileError(requestError instanceof Error ? requestError.message : "Unable to update archive status.");
    } finally {
      setReconcileBusy(false);
    }
  }

  return (
    <div className="page-grid">
      <PageHeader
        eyebrow="Library"
        title={title.name}
        description={`${title.studio || "Unknown studio"} | ${title.releaseYear ?? "Unknown year"} | ${selectedMachine ? selectedMachine.name : "No machine selected"}`}
        actions={
          <div className="page-actions">
            <StatusPill label={title.installState} tone={title.installState === "Installed" ? "success" : title.installState === "Failed" ? "danger" : title.installState === "Installing" || title.installState === "Uninstalling" ? "warning" : "neutral"} />
            <button type="button" disabled={!selectedMachineId || isBusy} onClick={() => void onPlay()}>
              {title.canPlay ? "Play" : title.canInstall ? "Install" : "Review"}
            </button>
            <button type="button" className="secondary-button inline-button" disabled={!selectedMachineId || isBusy || !title.canValidate} onClick={() => void onValidate()}>
              Validate
            </button>
            <button type="button" className="secondary-button inline-button" disabled={!selectedMachineId || isBusy || !title.canUninstall} onClick={() => void onUninstall()}>
              Uninstall
            </button>
            <button type="button" className="secondary-button inline-button" disabled={!selectedMachineId || isBusy} onClick={() => void onMarkNotInstalled()}>
              Mark Not Installed
            </button>
            <button type="button" className="secondary-button inline-button" onClick={() => void onToggleArchive()} disabled={reconcileBusy}>
              {title.isArchived ? "Restore Title" : "Archive Title"}
            </button>
            <Link className="secondary-button inline-button" to={`/packages/${title.id}`}>
              Advanced View
            </Link>
          </div>
        }
      />

      {activeJob ? (
        <>
          <InstallProgress job={activeJob} />
          {activeJob.state === "Failed" ? (
            <div className="install-progress-actions">
              <button type="button" onClick={() => { setActiveJob(null); setActiveJobId(null); void onPlay(); }}>Retry</button>
              <button type="button" className="secondary-button inline-button" onClick={() => { setActiveJob(null); setActiveJobId(null); }}>Dismiss</button>
            </div>
          ) : null}
        </>
      ) : null}
      {actionError ? <div className="inline-error">{actionError}</div> : null}

      <section className="title-hero" style={title.backdropImageUrl ? { backgroundImage: `linear-gradient(90deg, rgba(17,22,28,0.94), rgba(17,22,28,0.68)), url(${title.backdropImageUrl})`, backgroundSize: "cover", backgroundPosition: "center" } : undefined}>
        <div className="title-hero-poster">
          {title.posterImageUrl ?? title.coverImagePath ? (
            <img src={title.posterImageUrl ?? title.coverImagePath ?? undefined} alt={`${title.name} poster`} className="title-hero-image" />
          ) : (
            <div className="title-hero-image poster-fallback">{title.name.slice(0, 3).toUpperCase()}</div>
          )}
        </div>
        <div className="title-hero-copy">
          <div className="poster-chip-row">
            <span className="header-chip">{title.metadataStatus}</span>
            <span className="header-chip">{title.installReadiness}</span>
            <span className="header-chip">{title.supportedInstallPath}</span>
          </div>
          <h3>{title.name}</h3>
          <p>{title.storeDescription || "No storefront description is available for this title yet."}</p>
          <div className="genre-chip-row">
            {title.genres.map((genre) => (
              <span className="genre-chip" key={`${title.id}-${genre}`}>
                {genre}
              </span>
            ))}
          </div>
          <div className="hero-detail-row">
            <div className="hero-stat">
              <strong>{title.releaseYear ?? "Unknown"}</strong>
              <span>Release Year</span>
            </div>
            <div className="hero-stat">
              <strong>{title.studio || "Unknown"}</strong>
              <span>Studio</span>
            </div>
            <div className="hero-stat">
              <strong>{title.metadataPrimarySource ?? "Local"}</strong>
              <span>{typeof title.metadataConfidence === "number" ? `${Math.round(title.metadataConfidence * 100)}% confidence` : "Metadata Source"}</span>
            </div>
          </div>
        </div>
      </section>

      <div className="two-column shell-panels">
        <section className="table-card">
          <div className="table-section-header">
            <div>
              <h3>Overview</h3>
              <p>Machine-scoped install state, source health, and latest activity.</p>
            </div>
          </div>
          <div className="detail-grid">
            <div>
              <label>Install State</label>
              <p>{title.installState}</p>
            </div>
            <div>
              <label>Validation</label>
              <p>{title.validationSummary ?? "No validation history"}{title.lastValidatedAtUtc ? ` | ${relative(title.lastValidatedAtUtc)}` : ""}{title.isInstallStateStale ? " | Stale" : ""}</p>
            </div>
            <div>
              <label>Source Health</label>
              <p>{title.sourceHealth}{title.sourceConflictCount ? ` | ${title.sourceConflictCount} conflict(s)` : ""}</p>
            </div>
            <div>
              <label>Source Summary</label>
              <p>{title.sourceSummary}</p>
            </div>
            <div>
              <label>Install Path</label>
              <p>{title.supportedInstallPath} | {title.installReadiness}</p>
            </div>
            <div>
              <label>Processing State</label>
              <p>{title.processingState}{title.normalizedAtUtc ? ` | ${relative(title.normalizedAtUtc)}` : ""}</p>
            </div>
            <div>
              <label>Latest Activity</label>
              <p>{title.latestJobActionType ? `${title.latestJobActionType} | ${relative(title.latestJobCreatedAtUtc)}` : "No recent activity"}</p>
            </div>
            <div>
              <label>Genres</label>
              <p>{title.genres.join(", ") || "None"}</p>
            </div>
            <div>
              <label>Launch Path</label>
              <p>{title.launchExecutablePath ?? "Detection-derived"}</p>
            </div>
            <div>
              <label>Install Script</label>
              <p>{installScriptPath}</p>
            </div>
            <div>
              <label>Notes</label>
              <p>{notes || "None"}</p>
            </div>
            <div>
              <label>Diagnostics</label>
              <p>{title.recipeDiagnostics}</p>
            </div>
            <div>
              <label>Normalization</label>
              <p>{title.normalizationDiagnostics || "No normalization diagnostics."}</p>
            </div>
            <div>
              <label>Review Status</label>
              <p>{title.reviewRequiredReason ?? "Ready"}</p>
            </div>
            <div>
              <label>Archived</label>
              <p>{title.isArchived ? `${title.archivedReason ?? "Archived"} | ${relative(title.archivedAtUtc)}` : "Visible"}</p>
            </div>
            <div>
              <label>Metadata</label>
              <p>{title.metadataStatus}{title.metadataPrimarySource ? ` | ${title.metadataPrimarySource}` : ""}{title.metadataProvider ? ` | ${title.metadataSelectionKind}` : ""}</p>
            </div>
          </div>
        </section>

        <section className="table-card">
          <div className="table-section-header">
            <div>
              <h3>Latest Job</h3>
              <p>Most recent job for the selected machine.</p>
            </div>
          </div>
          {latestJob ? (
            <div className="detail-grid">
              <div>
                <label>Action</label>
                <p>{latestJob.actionType}</p>
              </div>
              <div>
                <label>Status</label>
                <p>{latestJob.state}</p>
              </div>
              <div>
                <label>Updated</label>
                <p>{relative(latestJob.updatedAtUtc)}</p>
              </div>
              <div>
                <label>Summary</label>
                <p>{latestJob.outcomeSummary ?? latestJob.latestEventMessage ?? "No summary"}</p>
              </div>
              <div>
                <label>Detail</label>
                <p>
                  <Link className="table-title-link" to={`/jobs/${latestJob.id}`}>
                    Open Job
                  </Link>
                </p>
              </div>
            </div>
          ) : (
            <PageState title="No jobs yet" description="Install or play this title to create activity." />
          )}
        </section>
      </div>

      <section className="table-card">
        <div className="table-section-header">
          <div>
            <h3>Edit Details</h3>
            <p>Manually override name, description, genres, and other metadata fields.</p>
          </div>
          {editForm ? (
            <div className="table-actions">
              <button type="button" onClick={() => void onSaveEdit()} disabled={editSaving}>
                {editSaving ? "Saving..." : "Save"}
              </button>
              <button type="button" className="secondary-button inline-button" onClick={() => { setEditForm(null); setEditError(null); }} disabled={editSaving}>
                Cancel
              </button>
            </div>
          ) : (
            <button type="button" className="secondary-button inline-button" onClick={() => setEditForm(titleToForm(title))}>
              Edit
            </button>
          )}
        </div>
        {editError ? <div className="inline-error">{editError}</div> : null}
        {editForm ? (
          <div className="detail-grid">
            <div>
              <label>Name</label>
              <input value={editForm.name} onChange={(e) => setEditForm({ ...editForm, name: e.target.value })} />
            </div>
            <div>
              <label>Slug</label>
              <input value={editForm.slug} onChange={(e) => setEditForm({ ...editForm, slug: e.target.value })} />
            </div>
            <div>
              <label>Studio</label>
              <input value={editForm.studio} onChange={(e) => setEditForm({ ...editForm, studio: e.target.value })} />
            </div>
            <div>
              <label>Release Year</label>
              <input type="number" value={editForm.releaseYear} onChange={(e) => setEditForm({ ...editForm, releaseYear: e.target.value })} />
            </div>
            <div>
              <label>Genres (comma-separated)</label>
              <input value={editForm.genres} onChange={(e) => setEditForm({ ...editForm, genres: e.target.value })} />
            </div>
            <div>
              <label>Cover Image URL</label>
              <input value={editForm.coverImagePath} onChange={(e) => setEditForm({ ...editForm, coverImagePath: e.target.value })} />
            </div>
            <div>
              <label>Metadata Provider</label>
              <input value={editForm.metadataProvider} placeholder="e.g. IGDB, Steam" onChange={(e) => setEditForm({ ...editForm, metadataProvider: e.target.value })} />
            </div>
            <div>
              <label>Metadata Source URL</label>
              <input value={editForm.metadataSourceUrl} onChange={(e) => setEditForm({ ...editForm, metadataSourceUrl: e.target.value })} />
            </div>
            <div style={{ gridColumn: "1 / -1" }}>
              <label>Description</label>
              <textarea rows={4} value={editForm.description} onChange={(e) => setEditForm({ ...editForm, description: e.target.value })} style={{ width: "100%", resize: "vertical" }} />
            </div>
            <div style={{ gridColumn: "1 / -1" }}>
              <label>Notes</label>
              <textarea rows={3} value={editForm.notes} onChange={(e) => setEditForm({ ...editForm, notes: e.target.value })} style={{ width: "100%", resize: "vertical" }} />
            </div>
          </div>
        ) : (
          <div className="detail-grid">
            <div>
              <label>Name</label>
              <p>{title.name}</p>
            </div>
            <div>
              <label>Slug</label>
              <p>{title.slug}</p>
            </div>
            <div>
              <label>Studio</label>
              <p>{title.studio || "—"}</p>
            </div>
            <div>
              <label>Release Year</label>
              <p>{title.releaseYear ?? "—"}</p>
            </div>
            <div>
              <label>Genres</label>
              <p>{title.genres.join(", ") || "—"}</p>
            </div>
            <div>
              <label>Cover Image</label>
              <p>{title.coverImagePath || "—"}</p>
            </div>
            <div style={{ gridColumn: "1 / -1" }}>
              <label>Description</label>
              <p style={{ whiteSpace: "pre-wrap" }}>{title.description || "—"}</p>
            </div>
          </div>
        )}
      </section>

      <div className="two-column shell-panels">
        <section className="table-card">
          <div className="table-section-header">
            <div>
              <h3>Reconcile</h3>
              <p>Re-run metadata matching and update this title in place without changing the install recipe.</p>
            </div>
            <button type="button" className="secondary-button inline-button" onClick={() => void onPreviewReconcile()} disabled={reconcileBusy}>
              {reconcileBusy ? "Loading..." : "Re-match"}
            </button>
          </div>
          {reconcileError ? <div className="inline-error">{reconcileError}</div> : null}
          {reconcilePreview ? (
            <div className="detail-grid">
              <div style={{ gridColumn: "1 / -1" }}>
                <label>Manual Search</label>
                <div className="table-actions">
                  <input value={manualSearchQuery} onChange={(event) => setManualSearchQuery(event.target.value)} placeholder="Search providers for a better title match" />
                  <button type="button" className="secondary-button inline-button" onClick={() => void onSearchMetadata()} disabled={reconcileBusy || !manualSearchQuery.trim()}>
                    Search Metadata
                  </button>
                </div>
              </div>
              <div>
                <label>Current</label>
                <p>{reconcilePreview.current.title}</p>
              </div>
              <div>
                <label>Local Only</label>
                <p>{reconcilePreview.localOnly.title}</p>
              </div>
              <div>
                <label>Decision</label>
                <p>{reconcilePreview.matchSummary}</p>
              </div>
              <div>
                <label>Warnings</label>
                <p>{reconcilePreview.warningSignals.join(" | ") || "None"}</p>
              </div>
              <div>
                <label>Choose Match</label>
                <select value={selectedMatchKey} onChange={(event) => setSelectedMatchKey(event.target.value)}>
                  {(manualSearchResult?.alternativeMatches ?? reconcilePreview.alternativeMatches).map((match) => (
                    <option key={match.key} value={match.key}>
                      {match.title}{match.releaseYear ? ` (${match.releaseYear})` : ""} | {(match.score * 100).toFixed(0)}%
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label>Actions</label>
                <p className="table-actions">
                  {manualSearchResult ? (
                    <button type="button" onClick={() => void onApplyManualSearch()} disabled={reconcileBusy || !selectedMatchKey}>
                      Apply Search Result
                    </button>
                  ) : (
                    <button type="button" onClick={() => void onApplyReconcile(false)} disabled={reconcileBusy || !selectedMatchKey}>
                      Apply Match
                    </button>
                  )}
                  <button type="button" className="secondary-button inline-button" onClick={() => void onApplyReconcile(true)} disabled={reconcileBusy}>
                    Keep Local Only
                  </button>
                </p>
              </div>
              {manualSearchResult ? (
                <div style={{ gridColumn: "1 / -1" }}>
                  <label>Search Result</label>
                  <p>{manualSearchResult.matchSummary}</p>
                </div>
              ) : null}
            </div>
          ) : (
            <PageState title="No reconcile preview" description="Run Re-match to inspect alternate metadata matches and local-only fallback." />
          )}
        </section>

        <section className="table-card">
          <div className="table-section-header">
            <div>
              <h3>Sources</h3>
              <p>Imported media and source-session metadata.</p>
            </div>
          </div>
          <table className="data-table">
            <thead>
              <tr>
                <th>Label</th>
                <th>Path</th>
                <th>Kind</th>
                <th>Disc</th>
              </tr>
            </thead>
            <tbody>
              {sources.map((source) => (
                <tr key={`${source.path}-${source.label}`}>
                  <td>{source.label}</td>
                  <td>{source.path}</td>
                  <td>{source.mediaType} / {source.sourceKind}</td>
                  <td>{source.discNumber ?? "-"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>

        <section className="table-card">
          <div className="table-section-header">
            <div>
              <h3>Source Conflicts</h3>
              <p>Warnings for duplicate paths or shared roots across other library titles.</p>
            </div>
          </div>
          {state.data.sourceConflicts.length ? (
            <div className="list">
              {state.data.sourceConflicts.map((conflict) => (
                <div className="list-row" key={`${conflict.conflictType}-${conflict.path}-${conflict.packageId}`}>
                  <div>
                    <strong>{conflict.conflictType} | {conflict.packageName}</strong>
                    <p>{conflict.path}</p>
                  </div>
                  <Link className="secondary-button inline-button" to={`/library/${conflict.packageId}`}>
                    Open
                  </Link>
                </div>
              ))}
            </div>
          ) : (
            <PageState title="No source conflicts" description="No duplicate source paths or roots were detected for this title." />
          )}
        </section>

        <section className="table-card">
          <div className="table-section-header">
            <div>
              <h3>Detection Rules</h3>
              <p>Validation checks used after installation.</p>
            </div>
          </div>
          {detectionRules.length ? (
            <div className="list">
              {detectionRules.map((rule) => (
                <div className="list-row" key={rule.id}>
                  <div>
                    <strong>{rule.ruleType}</strong>
                    <p>{rule.value}</p>
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <PageState title="No detection rules" description="This title does not yet declare validation checks." />
          )}
        </section>
      </div>
    </div>
  );
}
