# Load-test Darg (WOR-0076): launch the debug build on a throwaway instance (.shots/perf-home, a copy of a board),
# start agents on N cards at once through the API's work-on (the same path as ▶ Work on N), and print stalls.log:
# every gap over -StallMs in the UI thread's heartbeat, with what ran in it. The agents are fake_claude.py, which
# streams tool calls at -Rate a second for -Seconds (-Touch: rewriting its card every FAKE_TOUCH_EVERY s); -Real
# uses the real claude (it costs sessions). -Switch then flips sections and agents, -Scroll scrolls the open
# transcript through UI Automation. Toasts show for real when the fake turns end. Build first: dotnet build.
#   pwsh -File tools/perf/load.ps1 -Cards 5 -Section Board -Touch -StallMs 100
param([string]$Board = "$env:APPDATA/Workbench/home/projects/workbench/tasks",
      [int]$Cards = 5, [int]$Wait = 40, [string]$Section = 'Board', [switch]$Real, [switch]$Keep,
      [double]$Rate = 5, [int]$Seconds = 30, [int]$Result = 4000, [switch]$Touch, [switch]$Switch, [switch]$Scroll, [int]$StallMs = 200)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$wt = (Resolve-Path "$here/../..").Path
$exe = "$wt/src/Darg.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Darg.Desktop.exe"
$home2 = "$wt/.shots/perf-home"
New-Item -ItemType Directory -Force "$home2/proj/repo" | Out-Null
if (-not (Test-Path "$home2/proj/repo/.git")) { git -C "$home2/proj/repo" init -q }
$r = $home2.Replace('\', '/')
@"
{ "name": "perf", "projects": [ { "id": "perf", "name": "Perf", "color": "#e07ab8",
  "boards": "$r/proj/tasks", "repos": [ "$r/proj/repo" ], "features": [] } ] }
"@ | Set-Content -LiteralPath "$home2/instance.json" -Encoding utf8
Set-Content -LiteralPath "$home2/claude.cmd" -Value "@python `"$here/fake_claude.py`" %*" -Encoding ascii
Remove-Item -Force -ErrorAction SilentlyContinue "$home2/stalls.log", "$home2/crash.log"
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue "$home2/agents", "$home2/proj/tasks"
# a fresh copy of the board each run, so the same cards are free to start
Copy-Item -Recurse -Force -LiteralPath $Board -Destination "$home2/proj/tasks"
$main = (Get-ChildItem "$home2/proj/tasks" -Directory | Select-Object -First 1).FullName
$env:DARG_HOME = $home2
$env:DARG_SECTION = $Section
$env:DARG_STALL_MS = "$StallMs"
if (-not $Real) { $env:CLAUDE_PATH = "$home2/claude.cmd" } else { Remove-Item Env:\CLAUDE_PATH -ErrorAction SilentlyContinue }
$env:FAKE_RATE = "$Rate"; $env:FAKE_SECONDS = "$Seconds"; $env:FAKE_RESULT = "$Result"; $env:FAKE_TOUCH = $(if ($Touch) { '1' } else { '0' })
$p = Start-Process -FilePath $exe -WindowStyle Normal -PassThru
try {
    $token = $null; $port = $null
    for ($i = 0; $i -lt 60 -and -not $port; $i++) {
        Start-Sleep -Milliseconds 500
        $token = (Get-Content -Raw -ErrorAction SilentlyContinue "$home2/artifact-token")
        # HttpListener's socket belongs to http.sys: find the URL registered in this process's request queue
        $pids = @(); $mine = $false
        foreach ($line in (netsh http show servicestate view=requestq)) {
            if ($line -match '^Request queue name:') { $mine = $false; continue }
            if ($line -match '^\s+ID: (\d+),') { if ([int]$Matches[1] -eq $p.Id) { $mine = $true }; continue }
            if ($mine -and $line -match 'LOCALHOST:(\d+)/') { $port = [int]$Matches[1]; break }
        }
    }
    if (-not $port) { throw "no API port" }
    $token = $token.Trim()
    Start-Sleep -Seconds 4  # let it settle: startup stalls aren't what's measured

    # the first cards by id (on the Workbench board, done ones: start visible ones by hand with the API to watch them)
    $ids = @(Get-ChildItem "$main/items" -Filter *.md | ForEach-Object { ($_.BaseName -split ' - ')[0] } | Sort-Object | Select-Object -First $Cards)
    "starting $($ids -join ' ') on port $port at $(Get-Date -Format HH:mm:ss.fff)"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $r = Invoke-RestMethod -Method Post -Uri "http://localhost:$port/api/$token/work-on" -ContentType 'application/json' `
        -Body (@{ project = 'perf'; cards = ($ids -join ' ') } | ConvertTo-Json)
    "work-on returned in $($sw.ElapsedMilliseconds) ms: $r"
    Start-Sleep -Seconds $Wait
    if ($Switch) {
        function Api($name, $body) {
            $sw = [Diagnostics.Stopwatch]::StartNew()
            $r = Invoke-RestMethod -Method Post -Uri "http://localhost:$port/api/$token/$name" -ContentType 'application/json' -Body ($body | ConvertTo-Json)
            "$(Get-Date -Format HH:mm:ss.fff) $name $($body | ConvertTo-Json -Compress) $($sw.ElapsedMilliseconds) ms"
            Start-Sleep -Milliseconds 1500
        }
        $list = (Invoke-WebRequest -Method Post -Uri "http://localhost:$port/api/$token/agents" -ContentType 'application/json' -Body '{"project":"perf"}').Content
        $agentIds = @($list | Select-String -AllMatches '"id":\s*"([0-9a-f]{10})"' | ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value })
        foreach ($s in 'Board', 'Home', 'Agents', 'Board', 'Notes', 'Agents') { Api 'show' @{ project = 'perf'; section = $s } }
        foreach ($a in $agentIds) { Api 'show' @{ project = 'perf'; agent = $a } }
    }
    if ($Scroll) {
        # scroll the open transcript top to bottom through UI Automation (the scroll viewer with the most to scroll)
        Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
        $root = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id))
        $cond = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty, $true)
        $best = $null; $size = 101
        foreach ($e in $root.FindAll([Windows.Automation.TreeScope]::Descendants, $cond)) {
            $sp = $e.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
            if ($sp.Current.VerticallyScrollable -and $sp.Current.VerticalViewSize -lt $size) { $best = $sp; $size = $sp.Current.VerticalViewSize }
        }
        if ($best) {
            "$(Get-Date -Format HH:mm:ss.fff) scrolling a view showing $([math]::Round($size,1))% of its height"
            foreach ($pct in (100, 95, 90, 85, 80, 75, 70, 65, 60, 55, 50, 45, 40, 35, 30, 25, 20, 15, 10, 5, 0)) {
                $best.SetScrollPercent(-1, $pct); Start-Sleep -Milliseconds 60
            }
            "$(Get-Date -Format HH:mm:ss.fff) scrolled"
        } else { "no scrollable view found" }
    }
}
finally {
    if (-not $Keep) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}
"--- stalls.log"
Get-Content -ErrorAction SilentlyContinue "$home2/stalls.log"
if (Test-Path "$home2/crash.log") { "--- crash.log"; Get-Content "$home2/crash.log" | Select-Object -First 30 }
