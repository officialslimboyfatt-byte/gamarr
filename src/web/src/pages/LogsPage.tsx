import { useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api } from "../api/client";
import { PageHeader } from "../components/PageHeader";
import { PageState } from "../components/PageState";
import { StatusPill } from "../components/StatusPill";
import { useSelectedMachine } from "../context/SelectedMachineContext";
import { useAsyncData } from "../hooks/useAsyncData";
import { usePollingAsyncData } from "../hooks/usePollingAsyncData";

function formatBytes(value: number) {
  if (value < 1024) return `${value} B`;
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`;
  return `${(value / (1024 * 1024)).toFixed(1)} MB`;
}

function levelTone(level: string) {
  switch (level) {
    case "Error":
      return "danger";
    case "Warning":
      return "warning";
    case "Information":
      return "success";
    default:
      return "neutral";
  }
}

export function LogsPage() {
  const { machines } = useSelectedMachine();
  const [searchParams, setSearchParams] = useSearchParams();
  const [selectedFileId, setSelectedFileId] = useState("");
  const machineId = searchParams.get("machineId") ?? "";
  const level = searchParams.get("level") ?? "";
  const source = searchParams.get("source") ?? "";
  const search = searchParams.get("search") ?? "";

  const logsState = usePollingAsyncData(
    () => api.listSystemLogs({ machineId: machineId || undefined, level: level || undefined, source: source || undefined, search: search || undefined, limit: 150 }),
    [machineId, level, source, search],
    10000,
    true
  );
  const filesState = usePollingAsyncData(() => api.listSystemLogFiles(), [], 10000, true);
  const fileState = useAsyncData(
    () => selectedFileId ? api.getSystemLogFile(selectedFileId, 220) : Promise.resolve(null),
    [selectedFileId]
  );

  useEffect(() => {
    if (!selectedFileId && filesState.data?.[0]?.id) {
      setSelectedFileId(filesState.data[0].id);
    }
  }, [filesState.data, selectedFileId]);

  function updateParam(key: string, value: string) {
    const next = new URLSearchParams(searchParams);
    if (value) next.set(key, value);
    else next.delete(key);
    setSearchParams(next);
  }

  const logs = logsState.data ?? [];
  const files = filesState.data ?? [];
  const sources = useMemo(() => Array.from(new Set(logs.map((log) => log.source))).sort(), [logs]);

  return (
    <div className="page-grid">
      <PageHeader
        eyebrow="System"
        title="Logs"
        description="Inspect structured job logs alongside live runtime log files without leaving the UI."
        actions={<button type="button" className="secondary-button inline-button" onClick={() => { void logsState.reload(); void filesState.reload(); }}>Refresh</button>}
      />
      <div className="toolbar-card">
        <div className="table-toolbar activity-toolbar">
          <input value={search} onChange={(event) => updateParam("search", event.target.value)} placeholder="Search log message or payload" />
          <select value={machineId} onChange={(event) => updateParam("machineId", event.target.value)}>
            <option value="">All machines</option>
            {machines.map((machine) => (
              <option key={machine.id} value={machine.id}>{machine.name}</option>
            ))}
          </select>
          <select value={level} onChange={(event) => updateParam("level", event.target.value)}>
            <option value="">All levels</option>
            <option value="Trace">Trace</option>
            <option value="Information">Information</option>
            <option value="Warning">Warning</option>
            <option value="Error">Error</option>
          </select>
          <select value={source} onChange={(event) => updateParam("source", event.target.value)}>
            <option value="">All sources</option>
            {sources.map((item) => (
              <option key={item} value={item}>{item}</option>
            ))}
          </select>
        </div>
      </div>
      <div className="system-two-column">
        <div className="table-card">
          <div className="table-section-header">
            <div>
              <h3>Structured Logs</h3>
              <p>Persisted logs tied to package jobs and agent activity.</p>
            </div>
          </div>
          {logsState.loading && logs.length === 0 ? (
            <PageState title="Loading logs" description="Fetching structured operational logs." tone="loading" />
          ) : logsState.error && logs.length === 0 ? (
            <PageState title="Structured logs unavailable" description={logsState.error} actionLabel="Retry" onAction={() => void logsState.reload()} tone="error" />
          ) : logs.length === 0 ? (
            <PageState title="No structured logs" description="No log entries match the current filters." />
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th>When</th>
                  <th>Level</th>
                  <th>Source</th>
                  <th>Message</th>
                  <th>Context</th>
                </tr>
              </thead>
              <tbody>
                {logs.map((log) => (
                  <tr key={log.id}>
                    <td>{new Date(log.createdAtUtc).toLocaleString()}</td>
                    <td><StatusPill label={log.level} tone={levelTone(log.level)} /></td>
                    <td>{log.source}</td>
                    <td className="log-message-cell">
                      <div>{log.message}</div>
                      {log.payloadJson ? <pre className="inline-code-block">{log.payloadJson}</pre> : null}
                    </td>
                    <td>{log.actionPath ? <Link className="table-title-link" to={log.actionPath}>{[log.packageName, log.machineName].filter(Boolean).join(" | ") || "Job"}</Link> : ([log.packageName, log.machineName].filter(Boolean).join(" | ") || "-")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
        <div className="table-card">
          <div className="table-section-header">
            <div>
              <h3>Runtime Files</h3>
              <p>Tail the approved runtime log files produced by the local stack.</p>
            </div>
          </div>
          {filesState.loading && files.length === 0 ? (
            <PageState title="Loading log files" description="Discovering runtime log files." tone="loading" />
          ) : filesState.error && files.length === 0 ? (
            <PageState title="Runtime logs unavailable" description={filesState.error} actionLabel="Retry" onAction={() => void filesState.reload()} tone="error" />
          ) : (
            <div className="log-file-layout">
              <div className="log-file-list">
                {files.map((file) => (
                  <button
                    key={file.id}
                    type="button"
                    className={selectedFileId === file.id ? "log-file-button active" : "log-file-button"}
                    onClick={() => setSelectedFileId(file.id)}
                  >
                    <strong>{file.name}</strong>
                    <span>{formatBytes(file.sizeBytes)} | {new Date(file.updatedAtUtc).toLocaleString()}</span>
                  </button>
                ))}
              </div>
              <div className="log-file-preview">
                {fileState.loading && !fileState.data ? (
                  <PageState title="Loading file" description="Reading the current log tail." tone="loading" />
                ) : fileState.error && !fileState.data ? (
                  <PageState title="Log file unavailable" description={fileState.error} actionLabel="Retry" onAction={() => void fileState.reload()} tone="error" />
                ) : fileState.data ? (
                  <>
                    <div className="log-file-meta">
                      <strong>{fileState.data.displayName}</strong>
                      <span>{formatBytes(fileState.data.sizeBytes)} | {new Date(fileState.data.updatedAtUtc).toLocaleString()}</span>
                    </div>
                    <pre className="log-file-content">{fileState.data.content || "File is empty."}</pre>
                    {fileState.data.truncated ? <div className="sidebar-note">Showing the most recent tail of this file.</div> : null}
                  </>
                ) : (
                  <PageState title="No file selected" description="Choose a runtime log file to preview it here." />
                )}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
