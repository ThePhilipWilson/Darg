# live.ps1: run Darg as a live copy (WOR-0126), one that takes updates without restarting.
#
# Darg runs under `dotnet watch` (Debug build) from a worktree of its own, detached at a branch (the one
# the repo is on). The ⟳ menu's "Pull in" moves that worktree to the branch's newest commit, and dotnet
# watch applies the change to the running app: C# through .NET hot reload, .axaml through HotAvalonia.
# An edit hot reload can't take (a changed signature, a new package) makes watch close and reopen Darg by
# itself, which stops agents mid-turn as a restart would. Agents' half-made edits in the repo never reach
# it: only what has landed on the branch does, and only when pulled in.
#
#   pwsh -File tools/live.ps1 [-Branch main] [-Tree <worktree>] [-Shortcut] [-Force]
#
# It opens a console window of its own showing dotnet watch, and that window goes when Darg is closed.
# -Shortcut makes "Darg (live)" Start menu and desktop shortcuts that run this script.
# The installed copy (D:\Apps\Darg) and a live copy on the same instance would fight over its agents and
# files, so this refuses while the installed one runs, unless -Force (or DARG_HOME points elsewhere).
param([string]$Branch, [string]$Tree, [switch]$Shortcut, [switch]$Force, [switch]$Here)
$ErrorActionPreference = 'Stop'

# the main checkout, even when this runs from inside the live worktree (a restart does)
$common = (git -C $PSScriptRoot rev-parse --path-format=absolute --git-common-dir).Trim()
$repo = Split-Path -Parent $common
if (-not $Branch) { $Branch = (git -C $repo branch --show-current).Trim() }
if (-not $Branch) { throw "$repo isn't on a branch; say which to follow with -Branch." }
if (-not $Tree) { $Tree = Join-Path $repo '.claude/worktrees/live' }
$Tree = [IO.Path]::GetFullPath($Tree)
$script = $PSCommandPath # restarts and the shortcut run this same copy

if ($Shortcut -and $IsWindows) {
    $shell = New-Object -ComObject WScript.Shell
    foreach ($dir in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {
        $lnk = $shell.CreateShortcut((Join-Path $dir 'Darg (live).lnk'))
        $lnk.TargetPath = (Get-Command pwsh).Source
        $lnk.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$script`""
        $lnk.WorkingDirectory = $repo
        $lnk.IconLocation = (Join-Path $repo 'src/Darg.Desktop/darg.ico')
        $lnk.Description = 'Darg, live: takes updates from the repo without restarting'
        $lnk.Save()
        "shortcut: $dir\Darg (live).lnk"
    }
    return
}

if (-not $Here) {
    $running = @(Get-CimInstance Win32_Process -Filter "Name='Darg.Desktop.exe'" | Where-Object { $_.ExecutablePath })
    $live = @($running | Where-Object { $_.ExecutablePath.StartsWith($Tree + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) })
    if ($live) { Write-Error "A live copy is already running from $Tree (process $($live[0].ProcessId))."; exit 1 }
    $installed = @($running | Where-Object { Test-Path -LiteralPath (Join-Path (Split-Path $_.ExecutablePath) 'source.txt') })
    if ($installed -and -not $Force -and -not $env:DARG_HOME) {
        Write-Error "The installed Darg is running ($($installed[0].ExecutablePath)). A live copy on the same instance would fight it over agents and files: close it first, or pass -Force."
        exit 1
    }

    # the worktree: made the first time (detached, so it holds no branch others need), else brought up to the branch
    $exclude = Join-Path $common 'info/exclude'
    if (-not (Select-String -LiteralPath $exclude -SimpleMatch '.claude/worktrees/' -Quiet -ErrorAction SilentlyContinue)) { Add-Content -LiteralPath $exclude '.claude/worktrees/' }
    if (-not (Test-Path -LiteralPath (Join-Path $Tree '.git'))) {
        git -C $repo worktree add --detach $Tree $Branch
        if ($LASTEXITCODE -ne 0) { throw "couldn't make the worktree $Tree" }
    } elseif (-not (git -C $Tree status --porcelain --untracked-files=no)) {
        git -C $Tree checkout --detach $Branch -q
    } else {
        Write-Warning "$Tree has edits of its own, so it was left where it is."
    }

    # dotnet watch in a window of its own, which this same script runs (-Here)
    $argv = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$script`"", '-Here', '-Branch', $Branch, '-Tree', "`"$Tree`"")
    if ($IsWindows) { Start-Process pwsh -ArgumentList $argv -WindowStyle Normal } else { Start-Process pwsh -ArgumentList $argv }
    "Darg (live) is starting from $Tree, following $Branch."
    return
}

# -Here: run dotnet watch, show what it says, and stop it when Darg is closed rather than restarted by watch
$Host.UI.RawUI.WindowTitle = "Darg (live): dotnet watch, following $Branch"
if (-not $env:NUGET_PACKAGES -and (Test-Path 'D:\nuget\packages')) { $env:NUGET_PACKAGES = 'D:\nuget\packages' }
$env:DOTNET_WATCH_SUPPRESS_EMOJIS = '1'
$env:DARG_LIVE_TREE = $Tree; $env:DARG_LIVE_BRANCH = $Branch; $env:DARG_LIVE_SCRIPT = $script; $env:DARG_LIVE_HOST = $PID
"Darg (live) from $Tree, following $Branch. Closing Darg closes this window."

$psi = [Diagnostics.ProcessStartInfo]::new('dotnet')
foreach ($a in 'watch', '--non-interactive', '--project', (Join-Path $Tree 'src/Darg.Desktop')) { $psi.ArgumentList.Add($a) }
$psi.WorkingDirectory = $Tree
$psi.UseShellExecute = $false; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
$watch = [Diagnostics.Process]::new(); $watch.StartInfo = $psi
$lines = [Collections.Concurrent.BlockingCollection[string]]::new()
$out = Register-ObjectEvent $watch OutputDataReceived -MessageData $lines -Action { if ($null -ne $EventArgs.Data) { $Event.MessageData.Add($EventArgs.Data) } }
$err = Register-ObjectEvent $watch ErrorDataReceived -MessageData $lines -Action { if ($null -ne $EventArgs.Data) { $Event.MessageData.Add($EventArgs.Data) } }
[void]$watch.Start(); $watch.BeginOutputReadLine(); $watch.BeginErrorReadLine()

$line = $null
while (-not $watch.HasExited) {
    if (-not $lines.TryTake([ref]$line, 500)) { continue }
    $line
    # watch says this when the app exits on its own (closed from its window, or by ⟳ Restart); when watch
    # itself restarts it for an edit it couldn't apply, it says "Restart is needed" and builds again instead
    if ($line -match 'Waiting for a file to change before restarting') { break }
}
if (-not $watch.HasExited) {
    if ($IsWindows) { taskkill /T /F /PID $watch.Id | Out-Null } else { $watch.Kill($true) }
}
Unregister-Event -SourceIdentifier $out.Name; Unregister-Event -SourceIdentifier $err.Name
