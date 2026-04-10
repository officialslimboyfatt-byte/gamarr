$ErrorActionPreference = "Stop"

param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = "E:\dev\spool\.runtime-server\publish"
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repoRoot "src\web"
$serverProject = Join-Path $repoRoot "src\server\Gamarr.Api\Gamarr.Api.csproj"
$publishRoot = Resolve-Path (Split-Path -Parent $OutputRoot) -ErrorAction SilentlyContinue
if (-not $publishRoot) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $OutputRoot) -Force | Out-Null
}

Write-Host "[1/3] Building web bundle..." -ForegroundColor Cyan
cmd /c "npm run build --prefix `"$webRoot`" -- --emptyOutDir"
if ($LASTEXITCODE -ne 0) { throw "Web build failed." }

Write-Host "[2/3] Publishing server..." -ForegroundColor Cyan
dotnet publish $serverProject -c $Configuration -o $OutputRoot
if ($LASTEXITCODE -ne 0) { throw "Server publish failed." }

Write-Host "[3/3] Copying embedded web assets..." -ForegroundColor Cyan
$wwwroot = Join-Path $OutputRoot "wwwroot"
New-Item -ItemType Directory -Path $wwwroot -Force | Out-Null
Copy-Item -Path (Join-Path $webRoot "dist\*") -Destination $wwwroot -Recurse -Force

Write-Host "Published server to $OutputRoot" -ForegroundColor Green
