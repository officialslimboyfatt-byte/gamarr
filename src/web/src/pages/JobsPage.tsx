import { FormEvent, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api/client";
import { Card } from "../components/Card";
import { PageHeader } from "../components/PageHeader";
import { PageState } from "../components/PageState";
import { QuickInstallModal } from "../components/QuickInstallModal";
import { StatusPill } from "../components/StatusPill";
import { usePollingAsyncData } from "../hooks/usePollingAsyncData";
import type { JobRecord, MachineRecord, PackageRecord } from "../types/api";

export function JobsPage() {
  const { data, loading, error, reload, setData } = usePollingAsyncData(
    () => Promise.all([api.listJobs(), api.listPackages(), api.listMachines()]),
    [],
    10000,
    true
  );
  const [packageId, setPackageId] = useState("");
  const [machineId, setMachineId] = useState("");
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [showQuickInstall, setShowQuickInstall] = useState(false);
  const [jobs, packages, machines] = data ?? ([[], [], []] as [JobRecord[], PackageRecord[], MachineRecord[]]);

  useEffect(() => {
    if (packages[0]?.id && !packageId) {
      setPackageId(packages[0].id);
    }

    if (machines[0]?.id && !machineId) {
      setMachineId(machines[0].id);
    }
  }, [packages, machines, packageId, machineId]);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setSubmitError(null);

    try {
      const created = await api.createJob({
        packageId,
        machineId,
        actionType: "Install",
        requestedBy: "web-ui"
      });

      setData((current) => {
        const existing = current ?? ([[], [], []] as [JobRecord[], PackageRecord[], MachineRecord[]]);
        return [[created, ...existing[0]], existing[1], existing[2]];
      });
    } catch (err) {
      setSubmitError(err instanceof Error ? err.message : "Failed to create job.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="page-grid two-column">
      <PageHeader
        eyebrow="Queue"
        title="Jobs"
        description="Assign package installs to managed machines and track progress over time."
        actions={
          <div className="page-actions">
            <button
              type="button"
              className="secondary-button inline-button"
              onClick={() => setShowQuickInstall(true)}
              disabled={machines.length === 0}
            >
              Quick Install
            </button>
          </div>
        }
      />
      {showQuickInstall ? (
        <QuickInstallModal
          machines={machines}
          onClose={() => { setShowQuickInstall(false); void reload(); }}
        />
      ) : null}
      <Card title="Job Queue" subtitle="Persisted package deployment jobs">
        {loading ? (
          <PageState title="Loading jobs" description="Fetching queue state, packages, and machines." tone="loading" />
        ) : error ? (
          <PageState title="Jobs unavailable" description={error} actionLabel="Retry" onAction={() => void reload()} tone="error" />
        ) : jobs.length === 0 ? (
          <PageState title="Queue is empty" description="No deployment jobs are currently queued or completed." />
        ) : (
          <div className="list">
            {jobs.map((job) => (
              <Link className="list-row link-row" key={job.id} to={`/jobs/${job.id}`}>
                <div>
                  <strong>{job.packageName}</strong>
                  <p>{job.machineName}</p>
                </div>
                <StatusPill
                  label={job.state}
                  tone={job.state === "Completed" ? "success" : job.state === "Failed" ? "danger" : "warning"}
                />
              </Link>
            ))}
          </div>
        )}
      </Card>
      <Card title="Create Install Job" subtitle="Queue an install for a registered machine">
        <form className="form" onSubmit={onSubmit}>
          <select value={packageId} onChange={(e) => setPackageId(e.target.value)}>
            {packages.map((item) => (
              <option key={item.id} value={item.id}>
                {item.name}
              </option>
            ))}
          </select>
          <select value={machineId} onChange={(e) => setMachineId(e.target.value)}>
            {machines.map((item) => (
              <option key={item.id} value={item.id}>
                {item.name}
              </option>
            ))}
          </select>
          {packages.length === 0 || machines.length === 0 ? (
            <PageState
              title="Creation blocked"
              description="At least one package and one machine must exist before a job can be queued."
            />
          ) : null}
          {submitError ? <div className="inline-error">{submitError}</div> : null}
          <button
            type="submit"
            disabled={!packageId || !machineId || submitting || packages.length === 0 || machines.length === 0}
          >
            {submitting ? "Queueing..." : "Queue Install Job"}
          </button>
        </form>
      </Card>
    </div>
  );
}
