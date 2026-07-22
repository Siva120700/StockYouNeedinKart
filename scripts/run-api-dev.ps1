param(
  [int]$Port = 5080,
  [string]$ProjectPath = "$PSScriptRoot\..\backend\src\StockYouNeed.Api\StockYouNeed.Api.csproj"
)

$ErrorActionPreference = "Stop"

$listening = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($listening) {
  Write-Host "[api] Already running on http://localhost:$Port (skip second start). Use F5 debug OR npm run dev:api — not both."
  exit 0
}

$running = Get-CimInstance Win32_Process -Filter "name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
  Where-Object { $_.CommandLine -match 'StockYouNeed\.Api' }
if ($running) {
  Write-Host "[api] StockYouNeed.Api process already running (PID $($running.ProcessId)). Skipping npm start."
  exit 0
}

& "$PSScriptRoot\build-backend.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "[api] Starting GraphQL Api on http://localhost:$Port ..."
dotnet run --project $ProjectPath --urls "http://localhost:$Port" --no-build
