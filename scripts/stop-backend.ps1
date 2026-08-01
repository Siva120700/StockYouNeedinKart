param(
  [ValidateSet("All", "Worker", "Api")]
  [string]$Target = "All"
)

function Stop-MatchingDotnet {
  param([string[]]$Patterns)
  Get-CimInstance Win32_Process -Filter "name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object {
      $cmd = $_.CommandLine
      $Patterns | Where-Object { $cmd -match $_ }
    } |
    ForEach-Object {
      Write-Host "Stopping $($_.CommandLine) (PID $($_.ProcessId))"
      Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

function Stop-AppHost {
  param([string[]]$Names)
  foreach ($name in $Names) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
      Write-Host "Stopping $($_.ProcessName).exe (PID $($_.Id))"
      Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
    }
  }
}

switch ($Target) {
  "Worker" {
    Stop-MatchingDotnet -Patterns @("StockYouNeed\.Worker")
    Stop-AppHost -Names @("StockYouNeed.Worker")
  }
  "Api" {
    Stop-MatchingDotnet -Patterns @("StockYouNeed\.Api")
    Stop-AppHost -Names @("StockYouNeed.Api")
  }
  default {
    Stop-MatchingDotnet -Patterns @("StockYouNeed")
    Stop-AppHost -Names @("StockYouNeed.Api", "StockYouNeed.Worker")
  }
}

# Free listen ports if something is still bound after process kill.
if ($Target -eq "Api" -or $Target -eq "All") {
  Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object {
      $opid = $_.OwningProcess
      if ($opid -and $opid -ne 0) {
        Write-Host "Stopping process on port 5080 (PID $opid)"
        Stop-Process -Id $opid -Force -ErrorAction SilentlyContinue
      }
    }
}

Start-Sleep -Milliseconds 500
