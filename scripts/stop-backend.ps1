param(
  [ValidateSet("All", "Worker", "Api")]
  [string]$Target = "All"
)

$patterns = switch ($Target) {
  "Worker" { @("StockYouNeed\.Worker") }
  "Api"    { @("StockYouNeed\.Api") }
  default  { @("StockYouNeed") }
}

Get-CimInstance Win32_Process -Filter "name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
  Where-Object {
    $cmd = $_.CommandLine
    $patterns | Where-Object { $cmd -match $_ }
  } |
  ForEach-Object {
    Write-Host "Stopping $($_.CommandLine) (PID $($_.ProcessId))"
    Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
  }

Start-Sleep -Milliseconds 500
