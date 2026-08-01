param(
  [Parameter(Mandatory = $true)][int]$ParentPid,
  [Parameter(Mandatory = $true)][int]$ApiPid
)

# Watchdog for `npm run dev`: when the launching PowerShell disappears
# (Ctrl+C, terminal closed, concurrently SIGTERM), stop the Api it started.
$ErrorActionPreference = "SilentlyContinue"

while (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue) {
  Start-Sleep -Seconds 1
}

Stop-Process -Id $ApiPid -Force -ErrorAction SilentlyContinue
& "$PSScriptRoot\stop-backend.ps1" -Target Api | Out-Null
