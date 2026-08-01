param(
  [int]$Port = 5080,
  [string]$ProjectPath = "$PSScriptRoot\..\backend\src\StockYouNeed.Api\StockYouNeed.Api.csproj"
)

$ErrorActionPreference = "Stop"

# This script owns the Api for the current `npm run dev` session:
# always start fresh, and stop the Api when this script goes away.
Write-Host '[api] Stopping any existing Api ...'
& "$PSScriptRoot\stop-backend.ps1" -Target Api

Write-Host '[api] Building Api project ...'
dotnet build $ProjectPath /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ("[api] Starting GraphQL Api on http://localhost:{0} ..." -f $Port)
$env:ASPNETCORE_ENVIRONMENT = "Development"

$dotnetArgs = @(
  "run",
  "--project", $ProjectPath,
  "--urls", "http://localhost:$Port",
  "--no-build",
  "--environment", "Development"
)

$proc = Start-Process -FilePath "dotnet" -ArgumentList $dotnetArgs -NoNewWindow -PassThru

# concurrently kills this PowerShell abruptly, so `finally` alone is not enough.
$watchdog = Start-Process -FilePath "powershell" -WindowStyle Hidden -PassThru -ArgumentList @(
  "-NoProfile", "-ExecutionPolicy", "Bypass",
  "-File", "$PSScriptRoot\watch-api-parent.ps1",
  "-ParentPid", $PID,
  "-ApiPid", $proc.Id
)

try {
  Wait-Process -Id $proc.Id
  exit $proc.ExitCode
}
finally {
  $ErrorActionPreference = "SilentlyContinue"
  if ($proc -and -not $proc.HasExited) {
    Write-Host ("[api] Stopping Api (PID {0}) ..." -f $proc.Id)
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
  }
  & "$PSScriptRoot\stop-backend.ps1" -Target Api | Out-Null
  if ($watchdog -and -not $watchdog.HasExited) {
    Stop-Process -Id $watchdog.Id -Force -ErrorAction SilentlyContinue
  }
}
