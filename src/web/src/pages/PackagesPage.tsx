import { FormEvent, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api/client";
import { Card } from "../components/Card";
import { PageHeader } from "../components/PageHeader";
import { PageState } from "../components/PageState";
import { useAsyncData } from "../hooks/useAsyncData";

const initialForm = {
  slug: "",
  name: "",
  description: "",
  notes: "",
  genres: "",
  studio: "",
  releaseYear: "",
  mediaType: "DiskImage" as const,
  mediaPath: "",
  discNumber: "",
  entrypointHint: "",
  sourceKind: "Auto" as const,
  scratchPolicy: "Temporary" as const,
  installScriptKind: "PowerShell" as const,
  installScriptPath: "",
  launchExecutablePath: ""
};

export function PackagesPage() {
  const { data, loading, error, reload, setData } = useAsyncData(() => api.listPackages(), []);
  const [form, setForm] = useState(initialForm);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const packages = data ?? [];

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setSubmitError(null);

    try {
      const created = await api.createPackage({
        slug: form.slug,
        name: form.name,
        description: form.description,
        notes: form.notes,
        tags: ["local-media"],
        genres: form.genres.split(",").map((value) => value.trim()).filter(Boolean),
        studio: form.studio,
        releaseYear: form.releaseYear ? Number(form.releaseYear) : null,
        coverImagePath: null,
        version: {
          versionLabel: "1.0",
          supportedOs: "Windows 10, Windows 11",
          architecture: "X64",
          installScriptKind: form.installScriptKind,
          installScriptPath: form.installScriptPath,
          uninstallScriptPath: null,
          timeoutSeconds: 1800,
          notes: "Created from the MVP UI.",
          installStrategy: form.installScriptPath === "builtin:portable-copy" ? "PortableCopy" : form.installScriptPath === "builtin:auto-install" ? "AutoInstall" : "NeedsReview",
          installerFamily: form.installScriptPath === "builtin:portable-copy" ? "Portable" : "Unknown",
          installerPath: form.entrypointHint || null,
          silentArguments: null,
          installDiagnostics: "Created from the MVP UI.",
          launchExecutablePath: form.launchExecutablePath || null,
          media: [
            {
              mediaType: form.mediaType,
              label: "Installer Media",
              path: form.mediaPath,
              discNumber: form.discNumber ? Number(form.discNumber) : null,
              entrypointHint: form.entrypointHint || null,
              sourceKind: form.sourceKind,
              scratchPolicy: form.scratchPolicy
            }
          ],
          detectionRules: [],
          prerequisites: []
        }
      });

      setData((current) => [created, ...(current ?? [])]);
      setForm(initialForm);
    } catch (err) {
      setSubmitError(err instanceof Error ? err.message : "Failed to create package.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="page-grid two-column">
      <PageHeader
        eyebrow="Library"
        title="Packages"
        description="Register local installer media and deployment recipes for managed machines."
      />
      <Card title="Package Library" subtitle="Registered game media and deployment recipes">
        {loading ? (
          <PageState title="Loading packages" description="Reading the package catalog from the server." tone="loading" />
        ) : error ? (
          <PageState title="Packages unavailable" description={error} actionLabel="Retry" onAction={() => void reload()} tone="error" />
        ) : packages.length === 0 ? (
          <PageState title="No packages registered" description="Add your first package with a local ISO, installer folder, or executable path." />
        ) : (
          <div className="list">
            {packages.map((item) => (
              <Link className="list-row link-row" key={item.id} to={`/packages/${item.id}`}>
                <div>
                  <strong>{item.name}</strong>
                  <p>{item.description}</p>
                </div>
                <span>{item.versions[0] ? `${item.versions[0].versionLabel} | ${item.versions[0].installScriptKind}` : "No version"}</span>
              </Link>
            ))}
          </div>
        )}
      </Card>
      <Card title="Create Package" subtitle="Register user-supplied media and script paths">
        <form className="form" onSubmit={onSubmit}>
          <input placeholder="Slug" value={form.slug} onChange={(e) => setForm({ ...form, slug: e.target.value })} />
          <input placeholder="Name" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          <textarea
            placeholder="Description"
            value={form.description}
            onChange={(e) => setForm({ ...form, description: e.target.value })}
          />
          <input placeholder="Studio" value={form.studio} onChange={(e) => setForm({ ...form, studio: e.target.value })} />
          <input placeholder="Genres (comma separated)" value={form.genres} onChange={(e) => setForm({ ...form, genres: e.target.value })} />
          <input placeholder="Release year" value={form.releaseYear} onChange={(e) => setForm({ ...form, releaseYear: e.target.value })} />
          <select value={form.mediaType} onChange={(e) => setForm({ ...form, mediaType: e.target.value as typeof form.mediaType })}>
            <option value="DiskImage">Disk image (.iso, .img, .bin/.cue, .mdf/.mds, .nrg, .vhd, .vhdx)</option>
            <option value="Iso">ISO image</option>
            <option value="InstallerFolder">Installer folder</option>
            <option value="Executable">Executable</option>
            <option value="Patch">Patch</option>
            <option value="SupportFiles">Support files</option>
          </select>
          <input
            placeholder="Local media path"
            value={form.mediaPath}
            onChange={(e) => setForm({ ...form, mediaPath: e.target.value })}
          />
          <input
            placeholder="Disc number (optional)"
            value={form.discNumber}
            onChange={(e) => setForm({ ...form, discNumber: e.target.value })}
          />
          <input
            placeholder="Entrypoint hint (optional)"
            value={form.entrypointHint}
            onChange={(e) => setForm({ ...form, entrypointHint: e.target.value })}
          />
          <select value={form.sourceKind} onChange={(e) => setForm({ ...form, sourceKind: e.target.value as typeof form.sourceKind })}>
            <option value="Auto">Auto-detect source kind</option>
            <option value="DirectFolder">Direct folder</option>
            <option value="MountedVolume">Mounted volume</option>
            <option value="ExtractedWorkspace">Extracted workspace</option>
            <option value="MultiDisc">Multi-disc set</option>
          </select>
          <select value={form.scratchPolicy} onChange={(e) => setForm({ ...form, scratchPolicy: e.target.value as typeof form.scratchPolicy })}>
            <option value="Temporary">Temporary scratch</option>
            <option value="Persistent">Persistent cache</option>
            <option value="Prompt">Prompt per title</option>
          </select>
          <select
            value={form.installScriptKind}
            onChange={(e) => setForm({ ...form, installScriptKind: e.target.value as typeof form.installScriptKind })}
          >
            <option value="PowerShell">PowerShell</option>
            <option value="MockRecipe">Mock recipe</option>
          </select>
          <input
            placeholder="Install script path"
            value={form.installScriptPath}
            onChange={(e) => setForm({ ...form, installScriptPath: e.target.value })}
          />
          <input
            placeholder="Launch path override"
            value={form.launchExecutablePath}
            onChange={(e) => setForm({ ...form, launchExecutablePath: e.target.value })}
          />
          <textarea placeholder="Notes" value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
          {submitError ? <div className="inline-error">{submitError}</div> : null}
          <button type="submit" disabled={submitting}>
            {submitting ? "Creating..." : "Create Package"}
          </button>
        </form>
      </Card>
    </div>
  );
}
