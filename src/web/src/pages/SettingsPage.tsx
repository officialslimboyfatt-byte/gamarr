import { type FormEvent, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api/client";
import { PageHeader } from "../components/PageHeader";
import { PageState } from "../components/PageState";
import { useAsyncData } from "../hooks/useAsyncData";
import type { MediaManagementSettingsRecord, MetadataSettingsRecord } from "../types/api";

function patternsToText(patterns: string[]) {
  return patterns.join("\n");
}

function textToPatterns(value: string) {
  return value
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
}

export function SettingsPage() {
  const settingsState = useAsyncData(() => api.getSettings(), []);
  const [feedback, setFeedback] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [metadataSaving, setMetadataSaving] = useState(false);
  const [mediaSaving, setMediaSaving] = useState(false);
  const [resettingLibrary, setResettingLibrary] = useState(false);

  const [metadataForm, setMetadataForm] = useState<MetadataSettingsRecord | null>(null);
  const [mediaForm, setMediaForm] = useState<MediaManagementSettingsRecord | null>(null);
  const [igdbSecret, setIgdbSecret] = useState("");
  const [clearIgdbSecret, setClearIgdbSecret] = useState(false);
  const [includePatternsText, setIncludePatternsText] = useState("");
  const [excludePatternsText, setExcludePatternsText] = useState("");

  useEffect(() => {
    if (!settingsState.data) {
      return;
    }

    setMetadataForm(settingsState.data.metadata);
    setMediaForm(settingsState.data.media);
    setIncludePatternsText(patternsToText(settingsState.data.media.includePatterns));
    setExcludePatternsText(patternsToText(settingsState.data.media.excludePatterns));
    setIgdbSecret("");
    setClearIgdbSecret(false);
  }, [settingsState.data]);

  if (settingsState.loading || !metadataForm || !mediaForm) {
    return <PageState title="Loading settings" description="Fetching current server-side product settings." tone="loading" />;
  }

  if (settingsState.error) {
    return <PageState title="Settings unavailable" description={settingsState.error} actionLabel="Retry" onAction={() => void settingsState.reload()} tone="error" />;
  }

  const metadata = metadataForm;
  const media = mediaForm;
  const settings = settingsState.data!;

  async function onSaveMetadata(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setMetadataSaving(true);
    setFeedback(null);
    setError(null);

    try {
      const saved = await api.updateMetadataSettings({
        preferIgdb: metadata.preferIgdb,
        igdbEnabled: metadata.igdbEnabled,
        igdbClientId: metadata.igdbClientId ?? null,
        igdbClientSecret: igdbSecret.trim() || null,
        clearIgdbClientSecret: clearIgdbSecret,
        useSteamFallback: metadata.useSteamFallback,
        autoImportThreshold: metadata.autoImportThreshold,
        reviewThreshold: metadata.reviewThreshold
      });

      setMetadataForm(saved);
      settingsState.setData((current) => current ? { ...current, metadata: saved } : current);
      setIgdbSecret("");
      setClearIgdbSecret(false);
      setFeedback("Metadata settings saved.");
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to save metadata settings.");
    } finally {
      setMetadataSaving(false);
    }
  }

  async function onSaveMedia(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setMediaSaving(true);
    setFeedback(null);
    setError(null);

    try {
      const saved = await api.updateMediaSettings({
        defaultLibraryRootPath: media.defaultLibraryRootPath ?? null,
        normalizedAssetRootPath: media.normalizedAssetRootPath ?? null,
        autoScanOnRootCreate: media.autoScanOnRootCreate,
        autoNormalizeOnImport: media.autoNormalizeOnImport,
        autoImportHighConfidenceMatches: media.autoImportHighConfidenceMatches,
        includePatterns: textToPatterns(includePatternsText),
        excludePatterns: textToPatterns(excludePatternsText)
      });

      setMediaForm(saved);
      settingsState.setData((current) => current ? { ...current, media: saved } : current);
      setFeedback("Media management settings saved.");
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to save media settings.");
    } finally {
      setMediaSaving(false);
    }
  }

  async function onResetLibrary() {
    const confirmed = window.confirm(
      "Reset the library database state and clear the normalized asset cache? This keeps your source folders but removes imported titles, scans, candidates, jobs, and cached normalized assets."
    );

    if (!confirmed) {
      return;
    }

    setResettingLibrary(true);
    setFeedback(null);
    setError(null);

    try {
      const result = await api.resetLibrary({ preserveRoots: true, deleteNormalizedAssets: true });
      await settingsState.reload();
      setFeedback(
        `Library reset complete. Removed ${result.packagesDeleted} package(s), ${result.candidatesDeleted} candidate(s), ${result.scansDeleted} scan(s), ${result.jobsDeleted} job(s), and cleared ${result.normalizedAssetRootPath ?? "the normalized asset cache"}.`
      );
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to reset the library.");
    } finally {
      setResettingLibrary(false);
    }
  }

  return (
    <div className="page-grid">
      <PageHeader
        eyebrow="Settings"
        title="Settings"
        description="Configure provider access, match thresholds, and import defaults from the UI. Host/runtime settings still live in appsettings."
      />

      {feedback ? <div className="feedback-banner success">{feedback}</div> : null}
      {error ? <div className="feedback-banner error">{error}</div> : null}

      <div className="settings-grid">
        <section className="settings-panel">
          <div className="table-section-header">
            <div>
              <h3>Metadata</h3>
              <p>Configure IGDB and Steam fallback, then control how aggressively matches are auto-imported.</p>
            </div>
          </div>

          <form className="settings-form" onSubmit={(event) => void onSaveMetadata(event)}>
            <label className="settings-field checkbox">
              <input
                type="checkbox"
                checked={metadata.igdbEnabled}
                onChange={(event) => setMetadataForm({ ...metadata, igdbEnabled: event.target.checked })}
              />
              <span>Enable IGDB as a provider</span>
            </label>

            <label className="settings-field checkbox">
              <input
                type="checkbox"
                checked={metadata.preferIgdb}
                onChange={(event) => setMetadataForm({ ...metadata, preferIgdb: event.target.checked })}
              />
              <span>Prefer IGDB before Steam</span>
            </label>

            <label className="settings-field checkbox">
              <input
                type="checkbox"
                checked={metadata.useSteamFallback}
                onChange={(event) => setMetadataForm({ ...metadata, useSteamFallback: event.target.checked })}
              />
              <span>Use Steam as fallback when IGDB is missing or weak</span>
            </label>

            <label className="settings-field">
              <span>IGDB Client ID</span>
              <input
                type="text"
                value={metadata.igdbClientId ?? ""}
                onChange={(event) => setMetadataForm({ ...metadata, igdbClientId: event.target.value })}
                placeholder="Paste your Twitch/IGDB client id"
              />
            </label>

            <label className="settings-field">
              <span>IGDB Client Secret</span>
              <input
                type="password"
                value={igdbSecret}
                onChange={(event) => {
                  setIgdbSecret(event.target.value);
                  if (event.target.value) {
                    setClearIgdbSecret(false);
                  }
                }}
                placeholder={metadata.hasIgdbClientSecret ? "Secret is already configured" : "Paste a new client secret"}
              />
            </label>

            <label className="settings-field checkbox">
              <input
                type="checkbox"
                checked={clearIgdbSecret}
                onChange={(event) => {
                  setClearIgdbSecret(event.target.checked);
                  if (event.target.checked) {
                    setIgdbSecret("");
                  }
                }}
              />
              <span>Clear the stored IGDB client secret</span>
            </label>

            <label className="settings-field">
              <span>Auto-import threshold</span>
              <input
                type="number"
                min="0.5"
                max="0.99"
                step="0.01"
                value={metadata.autoImportThreshold}
                onChange={(event) => setMetadataForm({ ...metadata, autoImportThreshold: Number(event.target.value) })}
              />
            </label>

            <label className="settings-field">
              <span>Review threshold</span>
              <input
                type="number"
                min="0.5"
                max="0.99"
                step="0.01"
                value={metadata.reviewThreshold}
                onChange={(event) => setMetadataForm({ ...metadata, reviewThreshold: Number(event.target.value) })}
              />
            </label>

            <div className="settings-note">
              <strong>Status</strong>
              <p>{metadata.providerStatus}</p>
            </div>

            <div className="page-actions">
              <button type="submit" disabled={metadataSaving}>{metadataSaving ? "Saving..." : "Save Metadata Settings"}</button>
            </div>
          </form>
        </section>

        <section className="settings-panel">
          <div className="table-section-header">
            <div>
              <h3>Media Management</h3>
              <p>Set import defaults that drive new library roots and scan behavior.</p>
            </div>
          </div>

          <form className="settings-form" onSubmit={(event) => void onSaveMedia(event)}>
            <label className="settings-field">
              <span>Default library root path</span>
              <input
                type="text"
                value={media.defaultLibraryRootPath ?? ""}
                onChange={(event) => setMediaForm({ ...media, defaultLibraryRootPath: event.target.value })}
                placeholder="E:\\Games"
              />
            </label>

            <label className="settings-field">
              <span>Normalized asset root path</span>
              <input
                type="text"
                value={media.normalizedAssetRootPath ?? ""}
                onChange={(event) => setMediaForm({ ...media, normalizedAssetRootPath: event.target.value })}
                placeholder="E:\\DEV\\Spool\\.normalized-assets"
              />
            </label>

            <label className="settings-field checkbox">
              <input
                type="checkbox"
                checked={media.autoScanOnRootCreate}
                onChange={(event) => setMediaForm({ ...media, autoScanOnRootCreate: event.target.checked })}
              />
              <span>Automatically run a scan after adding a new library root</span>
            </label>

            <label className="settings-field checkbox">
              <input
                type="checkbox"
                checked={media.autoNormalizeOnImport}
                onChange={(event) => setMediaForm({ ...media, autoNormalizeOnImport: event.target.checked })}
              />
              <span>Automatically normalize imported titles into managed package assets</span>
            </label>

            <label className="settings-field checkbox">
              <input
                type="checkbox"
                checked={media.autoImportHighConfidenceMatches}
                onChange={(event) => setMediaForm({ ...media, autoImportHighConfidenceMatches: event.target.checked })}
              />
              <span>Automatically import near-perfect matches during scans</span>
            </label>

            <label className="settings-field">
              <span>Include patterns</span>
              <textarea
                rows={4}
                value={includePatternsText}
                onChange={(event) => setIncludePatternsText(event.target.value)}
                placeholder={"Examples:\nRetro\\*\nPC\\*"}
              />
            </label>

            <label className="settings-field">
              <span>Exclude patterns</span>
              <textarea
                rows={4}
                value={excludePatternsText}
                onChange={(event) => setExcludePatternsText(event.target.value)}
                placeholder={"Examples:\n*\\Temp\\*\n*\\Redist\\*"}
              />
            </label>

            <div className="settings-note">
              <strong>Supported install policy</strong>
              <p>{media.supportedInstallPathSummary}</p>
              <p>
                Library roots themselves are still managed from <Link to="/imports">Import</Link>.
              </p>
            </div>

            <div className="page-actions">
              <button type="submit" disabled={mediaSaving}>{mediaSaving ? "Saving..." : "Save Media Settings"}</button>
            </div>
          </form>
        </section>

        <section className="settings-panel disabled">
          <h3>Network & Agent Access</h3>
          <p>{settings.network.summary}</p>
          <div className="settings-note">
            <strong>Public server URL</strong>
            <p>{settings.network.publicServerUrl}</p>
            <strong>Agent server URL</strong>
            <p>{settings.network.agentServerUrl}</p>
            <strong>API listen URLs</strong>
            <p>{settings.network.apiListenUrls}</p>
            <strong>Web listen host</strong>
            <p>{settings.network.webListenHost}</p>
            <p>
              These values come from process startup and environment config. Use <Link to="/machines/add">Add Machine</Link> for remote agent onboarding.
            </p>
          </div>
        </section>

        <section className="settings-panel">
          <h3>Reset Library</h3>
          <p>Use this to wipe imported library state and cached normalized assets while keeping your configured roots for a clean re-scan.</p>
          <div className="page-actions">
            <button type="button" className="secondary-button inline-button" onClick={() => void onResetLibrary()} disabled={resettingLibrary}>
              {resettingLibrary ? "Resetting..." : "Reset Library State"}
            </button>
          </div>
        </section>
      </div>
    </div>
  );
}
