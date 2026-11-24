# Rename this repo's folder (D:\GitHub\ThePhilipWilson\Workbench -> ...\Darg, WOR-0196) and everything that points at it.
# Run it from a terminal with Darg closed, from outside the folder. Without -Apply it only says what it would do.
#   pwsh -File D:\GitHub\ThePhilipWilson\Workbench\tools\rename-folder.ps1           # dry run
#   cd D:\ ; pwsh -File D:\GitHub\ThePhilipWilson\Workbench\tools\rename-folder.ps1 -Apply
# What it changes:
#   - the folder itself (one rename; nothing in it is copied)
#   - git's links to every worktree (git worktree repair), those inside the folder and outside it
#   - the paths in the Darg instance's JSON files: instance.json (the project's repos, boards, docs, commit rules)
#     and the saved agents (their working folders and --add-dir), so they resume where they were
#   - Claude Code's per-folder data in ~/.claude/projects (sessions, so saved agents can --resume, and the
#     project's memory), renamed to the new folder's name, and the folder's entry in ~/.claude.json
#   - the installed copy's source.txt (what Update builds) and any Start menu or desktop shortcut naming the folder
# Run again after a failure: each step skips what is already done. A copy of each changed file is kept as <file>.pre-rename.
param(
    [string]$From = (Split-Path -Parent $PSScriptRoot),
    [string]$To = '',
    [string]$InstanceHome = '',
    [string]$ClaudeHome = (Join-Path $HOME '.claude'),
    [string]$ClaudeJson = (Join-Path $HOME '.claude.json'),
    [string]$Install = $(if ($env:DARG_INSTALL) { $env:DARG_INSTALL } else { Join-Path $env:LOCALAPPDATA 'Programs\Darg' }),
    [string[]]$ShortcutDirs = @([Environment]::GetFolderPath('Desktop'), (Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs')),
    [switch]$Apply,
    [switch]$SkipProcessCheck # for the test, which runs while Darg may be open on other work
)
$ErrorActionPreference = 'Stop'
$From = [IO.Path]::GetFullPath($From).TrimEnd('\', '/')
if (-not $To) { $To = Join-Path (Split-Path -Parent $From) 'Darg' }
$To = [IO.Path]::GetFullPath($To).TrimEnd('\', '/')
if (-not $InstanceHome) {
    $InstanceHome = @((Join-Path $env:APPDATA 'Darg\home'), (Join-Path $env:APPDATA 'Workbench\home')) |
        Where-Object { Test-Path -LiteralPath (Join-Path $_ 'instance.json') } | Select-Object -First 1
}
$mode = if ($Apply) { '' } else { '(dry run) ' }
function Say([string]$what) { Write-Host "$mode$what" }
function Stop-With([string]$why) { Write-Host "Stopped: $why" -ForegroundColor Red; exit 1 }

# Claude Code names a folder's data after its path with every character but letters and digits made '-'
function Mangle([string]$path) { $path -replace '[^A-Za-z0-9]', '-' }

# The old path in every way a file writes it (forward slashes, backslashes, JSON's doubled backslashes), and only
# as a whole folder name: D:\x\Workbench-wor-0072 beside it is another folder and stays.
$forms = @($From, ($From -replace '\\', '/'), ($From -replace '\\', '\\')) | Select-Object -Unique
$news = @{}
foreach ($f in $forms) { $news[$f] = if ($f.Contains('/')) { $To -replace '\\', '/' } elseif ($f.Contains('\\')) { $To -replace '\\', '\\' } else { $To } }
function Repath([string]$text) {
    foreach ($f in $forms) { $text = [regex]::Replace($text, [regex]::Escape($f) + '(?![\w-])', { param($m) $news[$f] }.GetNewClosure(), 'IgnoreCase') }
    $text
}

function Rewrite([string]$file) {
    $text = [IO.File]::ReadAllText($file)
    $new = Repath $text
    if ($new -ceq $text) { return $false }
    Say "rewrite paths in $file"
    if ($Apply) {
        if (-not (Test-Path -LiteralPath "$file.pre-rename")) { Copy-Item -LiteralPath $file -Destination "$file.pre-rename" }
        [IO.File]::WriteAllText($file, $new, [Text.UTF8Encoding]::new($false))
    }
    $true
}

# --- before anything moves
$moved = -not (Test-Path -LiteralPath $From) -and (Test-Path -LiteralPath $To)
if (-not $moved) {
    if (-not (Test-Path -LiteralPath (Join-Path $From '.git'))) { Stop-With "$From isn't a git repo." }
    if (Test-Path -LiteralPath $To) { Stop-With "$To is there already." }
    $here = (Get-Location).ProviderPath.TrimEnd('\')
    if ($here -eq $From -or $here.StartsWith("$From\", [StringComparison]::OrdinalIgnoreCase)) { Stop-With "this terminal is inside $From; cd somewhere else first." }
}
if (-not $SkipProcessCheck) {
    $mine = @($PID); $p = $PID
    while ($p -and ($parent = (Get-CimInstance Win32_Process -Filter "ProcessId=$p" -ErrorAction SilentlyContinue).ParentProcessId) -and $parent -notin $mine) { $mine += $parent; $p = $parent }
    $darg = @(Get-Process -Name 'Darg.Desktop' -ErrorAction SilentlyContinue)
    if ($darg) { Stop-With "Darg is running ($($darg.Count) cop$(if ($darg.Count -eq 1) { 'y' } else { 'ies' })); its agents work in the folder. Close it first." }
    $using = @(Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -notin $mine -and ("$($_.ExecutablePath) $($_.CommandLine)" -match [regex]::Escape($From) + '(?![\w-])') })
    if ($using) { Stop-With ("these run from the folder; end them first:`n" + (($using | ForEach-Object { "  $($_.ProcessId) $($_.Name)" }) -join "`n")) }
}

# --- the folder, and git's worktree links both ways
if ($moved) { Say "$From is already $To" }
else {
    $worktrees = @(git -C $From worktree list --porcelain | Where-Object { $_ -like 'worktree *' } | ForEach-Object { $_.Substring(9) -replace '/', '\' } | Select-Object -Skip 1)
    Say "move $From -> $To"
    if ($Apply) {
        try { [IO.Directory]::Move($From, $To) }
        catch { Stop-With "couldn't move the folder ($($_.Exception.Message)). Something has it open: a terminal, editor or Claude session started in it, or Explorer. Close it and run this again." }
    }
}
$root = if ($Apply -or $moved) { $To } else { $From }
$linked = @(git -C $root worktree list --porcelain | Where-Object { $_ -like 'worktree *' } | ForEach-Object { $_.Substring(9) -replace '/', '\' } | Select-Object -Skip 1)
if (-not $moved -and $Apply) { $linked = $worktrees }
$repair = @($linked | ForEach-Object { if ($Apply -or $moved) { Repath $_ } else { $_ } } | Where-Object { Test-Path -LiteralPath $_ })
Say "repair git's links to $($repair.Count) worktree(s)"
if ($Apply -and $repair) {
    git -C $To worktree repair @repair 2>&1 | ForEach-Object { "  $_" }
    if ($LASTEXITCODE) { Write-Host "  git worktree repair said $LASTEXITCODE; carry on and check 'git worktree list'" -ForegroundColor Yellow }
}

# --- files that name the folder
if ($InstanceHome -and (Test-Path -LiteralPath $InstanceHome)) {
    $n = 0
    foreach ($f in Get-ChildItem -LiteralPath $InstanceHome -Recurse -File -Filter *.json) { if (Rewrite $f.FullName) { $n++ } }
    Say "the instance ($InstanceHome): $n file(s) name the folder"
} else { Say "no Darg instance found; nothing of its to change" }
if (Test-Path -LiteralPath $ClaudeJson) { [void](Rewrite $ClaudeJson) }
$source = Join-Path $Install 'source.txt'
if (Test-Path -LiteralPath $source) { [void](Rewrite $source) }
$shell = $null
foreach ($dir in $ShortcutDirs | Where-Object { $_ -and (Test-Path -LiteralPath $_) }) {
    foreach ($lnk in Get-ChildItem -LiteralPath $dir -Recurse -Filter *.lnk -ErrorAction SilentlyContinue) {
        if (-not $shell) { $shell = New-Object -ComObject WScript.Shell }
        $s = $shell.CreateShortcut($lnk.FullName)
        $was = "$($s.TargetPath)|$($s.Arguments)|$($s.WorkingDirectory)|$($s.IconLocation)"
        if ((Repath $was) -ceq $was) { continue }
        Say "repoint shortcut $($lnk.FullName)"
        if ($Apply) {
            $s.TargetPath = Repath $s.TargetPath; $s.Arguments = Repath $s.Arguments
            $s.WorkingDirectory = Repath $s.WorkingDirectory; $s.IconLocation = Repath $s.IconLocation
            $s.Save()
        }
    }
}

# --- Claude Code's data for the folder and everything under it (sessions, memory)
$projects = Join-Path $ClaudeHome 'projects'
if (Test-Path -LiteralPath $projects) {
    $children = @(Get-ChildItem -LiteralPath $root -Force -Name)
    $old = Mangle $From; $new = Mangle $To
    # the folder's own name, or a path under it: '-' then a child's mangled name. A sibling such as
    # Workbench-wor-0072 mangles the same way, so only names under a real child of the folder count.
    $under = @($children | ForEach-Object { "$old-$(Mangle $_)" })
    foreach ($d in Get-ChildItem -LiteralPath $projects -Directory) {
        $name = $d.Name
        $ours = $name -ceq $old -or @($under | Where-Object { $name -ceq $_ -or $name.StartsWith("$_-", [StringComparison]::Ordinal) }).Count
        if (-not $ours) { continue }
        $target = Join-Path $projects ($new + $name.Substring($old.Length))
        if (Test-Path -LiteralPath $target) { Write-Host "  $target is there already; left $name as it is" -ForegroundColor Yellow; continue }
        Say "rename $name -> $(Split-Path -Leaf $target)"
        if ($Apply) { Rename-Item -LiteralPath $d.FullName -NewName (Split-Path -Leaf $target) }
    }
}

if ($Apply) {
    Write-Host "Done. $To is the repo now. The first build restores again (obj/ holds the old paths)."
    Write-Host "Not changed: the project's id in Darg stays 'workbench' (its data is kept under that id)."
} else { Write-Host "Nothing changed. Run with -Apply to do it." }
