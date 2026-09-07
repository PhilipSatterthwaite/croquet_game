<#
.SYNOPSIS
  Builds the Android player and puts it on the phone plugged into this machine.

.DESCRIPTION
  The device loop, the way play.ps1 is the feel loop. It drives the editor in
  batch mode, so nothing has to be clicked and the build is the same one every
  time, then installs over adb and starts it.

  Everything it needs ships with the editor -- the Android SDK, the NDK, a JDK
  and adb all live under the AndroidPlayer module, so there is nothing to
  install and no ANDROID_HOME to set. The phone needs USB debugging turned on;
  see CLAUDE.md.

  Over a cable or over Wi-Fi, indifferently: adb hands back the same kind of
  device either way, so nothing downstream of -Pair and -Connect knows which
  one it is talking to. Wireless is worth reaching for first anyway -- a USB
  cable sold with something that charges may have no data wires in it at all,
  and it looks identical to one that does.

  The first build is slow -- IL2CPP compiles the whole game to C++ and then to
  ARM64, which is minutes. Later ones are quicker.

.EXAMPLE
  .\android.ps1                 # build, install, launch
  .\android.ps1 -Release        # without the development player and its overhead
  .\android.ps1 -NoInstall      # just leave an apk in build/android
  .\android.ps1 -Log            # follow the game's log after launching
  .\android.ps1 -Devices        # list what adb can see, and stop
  .\android.ps1 -Cable          # is this cable a data cable? watch the USB bus

  # Wireless, on Android 11 and later. Once, from Developer options >
  # Wireless debugging > Pair device with pairing code:
  .\android.ps1 -Pair 192.168.1.23:37103 -Code 123456
  # then, using the address shown on the Wireless debugging screen itself --
  # a different port from the pairing one, and it changes on every reboot:
  .\android.ps1 -Connect 192.168.1.23:41234
#>

[CmdletBinding()]
param(
    [switch]$Release,
    [switch]$NoInstall,
    [switch]$Log,
    [switch]$Devices,
    [switch]$Cable,
    [string]$Pair,
    [string]$Code,
    [string]$Connect
)

$ErrorActionPreference = 'Stop'

$root    = $PSScriptRoot
$project = Join-Path $root 'unity'
$apk     = Join-Path $root 'build/android/croquet.apk'
$package = 'com.satterthwaite.croquet'

# ---- find the editor this project is pinned to -------------------------------
#
# The version out of ProjectVersion.txt and no other: a different editor would
# silently upgrade the project on open, in batch mode, with nobody watching.
$pinned = Get-Content (Join-Path $project 'ProjectSettings/ProjectVersion.txt') -Raw
if ($pinned -notmatch 'm_EditorVersion:\s*(\S+)') {
    throw "Could not read the editor version out of ProjectVersion.txt."
}
$version = $Matches[1]

$editor = "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
if (-not (Test-Path $editor)) {
    throw "Unity $version is not installed at $editor. Install it from the Hub."
}

$module = "C:/Program Files/Unity/Hub/Editor/$version/Editor/Data/PlaybackEngines/AndroidPlayer"
$adb    = Join-Path $module 'SDK/platform-tools/adb.exe'
if (-not (Test-Path $adb)) {
    throw "No adb at $adb. Add Android Build Support to Unity $version in the Hub " +
          "(with the SDK & NDK Tools, not just the module)."
}

# ---- what adb can see --------------------------------------------------------

function Get-Device {
    $lines = & $adb devices | Select-Object -Skip 1 | Where-Object { $_.Trim() }
    $ready = $lines | Where-Object { $_ -match '\sdevice$' }
    $unauth = $lines | Where-Object { $_ -match '\sunauthorized$' }

    if ($unauth) {
        throw "The phone is connected but has not authorised this computer. " +
              "Unlock it and accept the 'Allow USB debugging' prompt, then run this again."
    }
    if (-not $ready) {
        throw "No device. Over a cable: check it is a DATA cable -- a charging cable " +
              "looks identical and Windows will not see the phone at all -- that USB " +
              "debugging is on, and that the USB mode is File transfer rather than " +
              "Charging only. Or skip the cable: .\android.ps1 -Pair <ip:port> -Code <code>"
    }
    if (@($ready).Count -gt 1) {
        Write-Host "More than one device; using the first." -ForegroundColor DarkYellow
    }
    (@($ready)[0] -split '\s+')[0]
}

# ---- is this cable a data cable ----------------------------------------------
#
# There is no way to tell by looking, and a charge-only cable fails in the most
# misleading way available: the phone charges, so the cable is visibly "working",
# and Windows enumerates nothing whatever, which reads as a broken driver or a
# phone that needs more settings turned on. So ask the USB bus instead. A cable
# with data wires makes the phone appear the instant it is plugged in -- adb's
# interface is exposed whenever USB debugging is on, whatever file-transfer mode
# the phone is in -- and one without makes nothing appear, ever.

# It runs until Ctrl+C rather than testing one cable and stopping, because the
# realistic situation is a drawer full of cables and no idea which is which.
# Testing them one process at a time is thirty seconds of waiting per cable;
# this way the answer arrives about a second after each one goes in, and a pile
# can be got through as fast as they can be swapped.

if ($Cable) {
    Write-Host "Watching the USB bus. Plug a cable in; unplug it; try the next." -ForegroundColor Cyan
    Write-Host "Ctrl+C to stop.`n" -ForegroundColor DarkGray
    Write-Host "Before that, a prefilter worth doing by eye: look into the USB-A end and" -ForegroundColor DarkGray
    Write-Host "count the flat gold contacts on the tongue. FOUR means the data wires are" -ForegroundColor DarkGray
    Write-Host "there. TWO, at the outer edges only, means power and nothing else. A blue" -ForegroundColor DarkGray
    Write-Host "tongue is USB 3 and always carries data. USB-C ends tell you nothing --" -ForegroundColor DarkGray
    Write-Host "all 24 pins are moulded in whether or not there are wires behind them.`n" -ForegroundColor DarkGray

    $usb = { (Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
              Where-Object { $_.InstanceId -like 'USB*' }) }

    $seen = @{}
    foreach ($d in @(& $usb)) { $seen[$d.InstanceId] = $d.FriendlyName }
    Write-Host "Baseline: $($seen.Count) USB devices already present.`n" -ForegroundColor DarkGray

    while ($true) {
        Start-Sleep -Milliseconds 700

        $now = @{}
        foreach ($d in @(& $usb)) { $now[$d.InstanceId] = $d.FriendlyName }

        $arrived = @($now.Keys | Where-Object { -not $seen.ContainsKey($_) })
        foreach ($id in @($seen.Keys | Where-Object { -not $now.ContainsKey($_) })) {
            Write-Host "  - gone: $($seen[$id])" -ForegroundColor DarkGray
        }

        foreach ($id in $arrived) {
            Write-Host "`n  THIS CABLE CARRIES DATA -- $($now[$id])" -ForegroundColor Green

            # Whether it is the phone AND talking is a second question, so ask
            # adb too. A cable can be sound while the phone is still refusing.
            $lines = & $adb devices | Select-Object -Skip 1 | Where-Object { $_.Trim() }
            if ($lines | Where-Object { $_ -match '\sdevice$' }) {
                Write-Host "  adb has it. Ctrl+C and run .\android.ps1" -ForegroundColor Green
            }
            elseif ($lines | Where-Object { $_ -match '\sunauthorized$' }) {
                Write-Host "  adb sees it but is not authorised -- unlock the phone and accept" -ForegroundColor DarkYellow
                Write-Host "  the 'Allow USB debugging' prompt." -ForegroundColor DarkYellow
            }
            else {
                Write-Host "  adb does not have it yet. If this is the phone rather than a hub," -ForegroundColor DarkYellow
                Write-Host "  turn on USB debugging in Developer options." -ForegroundColor DarkYellow
            }
        }

        $seen = $now
    }
}

# ---- wireless, for when the cable is the problem -----------------------------
#
# Two addresses on the phone and they are not the same one, which is the whole
# difficulty with this: pairing has its own temporary port, shown only inside
# the "Pair device with pairing code" dialog, while connecting uses the port on
# the Wireless debugging screen behind it. That one changes on every reboot, so
# -Connect is a thing to re-run; -Pair is once.

function Assert-Address($address, $switchName) {
    if ($address -match '^\d{1,3}(\.\d{1,3}){3}:\d+$') { return }

    # The phone prints host:port and the separator is easy to read as another
    # dot in the address. adb's own answer to a malformed one is "protocol
    # fault (couldn't read status message)", which sends you looking at the
    # network instead of at what you typed.
    if ($address -match '^(\d{1,3}(?:\.\d{1,3}){3})\.(\d+)$') {
        throw "$address has a dot where the port separator should be a colon. " +
              "Try: .\android.ps1 -$switchName $($Matches[1]):$($Matches[2])"
    }
    throw "$address is not an address adb understands. It wants <ip>:<port>, " +
          "exactly as the phone shows it."
}

if ($Pair) {
    Assert-Address $Pair 'Pair'
    Write-Host "Pairing with $Pair..." -ForegroundColor DarkGray
    if ($Code) { & $adb pair $Pair $Code } else { & $adb pair $Pair }
    if ($LASTEXITCODE -ne 0) {
        throw "Pairing failed. The dialog on the phone must still be open -- its code " +
              "and port both expire when it closes -- and this computer must be on the " +
              "same network as the phone."
    }
    Write-Host "Paired. Now connect, using the address on the Wireless debugging screen" -ForegroundColor Green
    Write-Host "itself rather than the one from the pairing dialog:" -ForegroundColor Green
    Write-Host "  .\android.ps1 -Connect <ip:port>" -ForegroundColor Green
    return
}

if ($Connect) {
    Assert-Address $Connect 'Connect'
    & $adb connect $Connect
    # adb connect reports failure in its output rather than its exit code, so
    # the list is the only honest confirmation.
    & $adb devices -l
    return
}

if ($Devices) {
    & $adb devices -l
    return
}

# Checked BEFORE the build rather than after it, because an IL2CPP build is
# minutes long and finding out then that the cable is dead is minutes wasted.
$device = if ($NoInstall) { $null } else { Get-Device }
if ($device) { Write-Host "Device: $device" -ForegroundColor DarkGray }

# ---- build -------------------------------------------------------------------

# A batchmode editor cannot take the project lock off an editor that is already
# open, and the error it gives for that says nothing useful.
$open = Get-Process Unity -ErrorAction SilentlyContinue
if ($open) {
    Write-Host "Unity looks like it is already open on a project. If it is this one, " -ForegroundColor DarkYellow -NoNewline
    Write-Host "close it, or use Croquet > Build for Android from the editor instead." -ForegroundColor DarkYellow
}

$log = Join-Path $root 'build/android/build.log'
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
if (Test-Path $apk) { Remove-Item $apk -Force }

$unityArgs = @(
    '-batchmode', '-nographics',
    '-projectPath', $project,
    '-buildTarget', 'Android',
    '-executeMethod', 'AndroidBuild.Build',
    '-logFile', $log
)
if (-not $Release) { $unityArgs += '-development' }

Write-Host "Building $(if ($Release) { 'release' } else { 'development' }) apk. " -ForegroundColor DarkGray -NoNewline
Write-Host "IL2CPP takes a few minutes; log at $log" -ForegroundColor DarkGray

$build = Start-Process -FilePath $editor -ArgumentList $unityArgs -PassThru -Wait -NoNewWindow
if ($build.ExitCode -ne 0 -or -not (Test-Path $apk)) {
    Write-Host "--- last of $log ---" -ForegroundColor DarkRed
    if (Test-Path $log) { Get-Content $log -Tail 40 }
    throw "Build failed (exit $($build.ExitCode))."
}

$mb = [math]::Round((Get-Item $apk).Length / 1MB, 1)
Write-Host "Built $apk ($mb MB)" -ForegroundColor Green

if ($NoInstall) { return }

# ---- install and run ---------------------------------------------------------

Write-Host "Installing..." -ForegroundColor DarkGray
& $adb -s $device install -r $apk
if ($LASTEXITCODE -ne 0) {
    # The usual cause is an apk already there signed with a different key --
    # a release build over a development one, or a build from another machine.
    Write-Host "Install failed. If it mentions signatures, uninstall the old one:" -ForegroundColor DarkYellow
    Write-Host "  & '$adb' uninstall $package" -ForegroundColor DarkYellow
    throw "adb install failed."
}

# monkey rather than a named activity: which activity Unity generates depends on
# the application entry point setting, and this asks the launcher instead.
& $adb -s $device shell monkey -p $package -c android.intent.category.LAUNCHER 1 | Out-Null
Write-Host "Running on $device." -ForegroundColor Green

if ($Log) {
    Write-Host "Following the log; Ctrl+C to stop." -ForegroundColor DarkGray
    & $adb -s $device logcat -c
    & $adb -s $device logcat Unity:V "*:S"
}
else {
    # No rebuild needed to read it -- adb is talking to the running player.
    Write-Host "For the game's log, including the determinism report:" -ForegroundColor DarkGray
    Write-Host "  & '$adb' logcat Unity:V '*:S'" -ForegroundColor DarkGray
}
