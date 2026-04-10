import { useMemo, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { api } from "../api/client";
import { PageHeader } from "../components/PageHeader";
import { PageState } from "../components/PageState";
import { StatusPill } from "../components/StatusPill";
import { useSelectedMachine } from "../context/SelectedMachineContext";
import { usePollingAsyncData } from "../hooks/usePollingAsyncData";
import type { BulkReMatchRecord, LibraryInstallState, LibraryTitleRecord, NormalizationJobRecord } from "../types/api";

type LibraryViewMode = "poster" | "table";

function getInstallTone(state: LibraryInstallState) {
  switch (state) {
    case "Installed":
      return "success";
    case "Failed":
      return "danger";
    case "Installing":
    case "Uninstalling":
      return "warning";
    default:
      return "neutral";
  }
}

function getMetadataTone(status: string) {
  if (status.includes("IGDB") || status.includes("Steam Match")) {
    return "success";
  }

  if (status.includes("Review")) {
    return "warning";
  }

  return "neutral";
}

function formatRelativeTime(value?: string | null) {
  if (!value) {
    return "No activity";
  }

  const diffMinutes = Math.max(0, Math.round((Date.now() - new Date(value).getTime()) / 60000));
  if (diffMinutes < 1) return "Just now";
  if (diffMinutes < 60) return `${diffMinutes}m ago`;
  const diffHours = Math.round(diffMinutes / 60);
  if (diffHours < 24) return `${diffHours}h ago`;
  return `${Math.round(diffHours / 24)}d ago`;
}

function trimDescription(value: string, maxLength = 150) {
  const trimmed = value.trim();
  if (!trimmed) {
    return "Metadata and library summary will appear here once this title has a richer provider match or a manual description.";
  }

  return trimmed.length <= maxLength ? trimmed : `${trimmed.slice(0, maxLength).trimEnd()}...`;
}

function posterFallback(title: string) {
  return title
    .split(" ")
    .map((token) => token[0])
    .join("")
    .slice(0, 3)
    .toUpperCase();
}

function ActionButton({
  title,
  selectedMachineId,
  onPlay,
  onReview
}: {
  title: LibraryTitleRecord;
  selectedMachineId: string;
  onPlay: (title: LibraryTitleRecord) => Promise<void>;
  onReview: (title: LibraryTitleRecord) => void;
}) {
  const actionLabel = title.canPlay ? "Play" : title.canInstall ? "Install" : "Review";
  const disabledReason = !selectedMachineId
    ? "No machine selected"
    : title.installState === "Installing" || title.installState === "Uninstalling"
      ? "Job already active"
      : !title.canInstall && !title.canPlay
        ? (title.reviewRequiredReason ?? "This title requires review before it can be installed.")
        : null;

  return (
    <button
      type="button"
      title={disabledReason ?? undefined}
      disabled={Boolean(disabledReason)}
      onClick={() => void (title.canInstall || title.canPlay ? onPlay(title) : onReview(title))}
    >
      {actionLabel}
    </button>
  );
}

function PosterCard({
  title,
  selectedMachineId,
  onPlay,
  onReview
}: {
  title: LibraryTitleRecord;
  selectedMachineId: string;
  onPlay: (title: LibraryTitleRecord) => Promise<void>;
  onReview: (title: LibraryTitleRecord) => void;
}) {
  return (
    <article className="poster-card">
      <Link className="poster-link" to={`/library/${title.id}`}>
        {title.posterImageUrl ?? title.coverImagePath ? (
          <img className="poster-image" src={title.posterImageUrl ?? title.coverImagePath ?? undefined} alt={`${title.name} poster`} />
        ) : (
          <div className="poster-image poster-fallback">{posterFallback(title.name)}</div>
        )}
      </Link>
      <div className="poster-body">
        <div className="poster-heading">
          <div>
            <Link className="poster-title" to={`/library/${title.id}`}>
              {title.name}
            </Link>
            <p className="poster-subtitle">
              {title.releaseYear ?? "Unknown year"} | {title.studio || "Unknown studio"}
            </p>
          </div>
          <StatusPill label={title.installState} tone={getInstallTone(title.installState)} />
        </div>
        <p className="poster-description">{trimDescription(title.storeDescription, 180)}</p>
        <div className="poster-chip-row">
          <span className="header-chip">{title.installReadiness}</span>
          <span className="header-chip">{title.metadataStatus}</span>
          <span className="header-chip">{title.supportedInstallPath}</span>
        </div>
        <div className="genre-chip-row">
          {title.genres.slice(0, 4).map((genre) => (
            <span className="genre-chip" key={`${title.id}-${genre}`}>
              {genre}
            </span>
          ))}
        </div>
        <div className="poster-footer">
          <div className="poster-meta">
            <strong>{title.latestJobActionType ?? "No activity"}</strong>
            <span>{formatRelativeTime(title.latestJobCreatedAtUtc)}</span>
          </div>
          <div className="poster-actions">
            <ActionButton
              title={title}
              selectedMachineId={selectedMachineId}
              onPlay={onPlay}
              onReview={onReview}
            />
            <Link className="secondary-button inline-button" to={`/library/${title.id}`}>
              Details
            </Link>
          </div>
        </div>
      </div>
    </article>
  );
}

export function LibraryPage() {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const { selectedMachineId, selectedMachine, loading: machinesLoading, error: machinesError } = useSelectedMachine();
  const [reMatching, setReMatching] = useState(false);
  const [reMatchResult, setReMatchResult] = useState<BulkReMatchRecord | null>(null);
  const [reMatchError, setReMatchError] = useState<string | null>(null);
  const [reNormalizing, setReNormalizing] = useState(false);
  const [reNormalizeResult, setReNormalizeResult] = useState<{ queued: number; jobs: NormalizationJobRecord[] } | null>(null);
  const [reNormalizeError, setReNormalizeError] = useState<string | null>(null);

  const search = searchParams.get("search") ?? "";
  const genre = searchParams.get("genre") ?? "";
  const studio = searchParams.get("studio") ?? "";
  const installState = searchParams.get("state") ?? "";
  const sortBy = searchParams.get("sortBy") ?? "title";
  const year = searchParams.get("year") ?? "";
  const sourceHealth = searchParams.get("sourceHealth") ?? "";
  const view = (searchParams.get("view") ?? "poster") as LibraryViewMode;

  const libraryState = usePollingAsyncData(
    () =>
      api.listLibrary({
        machineId: selectedMachineId || undefined,
        genre: genre || undefined,
        studio: studio || undefined,
        year: year ? Number(year) : undefined,
        sortBy
      }),
    [selectedMachineId, genre, studio, year, sortBy],
    10000,
    true
  );

  const titles = libraryState.data ?? [];
  const filteredTitles = useMemo(() => {
    return titles.filter((title) => {
      const haystack = `${title.name} ${title.storeDescription} ${title.studio} ${title.genres.join(" ")} ${title.tags.join(" ")} ${title.sourceSummary} ${title.metadataStatus}`.toLowerCase();
      const matchesSearch = !search.trim() || haystack.includes(search.trim().toLowerCase());
      const matchesState = !installState || title.installState === installState;
      const matchesSource = !sourceHealth || title.sourceHealth === sourceHealth;
      return matchesSearch && matchesState && matchesSource;
    });
  }, [installState, search, sourceHealth, titles]);

  const genres = useMemo(
    () => Array.from(new Set(titles.flatMap((title) => title.genres))).sort((left, right) => left.localeCompare(right)),
    [titles]
  );
  const studios = useMemo(
    () => Array.from(new Set(titles.map((title) => title.studio).filter(Boolean))).sort((left, right) => left.localeCompare(right)),
    [titles]
  );
  const years = useMemo(
    () => Array.from(new Set(titles.map((title) => title.releaseYear).filter((value): value is number => Boolean(value)))).sort((left, right) => right - left),
    [titles]
  );

  const activeFilters = [
    search ? `Search: ${search}` : null,
    genre ? `Genre: ${genre}` : null,
    studio ? `Studio: ${studio}` : null,
    installState ? `State: ${installState}` : null,
    year ? `Year: ${year}` : null,
    sourceHealth ? `Source: ${sourceHealth}` : null
  ].filter(Boolean) as string[];

  function updateParam(key: string, value: string) {
    const next = new URLSearchParams(searchParams);
    if (value) {
      next.set(key, value);
    } else {
      next.delete(key);
    }
    setSearchParams(next);
  }

  async function onPlay(title: LibraryTitleRecord) {
    void navigate(`/library/${title.id}`);
    await Promise.resolve();
  }

  async function onBulkReNormalize() {
    setReNormalizing(true);
    setReNormalizeResult(null);
    setReNormalizeError(null);
    try {
      const queued = await api.bulkReNormalizeNeedsReview();
      // Wait 4s for fast jobs to complete, then fetch results and reload
      await new Promise((resolve) => setTimeout(resolve, 4000));
      const jobs = await api.listNormalizationJobs();
      setReNormalizeResult({ queued, jobs: jobs.slice(0, Math.max(queued, 10)) });
      await libraryState.reload();
    } catch (error) {
      setReNormalizeError(error instanceof Error ? error.message : "Re-normalize failed.");
    } finally {
      setReNormalizing(false);
    }
  }

  async function onBulkReMatch() {
    setReMatching(true);
    setReMatchResult(null);
    setReMatchError(null);
    try {
      const result = await api.bulkReMatchLibrary();
      setReMatchResult(result);
      await libraryState.reload();
    } catch (error) {
      setReMatchError(error instanceof Error ? error.message : "Re-match failed.");
    } finally {
      setReMatching(false);
    }
  }

  function clearFilters() {
    setSearchParams(new URLSearchParams({ sortBy, view }));
  }

  function onReview(title: LibraryTitleRecord) {
    void navigate(`/packages/${title.id}`);
  }

  return (
    <div className="page-grid">
      <PageHeader
        eyebrow="Library"
        title="Game Library"
        description={`Browse, install, and launch your library${selectedMachine ? ` on ${selectedMachine.name}` : ""} without leaving the main catalog.`}
        actions={
          <div className="page-actions">
            <div className="view-toggle">
              <button type="button" className={view === "poster" ? "view-toggle-button active" : "view-toggle-button"} onClick={() => updateParam("view", "poster")}>
                Posters
              </button>
              <button type="button" className={view === "table" ? "view-toggle-button active" : "view-toggle-button"} onClick={() => updateParam("view", "table")}>
                Table
              </button>
            </div>
            <button type="button" className="secondary-button inline-button" onClick={() => void libraryState.reload()}>
              Refresh
            </button>
            <button type="button" className="secondary-button inline-button" disabled={reNormalizing} onClick={() => void onBulkReNormalize()}>
              {reNormalizing ? "Re-normalizing..." : "Re-normalize NeedsReview"}
            </button>
            <button type="button" className="secondary-button inline-button" disabled={reMatching} onClick={() => void onBulkReMatch()}>
              {reMatching ? "Re-matching..." : "Re-match Unmatched"}
            </button>
            <Link className="secondary-button inline-button" to="/imports">
              Add New
            </Link>
          </div>
        }
      />
      <div className="store-hero">
        <div>
          <span className="eyebrow">Library Overview</span>
          <h3>Keep the library front and center while the system pages handle operational detail.</h3>
          <p>Artwork and summary data still make browsing usable, but install state, source health, metadata quality, and machine readiness stay visible where decisions get made.</p>
        </div>
        <div className="store-hero-stats">
          <div className="hero-stat">
            <strong>{filteredTitles.length}</strong>
            <span>Visible titles</span>
          </div>
          <div className="hero-stat">
            <strong>{titles.filter((title) => title.metadataStatus.includes("Match")).length}</strong>
            <span>Remote matches</span>
          </div>
          <div className="hero-stat">
            <strong>{titles.filter((title) => title.installReadiness === "Ready").length}</strong>
            <span>Install ready</span>
          </div>
        </div>
      </div>
      <div className="toolbar-card">
        <div className="table-toolbar library-toolbar">
          <input value={search} onChange={(event) => updateParam("search", event.target.value)} placeholder="Search titles, genres, metadata, or source paths" />
          <select value={genre} onChange={(event) => updateParam("genre", event.target.value)}>
            <option value="">All genres</option>
            {genres.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </select>
          <select value={studio} onChange={(event) => updateParam("studio", event.target.value)}>
            <option value="">All studios</option>
            {studios.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </select>
          <select value={installState} onChange={(event) => updateParam("state", event.target.value)}>
            <option value="">All states</option>
            <option value="NotInstalled">Not Installed</option>
            <option value="Installing">Installing</option>
            <option value="Uninstalling">Uninstalling</option>
            <option value="Installed">Installed</option>
            <option value="Failed">Failed</option>
          </select>
          <select value={sourceHealth} onChange={(event) => updateParam("sourceHealth", event.target.value)}>
            <option value="">All sources</option>
            <option value="Available">Available</option>
            <option value="Missing">Missing</option>
            <option value="Review Required">Review Required</option>
          </select>
          <select value={year} onChange={(event) => updateParam("year", event.target.value)}>
            <option value="">All years</option>
            {years.map((value) => (
              <option key={value} value={value.toString()}>
                {value}
              </option>
            ))}
          </select>
          <select value={sortBy} onChange={(event) => updateParam("sortBy", event.target.value)}>
            <option value="title">Sort by title</option>
            <option value="year">Sort by year</option>
            <option value="studio">Sort by studio</option>
            <option value="recent">Recently updated</option>
          </select>
        </div>
        <div className="toolbar-meta">
          <span>{filteredTitles.length} title(s)</span>
          {activeFilters.length ? (
            <>
              <div className="filter-chip-row">
                {activeFilters.map((filter) => (
                  <span className="header-chip" key={filter}>
                    {filter}
                  </span>
                ))}
              </div>
              <button type="button" className="secondary-button inline-button" onClick={clearFilters}>
                Clear Filters
              </button>
            </>
          ) : null}
        </div>
      </div>
      {reNormalizeError ? <div className="inline-error">{reNormalizeError}</div> : null}
      {reNormalizing ? (
        <div className="inline-banner">Re-normalizing packages — please wait…</div>
      ) : reNormalizeResult !== null ? (
        <div className="inline-success">
          {reNormalizeResult.queued === 0
            ? "No packages were in NeedsReview state — nothing to re-normalize."
            : `Re-normalization complete — ${reNormalizeResult.queued} package(s) processed.`}
          {reNormalizeResult.jobs.length > 0 && (
            <ul style={{ margin: "6px 0 0", paddingLeft: "1.2em", fontSize: "0.85em" }}>
              {reNormalizeResult.jobs.map((j) => (
                <li key={j.id}>
                  <strong>{j.packageName}</strong>
                  {" — "}
                  {j.state === "Failed" ? `Failed: ${j.errorMessage ?? j.summary}` : j.summary}
                </li>
              ))}
            </ul>
          )}
        </div>
      ) : null}
      {reMatchError ? <div className="inline-error">{reMatchError}</div> : null}
      {reMatchResult ? (
        <div className="inline-success">
          Re-matched {reMatchResult.processedCount} title(s): {reMatchResult.autoImportedCount} auto-imported, {reMatchResult.nowReviewableCount} now reviewable, {reMatchResult.stillUnmatchedCount} still unmatched.
        </div>
      ) : null}
      {machinesLoading || libraryState.loading ? (
        <PageState title="Loading library" description="Fetching titles, machine selection, and current install state." tone="loading" />
      ) : machinesError ? (
        <PageState title="Machines unavailable" description={machinesError} tone="error" />
      ) : libraryState.error ? (
        <PageState title="Library unavailable" description={libraryState.error} actionLabel="Retry" onAction={() => void libraryState.reload()} tone="error" />
      ) : filteredTitles.length === 0 ? (
        <PageState title="No titles found" description="Import a library root or adjust your filters to see titles here." />
      ) : view === "poster" ? (
        <div className="poster-grid">
          {filteredTitles.map((title) => (
            <PosterCard
              key={title.id}
              title={title}
              selectedMachineId={selectedMachineId}
              onPlay={onPlay}
              onReview={onReview}
            />
          ))}
        </div>
      ) : (
        <div className="table-card">
          <table className="data-table">
            <thead>
              <tr>
                <th>Title</th>
                <th>Status</th>
                <th>Studio</th>
                <th>Year</th>
                <th>Metadata</th>
                <th>Source</th>
                <th>Readiness</th>
                <th>Last Activity</th>
                <th className="actions-column">Actions</th>
              </tr>
            </thead>
            <tbody>
              {filteredTitles.map((title) => (
                <tr key={title.id}>
                  <td>
                    <div className="table-title-with-poster">
                      {title.posterImageUrl ? <img className="table-poster-thumb" src={title.posterImageUrl} alt={`${title.name} poster`} /> : <div className="table-poster-thumb fallback">{posterFallback(title.name)}</div>}
                      <div className="table-title-cell">
                        <Link className="table-title-link" to={`/library/${title.id}`}>
                          {title.name}
                        </Link>
                        <span>{title.versionLabel}</span>
                      </div>
                    </div>
                  </td>
                  <td>
                    <StatusPill label={title.installState} tone={getInstallTone(title.installState)} />
                  </td>
                  <td>{title.studio || "Unknown"}</td>
                  <td>{title.releaseYear ?? "Unknown"}</td>
                  <td>
                    <div className="table-title-cell">
                      <strong>{title.metadataStatus}</strong>
                      <span>{title.metadataPrimarySource ?? "Local metadata only"}{typeof title.metadataConfidence === "number" ? ` | ${Math.round(title.metadataConfidence * 100)}%` : ""}</span>
                    </div>
                  </td>
                  <td>
                    <div className="table-title-cell">
                      <strong>{title.sourceHealth}</strong>
                      <span>{title.sourceSummary}</span>
                    </div>
                  </td>
                  <td>
                    <div className="table-title-cell">
                      <strong>{title.installReadiness}</strong>
                      <span>{title.supportedInstallPath} | {title.playReadiness}</span>
                    </div>
                  </td>
                  <td>
                    <div className="table-title-cell">
                      <strong>{title.latestJobActionType ?? "No activity"}</strong>
                      <span>{formatRelativeTime(title.latestJobCreatedAtUtc)}</span>
                    </div>
                  </td>
                  <td className="actions-column">
                    <div className="table-actions">
                      <ActionButton
                        title={title}
                        selectedMachineId={selectedMachineId}
                        onPlay={onPlay}
                        onReview={onReview}
                      />
                      {title.lastJobId ? (
                        <Link className="secondary-button inline-button" to={`/jobs/${title.lastJobId}`}>
                          Open Job
                        </Link>
                      ) : null}
                      <Link className="secondary-button inline-button" to={`/library/${title.id}`}>
                        View
                      </Link>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
