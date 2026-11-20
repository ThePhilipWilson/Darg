# Darg's whole test suite (WOR-0127): builds everything (so a XAML or app compile error fails too), then runs
#   unit tests         tests/Darg.Core.Tests      Core, no UI
#   integration tests  tests/Darg.Desktop.Tests   the real app headless on a throwaway instance, fake claude
# The integration tests leave a folder per test in .shots/integration/<test>: its instance, board files,
# fake-claude.jsonl (what each agent was started with and sent) and PNGs of what the window showed.
# Exits non-zero if anything fails, and if no test ran at all (a -Filter that matches nothing is a failure).
# One run per repo folder at a time: a second waits for the first (builds and .shots would collide), saying whose.
# Needs python on PATH (DARG_TEST_PYTHON to name another) for the fake claude.
#   pwsh -File tools/test.ps1                     everything
#   pwsh -File tools/test.ps1 -Unit               Core only (seconds)
#   pwsh -File tools/test.ps1 -Integration -Filter Send_back
param([switch]$Unit, [switch]$Integration, [string]$Filter, [int]$WaitMinutes = 20)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/..").Path
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = 'D:/nuget/packages' }
$both = -not $Unit -and -not $Integration

# Whatever started this (the live copy's DARG_LIVE_*, a debug DARG_SECTION, DARG_HOME) stays out of the build and
# the tests: DARG_LIVE_TREE alone pulls HotAvalonia into the build. Kept: DARG_TEST_* and DARG_LIVE (Core's live test).
Get-ChildItem env: | Where-Object { $_.Name -match '^(DARG|WORKBENCH)_' -and $_.Name -notmatch '^DARG_TEST_' -and $_.Name -ne 'DARG_LIVE' } |
    ForEach-Object { Remove-Item -LiteralPath "env:$($_.Name)" }

# One run per folder: two at once fight over bin/ (MSB4018, a deps.json held by the other) and over .shots.
$shots = New-Item -ItemType Directory -Force (Join-Path $repo '.shots')
$lockPath = Join-Path $shots 'test.lock'
$lock = $null; $said = $false; $until = (Get-Date).AddMinutes($WaitMinutes)
while (-not $lock) {
    try {
        $lock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'Read')
        $lock.SetLength(0)
        $me = [Text.Encoding]::UTF8.GetBytes("pid $PID, started $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
        $lock.Write($me, 0, $me.Length); $lock.Flush()
    } catch [IO.IOException] {
        $who = try { $r = [IO.File]::Open($lockPath, 'Open', 'Read', 'ReadWrite'); try { (New-Object IO.StreamReader($r)).ReadToEnd() } finally { $r.Dispose() } } catch { 'unknown' }
        if ((Get-Date) -gt $until) { Write-Host "Another test run has had this folder for $WaitMinutes min ($who). Giving up." -ForegroundColor Red; exit 1 }
        if (-not $said) { Write-Host "Another test run is going in this folder ($who); waiting for it to finish." -ForegroundColor Yellow; $said = $true }
        Start-Sleep -Seconds 2
    }
}

# And a few runs on the whole machine at a time (WOR-0170): each worktree is its own folder, so five agents ran five
# builds and suites at once (a suite alone peaks near 1.6 GB), beside two emulators, until Windows ran out of memory
# and the PC bluescreened. A run takes one of DARG_TEST_SLOTS slots (default 2), waiting for one, and then waits while
# the machine's committed memory is above DARG_TEST_COMMIT_PCT (default 85) percent of what it can commit.
$slotDir = New-Item -ItemType Directory -Force (Join-Path $env:LOCALAPPDATA 'Darg\test-slots')
$slots = if ($env:DARG_TEST_SLOTS -as [int]) { [math]::Max(1, [int]$env:DARG_TEST_SLOTS) } else { 2 }
$slot = $null; $said = $false
while (-not $slot) {
    $busy = @()
    for ($i = 1; $i -le $slots -and -not $slot; $i++) {
        $path = Join-Path $slotDir "slot$i.lock"
        try {
            $slot = [IO.File]::Open($path, 'OpenOrCreate', 'ReadWrite', 'Read')
            $slot.SetLength(0)
            $me = [Text.Encoding]::UTF8.GetBytes("$repo, pid $PID, started $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
            $slot.Write($me, 0, $me.Length); $slot.Flush()
        } catch [IO.IOException] {
            $busy += try { $r = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite'); try { (New-Object IO.StreamReader($r)).ReadToEnd() } finally { $r.Dispose() } } catch { 'unknown' }
        }
    }
    if ($slot) { break }
    if ((Get-Date) -gt $until) { $lock.Dispose(); Write-Host "All $slots test slots on this machine have been busy for $WaitMinutes min ($($busy -join '; ')). Giving up." -ForegroundColor Red; exit 1 }
    if (-not $said) { Write-Host "All $slots test slots on this machine are in use ($($busy -join '; ')); waiting for one to come free." -ForegroundColor Yellow; $said = $true }
    Start-Sleep -Seconds 3
}
$pct = if ($env:DARG_TEST_COMMIT_PCT -as [int]) { [int]$env:DARG_TEST_COMMIT_PCT } else { 85 }
$said = $false
while ($true) {
    $os = Get-CimInstance Win32_OperatingSystem
    $used = 100 * ($os.TotalVirtualMemorySize - $os.FreeVirtualMemory) / $os.TotalVirtualMemorySize
    if ($used -le $pct) { break }
    if ((Get-Date) -gt $until) { $slot.Dispose(); $lock.Dispose(); Write-Host ("The machine's memory has been {0:N0}% committed (over {1}%) for too long. Giving up: see what's using it (an emulator, other agents' builds)." -f $used, $pct) -ForegroundColor Red; exit 1 }
    if (-not $said) { Write-Host ("The machine's memory is {0:N0}% committed (over {1}%); waiting for some to be freed." -f $used, $pct) -ForegroundColor Yellow; $said = $true }
    Start-Sleep -Seconds 5
}

try {
    dotnet build "$repo/Darg.sln" -v quiet -nologo
    if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED" -ForegroundColor Red; exit 1 }

    $results = Join-Path $shots 'test-results'
    if (Test-Path -LiteralPath $results) { Remove-Item -LiteralPath $results -Recurse -Force }
    $failed = @(); $ran = @{}
    function Invoke-Suite([string]$name, [string]$project) {
        Write-Host "`n== $name ==" -ForegroundColor Cyan
        $a = @('test', "$repo/tests/$project", '--no-build', '-nologo', '--results-directory', $results, '--logger', "trx;LogFileName=$name.trx")
        if ($Filter) { $a += @('--filter', "FullyQualifiedName~$Filter") }
        & dotnet @a
        if ($LASTEXITCODE -ne 0) { $script:failed += $name }
        # how many tests ran, from the trx (none is written when the filter matched nothing)
        $trx = Join-Path $results "$name.trx"
        $script:ran[$name] = if (Test-Path -LiteralPath $trx) { [int]([xml](Get-Content -LiteralPath $trx -Raw)).TestRun.ResultSummary.Counters.total } else { 0 }
    }
    if ($both -or $Unit) { Invoke-Suite 'unit' 'Darg.Core.Tests' }
    if ($both -or $Integration) {
        Invoke-Suite 'integration' 'Darg.Desktop.Tests'
        Write-Host "Integration folders and pictures: $repo/.shots/integration"
    }

    # No tests run is not a pass: a mistyped -Filter, or a suite that found nothing to run.
    $total = ($ran.Values | Measure-Object -Sum).Sum
    if ($Filter -and $total -eq 0) {
        Write-Host "`nNo test matches -Filter '$Filter' (it matches against Namespace.Class.Method, e.g. Send_back)." -ForegroundColor Red
        exit 1
    }
    if (-not $Filter) { foreach ($k in $ran.Keys) { if ($ran[$k] -eq 0 -and $failed -notcontains $k) { $failed += "$k (no tests ran)" } } }

    $counts = ($ran.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Name) $($_.Value)" }) -join ', '
    if ($failed.Count) { Write-Host "`nFAILED: $($failed -join ', ') (tests run: $counts)" -ForegroundColor Red; exit 1 }
    Write-Host "`nAll passed ($counts tests)." -ForegroundColor Green
}
finally { $slot.Dispose(); $lock.Dispose() }
