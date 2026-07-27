param(
  [int]$Port = 5080,
  [string]$ProjectPath = "$PSScriptRoot\..\backend\src\StockYouNeed.Api\StockYouNeed.Api.csproj"
)

$ErrorActionPreference = "Stop"

$listening = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($listening) {
  Write-Host ('[api] Already running on http://localhost:{0} (skip second start). Use F5 debug OR npm run dev:api - not both.' -f $Port)
  exit 0
}

$running = Get-CimInstance Win32_Process -Filter "name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
  Where-Object { $_.CommandLine -match 'StockYouNeed\.Api' }
if ($running) {
  $pidList = @($running | ForEach-Object { $_.ProcessId }) -join ','
  Write-Host ('[api] StockYouNeed.Api process already running (PID {0}). Skipping npm start.' -f $pidList)
  exit 0
}

# Build Api only — full solution fails while F5 Worker holds Worker DLLs.
Write-Host '[api] Building Api project ...'
dotnet build $ProjectPath /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ('[api] Starting GraphQL Api on http://localhost:{0} ...' -f $Port)
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project $ProjectPath --urls "http://localhost:$Port" --no-build --environment Development
