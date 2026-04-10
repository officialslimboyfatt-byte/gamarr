import { api } from "../api/client";
import { Card } from "../components/Card";
import { PageHeader } from "../components/PageHeader";
import { PageState } from "../components/PageState";
import { StatusPill } from "../components/StatusPill";
import { useAsyncData } from "../hooks/useAsyncData";
import type { JobRecord, MachineRecord, PackageRecord } from "../types/api";

export function DashboardPage() {
  const { data, loading, error, reload } = useAsyncData(
    () => Promise.all([api.listPackages(), api.listMachines(), api.listJobs()]),
    []
  );
  const [packages, machines, jobs] = data ?? ([[], [], []] as [PackageRecord[], MachineRecord[], JobRecord[]]);
  const onlineMachines = machines.filter((machine) => machine.status === "Online").length;
  const activeJobs = jobs.filter((job) => !["Completed", "Failed", "Cancelled"].includes(job.state)).length;

  if (loading) {
    return <PageState title="Loading dashboard" description="Fetching packages, machines, and recent jobs." tone="loading" />;
  }

  if (error) {
    return <PageState title="Dashboard unavailable" description={error} actionLabel="Retry" onAction={() => void reload()} tone="error" />;
  }

  return (
    <div className="page-grid">
      <PageHeader
        eyebrow="Overview"
        title="Control Plane"
        description="Monitor the package catalog, machine availability, and active deployment queue."
      />
      <div className="stats-grid">
        <Card title="Packages" subtitle="Registered deployable titles">
          <strong className="metric">{packages.length}</strong>
        </Card>
        <Card title="Online Machines" subtitle="Available managed hosts">
          <strong className="metric">{onlineMachines}</strong>
        </Card>
        <Card title="Active Jobs" subtitle="Queued or in-flight work">
          <strong className="metric">{activeJobs}</strong>
        </Card>
      </div>
      <Card title="Recent Jobs" subtitle="Latest persisted job state and outcome">
        {jobs.length === 0 ? (
          <PageState title="No jobs yet" description="Queue an install from the Jobs view to populate deployment history." />
        ) : (
          <div className="list">
            {jobs.slice(0, 6).map((job) => (
              <div className="list-row" key={job.id}>
                <div>
                  <strong>{job.packageName}</strong>
                  <p>{job.machineName}</p>
                </div>
                <StatusPill
                  label={job.state}
                  tone={job.state === "Completed" ? "success" : job.state === "Failed" ? "danger" : "warning"}
                />
              </div>
            ))}
          </div>
        )}
      </Card>
    </div>
  );
}
