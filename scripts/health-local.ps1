$ErrorActionPreference = "Stop"

$api = Invoke-RestMethod -Uri "http://localhost:5000/health/ready" -Method Get
$machines = Invoke-RestMethod -Uri "http://localhost:5000/api/machines" -Method Get
$web = Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:5000"

[PSCustomObject]@{
  api = $api.status
  machineCount = @($machines).Count
  webStatus = $web.StatusCode
} | Format-List
