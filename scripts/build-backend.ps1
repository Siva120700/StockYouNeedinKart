param(
  [string]$SolutionPath = "$PSScriptRoot\..\backend\StockYouNeed.sln",
  [ValidateSet("All", "Worker", "Api")]
  [string]$StopTarget = "All"
)

$ErrorActionPreference = "Stop"
$mutexName = "Global\StockYouNeed_BackendBuild"

$mutex = New-Object System.Threading.Mutex($false, $mutexName)
$acquired = $false

try {
  Write-Host "Waiting for build lock..."
  $acquired = $mutex.WaitOne([TimeSpan]::FromMinutes(2))
  if (-not $acquired) {
    throw "Timed out waiting for backend build lock."
  }

  & "$PSScriptRoot\stop-backend.ps1" -Target $StopTarget

  Write-Host "Building $SolutionPath ..."
  dotnet build $SolutionPath /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
  if ($acquired) { $mutex.ReleaseMutex() | Out-Null }
  $mutex.Dispose()
}
