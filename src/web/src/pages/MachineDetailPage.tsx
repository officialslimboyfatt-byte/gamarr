import { Link, useNavigate } from "react-router-dom";
import { useParams } from "react-router-dom";
import { api } from "../api/client";
import { Card } from "../components/Card";
import { MountIsoPanel } from "../components/MountIsoPanel";
import { PageHeader } from "../components/PageHeader";
import { PageState } from "../components/PageState";
import { useSelectedMachine } from "../context/SelectedMachineContext";
import { usePollingAsyncData } from "../hooks/usePollingAsyncData";
import { useState } from "react";

export function MachineDetailPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const { selectedMachineId, setSelectedMachineId, machines, reloadMachines } = useSelectedMachine();
  const { data: machine, loading, error, reload } = usePollingAsyncData(() => api.getMachine(id ?? ""), [id], 10000, Boolean(id));
  const [installingWinCDEmu, setInstallingWinCDEmu] = useState(false);
  const [removingMachine, setRemovingMachine] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  if (!id) {
    return <PageState title="Machine not found" description="The requested machine id is missing." tone="error" />;
  }

  if (loading) {
    return <PageState title="Loading machine" description="Fetching registration and heartbeat details." tone="loading" />;
  }

  if (error || !machine) {
    return (
      <PageState
        title="Machine unavailable"
        description={error ?? "The machine could not be loaded."}
        actionLabel="Retry"
        onAction={() => void reload()}
        tone="error"
      />
    );
  }

  const currentMachine = machine;

  const hasWinCDEmu = currentMachine.capabilities.some((value) => value.localeCompare("wincdemu", undefined, { sensitivity: "accent" }) === 0);

  async function installWinCDEmu() {
    setInstallingWinCDEmu(true);
    setActionError(null);
    try {
      await api.installWinCDEmu(currentMachine.id);
      await reload();
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Could not queue WinCDEmu install.");
    } finally {
      setInstallingWinCDEmu(false);
    }
  }

  async function removeMachine() {
    if (!currentMachine.canRemove) {
      setActionError(currentMachine.removeBlockedReason ?? "This machine cannot be removed.");
      return;
    }

    if (selectedMachineId === currentMachine.id && machines.length <= 1) {
      setActionError("Select or register another machine before removing the current shell target.");
      return;
    }

    const confirmed = window.confirm(`Remove stale machine '${currentMachine.name}'?`);
    if (!confirmed) {
      return;
    }

    setRemovingMachine(true);
    setActionError(null);
    try {
      if (selectedMachineId === currentMachine.id) {
        const fallback = machines.find((machine) => machine.id !== currentMachine.id);
        if (fallback) {
          setSelectedMachineId(fallback.id);
        }
      }
      await api.removeMachine(currentMachine.id);
      await reloadMachines();
      void navigate("/machines");
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Could not remove machine.");
    } finally {
      setRemovingMachine(false);
    }
  }

  return (
    <div className="page-grid">
      <PageHeader
        eyebrow="Machine Detail"
        title={currentMachine.name}
        description={`${currentMachine.hostname} | ${currentMachine.operatingSystem}`}
        actions={
          <div className="page-actions">
            <Link className="secondary-button inline-button" to="/machines/add">
              Add Machine
            </Link>
            {currentMachine.canRemove ? (
              <button type="button" className="secondary-button inline-button" disabled={removingMachine} onClick={() => void removeMachine()}>
                {removingMachine ? "Removing..." : "Remove Stale Machine"}
              </button>
            ) : null}
          </div>
        }
      />
      <Card title={currentMachine.name} subtitle={currentMachine.hostname}>
        <div className="detail-grid">
          <div>
            <label>Stable Key</label>
            <p>{currentMachine.stableKey}</p>
          </div>
          <div>
            <label>Agent Version</label>
            <p>{currentMachine.agentVersion}</p>
          </div>
          <div>
            <label>Last Heartbeat</label>
            <p>{new Date(currentMachine.lastHeartbeatUtc).toLocaleString()}</p>
          </div>
          <div>
            <label>Capabilities</label>
            <p>{currentMachine.capabilities.join(", ")}</p>
          </div>
          <div>
            <label>Lifecycle</label>
            <p>{currentMachine.isStale ? "Stale / offline record" : "Active machine record"}</p>
          </div>
          <div>
            <label>Removal</label>
            <p>{currentMachine.canRemove ? "Safe to remove." : currentMachine.removeBlockedReason ?? "Not removable."}</p>
          </div>
        </div>
        {!hasWinCDEmu ? (
          <div className="table-actions">
            <button type="button" onClick={() => void installWinCDEmu()} disabled={installingWinCDEmu || currentMachine.status === "Offline"}>
              {installingWinCDEmu ? "Queueing..." : "Install WinCDEmu"}
            </button>
            <span>{currentMachine.status === "Offline" ? "Machine is offline." : "Queues a one-click WinCDEmu install job for this machine."}</span>
          </div>
        ) : null}
        {actionError ? <div className="inline-error">{actionError}</div> : null}
      </Card>
      <Card title="Image Mount" subtitle="Manually mount disc and virtual disk images on this machine">
        <MountIsoPanel machineId={currentMachine.id} capabilities={currentMachine.capabilities} />
      </Card>
    </div>
  );
}
