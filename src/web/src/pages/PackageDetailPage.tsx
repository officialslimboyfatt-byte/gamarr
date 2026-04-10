import { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import { api } from "../api/client";
import { Card } from "../components/Card";
import { PageHeader } from "../components/PageHeader";
import { PageState } from "../components/PageState";
import { useAsyncData } from "../hooks/useAsyncData";

export function PackageDetailPage() {
  const { id } = useParams();
  const { data: pkg, loading, error, reload } = useAsyncData(() => api.getPackage(id ?? ""), [id]);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saveFeedback, setSaveFeedback] = useState<string | null>(null);
  const [installStrategy, setInstallStrategy] = useState("AutoInstall");
  const [installerFamily, setInstallerFamily] = useState("Unknown");
  const [installerPath, setInstallerPath] = useState("");
  const [silentArguments, setSilentArguments] = useState("");
  const [launchExecutablePath, setLaunchExecutablePath] = useState("");
  const [uninstallScriptPath, setUninstallScriptPath] = useState("");
  const [uninstallArguments, setUninstallArguments] = useState("");
  const [installDiagnostics, setInstallDiagnostics] = useState("");
  const [detectionRules, setDetectionRules] = useState<Array<{ ruleType: string; value: string }>>([]);

  useEffect(() => {
    const version = pkg?.versions[0];
    if (!version) {
      return;
    }

    setInstallStrategy(version.installStrategy);
    setInstallerFamily(version.installerFamily);
    setInstallerPath(version.installerPath ?? "");
    setSilentArguments(version.silentArguments ?? "");
    setLaunchExecutablePath(version.launchExecutablePath ?? "");
    setUninstallScriptPath(version.uninstallScriptPath ?? "");
    setUninstallArguments(version.uninstallArguments ?? "");
    setInstallDiagnostics(version.installDiagnostics);
    setDetectionRules(version.detectionRules.map((rule) => ({ ruleType: rule.ruleType, value: rule.value })));
  }, [pkg]);

  if (!id) {
    return <PageState title="Package not found" description="The requested package id is missing." tone="error" />;
  }

  if (loading) {
    return <PageState title="Loading package" description="Fetching package details and media references." tone="loading" />;
  }

  if (error || !pkg) {
    return <PageState title="Package unavailable" description={error ?? "The package could not be loaded."} actionLabel="Retry" onAction={() => void reload()} tone="error" />;
  }

  const version = pkg.versions[0];
  const packageId = pkg.id;

  async function onSaveInstallPlan() {
    setSaving(true);
    setSaveError(null);
    setSaveFeedback(null);

    try {
      await api.updatePackageInstallPlan(packageId, {
        installStrategy,
        installerFamily,
        installerPath: installerPath || null,
        silentArguments: silentArguments || null,
        installDiagnostics,
        launchExecutablePath: launchExecutablePath || null,
        uninstallScriptPath: uninstallScriptPath || null,
        uninstallArguments: uninstallArguments || null,
        detectionRules: detectionRules.filter((rule) => rule.ruleType.trim() && rule.value.trim())
      });
      setSaveFeedback("Install plan saved.");
      await reload();
    } catch (requestError) {
      setSaveError(requestError instanceof Error ? requestError.message : "Failed to save install plan.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="page-grid two-column">
      <PageHeader eyebrow="Package Detail" title={pkg.name} description={pkg.description} />
      <Card title={pkg.name} subtitle={pkg.description}>
        <div className="detail-grid">
          <div>
            <label>Studio</label>
            <p>{pkg.studio || "Unknown studio"}</p>
          </div>
          <div>
            <label>Release Year</label>
            <p>{pkg.releaseYear ?? "Unknown"}</p>
          </div>
          <div>
            <label>Slug</label>
            <p>{pkg.slug}</p>
          </div>
          <div>
            <label>Supported OS</label>
            <p>{version?.supportedOs}</p>
          </div>
          <div>
            <label>Install Script</label>
            <p>{version?.installScriptPath}</p>
          </div>
          <div>
            <label>Install Strategy</label>
            <p>{version?.installStrategy}</p>
          </div>
          <div>
            <label>Installer Family</label>
            <p>{version?.installerFamily}</p>
          </div>
          <div>
            <label>Processing State</label>
            <p>{version?.processingState}</p>
          </div>
          <div>
            <label>Normalized Asset</label>
            <p>{version?.normalizedAssetRootPath ?? "Not built yet"}</p>
          </div>
          <div>
            <label>Launch Path</label>
            <p>{version?.launchExecutablePath ?? "Detection-derived"}</p>
          </div>
          <div>
            <label>Notes</label>
            <p>{pkg.notes}</p>
          </div>
          <div>
            <label>Version</label>
            <p>{version?.versionLabel}</p>
          </div>
          <div>
            <label>Genres</label>
            <p>{pkg.genres.join(", ") || "None"}</p>
          </div>
          <div>
            <label>Diagnostics</label>
            <p>{version?.installDiagnostics || "None"}</p>
          </div>
          <div>
            <label>Normalization</label>
            <p>{version?.normalizationDiagnostics || "None"}</p>
          </div>
          <div>
            <label>Archived</label>
            <p>{pkg.isArchived ? `${pkg.archivedReason ?? "Archived"}${pkg.archivedAtUtc ? ` | ${new Date(pkg.archivedAtUtc).toLocaleString()}` : ""}` : "Visible"}</p>
          </div>
        </div>
      </Card>
      <Card title="Install Review" subtitle="Override installer path, family, silent args, launch path, and detection rules">
        <div className="form">
          <select value={installStrategy} onChange={(event) => setInstallStrategy(event.target.value)}>
            <option value="PortableCopy">PortableCopy</option>
            <option value="AutoInstall">AutoInstall</option>
            <option value="NeedsReview">NeedsReview</option>
          </select>
          <select value={installerFamily} onChange={(event) => setInstallerFamily(event.target.value)}>
            <option value="Portable">Portable</option>
            <option value="Msi">Msi</option>
            <option value="Inno">Inno</option>
            <option value="Nsis">Nsis</option>
            <option value="InstallShield">InstallShield</option>
            <option value="Unknown">Unknown</option>
          </select>
          <input placeholder="Installer path relative to media root" value={installerPath} onChange={(event) => setInstallerPath(event.target.value)} />
          <input placeholder="Silent arguments" value={silentArguments} onChange={(event) => setSilentArguments(event.target.value)} />
          <input placeholder="Launch executable path" value={launchExecutablePath} onChange={(event) => setLaunchExecutablePath(event.target.value)} />
          <input placeholder="Uninstall command path" value={uninstallScriptPath} onChange={(event) => setUninstallScriptPath(event.target.value)} />
          <input placeholder="Uninstall arguments" value={uninstallArguments} onChange={(event) => setUninstallArguments(event.target.value)} />
          <textarea placeholder="Install diagnostics" value={installDiagnostics} onChange={(event) => setInstallDiagnostics(event.target.value)} />
          <div className="list">
            {detectionRules.map((rule, index) => (
              <div className="list-row" key={`${rule.ruleType}-${index}`}>
                <div className="form compact-form" style={{ width: "100%" }}>
                  <select
                    value={rule.ruleType}
                    onChange={(event) =>
                      setDetectionRules((current) => current.map((item, itemIndex) => (itemIndex === index ? { ...item, ruleType: event.target.value } : item)))
                    }
                  >
                    <option value="FileExists">FileExists</option>
                    <option value="RegistryValueExists">RegistryValueExists</option>
                    <option value="UninstallEntryExists">UninstallEntryExists</option>
                    <option value="FileVersionEquals">FileVersionEquals</option>
                    <option value="FileVersionAtLeast">FileVersionAtLeast</option>
                  </select>
                  <input
                    placeholder="Rule value"
                    value={rule.value}
                    onChange={(event) =>
                      setDetectionRules((current) => current.map((item, itemIndex) => (itemIndex === index ? { ...item, value: event.target.value } : item)))
                    }
                  />
                </div>
                <button
                  type="button"
                  className="secondary-button inline-button"
                  onClick={() => setDetectionRules((current) => current.filter((_, itemIndex) => itemIndex !== index))}
                >
                  Remove
                </button>
              </div>
            ))}
          </div>
          <button type="button" className="secondary-button inline-button" onClick={() => setDetectionRules((current) => [...current, { ruleType: "FileExists", value: "" }])}>
            Add Detection Rule
          </button>
          {saveError ? <div className="inline-error">{saveError}</div> : null}
          {saveFeedback ? <div className="header-chip">{saveFeedback}</div> : null}
          <button type="button" onClick={() => void onSaveInstallPlan()} disabled={saving}>
            {saving ? "Saving..." : "Save Install Plan"}
          </button>
        </div>
      </Card>
      <Card title="Media" subtitle="User-supplied paths referenced by this package">
        {version?.media.length ? (
          <div className="list">
            {version.media.map((media) => (
              <div className="list-row" key={media.id}>
                <div>
                  <strong>{media.label}</strong>
                  <p>{media.path}</p>
                </div>
                <span>{media.mediaType}</span>
              </div>
            ))}
          </div>
        ) : (
          <PageState title="No media registered" description="This package version does not yet reference any local installer sources." />
        )}
      </Card>
      <Card title="Detection Rules" subtitle="Prepared for real validation and install detection">
        {version?.detectionRules.length ? (
          <div className="list">
            {version.detectionRules.map((rule) => (
              <div className="list-row" key={rule.id}>
                <div>
                  <strong>{rule.ruleType}</strong>
                  <p>{rule.value}</p>
                </div>
              </div>
            ))}
          </div>
        ) : (
          <PageState title="No detection rules" description="Add detection rules to validate whether installation succeeded." />
        )}
      </Card>
      <Card title="Manifest" subtitle={version?.manifestFormatVersion ?? "Package definition artifact"}>
        {version?.manifestJson ? (
          <pre className="manifest-viewer">{version.manifestJson}</pre>
        ) : (
          <PageState title="No manifest" description="This package version does not have a persisted package manifest artifact." />
        )}
      </Card>
    </div>
  );
}
