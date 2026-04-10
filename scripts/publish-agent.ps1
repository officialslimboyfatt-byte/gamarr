$ErrorActionPreference = "Stop"

param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = "E:\dev\spool\.runtime-agent\publish"
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$agentProject = Join-Path $repoRoot "src\agent\Gamarr.Agent\Gamarr.Agent.csproj"

dotnet publish $agentProject -c $Configuration -o $OutputRoot
if ($LASTEXITCODE -ne 0) { throw "Agent publish failed." }

Write-Host "Published agent to $OutputRoot" -ForegroundColor Green
