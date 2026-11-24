# Summarise an instance's stalls.log and perf.log (WOR-0161): where the UI thread was held, by what, and how memory
# and CPU moved. Read-only; it never starts the app.
#   pwsh -File tools/perf/analyze.ps1                      # the real instance (%APPDATA%/Darg/home, or Workbench/home before it moved)
#   pwsh -File tools/perf/analyze.ps1 -InstanceHome .shots/perf-home -Since '2026-11-20 05:00'
# To collect a trace first: run the app (DARG_PERF_SECONDS=10 for a perf.log line every 10 s) through a stretch of
# normal use, or `pwsh -File tools/perf/load.ps1 -Cards 5 -Touch -StallMs 100` for a loaded one, then run this.
param([string]$InstanceHome = '', [string]$Since = '', [int]$Top = 12)
$ErrorActionPreference = 'Stop'
if (-not $InstanceHome) { # the instance that has data: Darg's once it has moved there (WOR-0196), else the old name's
    $InstanceHome = @("$env:APPDATA/Darg/home", "$env:APPDATA/Workbench/home") | Where-Object { Test-Path -LiteralPath "$_/instance.json" } | Select-Object -First 1
    if (-not $InstanceHome) { $InstanceHome = "$env:APPDATA/Darg/home" }
}
$stalls = Join-Path $InstanceHome 'stalls.log'
$perf = Join-Path $InstanceHome 'perf.log'
function Take([string]$p) { if (Test-Path -LiteralPath $p) { @(Get-Content -LiteralPath $p | Where-Object { $_ -match '^\d{4}-' -and (-not $Since -or $_.Substring(0, 19) -ge $Since) }) } else { @() } }

$s = Take $stalls
$rows = foreach ($l in $s) {
    if ($l -notmatch '^(\S+ \S+) stall (\d+) ms(?:: (.*?))? · (\d+) ms in no section(?: · GC paused (\d+) ms)?') { continue }
    $at = $Matches[1]; $ms = [int]$Matches[2]; $list = $Matches[3]; $none = [int]$Matches[4]; $gcms = [int]$Matches[5]
    $secs = @{}
    if ($list) {
        # "A > B 12 (x3), A 40" : the top-level ones (no ' > ') add up; the first segment names the cause
        foreach ($part in ($list -split ', (?=[A-Za-z])')) {
            if ($part -match '^(.*?) (\d+)(?: \(x(\d+)\))?$' -and $Matches[1] -notmatch ' > ') { $secs[$Matches[1]] = [int]$Matches[2] }
        }
    }
    [pscustomobject]@{ At = $at; Ms = $ms; Secs = $secs; None = $none; Gc = $gcms }
}
$rows = @($rows)
"== stalls.log: $($rows.Count) stalls$(if ($Since) { " since $Since" }) in $stalls"
if ($rows.Count) {
    $tot = ($rows | Measure-Object Ms -Sum).Sum
    "held the UI thread $([math]::Round($tot / 1000, 1)) s in total; median $((($rows.Ms | Sort-Object)[[int]($rows.Count / 2)])) ms, worst $(($rows.Ms | Measure-Object -Maximum).Maximum) ms"
    "$(@($rows | Where-Object Ms -ge 1000).Count) over 1 s, $(@($rows | Where-Object Ms -ge 500).Count) over 500 ms"
    $named = ($rows | ForEach-Object { ($_.Secs.Values | Measure-Object -Sum).Sum } | Measure-Object -Sum).Sum
    $none = ($rows | Measure-Object None -Sum).Sum
    $gc = ($rows | Measure-Object Gc -Sum).Sum
    "of that: $([math]::Round($named / 1000, 1)) s in named sections, $([math]::Round($none / 1000, 1)) s in no section (layout, rendering, waits), GC paused $([math]::Round($gc / 1000, 1)) s (overlaps both)"
    ""
    "-- by section (top-level ones, ms added up over every stall):"
    $by = @{}
    foreach ($r in $rows) { foreach ($k in $r.Secs.Keys) { if (-not $by[$k]) { $by[$k] = [pscustomobject]@{ Name = $k; Ms = 0; Stalls = 0; Worst = 0 } }; $by[$k].Ms += $r.Secs[$k]; $by[$k].Stalls++; $by[$k].Worst = [math]::Max($by[$k].Worst, $r.Secs[$k]) } }
    $by.Values | Sort-Object Ms -Descending | Select-Object -First $Top | Format-Table Name, Ms, Stalls, Worst -AutoSize | Out-String -Width 200
    "-- by hour (stalls, ms held):"
    $rows | Group-Object { $_.At.Substring(0, 13) } | ForEach-Object { '{0}:00  {1,4} stalls  {2,7} ms' -f $_.Name, $_.Count, ($_.Group | Measure-Object Ms -Sum).Sum }
    ""
    "-- the $Top longest:"
    $rows | Sort-Object Ms -Descending | Select-Object -First $Top | ForEach-Object { '{0}  {1,6} ms  {2}' -f $_.At, $_.Ms, ((($_.Secs.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 2 | ForEach-Object { "$($_.Key) $($_.Value)" }) -join ', ') + $(if (-not $_.Secs.Count) { 'no section' })) }
}
$p = Take $perf
$perfRows = @()
""
"== perf.log: $($p.Count) lines in $perf"
foreach ($l in $p) {
    if ($l -match '^(\S+ \S+) perf (\d+) s · CPU ([\d.]+) cores · (\d+) MB working set, (\d+) MB GC heap, (\d+) gen2 · GC paused (\d+) ms · pool (\d+) threads, (\d+) queued · (.*)$') {
        $cpu = [double]$Matches[3]; $ws = [int]$Matches[4]; $heap = [int]$Matches[5]
        $o = [pscustomobject]@{ At = $Matches[1]; Cpu = $cpu; Ws = $ws; Heap = $heap; Rest = $Matches[10] }
        $perfRows += , $o
    }
}
if ($perfRows) {
    "CPU cores: avg $([math]::Round(($perfRows.Cpu | Measure-Object -Average).Average, 2)), max $(($perfRows.Cpu | Measure-Object -Maximum).Maximum); working set $($perfRows[0].Ws) -> $($perfRows[-1].Ws) MB (max $(($perfRows.Ws | Measure-Object -Maximum).Maximum)); GC heap $($perfRows[0].Heap) -> $($perfRows[-1].Heap) MB"
    "-- busiest minutes (CPU):"
    $perfRows | Sort-Object Cpu -Descending | Select-Object -First 5 | ForEach-Object { '{0}  CPU {1:0.00}  {2} MB  {3}' -f $_.At, $_.Cpu, $_.Ws, $_.Rest }
    "-- file watchers and API calls, events added up over the whole log:"
    $ev = @{}
    foreach ($r in $perfRows) { foreach ($m in [regex]::Matches($r.Rest, '(?<n>(?:Watch|Api)[\w. ]*?) \(x(?<c>\d+)\)')) { $ev[$m.Groups['n'].Value.Trim()] += [int]$m.Groups['c'].Value } }
    $ev.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First $Top | ForEach-Object { '{0,-28} x{1}' -f $_.Key, $_.Value }
}
