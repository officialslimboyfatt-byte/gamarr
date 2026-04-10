# Gamarr

Gamarr is a self-hosted, local-first game library and deployment platform for real Windows PCs. It is built for importing, registering, packaging, versioning, and deploying user-supplied media only: local ISOs, installer folders, setup executables, patches, and metadata you provide.

## Product Boundaries

Gamarr does not include:

- torrenting
- release search
- indexer integration
- download client integration
- magnet handling
- piracy-source scraping
- automated content acquisition

Gamarr only manages content the user already has.

## Repository Layout

- `src/server/Gamarr.Api`: ASP.NET Core API entry point and controllers
- `src/server/Gamarr.Application`: DTOs, state rules, and application exceptions
- `src/server/Gamarr.Domain`: core entities and enums
- `src/server/Gamarr.Infrastructure`: EF Core persistence, services, RabbitMQ publisher, seed path
- `src/web`: React + TypeScript web UI
- `src/agent/Gamarr.Agent`: .NET worker / Windows service agent
- `deploy/docker-compose.yml`: PostgreSQL and RabbitMQ
- `docs/architecture.md`: system design, limitations, screenshots placeholders, roadmap
- `docs/examples`: sample package manifest and safe mock recipe files

## Package Definition Slice

The current vertical slice introduces a persisted package manifest artifact for each package version.

Each package version now stores:

- normalized relational fields for querying and UI rendering
- a canonical manifest artifact (`ManifestJson`)
- script execution metadata (`InstallScriptKind`, install script path)
- media definitions
- detection rules
- prerequisites

Current manifest format:

- `gamarr.package/v1`

Example files:

- [package-manifest.example.json](/E:/DEV/Spool/docs/examples/package-manifest.example.json)
- [install.mock](/E:/DEV/Spool/docs/examples/install.mock)

## Safe Mock Recipe Execution

The agent now supports a constrained mock execution mode for package versions whose install script kind is `MockRecipe`.

This is intentionally not arbitrary script execution.

Supported mock recipe instructions:

- `LOG <message>`
- `WAIT <seconds>`
- `WRITE_FILE <relativePath>|<content>`

Safety rules:

- only `MockRecipe` is executed by the agent in this slice
- `WRITE_FILE` only supports relative paths
- files are written under the agent-controlled mock install root in ProgramData
- detection rules can reference `%MOCK_INSTALL_ROOT%`

This is the bridge to the next pass, where real PowerShell execution and media handling will be added behind the same execution seam.

## Prerequisites

Install these before trying to run the stack:

- .NET 8 SDK
- Node.js 24+
- npm 11+
- Docker Desktop or Docker Engine with Compose

Optional but expected for normal development:

- Git
- a local PostgreSQL client or GUI
- a RabbitMQ management UI login via the bundled container

## Runtime Shape

Gamarr is now intended to run as:

- one Windows server host that serves both the API and the web UI on `:5000`
- one or more Windows agents that register themselves back to that server

For normal use there is no separate operator web port. `5173` remains development-only.

## Local Ports

- UI + API: `http://localhost:5000`
- Swagger: `http://localhost:5000/swagger`
- PostgreSQL: `localhost:5432`
- RabbitMQ AMQP: `localhost:5672`
- RabbitMQ management: `http://localhost:15672`

Default local credentials:

- PostgreSQL: `gamarr / gamarr`
- RabbitMQ: `gamarr / gamarr`

## Environment and Config

The repo includes `.env.example` with the main defaults used during local development.

Important runtime settings:

- API database connection:
  - `GAMARR_POSTGRES_CONNECTION`
- RabbitMQ connection:
  - `GAMARR_RABBITMQ_HOST`
  - `GAMARR_RABBITMQ_PORT`
  - `GAMARR_RABBITMQ_USERNAME`
  - `GAMARR_RABBITMQ_PASSWORD`
- Demo seeding:
  - `GAMARR_SEED_DEMO_DATA=true`

Current config files:

- API: [appsettings.json](/E:/DEV/Spool/src/server/Gamarr.Api/appsettings.json)
- Agent: [appsettings.json](/E:/DEV/Spool/src/agent/Gamarr.Agent/appsettings.json)
- Web dev proxy: [vite.config.ts](/E:/DEV/Spool/src/web/vite.config.ts)
- Server publish/install scripts: [scripts](/E:/DEV/Spool/scripts)

Network-oriented environment variables:

- `ASPNETCORE_URLS`
- `GAMARR_PUBLIC_SERVER_URL`
- `GAMARR_AGENT_SERVER_URL`
- `GAMARR_WEB_HOST`

Server runtime settings can also be stored in the API config under `GamarrServer`:

- `RunAsConsole`
- `ListenUrls`
- `PublicServerUrl`
- `AgentServerUrl`

## First-Time Local Setup

1. Start infrastructure:

```powershell
cd E:\DEV\Spool\deploy
docker compose up -d
```

2. Verify containers:

```powershell
docker compose ps
```

3. Start the local server + local agent:

```powershell
cd E:\DEV\Spool
.\scripts\start-local.ps1
```

Expected behavior:

- the script starts PostgreSQL/RabbitMQ if needed
- the script builds the web UI into `src/web/dist`
- the API applies the existing migrations on startup
- demo package and demo machine are seeded when enabled
- `http://localhost:5000/health/live` returns `200`
- `http://localhost:5000/` serves the embedded web UI

4. Prepare a local mock recipe file on the machine running the agent if you want to exercise the mock execution path.

Example:

```powershell
New-Item -ItemType Directory -Force -Path C:\GamarrMedia\HalfLifeDemo | Out-Null
@'
LOG Preparing install workspace
WAIT 1
WRITE_FILE installed\hl.exe|mock game executable
LOG Mock payload written
'@ | Set-Content C:\GamarrMedia\HalfLifeDemo\install.mock
```

5. If you are testing a second machine, run the agent there and point it at the server:

```powershell
cd E:\DEV\Spool\src\agent\Gamarr.Agent
$env:Gamarr__ServerBaseUrl = 'http://YOUR-SERVER-HOSTNAME:5000'
dotnet run
```

Expected behavior:

- the agent registers or re-registers using its stable key
- the agent sends periodic heartbeats
- the machine appears in the Machines view

## Manual MVP Verification Flow

1. Open the web UI at `http://localhost:5000`.
2. Confirm the seeded package appears in Packages.
3. Open Package Detail and confirm:
   - version metadata
   - media definitions
   - script kind/path
   - detection rules
   - persisted manifest
4. Confirm a machine exists:
   - either the seeded demo machine
   - or the real machine registered by the running agent
5. Open Jobs and create an install job.
6. Watch the job move through:
   - `Queued`
   - `Assigned`
   - `Preparing`
   - `Mounting`
   - `Installing`
   - `Validating`
   - `Completed`
7. Open Job Detail and confirm:
   - lifecycle timeline is present
   - structured action logs are present
   - detection validation logs are present

## Seed Data

When `Seed:DemoDataEnabled` is true, the API seeds:

- one manifest-backed sample package
- one sample machine for UI testing

The seeded package is intentionally sample-only. Replace those paths with real local media and a real local mock recipe file when testing the execution slice.

The seeded sample detection rule uses:

- `%MOCK_INSTALL_ROOT%\installed\hl.exe`

That path is expanded by the agent into its controlled mock install root at runtime.

## Troubleshooting

### API fails on startup

Check:

- PostgreSQL is running on `localhost:5432`
- the connection string matches your local credentials
- the port is not already in use

### Web UI shows request failures

Check:

- the API is running on `http://localhost:5000`
- the web bundle exists under `src/web/dist` for local console runs or `wwwroot` for published runs
- browser dev tools show the exact problem-details response from the API

### Agent never appears in Machines

Check:

- the API is running before the agent starts
- the agent `ServerBaseUrl` matches the API
- firewall rules allow local loopback traffic
- the agent state file under ProgramData is writable

### Making Gamarr reachable on your LAN

Gamarr is intended to evolve toward:

- one Windows server running API/web/infrastructure
- many Windows PCs running the agent only

For that model:

1. Start the main server with LAN-friendly bind settings:
   - `ASPNETCORE_URLS=http://0.0.0.0:5000`
   - `GAMARR_PUBLIC_SERVER_URL=http://YOUR-SERVER-HOSTNAME:5000`
   - `GAMARR_AGENT_SERVER_URL=http://YOUR-SERVER-HOSTNAME:5000`
2. Open firewall access for port `5000`.
3. On each remote Windows PC, install the agent and point:
   - `Gamarr:ServerBaseUrl=http://YOUR-SERVER-HOSTNAME:5000`
4. The machine will appear automatically in the Machines view after the agent registers.

### Service-oriented scripts

Useful scripts in [`scripts`](/E:/DEV/Spool/scripts):

- `start-local.ps1`: local dev/runtime helper that serves the UI from the API on `http://localhost:5000`
- `stop-local.ps1`: stop local server/agent helper processes
- `publish-server.ps1`: publish the API and copy built web assets into `wwwroot`
- `install-server-service.ps1`: install the published API as `Gamarr Server`
- `publish-agent.ps1`: publish the agent
- `install-agent-service.ps1`: install the published agent as `Gamarr Agent`

### Windows Service Deployment

For a product-like local or LAN deployment:

1. Publish/install the server:

```powershell
cd E:\DEV\Spool
.\scripts\install-server-service.ps1 -PublicServerUrl 'http://YOUR-SERVER-HOSTNAME:5000' -AgentServerUrl 'http://YOUR-SERVER-HOSTNAME:5000'
```

2. On each managed PC, install the agent service:

```powershell
cd E:\DEV\Spool
.\scripts\install-agent-service.ps1 -ServerBaseUrl 'http://YOUR-SERVER-HOSTNAME:5000'
```

3. Browse to:

- `http://YOUR-SERVER-HOSTNAME:5000/`

This is the intended reliable runtime model. The API hosts the UI, and both server and agent can be configured as Windows Services with service recovery.

### Job fails during install

Current mock install failures commonly mean:

- the install script kind is not `MockRecipe`
- the recipe file does not exist at the declared path
- the recipe contains an unsupported instruction
- the detection rule does not match the files created during the mock install

## Current Status

What is implemented:

- package create/list/detail
- package version metadata and persisted manifest artifacts
- media definitions, script references, detection rules, and prerequisites
- machine register/list/detail/heartbeat
- job create/list/detail/claim/event reporting
- agent polling and safe local mock recipe execution
- persisted job events and structured logs
- arr-style web shell with package manifest detail rendering

What is still stubbed:

- real ISO mounting
- real PowerShell package execution
- richer detection rule types
- richer install log streaming and live updates
- auth
- package editing / manifest import route

## Current Runtime Direction

Gamarr is being shaped toward a Windows-installed product:

- `Gamarr Server` Windows Service on the main host
- `Gamarr Agent` Windows Service on managed PCs
- web UI served directly by the server on `:5000`

Docker-backed PostgreSQL/RabbitMQ are still acceptable infrastructure dependencies for now, but Docker is not the intended end-user application shape.
