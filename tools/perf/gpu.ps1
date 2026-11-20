# Memory soak for Darg (WOR-0170): does Darg grow in GPU or CPU memory while it is used, and do repeated launches
# leave anything behind? Launches the debug build on a throwaway instance (.shots/gpu-home: a copy of a board, this
# repo as the project's repo), flips through the sections and opens cards through the API, and samples the process
# (and its WebView2 children): private bytes, GPU dedicated and shared memory (the "GPU Process Memory" counters,
# what dxgmms2 maps), plus the machine's commit charge. -Launches n starts and kills it n times; -Minutes keeps one
# copy cycling that long. Prints a row per sample and a verdict line comparing the last quarter with the first.
# Build first: dotnet build.
#   pwsh -File tools/perf/gpu.ps1 -Minutes 10
#   pwsh -File tools/perf/gpu.ps1 -Launches 20 -Minutes 0.5
param([string]$Board = 'D:/GitHub/ThePhilipWilson/Workbench/.darg/tasks',
      [int]$Launches = 1, [double]$Minutes = 5, [int]$EverySeconds = 15, [string]$Csv)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$wt = (Resolve-Path "$here/../..").Path
$exe = "$wt/src/Darg.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Darg.Desktop.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "No debug build at ${exe}: run dotnet build first." }
$home2 = "$wt/.shots/gpu-home"
New-Item -ItemType Directory -Force $home2 | Out-Null
$r = $home2.Replace('\', '/'); $repo = $wt.Replace('\', '/')
@"
{ "name": "gpu", "projects": [ { "id": "gpu", "name": "Gpu", "color": "#e07ab8",
  "boards": "$r/tasks", "repos": [ "$repo" ], "features": [] } ] }
"@ | Set-Content -LiteralPath "$home2/instance.json" -Encoding utf8
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue "$home2/tasks", "$home2/crash.log"
Copy-Item -Recurse -Force -LiteralPath $Board -Destination "$home2/tasks"
$env:DARG_HOME = $home2
$env:DARG_SECTION = 'Home'
$env:CLAUDE_PATH = 'false-claude-not-used'

function Tree([int]$root) {
    # the process and everything it started (WebView2's msedgewebview2, shells), by parent id
    $all = @(Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId)
    $ids = [System.Collections.Generic.List[int]]::new(); $ids.Add($root)
    for ($i = 0; $i -lt $ids.Count; $i++) { foreach ($p in $all) { if ($p.ParentProcessId -eq $ids[$i] -and -not $ids.Contains([int]$p.ProcessId)) { $ids.Add([int]$p.ProcessId) } } }
    $ids
}
function Gpu([int[]]$ids) {
    # GPU Process Memory instances are named pid_<pid>_luid_..._phys_n; sum every adapter's
    $ded = 0L; $sh = 0L
    try {
        $s = (Get-Counter '\GPU Process Memory(*)\Dedicated Usage', '\GPU Process Memory(*)\Shared Usage' -ErrorAction SilentlyContinue).CounterSamples
        foreach ($c in $s) {
            if ($c.InstanceName -match '^pid_(\d+)_' -and $ids -contains [int]$Matches[1]) {
                if ($c.Path -match 'dedicated usage$') { $ded += [long]$c.CookedValue } else { $sh += [long]$c.CookedValue }
            }
        }
    } catch { }
    @{ Ded = $ded; Shared = $sh }
}
function Commit() { $os = Get-CimInstance Win32_OperatingSystem; ($os.TotalVirtualMemorySize - $os.FreeVirtualMemory) * 1KB }
function Port([int]$id) {
    foreach ($line in (netsh http show servicestate view=requestq)) {
        if ($line -match '^Request queue name:') { $mine = $false; continue }
        if ($line -match '^\s+ID: (\d+),') { $mine = [int]$Matches[1] -eq $id; continue }
        if ($mine -and $line -match 'LOCALHOST:(\d+)/') { return [int]$Matches[1] }
    }
}
$mb = { param($b) [math]::Round($b / 1MB, 1) }
$rows = [System.Collections.Generic.List[object]]::new()
$sections = 'Home', 'Board', 'Notes', 'Docs', 'Agents', 'Reports', 'Artifacts', 'Mistakes', 'Code', 'Terminal'
$cards = @(Get-ChildItem "$home2/tasks" -Recurse -Filter *.md | Where-Object { $_.Directory.Name -eq 'items' } |
    ForEach-Object { ($_.BaseName -split ' - ')[0] } | Sort-Object -Descending | Select-Object -First 12)
$baseCommit = Commit
"commit before: $(& $mb $baseCommit) MB"

for ($n = 1; $n -le $Launches; $n++) {
    $p = Start-Process -FilePath $exe -WindowStyle Normal -PassThru
    try {
        $port = $null
        for ($i = 0; $i -lt 120 -and -not $port; $i++) { Start-Sleep -Milliseconds 500; if ($p.HasExited) { throw "Darg exited at start (code $($p.ExitCode)); see $home2/crash.log" }; $port = Port $p.Id }
        if (-not $port) { throw 'no API port after 60 s' }
        $token = (Get-Content -Raw -LiteralPath "$home2/artifact-token").Trim()
        $api = "http://localhost:$port/api/$token"
        $end = (Get-Date).AddMinutes($Minutes); $next = Get-Date; $k = 0
        while ((Get-Date) -lt $end) {
            if ($p.HasExited) { throw "Darg exited mid-run (code $($p.ExitCode)); see $home2/crash.log" }
            $body = if ($k % 3 -eq 2 -and $cards.Count) { @{ project = 'gpu'; card = $cards[$k % $cards.Count] } } else { @{ project = 'gpu'; section = $sections[$k % $sections.Count] } }
            try { Invoke-RestMethod -Method Post -Uri "$api/show" -ContentType 'application/json' -Body ($body | ConvertTo-Json) | Out-Null } catch { }
            $k++
            Start-Sleep -Milliseconds 700
            if ((Get-Date) -ge $next) {
                $ids = Tree $p.Id; $g = Gpu $ids; $p.Refresh()
                $priv = ($ids | ForEach-Object { (Get-Process -Id $_ -ErrorAction SilentlyContinue).PrivateMemorySize64 } | Measure-Object -Sum).Sum
                $row = [pscustomobject]@{ Launch = $n; At = (Get-Date -Format HH:mm:ss); Shows = $k; Procs = $ids.Count
                    DargMB = & $mb $p.PrivateMemorySize64; TreeMB = & $mb $priv; GpuDedMB = & $mb $g.Ded; GpuSharedMB = & $mb $g.Shared; CommitMB = & $mb (Commit) }
                $rows.Add($row); $row | Format-Table -HideTableHeaders:($rows.Count -gt 1) | Out-String -Stream | Where-Object { $_ }
                $next = (Get-Date).AddSeconds($EverySeconds)
            }
        }
    } finally {
        if (-not $p.HasExited) { & taskkill /PID $p.Id /T /F | Out-Null }
        $p.WaitForExit(15000) | Out-Null
    }
}
Start-Sleep -Seconds 3
$left = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Resolve-Path $exe).Path })
$afterCommit = Commit
if ($Csv) { $rows | Export-Csv -NoTypeInformation -LiteralPath $Csv }
"commit after: $(& $mb $afterCommit) MB (before $(& $mb $baseCommit)); Darg copies left running from this build: $($left.Count)"
if (Test-Path -LiteralPath "$home2/crash.log") { "crash.log:"; Get-Content -LiteralPath "$home2/crash.log" | Select-Object -Last 20 }
# the verdict: within one launch, the last quarter of samples against the first (after the first sample, startup)
$one = @($rows | Where-Object Launch -eq $Launches | Select-Object -Skip 1)
if ($one.Count -ge 4) {
    $q = [math]::Max(1, [int]($one.Count / 4))
    foreach ($m in 'DargMB', 'GpuDedMB', 'GpuSharedMB') {
        $a = ($one | Select-Object -First $q | Measure-Object $m -Average).Average; $b = ($one | Select-Object -Last $q | Measure-Object $m -Average).Average
        "{0}: first quarter {1:N1}, last quarter {2:N1} ({3:+0.0;-0.0} MB)" -f $m, $a, $b, ($b - $a)
    }
}
if ($Launches -gt 1) {
    $firsts = @($rows | Group-Object Launch | ForEach-Object { $_.Group | Select-Object -Last 1 })
    "per launch (last sample): " + (($firsts | ForEach-Object { "#$($_.Launch) gpu $($_.GpuDedMB)+$($_.GpuSharedMB) priv $($_.DargMB)" }) -join '; ')
}
