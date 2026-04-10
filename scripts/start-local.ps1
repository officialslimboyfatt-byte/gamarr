$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $repoRoot ".runtime-tests"
$machineName = [System.Net.Dns]::GetHostName()
if ([string]::IsNullOrWhiteSpace($machineName)) {
    $machineName = $env:COMPUTERNAME
}
New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null

$env:DOTNET_CLI_HOME = Join-Path $repoRoot ".dotnet"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = "0"
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = "false"
$env:Database__ApplyMigrationsOnStartup = "true"
if ([string]::IsNullOrWhiteSpace($env:ASPNETCORE_URLS)) {
    $env:ASPNETCORE_URLS = "http://0.0.0.0:5000"
}
if ([string]::IsNullOrWhiteSpace($env:GAMARR_PUBLIC_SERVER_URL)) {
    $env:GAMARR_PUBLIC_SERVER_URL = "http://$machineName:5000"
}
if ([string]::IsNullOrWhiteSpace($env:GAMARR_AGENT_SERVER_URL)) {
    $env:GAMARR_AGENT_SERVER_URL = $env:GAMARR_PUBLIC_SERVER_URL
}

# ---------------------------------------------------------------------------
# Step 1: Kill stale processes
# ---------------------------------------------------------------------------
Write-Host "[1/7] Stopping any stale Gamarr processes..." -ForegroundColor Cyan

Get-CimInstance Win32_Process |
  Where-Object {
    ($_.Name -eq "dotnet.exe" -and $_.CommandLine -like "*Gamarr*") -or
    $_.Name -match "^Gamarr\." -or
    (($_.Name -match "node|npm|cmd|python") -and ($_.CommandLine -like "*vite*" -or $_.CommandLine -like "*http.server 5173*"))
  } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

Start-Sleep -Seconds 1

# ---------------------------------------------------------------------------
# Step 2: Start Docker Compose and wait for Postgres
# ---------------------------------------------------------------------------
Write-Host "[2/7] Starting Docker infrastructure..." -ForegroundColor Cyan

docker compose -f "$repoRoot\deploy\docker-compose.yml" up -d

Write-Host "      Waiting for Postgres on port 5432..." -ForegroundColor Gray
$timeout = 30
$elapsed = 0
$ready = $false
while ($elapsed -lt $timeout) {
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $tcp.Connect("localhost", 5432)
        $tcp.Close()
        $ready = $true
        break
    } catch {
        Start-Sleep -Seconds 1
        $elapsed++
    }
}
if (-not $ready) {
    Write-Host "ERROR: Postgres did not become available within $timeout seconds." -ForegroundColor Red
    exit 1
}
Write-Host "      Postgres ready." -ForegroundColor Green

# ---------------------------------------------------------------------------
# Step 3: Build .NET solution
# ---------------------------------------------------------------------------
Write-Host "[3/7] Building .NET solution..." -ForegroundColor Cyan

dotnet build "$repoRoot\Gamarr.sln" -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet build failed." -ForegroundColor Red
    exit 1
}
Write-Host "      Build succeeded." -ForegroundColor Green

# ---------------------------------------------------------------------------
# Step 4: npm install/build web bundle
# ---------------------------------------------------------------------------
Write-Host "[4/6] Preparing embedded web UI..." -ForegroundColor Cyan

$webRoot = Join-Path $repoRoot "src\web"
if (-not (Test-Path (Join-Path $webRoot "node_modules"))) {
    Write-Host "      node_modules not found, running npm install..." -ForegroundColor Gray
    cmd /c "npm install --prefix `"$webRoot`""
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: npm install failed." -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "      node_modules present, skipping install." -ForegroundColor Gray
}

cmd /c "npm run build --prefix `"$webRoot`" -- --emptyOutDir"
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: web build failed." -ForegroundColor Red
    exit 1
}
Write-Host "      Web build succeeded." -ForegroundColor Green

# ---------------------------------------------------------------------------
# Step 5: Start API and wait for health
# ---------------------------------------------------------------------------
Write-Host "[5/6] Starting API..." -ForegroundColor Cyan

$apiOut   = Join-Path $runtimeRoot "api.live.out.log"
$apiErr   = Join-Path $runtimeRoot "api.live.err.log"
Remove-Item -LiteralPath $apiOut, $apiErr -ErrorAction SilentlyContinue

Start-Process "C:\WINDOWS\System32\WindowsPowerShell\v1.0\powershell.exe" `
    -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "Set-Location '$repoRoot'; `$env:ASPNETCORE_URLS='$env:ASPNETCORE_URLS'; `$env:GAMARR_PUBLIC_SERVER_URL='$env:GAMARR_PUBLIC_SERVER_URL'; `$env:GAMARR_AGENT_SERVER_URL='$env:GAMARR_AGENT_SERVER_URL'; dotnet src\server\Gamarr.Api\bin\Debug\net8.0\Gamarr.Api.dll" `
    -WorkingDirectory $repoRoot `
    -WindowStyle Minimized `
    -RedirectStandardOutput $apiOut `
    -RedirectStandardError $apiErr

Write-Host "      Waiting for API health..." -ForegroundColor Gray
$timeout = 30
$elapsed = 0
$healthy = $false
while ($elapsed -lt $timeout) {
    try {
        $resp = Invoke-WebRequest -Uri "http://localhost:5000/health/live" -UseBasicParsing -TimeoutSec 2
        if ($resp.StatusCode -eq 200) { $healthy = $true; break }
    } catch { }
    Start-Sleep -Seconds 1
    $elapsed++
}
if (-not $healthy) {
    Write-Host "ERROR: API did not become healthy. Check $apiErr for details." -ForegroundColor Red
    exit 1
}
Write-Host "      API healthy." -ForegroundColor Green

# ---------------------------------------------------------------------------
# Step 6: Start Agent
# ---------------------------------------------------------------------------
Write-Host "[6/6] Starting Agent..." -ForegroundColor Cyan

$agentOut = Join-Path $runtimeRoot "agent.live.out.log"
$agentErr = Join-Path $runtimeRoot "agent.live.err.log"
Remove-Item -LiteralPath $agentOut, $agentErr -ErrorAction SilentlyContinue

Start-Process "C:\WINDOWS\System32\WindowsPowerShell\v1.0\powershell.exe" `
    -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "Set-Location '$repoRoot'; `$env:Gamarr__ServerBaseUrl='$env:GAMARR_AGENT_SERVER_URL'; dotnet src\agent\Gamarr.Agent\bin\Debug\net8.0\Gamarr.Agent.dll" `
    -WorkingDirectory $repoRoot `
    -WindowStyle Minimized `
    -RedirectStandardOutput $agentOut `
    -RedirectStandardError $agentErr

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "Gamarr is running." -ForegroundColor Green
Write-Host ""
Write-Host "  UI / API : http://localhost:5000" -ForegroundColor White
Write-Host "  Swagger  : http://localhost:5000/swagger" -ForegroundColor White
Write-Host "  LAN UI   : $env:GAMARR_PUBLIC_SERVER_URL" -ForegroundColor White
Write-Host "  RabbitMQ : http://localhost:15672" -ForegroundColor White
Write-Host "  Logs     : $runtimeRoot" -ForegroundColor Gray
Write-Host ""
