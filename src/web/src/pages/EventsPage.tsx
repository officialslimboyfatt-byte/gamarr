import { Link, useSearchParams } from "react-router-dom";
import { api } from "../api/client";
import { PageHeader } from "../components/PageHeader";
import { PageState } from "../components/PageState";
import { StatusPill } from "../components/StatusPill";
import { useSelectedMachine } from "../context/SelectedMachineContext";
import { usePollingAsyncData } from "../hooks/usePollingAsyncData";

function relative(value?: string | null) {
  if (!value) return "Unknown";
  const diffMinutes = Math.max(0, Math.round((Date.now() - new Date(value).getTime()) / 60000));
  if (diffMinutes < 1) return "Just now";
  if (diffMinutes < 60) return `${diffMinutes}m ago`;
  if (diffMinutes < 24 * 60) return `${Math.round(diffMinutes / 60)}h ago`;
  return `${Math.round(diffMinutes / 1440)}d ago`;
}

function severityTone(severity: string) {
  switch (severity) {
    case "Error":
      return "danger";
    case "Warning":
      return "warning";
    default:
      return "neutral";
  }
}

export function EventsPage() {
  const { machines } = useSelectedMachine();
  const [searchParams, setSearchParams] = useSearchParams();
  const category = searchParams.get("category") ?? "";
  const severity = searchParams.get("severity") ?? "";
  const machineId = searchParams.get("machineId") ?? "";
  const search = searchParams.get("search") ?? "";

  const state = usePollingAsyncData(
    () => api.listSystemEvents({ category: category || undefined, severity: severity || undefined, machineId: machineId || undefined, search: search || undefined, limit: 200 }),
    [category, severity, machineId, search],
    10000,
    true
  );

  function updateParam(key: string, value: string) {
    const next = new URLSearchParams(searchParams);
    if (value) next.set(key, value);
    else next.delete(key);
    setSearchParams(next);
  }

  const events = state.data ?? [];

  return (
    <div className="page-grid">
      <PageHeader
        eyebrow="System"
        title="Events"
        description="Review a unified timeline of jobs, scans, normalization activity, and machine mount actions."
        actions={<button type="button" className="secondary-button inline-button" onClick={() => void state.reload()}>Refresh</button>}
      />
      <div className="toolbar-card">
        <div className="table-toolbar activity-toolbar">
          <input value={search} onChange={(event) => updateParam("search", event.target.value)} placeholder="Search message, package, or machine" />
          <select value={category} onChange={(event) => updateParam("category", event.target.value)}>
            <option value="">All categories</option>
            <option value="Job">Jobs</option>
            <option value="Scan">Scans</option>
            <option value="Normalization">Normalization</option>
            <option value="Mount">Mounts</option>
          </select>
          <select value={severity} onChange={(event) => updateParam("severity", event.target.value)}>
            <option value="">All severities</option>
            <option value="Info">Info</option>
            <option value="Warning">Warning</option>
            <option value="Error">Error</option>
          </select>
          <select value={machineId} onChange={(event) => updateParam("machineId", event.target.value)}>
            <option value="">All machines</option>
            {machines.map((machine) => (
              <option key={machine.id} value={machine.id}>{machine.name}</option>
            ))}
          </select>
        </div>
      </div>
      {state.loading && events.length === 0 ? (
        <PageState title="Loading events" description="Building the latest system activity timeline." tone="loading" />
      ) : state.error && events.length === 0 ? (
        <PageState title="Events unavailable" description={state.error} actionLabel="Retry" onAction={() => void state.reload()} tone="error" />
      ) : events.length === 0 ? (
        <PageState title="No events" description="No matching system events were found for the current filters." />
      ) : (
        <div className="table-card">
          <table className="data-table">
            <thead>
              <tr>
                <th>When</th>
                <th>Category</th>
                <th>Severity</th>
                <th>Title</th>
                <th>Message</th>
                <th>Context</th>
              </tr>
            </thead>
            <tbody>
              {events.map((event) => (
                <tr key={event.id}>
                  <td title={new Date(event.createdAtUtc).toLocaleString()}>{relative(event.createdAtUtc)}</td>
                  <td>{event.category}</td>
                  <td><StatusPill label={event.severity} tone={severityTone(event.severity)} /></td>
                  <td>{event.actionPath ? <Link className="table-title-link" to={event.actionPath}>{event.title}</Link> : event.title}</td>
                  <td>{event.message}</td>
                  <td>{[event.packageName, event.machineName].filter(Boolean).join(" | ") || "-"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
