import type { JobRecord } from "../types/api";

const STATE_PROGRESS: Record<string, number> = {
  Queued: 10,
  Assigned: 20,
  Preparing: 35,
  Mounting: 50,
  Installing: 70,
  Validating: 85,
  Completed: 100,
  Failed: 100,
  Cancelled: 100
};

const STATE_LABEL: Record<string, string> = {
  Queued: "Queued…",
  Assigned: "Assigned to agent…",
  Preparing: "Preparing…",
  Mounting: "Mounting ISO…",
  Installing: "Installing…",
  Validating: "Validating…",
  Completed: "Installed",
  Failed: "Failed",
  Cancelled: "Cancelled"
};

interface Props {
  job: JobRecord;
}

export function InstallProgress({ job }: Props) {
  const pct = STATE_PROGRESS[job.state] ?? 0;
  const label = STATE_LABEL[job.state] ?? job.state;
  const isError = job.state === "Failed";
  const isSuccess = job.state === "Completed";

  const detail = job.outcomeSummary ?? job.latestEventMessage ?? "";
  const showLog = (job.state === "Installing" || job.state === "Validating") && detail;

  return (
    <div className="install-progress">
      <div className="install-progress-header">
        <strong>{label}</strong>
        {!showLog && <span>{detail}</span>}
      </div>
      <div className="install-progress-bar">
        <div
          className={`install-progress-fill${isError ? " error" : isSuccess ? " success" : ""}`}
          style={{ width: `${pct}%` }}
        />
      </div>
      {showLog ? <div className="install-progress-log">{detail}</div> : null}
    </div>
  );
}
