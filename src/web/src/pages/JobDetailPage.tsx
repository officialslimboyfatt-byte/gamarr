import { useState } from "react";
import { useParams } from "react-router-dom";
import { PageHeader } from "../components/PageHeader";
import { PageState } from "../components/PageState";
import { StatusPill } from "../components/StatusPill";
import { api } from "../api/client";
import { usePollingAsyncData } from "../hooks/usePollingAsyncData";

function isActiveState(state: string) {
  return ["Queued", "Assigned", "Preparing", "Mounting", "Installing", "Validating"].includes(state);
}

export function JobDetailPage() {
  const { id } = useParams();
  const state = usePollingAsyncData(() => api.getJob(id ?? ""), [id], 10000, Boolean(id));
  const [cancelling, setCancelling] = useState(false);
  const [cancelError, setCancelError] = useState<string | null>(null);

  async function onCancel() {
    if (!id) return;
    setCancelling(true);
    setCancelError(null);
    try {
      await api.cancelJob(id);
      await state.reload();
    } catch (err) {
      setCancelError(err instanceof Error ? err.message : "Unable to cancel job.");
    } finally {
      setCancelling(false);
    }
  }

  if (!id) {
    return <PageState title="Job not found" description="The requested job id is missing." tone="error" />;
  }

  if (state.loading) {
    return <PageState title="Loading activity item" description="Fetching queue timeline and structured logs." tone="loading" />;
  }

  if (state.error || !state.data) {
    return <PageState title="Activity item unavailable" description={state.error ?? "The requested job could not be loaded."} actionLabel="Retry" onAction={() => void state.reload()} tone="error" />;
  }

  const job = state.data;
  const tone = job.state === "Completed" ? "success" : job.state === "Failed" ? "danger" : "warning";
  const active = isActiveState(job.state);

  return (
    <div className="page-grid">
      <PageHeader
        eyebrow="Activity"
        title={job.packageName}
        description={`${job.actionType} on ${job.machineName}`}
        actions={
          <div className="page-actions">
            <StatusPill label={job.state} tone={tone} />
            <button type="button" className="secondary-button inline-button" onClick={() => void state.reload()}>
              Refresh
            </button>
            {active ? <span className="header-chip">Live refresh 10s</span> : null}
            {active ? (
              <button type="button" className="secondary-button inline-button" onClick={() => void onCancel()} disabled={cancelling}>
                {cancelling ? "Cancelling..." : "Cancel Job"}
              </button>
            ) : null}
          </div>
        }
      />
      {cancelError ? <div className="inline-error">{cancelError}</div> : null}
      <div className="toolbar-card sticky-summary">
        <div className="detail-grid">
          <div>
            <label>Machine</label>
            <p>{job.machineName}</p>
          </div>
          <div>
            <label>Created</label>
            <p>{new Date(job.createdAtUtc).toLocaleString()}</p>
          </div>
          <div>
            <label>Updated</label>
            <p>{new Date(job.updatedAtUtc).toLocaleString()}</p>
          </div>
          <div>
            <label>Duration</label>
            <p>{job.durationSeconds ? `${Math.round(job.durationSeconds)}s` : "-"}</p>
          </div>
          <div>
            <label>Summary</label>
            <p>{job.outcomeSummary ?? job.latestEventMessage ?? "No summary"}</p>
          </div>
        </div>
      </div>
      <div className="two-column shell-panels">
        <section className="table-card">
          <div className="table-section-header">
            <div>
              <h3>Timeline</h3>
              <p>Persisted state transitions for this activity item.</p>
            </div>
          </div>
          {job.events.length ? (
            <div className="event-list">
              {job.events.map((event) => (
                <div className="event-row" key={event.id}>
                  <div className="event-state">
                    <StatusPill label={event.state} tone={event.state === "Completed" ? "success" : event.state === "Failed" ? "danger" : "warning"} />
                  </div>
                  <div className="event-copy">
                    <strong>{event.message}</strong>
                    <span>{new Date(event.createdAtUtc).toLocaleString()}</span>
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <PageState title="No lifecycle events" description="This job has not emitted any persisted transitions yet." />
          )}
        </section>
        <section className="table-card">
          <div className="table-section-header">
            <div>
              <h3>Logs</h3>
              <p>Structured output reported by the agent during execution.</p>
            </div>
          </div>
          {job.logs.length ? (
            <div className="event-list">
              {job.logs.map((log) => (
                <div className="event-row" key={log.id}>
                  <div className="event-state">
                    <StatusPill label={log.level} tone={log.level === "Error" ? "danger" : log.level === "Warning" ? "warning" : "neutral"} />
                  </div>
                  <div className="event-copy">
                    <strong>{log.source}</strong>
                    <p>{log.message}</p>
                    {log.payloadJson ? <pre className="payload-viewer">{log.payloadJson}</pre> : null}
                    <span>{new Date(log.createdAtUtc).toLocaleString()}</span>
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <PageState title="No logs yet" description="Execution logs will appear here when the agent reports them." />
          )}
        </section>
      </div>
    </div>
  );
}
