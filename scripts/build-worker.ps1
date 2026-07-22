param(
  [string]$ProjectPath = "$PSScriptRoot\..\backend\src\StockYouNeed.Worker\StockYouNeed.Worker.csproj"
)

$ErrorActionPreference = "Stop"
$mutexName = "Global\StockYouNeed_BackendBuild"

$mutex = New-Object System.Threading.Mutex($false, $mutexName)
$acquired = $false

try {
  $acquired = $mutex.WaitOne([TimeSpan]::FromMinutes(2))
  if (-not $acquired) { throw "Timed out waiting for backend build lock." }

  # Do not stop Api — it is started by `npm run dev`.
  & "$PSScriptRoot\stop-backend.ps1" -Target Worker

  Write-Host "Building Worker (Api left running) ..."
  dotnet build $ProjectPath /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
  if ($acquired) { $mutex.ReleaseMutex() | Out-Null }
  $mutex.Dispose()
}
