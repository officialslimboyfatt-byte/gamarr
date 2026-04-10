import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api/client";
import { PageHeader } from "../components/PageHeader";
import { PageState } from "../components/PageState";
import { StatusPill } from "../components/StatusPill";
import { useSelectedMachine } from "../context/SelectedMachineContext";

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

export function MachinesPage() {
  const { allMachines, selectedMachineId, loading, error, reloadMachines, setSelectedMachineId, machines } = useSelectedMachine();
  const [showDuplicates, setShowDuplicates] = useState(false);
  const [busyMachineId, setBusyMachineId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const rows = useMemo(() => {
    const sorted = [...allMachines].sort((left, right) => {
      const keyCompare = `${left.hostname}:${left.name}`.localeCompare(`${right.hostname}:${right.name}`);
      if (keyCompare !== 0) return keyCompare;
      return new Date(right.lastHeartbeatUtc).getTime() - new Date(left.lastHeartbeatUtc).getTime();
    });

    return sorted.map((machine, index) => {
      const key = `${machine.hostname.trim().toLowerCase()}::${machine.name.trim().toLowerCase()}`;
      const priorIndex = sorted.findIndex((candidate) => `${candidate.hostname.trim().toLowerCase()}::${candidate.name.trim().toLowerCase()}` === key);
      return {
        machine,
        isDuplicate: priorIndex !== index,
        isSelected: selectedMachineId === machine.id
      };
    });
  }, [allMachines, selectedMachineId]);

  const visibleRows = showDuplicates ? rows : rows.filter((row) => !row.isDuplicate);

  async function installWinCDEmu(machineId: string) {
    setBusyMachineId(machineId);
    setActionError(null);
    try {
      await api.installWinCDEmu(machineId);
      await reloadMachines();
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Could not queue WinCDEmu install.");
    } finally {
      setBusyMachineId(null);
    }
  }

  async function removeMachine(machineId: string, machineName: string) {
    const current = allMachines.find((machine) => machine.id === machineId);
    if (!current) return;

    if (!current.canRemove) {
      setActionError(current.removeBlockedReason ?? "This machine cannot be removed.");
      return;
    }

    if (selectedMachineId === machineId && machines.length <= 1) {
      setActionError("Select or register another machine before removing the current shell target.");
      return;
    }

    const confirmed = window.confirm(`Remove stale machine '${machineName}'? This only deletes the machine record when it is safe to remove.`);
    if (!confirmed) {
      return;
    }

    setBusyMachineId(machineId);
    setActionError(null);
    try {
      if (selectedMachineId === machineId) {
        const fallback = machines.find((machine) => machine.id !== machineId);
        if (fallback) {
          setSelectedMachineId(fallback.id);
        }
      }

      await api.removeMachine(machineId);
      await reloadMachines();
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Could not remove machine.");
    } finally {
      setBusyMachineId(null);
    }
  }

  return (
    <div className="page-grid">
      <PageHeader
        eyebrow="Machines"
        title="Machines"
        description="Monitor registered agents, onboard new remote PCs, and clean up stale machine records."
        actions={
          <div className="page-actions">
            <Link className="secondary-button inline-button" to="/machines/add">
              Add Machine
            </Link>
            <button type="button" className="secondary-button inline-button" onClick={() => setShowDuplicates((value) => !value)}>
              {showDuplicates ? "Hide Duplicates" : "Show Duplicates"}
            </button>
            <button type="button" className="secondary-button inline-button" onClick={() => void reloadMachines()}>
              Refresh
            </button>
          </div>
        }
      />
      {loading ? (
        <PageState title="Loading machines" description="Fetching machine registrations and heartbeat state." tone="loading" />
      ) : error ? (
        <PageState title="Machines unavailable" description={error} actionLabel="Retry" onAction={() => void reloadMachines()} tone="error" />
      ) : visibleRows.length === 0 ? (
        <PageState title="No machines registered" description="Start the agent once to register a machine with the server." />
      ) : (
        <div className="table-card">
          {actionError ? <div className="inline-error">{actionError}</div> : null}
          <table className="data-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Host</th>
                <th>Status</th>
                <th>Last Heartbeat</th>
                <th>Registered</th>
                <th>Capabilities</th>
                <th>Record</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {visibleRows.map(({ machine, isDuplicate, isSelected }) => (
                <tr key={machine.id}>
                  <td>
                    <div className="table-title-cell">
                      <Link className="table-title-link" to={`/machines/${machine.id}`}>
                        {machine.name}
                      </Link>
                      <span>{isSelected ? "Shell target" : isDuplicate ? "Duplicate record" : "Primary record"}</span>
                    </div>
                  </td>
                  <td>{machine.hostname}</td>
                  <td>
                    <div className="table-actions">
                      <StatusPill
                        label={machine.status}
                        tone={machine.status === "Online" ? "success" : machine.status === "Busy" ? "warning" : "neutral"}
                      />
                      {isDuplicate ? <StatusPill label="Duplicate" tone="warning" /> : null}
                      {machine.isStale ? <StatusPill label="Stale" tone="warning" /> : null}
                    </div>
                  </td>
                  <td>{relative(machine.lastHeartbeatUtc)}</td>
                  <td>{new Date(machine.registeredAtUtc).toLocaleString()}</td>
                  <td>
                    <div className="table-actions">
                      <StatusPill label={hasCapability(machine.capabilities, "wincdemu") ? "WinCDEmu" : "No WinCDEmu"} tone={hasCapability(machine.capabilities, "wincdemu") ? "success" : "warning"} />
                      <StatusPill label={hasCapability(machine.capabilities, "7zip") ? "7-Zip" : "No 7-Zip"} tone={hasCapability(machine.capabilities, "7zip") ? "success" : "warning"} />
                      {!hasCapability(machine.capabilities, "wincdemu") && !isDuplicate ? (
                        <button
                          type="button"
                          className="secondary-button inline-button"
                          disabled={busyMachineId === machine.id || machine.status === "Offline"}
                          onClick={() => void installWinCDEmu(machine.id)}>
                          {busyMachineId === machine.id ? "Queueing..." : "Install WinCDEmu"}
                        </button>
                      ) : null}
                    </div>
                  </td>
                  <td>{machine.stableKey}</td>
                  <td>
                    <div className="table-actions">
                      {machine.canRemove ? (
                        <button
                          type="button"
                          className="secondary-button inline-button"
                          disabled={busyMachineId === machine.id}
                          onClick={() => void removeMachine(machine.id, machine.name)}
                        >
                          {busyMachineId === machine.id ? "Removing..." : "Remove"}
                        </button>
                      ) : (
                        <span className="sidebar-note">{machine.removeBlockedReason}</span>
                      )}
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
