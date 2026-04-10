$ErrorActionPreference = "Stop"

param(
    [string]$ServiceName = "Gamarr Server",
    [string]$PublishRoot = "E:\dev\spool\.runtime-server\publish",
    [string]$ListenUrls = "http://0.0.0.0:5000",
    [string]$PublicServerUrl = "http://localhost:5000",
    [string]$AgentServerUrl = "http://localhost:5000"
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $repoRoot "scripts\publish-server.ps1"
& $publishScript -OutputRoot $PublishRoot

$appsettingsPath = Join-Path $PublishRoot "appsettings.json"
$appsettings = Get-Content -Raw $appsettingsPath | ConvertFrom-Json
if (-not $appsettings.GamarrServer) {
    $appsettings | Add-Member -NotePropertyName "GamarrServer" -NotePropertyValue ([pscustomobject]@{})
}
$appsettings.GamarrServer.RunAsConsole = $false
$appsettings.GamarrServer.ListenUrls = $ListenUrls
$appsettings.GamarrServer.PublicServerUrl = $PublicServerUrl
$appsettings.GamarrServer.AgentServerUrl = $AgentServerUrl
$appsettings | ConvertTo-Json -Depth 10 | Set-Content -Path $appsettingsPath -Encoding UTF8

$dotnetPath = (Get-Command dotnet).Source
$dllPath = Join-Path $PublishRoot "Gamarr.Api.dll"
$binPath = "`"$dotnetPath`" `"$dllPath`""

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    sc.exe stop $ServiceName | Out-Null
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

sc.exe create $ServiceName binPath= $binPath start= auto | Out-Null
sc.exe description $ServiceName "Gamarr server host" | Out-Null
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null
sc.exe start $ServiceName | Out-Null

Write-Host "Installed and started $ServiceName" -ForegroundColor Green
