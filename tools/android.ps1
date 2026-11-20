<#
.SYNOPSIS
  Builds the Android app, installs it on a phone over adb, connects it to this PC's Darg and starts it (AND-0003).

.DESCRIPTION
  Needs tools/android-setup.ps1 run once. With a phone plugged in (USB debugging on) or the emulator running:

    1. builds src/Darg.Android (Debug, signed with the SDK's debug key) and installs it (dotnet build -t:Install);
    2. adb reverse tcp:47817 -> this PC's Darg API port, so the phone's localhost:47817 is the desktop;
    3. pairs: writes {address, token} into the app's private files (run-as, debug builds only), the token read from
       the instance folder's artifact-token, after checking the desktop answers with it;
    4. starts the app, which opens on the project list.

  The desktop must be running (any copy: installed, debug or live) on the same instance.

.EXAMPLE
  pwsh -File tools/android.ps1                 # build, install, pair, start
  pwsh -File tools/android.ps1 -Emulator       # same, starting the darg emulator if no phone is attached
  pwsh -File tools/android.ps1 -NoBuild        # pair and start only (after a cable re-plug, adb reverse is lost)
  pwsh -File tools/android.ps1 -NoPair         # leave the phone's saved connection alone
  pwsh -File tools/android.ps1 -StopEmulator   # stop the emulator (every copy) and nothing else: do it when done with it

  One emulator at a time (WOR-0170): one already running is waited for, never joined by a second, and one that hasn't
  booted in 5 minutes is stopped, not left behind. An emulator takes 4.5 GB or more; one night two were left running,
  one of them leaking, until Windows ran out of memory and the PC bluescreened.
#>
param(
    [switch]$NoBuild,
    [switch]$NoPair,
    [switch]$Emulator,
    [switch]$StopEmulator,
    [string]$Configuration = 'Debug',
    [string]$Device,                 # an adb serial when several are attached
    [int]$Port = 47817,              # the desktop's API port (ArtifactServer's; another if 47817 was taken)
    [string]$Instance                # the desktop's instance folder; default as Darg finds it
)
$ErrorActionPreference = 'Stop'

$root = if ($env:DARG_ANDROID_ROOT) { $env:DARG_ANDROID_ROOT } else { 'D:\Android' }
$dotnet = Join-Path $root 'dotnet\dotnet.exe'
$sdk = Join-Path $root 'sdk'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'src\Darg.Android\Darg.Android.csproj'
$package = 'uk.co.philipwilson.darg'
if (-not (Test-Path -LiteralPath $dotnet)) { throw "No Android toolchain at ${root}: run pwsh -File tools/android-setup.ps1 first." }
$env:DOTNET_ROOT = Split-Path $dotnet
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = 'D:\nuget\packages' }
$env:ANDROID_HOME = $sdk
$adb = Join-Path $sdk 'platform-tools\adb.exe'
if (-not (Test-Path -LiteralPath $adb)) { $adb = (Get-Command adb -ErrorAction Stop).Source }

function Get-Devices { @(& $adb devices | Select-String "`tdevice$" | ForEach-Object { ($_ -split "`t")[0] }) }

# the emulator's processes (emulator.exe, its qemu, headless or not, and crashpad), whoever started them
function Get-EmulatorProcesses {
    $dir = (Join-Path $sdk 'emulator').TrimEnd('\') + '\'
    @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($dir, [StringComparison]::OrdinalIgnoreCase) })
}
function Stop-Emulator {
    $running = @(Get-EmulatorProcesses)
    if ($running.Count -eq 0) { Write-Host 'No emulator running.'; return }
    Write-Host "== stopping the emulator: $(($running | ForEach-Object { "$($_.Name) $($_.ProcessId)" }) -join ', ')"
    & $adb emu kill 2>$null | Out-Null # lets it close its disk images, when it's online
    $deadline = (Get-Date).AddSeconds(10)
    while (@(Get-EmulatorProcesses).Count -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    foreach ($p in @(Get-EmulatorProcesses)) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
}
if ($StopEmulator) { Stop-Emulator; return }

# -- a device to install on
$devices = @(Get-Devices)
if ($devices.Count -eq 0 -and $Emulator) {
    $env:ANDROID_AVD_HOME = Join-Path $root 'avd'
    $emu = Join-Path $sdk 'emulator\emulator.exe'
    if (@(Get-EmulatorProcesses).Count) {
        Write-Host '== an emulator is already running: waiting for it to boot rather than starting another'
    } else {
        Write-Host "== starting the darg emulator"
        # software GPU: with the host GPU (the default) the emulator sat "offline" to adb for 25 minutes on this PC
        Start-Process -FilePath $emu -ArgumentList '-avd', 'darg', '-no-snapshot-save', '-no-boot-anim', '-gpu', 'swiftshader_indirect' -WindowStyle Normal
    }
    # never adb wait-for-device: it waits forever on an emulator that stays offline
    $deadline = (Get-Date).AddMinutes(5)
    while ((& $adb shell getprop sys.boot_completed 2>$null) -ne '1') {
        if (-not @(Get-EmulatorProcesses).Count) { throw 'The emulator closed before it finished booting.' }
        if ((Get-Date) -gt $deadline) {
            $seen = "$(& $adb devices | Select-Object -Skip 1)"
            Stop-Emulator # one stuck offline is stopped, not left running for hours
            throw "The emulator did not finish booting in 5 minutes (adb devices: $seen); it has been stopped."
        }
        Start-Sleep -Seconds 3
    }
    $devices = @(Get-Devices)
}
if ($devices.Count -eq 0) { throw 'No phone attached: plug one in with USB debugging on (and accept the prompt on it), or use -Emulator.' }
if ($devices.Count -gt 1 -and -not $Device) { throw "Several devices attached ($($devices -join ', ')): pick one with -Device." }
$target = if ($Device) { $Device } else { $devices[0] }
Write-Host "== device $target"

# -- build and install
if (-not $NoBuild) {
    Write-Host "== building and installing ($Configuration)"
    & $dotnet build $project -t:Install -f net10.0-android -c $Configuration "-p:AndroidSdkDirectory=$sdk" "-p:AdbTarget=-s $target" -v:minimal -nologo
    if ($LASTEXITCODE) { throw "build/install failed ($LASTEXITCODE)" }
}

# -- the desktop: its token, and proof it answers with it
if (-not $NoPair) {
    if (-not $Instance) {
        $Instance = if ($env:DARG_HOME) { $env:DARG_HOME } else {
            $name = if ($env:DARG_INSTANCE) { $env:DARG_INSTANCE } else { 'home' }
            $now = Join-Path $env:APPDATA "Darg\$name"; $old = Join-Path $env:APPDATA "Workbench\$name"
            if (-not (Test-Path -LiteralPath $now) -and (Test-Path -LiteralPath $old)) { $old } else { $now }
        }
    }
    $tokenFile = Join-Path $Instance 'artifact-token'
    if (-not (Test-Path -LiteralPath $tokenFile)) { throw "No artifact-token in ${Instance}: has Darg run on that instance?" }
    $token = (Get-Content -LiteralPath $tokenFile -Raw).Trim()
    try { $null = Invoke-RestMethod "http://localhost:$Port/api/$token" -TimeoutSec 5 }
    catch { throw "Darg isn't answering on localhost:$Port with that instance's token ($Instance). Is it running? ($($_.Exception.Message))" }

    Write-Host "== adb reverse: the phone's localhost:47817 -> this PC's :$Port"
    & $adb -s $target reverse tcp:47817 "tcp:$Port" | Out-Null
    if ($LASTEXITCODE) { throw 'adb reverse failed' }

    Write-Host "== pairing (writing the connection into the app's files)"
    $json = @{ address = 'http://localhost:47817'; token = $token } | ConvertTo-Json -Compress
    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
    & $adb -s $target shell "run-as $package sh -c 'mkdir -p files && echo $b64 | base64 -d > files/connection.json'"
    if ($LASTEXITCODE) { throw 'Writing the connection failed (run-as needs a debug build installed).' }
}

# -- start it fresh, so it reads the connection
& $adb -s $target shell am force-stop $package | Out-Null
& $adb -s $target shell am start -n "$package/.MainActivity" | Out-Null
if ($LASTEXITCODE) { throw 'Starting the app failed.' }
Write-Host "Started Darg on $target."
