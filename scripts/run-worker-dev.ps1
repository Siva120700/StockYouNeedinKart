param(
  [string]$ProjectPath = "$PSScriptRoot\..\backend\src\StockYouNeed.Worker\StockYouNeed.Worker.csproj"
)

$ErrorActionPreference = "Stop"

$running = Get-CimInstance Win32_Process -Filter "name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
  Where-Object { $_.CommandLine -match 'StockYouNeed\.Worker' }
if ($running) {
  $pidList = @($running | ForEach-Object { $_.ProcessId }) -join ','
  Write-Host ('[worker] StockYouNeed.Worker already running (PID {0}). Skipping npm start.' -f $pidList)
  exit 0
}

& "$PSScriptRoot\build-backend.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '[worker] Starting background worker ...'
dotnet run --project $ProjectPath --no-build
