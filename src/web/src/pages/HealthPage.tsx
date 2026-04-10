import { Link } from "react-router-dom";
import { api } from "../api/client";
import { PageHeader } from "../components/PageHeader";
import { PageState } from "../components/PageState";
import { StatusPill } from "../components/StatusPill";
import { usePollingAsyncData } from "../hooks/usePollingAsyncData";

function toneFromStatus(status: string) {
  switch (status) {
    case "Healthy":
      return "success";
    case "Error":
      return "danger";
    default:
      return "warning";
  }
}

export function HealthPage() {
  const state = usePollingAsyncData(() => api.getSystemHealth(), [], 10000, true);
  const health = state.data;

  return (
    <div className="page-grid">
      <PageHeader
        eyebrow="System"
        title="Health"
        description="Track machine availability, queue pressure, mounting issues, and core configuration problems from one place."
        actions={<button type="button" className="secondary-button inline-button" onClick={() => void state.reload()}>Refresh</button>}
      />
      {state.loading && !health ? (
        <PageState title="Loading health" description="Aggregating system checks and current operational status." tone="loading" />
      ) : state.error && !health ? (
        <PageState title="Health unavailable" description={state.error} actionLabel="Retry" onAction={() => void state.reload()} tone="error" />
      ) : health ? (
        <>
          <div className="stats-grid">
            <div className="metric-card">
              <span className="metric-label">System</span>
              <strong>{health.overallStatus}</strong>
              <StatusPill label={health.overallStatus} tone={toneFromStatus(health.overallStatus)} />
            </div>
            <div className="metric-card">
              <span className="metric-label">Machines Online</span>
              <strong>{health.metrics.onlineMachines}/{health.metrics.totalMachines}</strong>
              <span className="metric-copy">Current live agents</span>
            </div>
            <div className="metric-card">
              <span className="metric-label">Active Jobs</span>
              <strong>{health.metrics.activeJobs}</strong>
              <span className="metric-copy">Work in the queue right now</span>
            </div>
            <div className="metric-card">
              <span className="metric-label">Recent Failures</span>
              <strong>{health.metrics.failedJobsLast24Hours}</strong>
              <span className="metric-copy">Failed jobs in the last 24 hours</span>
            </div>
          </div>
          <div className="table-card">
            <div className="table-section-header">
              <div>
                <h3>Checks</h3>
                <p>{health.summary}</p>
              </div>
            </div>
            <table className="data-table">
              <thead>
                <tr>
                  <th>Check</th>
                  <th>Status</th>
                  <th>Summary</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                {health.checks.map((check) => (
                  <tr key={check.key}>
                    <td>{check.name}</td>
                    <td><StatusPill label={check.status} tone={toneFromStatus(check.status)} /></td>
                    <td>{check.summary}</td>
                    <td>
                      {check.actionPath ? (
                        <Link className="secondary-button inline-button" to={check.actionPath}>
                          Open
                        </Link>
                      ) : (
                        "-"
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      ) : null}
    </div>
  );
}
