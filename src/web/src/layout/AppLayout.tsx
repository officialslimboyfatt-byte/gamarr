import { NavLink } from "react-router-dom";
import type { PropsWithChildren } from "react";
import { useSelectedMachine } from "../context/SelectedMachineContext";
import { api } from "../api/client";
import { StatusPill } from "../components/StatusPill";
import { usePollingAsyncData } from "../hooks/usePollingAsyncData";

const navSections = [
  {
    heading: "Library",
    items: [
      { to: "/", label: "Titles" },
      { to: "/imports", label: "Import" }
    ]
  },
  {
    heading: "System",
    items: [
      { to: "/system/health", label: "Health" },
      { to: "/system/events", label: "Events" },
      { to: "/system/logs", label: "Logs" },
      { to: "/system/queue", label: "Queue" },
      { to: "/machines", label: "Machines" },
      { to: "/packages", label: "Packages" },
      { to: "/settings", label: "Settings" }
    ]
  }
];

export function AppLayout({ children }: PropsWithChildren) {
  const { machines, selectedMachineId, setSelectedMachineId, selectedMachine, loading, duplicateCount } = useSelectedMachine();
  const systemHealth = usePollingAsyncData(() => api.getSystemHealth(), [], 10000, true);

  function formatRelativeTime(value?: string | null) {
    if (!value) {
      return "Unknown";
    }

    const diffMinutes = Math.max(0, Math.round((Date.now() - new Date(value).getTime()) / 60000));
    if (diffMinutes < 1) return "Active now";
    if (diffMinutes < 60) return `Active ${diffMinutes}m ago`;
    const diffHours = Math.round(diffMinutes / 60);
    if (diffHours < 24) return `Active ${diffHours}h ago`;
    return `Active ${Math.round(diffHours / 24)}d ago`;
  }

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="sidebar-block">
          <div className="brand">Gamarr</div>
          <p className="brand-subtitle">Game library automation with a machine-aware system console, queue management, and operational health.</p>
        </div>
        <div className="sidebar-block">
          <div className="sidebar-heading">Target Machine</div>
          <div className="sidebar-panel">
            <strong>{selectedMachine?.name ?? (loading ? "Loading..." : "No machine")}</strong>
            <p>{selectedMachine ? `${selectedMachine.hostname} | ${selectedMachine.status} | ${formatRelativeTime(selectedMachine.lastHeartbeatUtc)}` : "Choose the active install/play target."}</p>
            <select
              className="shell-select"
              value={selectedMachineId}
              onChange={(event) => setSelectedMachineId(event.target.value)}
              disabled={!machines.length}
            >
              {machines.map((machine) => (
                <option key={machine.id} value={machine.id}>
                  {`${machine.name} | ${machine.status} | ${formatRelativeTime(machine.lastHeartbeatUtc)}`}
                </option>
              ))}
            </select>
            {duplicateCount > 0 ? <span className="sidebar-note">{duplicateCount} duplicate record{duplicateCount === 1 ? "" : "s"} hidden</span> : null}
          </div>
        </div>
        <div className="sidebar-block">
          <div className="sidebar-heading">System Status</div>
          <div className="sidebar-panel">
            <div className="sidebar-health-row">
              <strong>{systemHealth.data?.summary ?? "Loading system health..."}</strong>
              <StatusPill
                label={systemHealth.data?.overallStatus ?? "Loading"}
                tone={
                  systemHealth.data?.overallStatus === "Healthy"
                    ? "success"
                    : systemHealth.data?.overallStatus === "Error"
                      ? "danger"
                      : "warning"
                }
              />
            </div>
            <p>
              {systemHealth.data
                ? `${systemHealth.data.metrics.onlineMachines}/${systemHealth.data.metrics.totalMachines} machine(s) online, ${systemHealth.data.metrics.activeJobs} active job(s).`
                : "Checking health, queue, and machine availability."}
            </p>
          </div>
        </div>
        {navSections.map((section) => (
          <div className="sidebar-block" key={section.heading}>
            <div className="sidebar-heading">{section.heading}</div>
            <nav className="nav">
              {section.items.map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  className={({ isActive }) => (isActive ? "nav-link active" : "nav-link")}
                  end={item.to === "/"}
                >
                  {item.label}
                </NavLink>
              ))}
            </nav>
          </div>
        ))}
      </aside>
      <main className="content">
        <header className="topbar">
          <div className="topbar-title">
            <h1>Gamarr</h1>
            <p>Library-first game management with a system health, events, logs, and queue control plane.</p>
          </div>
          <div className="topbar-status">
            <span className="header-chip">Target <strong>{selectedMachine?.name ?? "No Machine"}</strong></span>
            <span className="header-chip">Queue <strong>{systemHealth.data?.metrics.activeJobs ?? 0} Active</strong></span>
            <span className="header-chip">System <strong>{systemHealth.data?.overallStatus ?? "Loading"}</strong></span>
          </div>
        </header>
        {children}
      </main>
    </div>
  );
}
