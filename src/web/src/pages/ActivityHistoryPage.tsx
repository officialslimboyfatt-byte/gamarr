import { Link, useSearchParams } from "react-router-dom";
import { api } from "../api/client";
import { PageHeader } from "../components/PageHeader";
import { PageState } from "../components/PageState";
import { StatusPill } from "../components/StatusPill";
import { useSelectedMachine } from "../context/SelectedMachineContext";
import { usePollingAsyncData } from "../hooks/usePollingAsyncData";

export function ActivityHistoryPage() {
  const { machines } = useSelectedMachine();
  const [searchParams, setSearchParams] = useSearchParams();
  const machineId = searchParams.get("machineId") ?? "";
  const actionType = searchParams.get("actionType") ?? "";
  const stateFilter = searchParams.get("state") ?? "";
  const search = searchParams.get("search") ?? "";
  const state = usePollingAsyncData(
    () =>
      api.listJobs({
        machineId: machineId || undefined,
        actionType: actionType || undefined,
        state: stateFilter || undefined,
        search: search || undefined,
        scope: "history"
      }),
    [machineId, actionType, stateFilter, search],
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
        title="History"
        description="Review completed and failed work when you need deeper job-by-job history beyond the event feed."
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
          </select>
          <select value={stateFilter} onChange={(event) => updateParam("state", event.target.value)}>
            <option value="">All results</option>
            <option value="Completed">Completed</option>
            <option value="Failed">Failed</option>
            <option value="Cancelled">Cancelled</option>
          </select>
        </div>
      </div>
      {state.loading ? (
        <PageState title="Loading history" description="Fetching completed and failed work." tone="loading" />
      ) : state.error ? (
        <PageState title="History unavailable" description={state.error} actionLabel="Retry" onAction={() => void state.reload()} tone="error" />
      ) : jobs.length === 0 ? (
        <PageState title="No history yet" description="Completed and failed activity will appear here." />
      ) : (
        <div className="table-card">
          <table className="data-table">
            <thead>
              <tr>
                <th>Title</th>
                <th>Machine</th>
                <th>Action</th>
                <th>Result</th>
                <th>Completed</th>
                <th>Duration</th>
                <th>Summary</th>
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
                    <StatusPill label={job.state} tone={job.state === "Completed" ? "success" : job.state === "Failed" ? "danger" : "neutral"} />
                  </td>
                  <td>{job.completedAtUtc ? new Date(job.completedAtUtc).toLocaleString() : "Not completed"}</td>
                  <td>{job.durationSeconds ? `${Math.round(job.durationSeconds)}s` : "-"}</td>
                  <td>{job.outcomeSummary ?? job.latestEventMessage ?? "No summary"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
