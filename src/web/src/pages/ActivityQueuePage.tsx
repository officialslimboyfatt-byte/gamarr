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

export function ActivityQueuePage() {
  const { machines } = useSelectedMachine();
  const [searchParams, setSearchParams] = useSearchParams();
  const machineId = searchParams.get("machineId") ?? "";
  const actionType = searchParams.get("actionType") ?? "";
  const search = searchParams.get("search") ?? "";
  const state = usePollingAsyncData(
    () =>
      api.listJobs({
        machineId: machineId || undefined,
        actionType: actionType || undefined,
        search: search || undefined,
        scope: "active"
      }),
    [machineId, actionType, search],
    10000,
    true
  );

  function updateParam(key: string, value: string) {
    const next = new URLSearchParams(searchParams);
    if (value) next.set(key, value);
    else next.delete(key);
    setSearchParams(next);
  }

  const jobs = state.data ?? [];

  return (
    <div className="page-grid">
      <PageHeader
        eyebrow="System"
        title="Queue"
        description="Watch active install, validation, uninstall, and launch work across machines."
        actions={<button type="button" className="secondary-button inline-button" onClick={() => void state.reload()}>Refresh</button>}
      />
      <div className="toolbar-card">
        <div className="table-toolbar activity-toolbar">
          <input value={search} onChange={(event) => updateParam("search", event.target.value)} placeholder="Search title or machine" />
          <select value={machineId} onChange={(event) => updateParam("machineId", event.target.value)}>
            <option value="">All machines</option>
            {machines.map((machine) => (
              <option key={machine.id} value={machine.id}>
                {machine.name}
              </option>
            ))}
          </select>
          <select value={actionType} onChange={(event) => updateParam("actionType", event.target.value)}>
            <option value="">All actions</option>
            <option value="Install">Install</option>
            <option value="Launch">Launch</option>
            <option value="Validate">Validate</option>
            <option value="Uninstall">Uninstall</option>
          </select>
        </div>
      </div>
      {state.loading ? (
        <PageState title="Loading queue" description="Fetching active jobs and current state transitions." tone="loading" />
      ) : state.error ? (
        <PageState title="Queue unavailable" description={state.error} actionLabel="Retry" onAction={() => void state.reload()} tone="error" />
      ) : jobs.length === 0 ? (
        <PageState title="Queue is empty" description="No active jobs are currently queued or running." />
      ) : (
        <div className="table-card">
          <table className="data-table">
            <thead>
              <tr>
                <th>Title</th>
                <th>Machine</th>
                <th>Action</th>
                <th>Status</th>
                <th>Age</th>
                <th>Current Stage</th>
              </tr>
            </thead>
            <tbody>
              {jobs.map((job) => (
                <tr key={job.id}>
                  <td>
                    <Link className="table-title-link" to={`/jobs/${job.id}`}>
                      {job.packageName}
                    </Link>
                  </td>
                  <td>{job.machineName}</td>
                  <td>{job.actionType}</td>
                  <td>
                    <StatusPill label={job.state} tone="warning" />
                  </td>
                  <td>{relative(job.createdAtUtc)}</td>
                  <td>{job.latestEventMessage ?? "Waiting for agent"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
