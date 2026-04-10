import { Link } from "react-router-dom";
import { api } from "../api/client";
import { Card } from "../components/Card";
import { PageHeader } from "../components/PageHeader";
import { PageState } from "../components/PageState";
import { useAsyncData } from "../hooks/useAsyncData";

export function AddMachinePage() {
  const state = useAsyncData(() => api.getSettings(), []);

  if (state.loading || !state.data) {
    return <PageState title="Loading machine onboarding" description="Fetching current server and agent connection settings." tone="loading" />;
  }

  if (state.error) {
    return <PageState title="Machine onboarding unavailable" description={state.error} actionLabel="Retry" onAction={() => void state.reload()} tone="error" />;
  }

  const { network } = state.data;
  const agentConfig = `{
  "Gamarr": {
    "ServerBaseUrl": "${network.agentServerUrl}",
    "HeartbeatIntervalSeconds": 15,
    "PollIntervalSeconds": 10,
    "RunAsConsole": false
  }
}`;

  return (
    <div className="page-grid">
      <PageHeader
        eyebrow="Machines"
        title="Add Machine"
        description="Machines register themselves when the Gamarr Agent connects to this server. Use this page as the operator handoff for remote PCs."
      />
      <div className="settings-grid">
        <section className="settings-panel">
          <div className="table-section-header">
            <div>
              <h3>Server Connection</h3>
              <p>This is the connection target remote agents should use.</p>
            </div>
          </div>
          <div className="settings-note">
            <strong>Agent Server URL</strong>
            <p>{network.agentServerUrl}</p>
            <strong>Public Server URL</strong>
            <p>{network.publicServerUrl}</p>
            <strong>Current status</strong>
            <p>{network.summary}</p>
          </div>
        </section>

        <section className="settings-panel">
          <div className="table-section-header">
            <div>
              <h3>Remote PC Workflow</h3>
              <p>For the current product shape, the remote machine installs only the agent and connects back to this server.</p>
            </div>
          </div>
          <ol className="operator-list">
            <li>Install the Gamarr Agent on the target Windows PC.</li>
            <li>Set its `ServerBaseUrl` to this server.</li>
            <li>Run it as a Windows service or launch it manually during testing.</li>
            <li>Open <Link to="/machines">Machines</Link> and wait for the new registration to appear.</li>
          </ol>
          <div className="settings-note">
            <strong>Agent appsettings.json</strong>
            <pre className="inline-code-block">{agentConfig}</pre>
          </div>
        </section>

        <Card title="What Adds a Machine?" subtitle="No manual DB record creation">
          <p className="panel-copy">
            A machine is added when the agent registers with the API. The UI does not create empty machine records because the agent is the source of truth for hostname, capabilities, and heartbeat state.
          </p>
        </Card>
      </div>
    </div>
  );
}
