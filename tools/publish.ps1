# publish.ps1: build Darg for use (Release) into an install folder and make Start menu and desktop
# shortcuts. Run again to update; a running copy from that folder is closed first.
#   pwsh -File tools/publish.ps1 [-To D:\Apps\Darg] [-NoShortcuts]
param([string]$To = 'D:\Apps\Darg', [switch]$NoShortcuts)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = 'D:\nuget\packages' }

# a copy running from the install folder holds its files: close it (its agents are saved after every turn)
$running = @(Get-CimInstance Win32_Process -Filter "Name='Darg.Desktop.exe' OR Name='Workbench.Desktop.exe'" |
    Where-Object { $p = $_.ExecutablePath; $p -and (@($To, (Join-Path (Split-Path $To) 'Workbench')) | Where-Object { $p.StartsWith($_.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) }) })

# ...unless this script was started from inside that copy (an agent in Darg running it): closing it
# would kill the agent mid-publish and leave the old build in place. Walk up the parent processes to check.
$ids = @{}; foreach ($r in $running) { $ids[[int]$r.ProcessId] = $true }
$walk = Get-CimInstance Win32_Process -Filter "ProcessId=$PID"
for ($i = 0; $walk -and $i -lt 30; $i++) {
    if ($ids.ContainsKey([int]$walk.ProcessId)) {
        Write-Error "This was started from inside the Darg in $To, which publishing has to close. Run it from a terminal outside Darg, or close Darg first."
        exit 1
    }
    $walk = Get-CimInstance Win32_Process -Filter "ProcessId=$($walk.ParentProcessId)"
}
$running | ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Start-Sleep -Seconds 1 }

$rid = if ($IsWindows) { 'win-x64' } elseif ($IsMacOS) { 'osx-arm64' } else { 'linux-x64' }
dotnet publish (Join-Path $repo 'src/Darg.Desktop') -c Release -r $rid --self-contained false -o $To -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed ($LASTEXITCODE)" }
$exe = Join-Path $To 'Darg.Desktop.exe'
# where this build came from, so the app's ⟳ Update can rebuild it from here
Set-Content -LiteralPath (Join-Path $To 'source.txt') -Value $repo
"published to $To"

if ($NoShortcuts -or -not $IsWindows) { return }
$shell = New-Object -ComObject WScript.Shell
foreach ($dir in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {
    Remove-Item -LiteralPath (Join-Path $dir 'Workbench.lnk') -ErrorAction SilentlyContinue # the old name
    $lnk = $shell.CreateShortcut((Join-Path $dir 'Darg.lnk'))
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $To
    $lnk.Description = 'Darg: boards, agents, docs and code for every project'
    $lnk.Save()
    "shortcut: $dir\Darg.lnk"
}
