import { FormEvent, useEffect, useMemo, useState } from "react";
import { api } from "../api/client";
import type { MachineMountRecord } from "../types/api";

interface Props {
  machineId: string;
  capabilities: string[];
}

const ACTIVE_STATUSES = new Set(["Pending", "Mounted", "DismountRequested"]);

function relative(value: string) {
  const diffMinutes = Math.max(0, Math.round((Date.now() - new Date(value).getTime()) / 60000));
  if (diffMinutes < 1) return "Just now";
  if (diffMinutes < 60) return `${diffMinutes}m ago`;
  if (diffMinutes < 24 * 60) return `${Math.round(diffMinutes / 60)}h ago`;
  return `${Math.round(diffMinutes / 1440)}d ago`;
}

function hasCapability(capabilities: string[], capability: string) {
  return capabilities.some((value) => value.localeCompare(capability, undefined, { sensitivity: "accent" }) === 0);
}

export function MountIsoPanel({ machineId, capabilities }: Props) {
  const [imagePath, setImagePath] = useState("");
  const [mounts, setMounts] = useState<MachineMountRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [busyMountId, setBusyMountId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function loadMounts() {
    const records = await api.listMounts(machineId);
    setMounts(records);
  }

  useEffect(() => {
    let cancelled = false;

    async function initialLoad() {
      setLoading(true);
      try {
        const records = await api.listMounts(machineId);
        if (!cancelled) {
          setMounts(records);
          setError(null);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Could not load mount history.");
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void initialLoad();
    return () => {
      cancelled = true;
    };
  }, [machineId]);

  const hasActiveMount = useMemo(() => mounts.some((mount) => ACTIVE_STATUSES.has(mount.status)), [mounts]);

  useEffect(() => {
    if (!hasActiveMount) {
      return undefined;
    }

    const handle = setInterval(() => {
      void loadMounts().catch(() => {
        // Keep the last known mount state if a poll fails.
      });
    }, 2000);

    return () => clearInterval(handle);
  }, [hasActiveMount, machineId]);

  async function onMount(event: FormEvent) {
    event.preventDefault();
    if (!imagePath.trim()) return;

    setSubmitting(true);
    setError(null);
    try {
      await api.createMount(machineId, imagePath.trim());
      setImagePath("");
      await loadMounts();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Mount request failed.");
    } finally {
      setSubmitting(false);
    }
  }

  async function onDismount(mountId: string) {
    setBusyMountId(mountId);
    setError(null);
    try {
      await api.requestDismount(machineId, mountId);
      await loadMounts();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Dismount request failed.");
    } finally {
      setBusyMountId(null);
    }
  }

  const supportsWinCDEmu = hasCapability(capabilities, "wincdemu");
  const formatSummary = supportsWinCDEmu
    ? "Supports ISO, IMG, VHD, VHDX, MDF/MDS, BIN/CUE, NRG, CCD, and CDI."
    : "Supports ISO, IMG, VHD, and VHDX directly. Raw optical images such as MDF/MDS or BIN/CUE need WinCDEmu on this machine.";

  return (
    <div className="mount-iso-panel">
      <h3>Mount Image</h3>
      <p className="panel-description">Use Gamarr as a manual image-mount queue for this machine. {formatSummary}</p>

      <form className="form" onSubmit={onMount}>
        <label>
          Image Path
          <input
            type="text"
            placeholder="E:\\Games\\Motocross Madness 2\\dvn-mcm2.cue"
            value={imagePath}
            onChange={(event) => setImagePath(event.target.value)}
          />
        </label>
        {error ? <div className="inline-error">{error}</div> : null}
        <button type="submit" disabled={!imagePath.trim() || submitting}>
          {submitting ? "Requesting..." : "Mount"}
        </button>
      </form>

      {loading ? (
        <p className="panel-description">Loading mount history…</p>
      ) : mounts.length === 0 ? (
        <p className="panel-description">No manual mounts have been requested for this machine yet.</p>
      ) : (
        <div className="table-card compact-table-card">
          <table className="data-table">
            <thead>
              <tr>
                <th>Image</th>
                <th>Status</th>
                <th>Drive</th>
                <th>Requested</th>
                <th>Result</th>
                <th className="actions-column">Actions</th>
              </tr>
            </thead>
            <tbody>
              {mounts.map((mount) => {
                const isMounted = mount.status === "Mounted";
                const isPending = mount.status === "Pending" || mount.status === "DismountRequested";
                return (
                  <tr key={mount.id}>
                    <td>
                      <div className="table-title-cell">
                        <strong>{mount.isoPath.split(/[\\/]/).pop() ?? mount.isoPath}</strong>
                        <span>{mount.isoPath}</span>
                      </div>
                    </td>
                    <td>{isPending ? `${mount.status}...` : mount.status}</td>
                    <td>{mount.driveLetter ?? "—"}</td>
                    <td title={new Date(mount.createdAtUtc).toLocaleString()}>{relative(mount.createdAtUtc)}</td>
                    <td>{mount.errorMessage ?? (mount.completedAtUtc ? new Date(mount.completedAtUtc).toLocaleString() : "—")}</td>
                    <td className="actions-column">
                      {isMounted ? (
                        <button
                          type="button"
                          className="secondary-button inline-button"
                          disabled={busyMountId === mount.id}
                          onClick={() => void onDismount(mount.id)}>
                          {busyMountId === mount.id ? "Requesting..." : "Dismount"}
                        </button>
                      ) : null}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
