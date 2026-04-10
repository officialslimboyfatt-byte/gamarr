import { FormEvent, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../api/client";
import type { MachineRecord } from "../types/api";

interface Props {
  machines: MachineRecord[];
  onClose: () => void;
}

export function QuickInstallModal({ machines, onClose }: Props) {
  const navigate = useNavigate();
  const [isoPath, setIsoPath] = useState("");
  const [machineId, setMachineId] = useState(machines[0]?.id ?? "");
  const [label, setLabel] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!label && isoPath) {
      const filename = isoPath.split(/[\\/]/).pop() ?? "";
      setLabel(filename.replace(/\.[^.]+$/, ""));
    }
  }, [isoPath, label]);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!isoPath.trim() || !machineId) return;
    setSubmitting(true);
    setError(null);

    try {
      const result = await api.quickInstall({
        isoPath: isoPath.trim(),
        machineId,
        label: label.trim() || undefined
      });
      onClose();
      navigate(`/jobs/${result.jobId}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Quick install failed.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>Quick Install from ISO</h2>
          <button type="button" className="modal-close" onClick={onClose}>✕</button>
        </div>
        <form className="form" onSubmit={onSubmit}>
          <label>
            ISO Path
            <input
              type="text"
              placeholder="E:\Games\HalfLife.iso"
              value={isoPath}
              onChange={(e) => setIsoPath(e.target.value)}
              autoFocus
            />
          </label>
          <label>
            Label <span className="field-hint">(optional — auto-filled from filename)</span>
            <input
              type="text"
              placeholder="Half-Life"
              value={label}
              onChange={(e) => setLabel(e.target.value)}
            />
          </label>
          <label>
            Machine
            <select value={machineId} onChange={(e) => setMachineId(e.target.value)}>
              {machines.map((m) => (
                <option key={m.id} value={m.id}>{m.name} ({m.hostname})</option>
              ))}
            </select>
          </label>
          {error ? <div className="inline-error">{error}</div> : null}
          <div className="modal-actions">
            <button type="button" className="secondary-button" onClick={onClose}>Cancel</button>
            <button type="submit" disabled={!isoPath.trim() || !machineId || submitting || machines.length === 0}>
              {submitting ? "Starting..." : "Install"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
