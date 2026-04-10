$ErrorActionPreference = "Stop"

Get-CimInstance Win32_Process |
  Where-Object {
    ($_.Name -eq "dotnet.exe" -and $_.CommandLine -like "*Gamarr*") -or
    $_.Name -match "^Gamarr\." -or
    (($_.Name -match "node|npm|cmd|python") -and ($_.CommandLine -like "*vite*" -or $_.CommandLine -like "*http.server 5173*")) -or
    (($_.Name -match "powershell") -and ($_.CommandLine -like "*Gamarr.Api.dll*" -or $_.CommandLine -like "*Gamarr.Agent.dll*"))
  } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

Write-Output "Stopped local Gamarr server and agent processes."
