# publish.ps1: build Workbench for use (Release) into an install folder and make Start menu and desktop
# shortcuts. Run again to update; a running copy from that folder is closed first.
#   pwsh -File tools/publish.ps1 [-To D:\Apps\Workbench] [-NoShortcuts]
param([string]$To = 'D:\Apps\Workbench', [switch]$NoShortcuts)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = 'D:\nuget\packages' }

# a copy running from the install folder holds its files: close it (its agents are saved after every turn)
Get-CimInstance Win32_Process -Filter "Name='Workbench.Desktop.exe'" |
    Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($To, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Start-Sleep -Seconds 1 }

$rid = if ($IsWindows) { 'win-x64' } elseif ($IsMacOS) { 'osx-arm64' } else { 'linux-x64' }
dotnet publish (Join-Path $repo 'src/Workbench.Desktop') -c Release -r $rid --self-contained false -o $To -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed ($LASTEXITCODE)" }
$exe = Join-Path $To 'Workbench.Desktop.exe'
"published to $To"

if ($NoShortcuts -or -not $IsWindows) { return }
$shell = New-Object -ComObject WScript.Shell
foreach ($dir in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {
    $lnk = $shell.CreateShortcut((Join-Path $dir 'Workbench.lnk'))
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $To
    $lnk.Description = 'Workbench: boards, agents, docs and code for every project'
    $lnk.Save()
    "shortcut: $dir\Workbench.lnk"
}
