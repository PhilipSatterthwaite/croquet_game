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

# Already running? Starting a second one just fails on the port, which reads as
# a build error and is not one. Open the running lab instead.
$busy = Get-NetTCPConnection -LocalPort 5055 -State Listen -ErrorAction SilentlyContinue
if ($busy) {
    Write-Host "The lab is already running at $url" -ForegroundColor Yellow
    if ($args -notcontains '--no-open') { Start-Process $url }
    return
}

Push-Location $PSScriptRoot
try {
    Write-Host "Building..." -ForegroundColor DarkGray
    dotnet run --project tools/Croquet.Lab --no-launch-profile -- @args
}
finally {
    Pop-Location
}
