$ErrorActionPreference = "Stop"

param(
    [string]$ServiceName = "Gamarr Agent",
    [string]$PublishRoot = "E:\dev\spool\.runtime-agent\publish",
    [string]$ServerBaseUrl = "http://localhost:5000"
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $repoRoot "scripts\publish-agent.ps1"
& $publishScript -OutputRoot $PublishRoot

$appsettingsPath = Join-Path $PublishRoot "appsettings.json"
$appsettings = Get-Content -Raw $appsettingsPath | ConvertFrom-Json
if (-not $appsettings.Gamarr) {
    $appsettings | Add-Member -NotePropertyName "Gamarr" -NotePropertyValue ([pscustomobject]@{})
}
$appsettings.Gamarr.ServerBaseUrl = $ServerBaseUrl
$appsettings.Gamarr.RunAsConsole = $false
$appsettings | ConvertTo-Json -Depth 10 | Set-Content -Path $appsettingsPath -Encoding UTF8

$dotnetPath = (Get-Command dotnet).Source
$dllPath = Join-Path $PublishRoot "Gamarr.Agent.dll"
$binPath = "`"$dotnetPath`" `"$dllPath`""

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    sc.exe stop $ServiceName | Out-Null
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

sc.exe create $ServiceName binPath= $binPath start= auto | Out-Null
sc.exe description $ServiceName "Gamarr machine agent" | Out-Null
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null
sc.exe start $ServiceName | Out-Null

Write-Host "Installed and started $ServiceName" -ForegroundColor Green
