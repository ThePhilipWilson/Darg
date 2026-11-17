# Live check of WOR-0125 (Work All and QA Run All) in the debug build: a throwaway instance in .shots/bulk-home with
# a board (2 in the backlog, 3 in In Progress, 2 in QA Ready) and fake agents (tools/perf/fake_claude.py). One card is
# started first; then the column buttons are read and pressed through UI Automation, the board files and the agents
# list are checked, and the window is captured before and after (.shots/bulk-*.png).
#   dotnet build; pwsh -File tools/bulk-start.ps1
param([int]$Settle = 4)
$ErrorActionPreference = 'Stop'
$wt = (Resolve-Path "$PSScriptRoot/..").Path
$exe = "$wt/src/Darg.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Darg.Desktop.exe"
$home2 = "$wt/.shots/bulk-home"
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $home2
$board = "$home2/proj/tasks/Main"
New-Item -ItemType Directory -Force "$board/items", "$home2/proj/repo" | Out-Null
git -C "$home2/proj/repo" init -q
$r = $home2.Replace('\', '/')
@"
{ "name": "bulk", "projects": [ { "id": "bulk", "name": "Bulk", "color": "#e07ab8",
  "boards": "$r/proj/tasks", "repos": [ "$r/proj/repo" ], "features": [] } ] }
"@ | Set-Content -LiteralPath "$home2/instance.json" -Encoding utf8
Set-Content -LiteralPath "$home2/claude.cmd" -Value "@python `"$wt/tools/perf/fake_claude.py`" %*" -Encoding ascii
@'
{"columns":[{"id":"c2","name":"In Progress"},{"id":"c4","name":"QA Ready"},{"id":"c5","name":"For Human Attention"},
 {"id":"c6","name":"needs more work"},{"id":"c3","name":"Done"}],"attentionColumn":true,
 "prefix":"BLK","nextId":9,"version":1,"order":{"backlog":["BLK-0001","BLK-0002"],"c2":["BLK-0003","BLK-0004","BLK-0005"],
 "c4":["BLK-0006","BLK-0007"],"c3":["BLK-0008"]}}
'@ | Set-Content -LiteralPath "$board/board.json" -Encoding utf8
$status = @{ 1 = 'Backlog'; 2 = 'Backlog'; 3 = 'In Progress'; 4 = 'In Progress'; 5 = 'In Progress'; 6 = 'QA Ready'; 7 = 'QA Ready'; 8 = 'Done' }
foreach ($n in 1..8) {
    "# BLK-000${n}: Card $n`n`n- **Status:** $($status[$n])`n- **Priority:**`n- **Type:** Task`n- **Labels:**`n- **Created:** 2026-11-17`n- **Done:**`n`n## Description`n`nCard $n.`n`n## How to check`n`nLook at card $n.`n" |
        Set-Content -LiteralPath "$board/items/BLK-000$n - Card $n.md" -Encoding utf8 -NoNewline
}
$fails = 0
function Check($what, $ok) { if ($ok) { "  ok    $what" } else { "  FAIL  $what"; $script:fails++ } }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
function Buttons($p) {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Button)
    $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
}
function Named($p, $like) { @(Buttons $p | Where-Object { $_.Current.Name -like $like }) }
function Press($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Seconds $Settle }
function Api($d, $name, $body) {
    (Invoke-WebRequest -Method Post -Uri "http://localhost:$($d.Port)/api/$($d.Token)/$name" -ContentType 'application/json' -Body ($body | ConvertTo-Json)).Content
}
function Order { (Get-Content -Raw -LiteralPath "$board/board.json" | ConvertFrom-Json).order }

$d = $null
try {
    $env:DARG_HOME = $home2; $env:DARG_SECTION = 'Board'; $env:CLAUDE_PATH = "$home2/claude.cmd"
    $env:FAKE_SECONDS = '300'; $env:FAKE_RATE = '0.2'; $env:FAKE_RESULT = '200'; $env:FAKE_TOUCH = '0'
    $p = Start-Process -FilePath $exe -WindowStyle Normal -PassThru
    $port = $null
    for ($i = 0; $i -lt 60 -and -not $port; $i++) {
        Start-Sleep -Milliseconds 500
        $mine = $false
        foreach ($line in (netsh http show servicestate view=requestq)) {
            if ($line -match '^Request queue name:') { $mine = $false; continue }
            if ($line -match '^\s+ID: (\d+),') { if ([int]$Matches[1] -eq $p.Id) { $mine = $true }; continue }
            if ($mine -and $line -match 'LOCALHOST:(\d+)/') { $port = [int]$Matches[1]; break }
        }
    }
    if (-not $port) { Stop-Process -Id $p.Id -Force; throw 'no API port' }
    Start-Sleep -Seconds $Settle
    $d = @{ Process = $p; Port = $port; Token = (Get-Content -Raw "$home2/artifact-token").Trim() }

    "1. one card already has an agent"
    Api $d 'work-on' @{ project = 'bulk'; card = 'BLK-0004' } | Out-Null
    Start-Sleep -Seconds $Settle
    $p.Refresh()
    & "$wt/tools/shot.ps1" -ProcessId $p.Id -Out "$wt/.shots/bulk-before.png"
    $work = Named $p '▶ Work All*'
    Check "two Work All buttons (backlog, In Progress): $(@($work | ForEach-Object { $_.Current.Name }) -join ' | ')" ($work.Count -eq 2)
    Check 'each counts 4 (2 backlog + 2 In Progress free)' (@($work | Where-Object { $_.Current.Name -eq '▶ Work All (4)' }).Count -eq 2)
    $qa = Named $p '🧪 QA Run All*'
    Check "one QA Run All button: $(@($qa | ForEach-Object { $_.Current.Name }) -join ' | ')" ($qa.Count -eq 1 -and $qa[0].Current.Name -eq '🧪 QA Run All (2)')

    "2. Work All: 4 is more than 3, so it asks first"
    Press $work[0]
    $start = Named $p 'Start them'
    Check 'the column asks before starting' ($start.Count -eq 1)
    Check 'nothing started yet' ((Order).backlog.Count -eq 2)
    & "$wt/tools/shot.ps1" -ProcessId $p.Id -Out "$wt/.shots/bulk-ask.png"
    Press $start[0]
    $o = Order
    Check 'backlog cards moved to In Progress' ($o.backlog.Count -eq 0 -and $o.c2.Count -eq 5)
    $agents = Api $d 'agents' @{ project = 'bulk' }
    $on = @('BLK-0001', 'BLK-0002', 'BLK-0003', 'BLK-0004', 'BLK-0005' | Where-Object { $agents -match $_ })
    Check "agents on all five In Progress cards ($($on -join ', '))" ($on.Count -eq 5)
    Check 'none on the Done card' ($agents -notmatch 'BLK-0008')
    $work = Named $p '▶ Work All*'
    Check "Work All now reads 0 and is disabled: $(@($work | ForEach-Object { $_.Current.Name }) -join ' | ')" (
        @($work | Where-Object { $_.Current.Name -eq '▶ Work All (0)' -and -not $_.Current.IsEnabled }).Count -eq 2)

    "3. QA Run All: 2 is a few, so it starts at once"
    Press (Named $p '🧪 QA Run All*')[0]
    $agents = Api $d 'agents' @{ project = 'bulk' }
    Check 'a QA agent on BLK-0006 and BLK-0007' ($agents -match 'BLK-0006.*\(QA\)' -and $agents -match 'BLK-0007.*\(QA\)')
    $qa = Named $p '🧪 QA Run All*'
    Check "QA Run All now reads 0: $($qa[0].Current.Name)" ($qa[0].Current.Name -eq '🧪 QA Run All (0)' -and -not $qa[0].Current.IsEnabled)
    $again = Api $d 'qa-all' @{ project = 'bulk' }
    Check "pressing again doubles nothing: $again" ($again -match 'Nothing to QA')
    $again = Api $d 'work-all' @{ project = 'bulk' }
    Check "Work All again doubles nothing: $again" ($again -match 'Nothing to start')
    & "$wt/tools/shot.ps1" -ProcessId $p.Id -Out "$wt/.shots/bulk-after.png"
}
finally {
    if ($d) { Stop-Process -Id $d.Process.Id -Force -ErrorAction SilentlyContinue }
    Get-Process python -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -like '*fake_claude*' } | Stop-Process -Force -ErrorAction SilentlyContinue
    if (Test-Path "$home2/crash.log") { "crash.log:"; Get-Content "$home2/crash.log" -Tail 20 }
}
"$fails failed"
exit [int]($fails -gt 0)
