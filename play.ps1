<#
.SYNOPSIS
  Starts the croquet lab and opens it in a browser.

.DESCRIPTION
  The lab is a dev tool: a browser front end onto the real Croquet.Core, for
  judging how the game feels. See CLAUDE.md.

  Reachable as `play croquet` from anywhere, via the `play` function in the
  PowerShell profile — which does nothing but call this script, so the repo
  stays the single source of truth for how the thing is launched.

.EXAMPLE
  .\play.ps1
  .\play.ps1 --no-open      # start the server without launching a browser
#>

$ErrorActionPreference = 'Stop'
$url = 'http://localhost:5055'

# Stop anything already running and start fresh.
#
# The old behaviour -- notice the port is taken and just open the browser --
# quietly served whatever code was running when it was last started. Every
# change made since then was invisible, and the lab looked broken rather than
# stale. Always running the current build is worth the second it costs.
$old = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
       Where-Object { $_.CommandLine -like '*Croquet.Lab*' }
foreach ($p in $old) {
    Write-Host "Stopping the lab already running (pid $($p.ProcessId))..." -ForegroundColor DarkGray
    Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
}
if ($old) { Start-Sleep -Milliseconds 800 }

Push-Location $PSScriptRoot
try {
    Write-Host "Building..." -ForegroundColor DarkGray
    dotnet run --project tools/Croquet.Lab --no-launch-profile -- @args
}
finally {
    Pop-Location
}
