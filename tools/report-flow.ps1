# Live check of WOR-0102 (reports follow a card) in the debug build, no clicking: a throwaway instance in
# .shots/flow-home with a board of four cards and fake agents (tools/perf/fake_claude.py). Cards are moved the way
# agents move them (Status line plus board.json), and the reports folder is checked after each move.
#   dotnet build; pwsh -File tools/report-flow.ps1
param([int]$Settle = 4)
$ErrorActionPreference = 'Stop'
$wt = (Resolve-Path "$PSScriptRoot/..").Path
$exe = "$wt/src/Darg.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Darg.Desktop.exe"
$home2 = "$wt/.shots/flow-home"
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $home2
$board = "$home2/proj/tasks/Main"
New-Item -ItemType Directory -Force "$board/items", "$home2/proj/repo" | Out-Null
git -C "$home2/proj/repo" init -q
$r = $home2.Replace('\', '/')
@"
{ "name": "flow", "projects": [ { "id": "flow", "name": "Flow", "color": "#e07ab8",
  "boards": "$r/proj/tasks", "repos": [ "$r/proj/repo" ], "features": [] } ] }
"@ | Set-Content -LiteralPath "$home2/instance.json" -Encoding utf8
Set-Content -LiteralPath "$home2/claude.cmd" -Value "@python `"$wt/tools/perf/fake_claude.py`" %*" -Encoding ascii
@'
{"columns":[{"id":"c2","name":"In Progress"},{"id":"c4","name":"QA Ready"},{"id":"c5","name":"For Human Attention"},
 {"id":"c6","name":"needs more work"},{"id":"c3","name":"Done"}],"attentionColumn":true,
 "prefix":"FLO","nextId":5,"version":1,"order":{"c2":["FLO-0001","FLO-0002","FLO-0003","FLO-0004"]}}
'@ | Set-Content -LiteralPath "$board/board.json" -Encoding utf8
foreach ($n in 1..4) {
    "# FLO-000${n}: Card $n`n`n- **Status:** In Progress`n- **Priority:**`n- **Type:** Task`n- **Labels:**`n- **Created:** 2026-11-17`n- **Done:**`n`n## Description`n`nCard $n.`n`n## How to check`n`nLook at card $n.`n" |
        Set-Content -LiteralPath "$board/items/FLO-000$n - Card $n.md" -Encoding utf8 -NoNewline
}
$reports = "$home2/projects/flow/reports"

function Move-Card($id, $columnId, $name) {
    $file = (Get-ChildItem -LiteralPath "$board/items" -Filter "$id - *.md").FullName
    (Get-Content -Raw -LiteralPath $file) -replace '(?m)^- \*\*Status:\*\*.*$', "- **Status:** $name" | Set-Content -LiteralPath $file -Encoding utf8 -NoNewline
    $j = Get-Content -Raw -LiteralPath "$board/board.json" | ConvertFrom-Json -AsHashtable
    foreach ($k in @($j.order.Keys)) { $j.order[$k] = @($j.order[$k] | Where-Object { $_ -ne $id }) }
    $j.order[$columnId] = @($j.order[$columnId]) + $id | Where-Object { $_ }
    $j | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$board/board.json" -Encoding utf8
    Start-Sleep -Seconds $Settle
}
function Listed($id) { @(Get-ChildItem -LiteralPath $reports -Filter "*-$id*.md" -ErrorAction SilentlyContinue) }
function Archived($id) { @(Get-ChildItem -LiteralPath "$reports/archive" -Filter "*-$id*.md" -ErrorAction SilentlyContinue) }
$fails = 0
function Check($what, $ok) { if ($ok) { "  ok    $what" } else { "  FAIL  $what"; $script:fails++ } }

function Start-Darg {
    $env:DARG_HOME = $home2; $env:DARG_SECTION = 'Board'; $env:CLAUDE_PATH = "$home2/claude.cmd"
    $env:FAKE_SECONDS = '120'; $env:FAKE_RATE = '1'; $env:FAKE_RESULT = '200'; $env:FAKE_TOUCH = '0'
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
    @{ Process = $p; Port = $port; Token = (Get-Content -Raw "$home2/artifact-token").Trim() }
}

try {
    "1. no agent: into QA, then Done"
    $d = Start-Darg
    Move-Card 'FLO-0001' 'c4' 'QA Ready'
    $qa = Listed 'FLO-0001'
    Check 'one QA report written on entering QA' ($qa.Count -eq 1 -and $qa[0].Name -like '*-qa.md')
    Check 'it is unread and pending' ((Get-Content -Raw $qa[0].FullName) -match 'read: false' -and (Get-Content -Raw $qa[0].FullName) -match 'decision: pending')
    Move-Card 'FLO-0001' 'c6' 'needs more work'; Move-Card 'FLO-0001' 'c4' 'QA Ready'
    Check 'out of QA and straight back: no second QA report' ((Listed 'FLO-0001').Count -eq 1)
    Move-Card 'FLO-0001' 'c3' 'Done'
    $now = Listed 'FLO-0001'
    Check 'on Done the QA report leaves the list and a done report is written' ($now.Count -eq 1 -and $now[0].Name -notlike '*-qa.md' -and (Get-Content -Raw $now[0].FullName) -notmatch 'status: qa')
    $arch = Archived 'FLO-0001'
    Check 'the QA report is in reports/archive, approved' ($arch.Count -eq 1 -and (Get-Content -Raw $arch[0].FullName) -match 'decision: approved')

    "2. a card agent at work (fake, 120 s turn): it writes its own reports, so Darg writes none"
    Invoke-RestMethod -Method Post -Uri "http://localhost:$($d.Port)/api/$($d.Token)/work-on" -ContentType 'application/json' -Body (@{ project = 'flow'; cards = 'FLO-0002' } | ConvertTo-Json) | Out-Null
    Start-Sleep -Seconds 6
    $agents = (Invoke-WebRequest -Method Post -Uri "http://localhost:$($d.Port)/api/$($d.Token)/agents" -ContentType 'application/json' -Body '{"project":"flow"}').Content
    Check 'the agent is working on FLO-0002' ($agents -match 'FLO-0002')
    Move-Card 'FLO-0002' 'c4' 'QA Ready'
    Check 'into QA: no QA report from Darg' ((Listed 'FLO-0002').Count -eq 0)
    Move-Card 'FLO-0002' 'c3' 'Done'
    Check 'into Done: no done report from Darg' ((Listed 'FLO-0002').Count -eq 0)

    "3. moves made while Darg is closed make nothing"
    Stop-Process -Id $d.Process.Id -Force; Start-Sleep -Seconds 2
    Move-Card 'FLO-0003' 'c4' 'QA Ready'
    Move-Card 'FLO-0004' 'c3' 'Done'
    $d = Start-Darg
    Check 'no report for FLO-0003 (QA) or FLO-0004 (Done)' ((Listed 'FLO-0003').Count -eq 0 -and (Listed 'FLO-0004').Count -eq 0)
    "4. ...but a move after it opens does"
    Move-Card 'FLO-0003' 'c3' 'Done'
    Check 'FLO-0003 to Done: a done report' ((Listed 'FLO-0003').Count -eq 1)
}
finally {
    if ($d) { Stop-Process -Id $d.Process.Id -Force -ErrorAction SilentlyContinue }
    if (Test-Path "$home2/crash.log") { "crash.log:"; Get-Content "$home2/crash.log" -Tail 20 }
}
"$fails failed"
exit [int]($fails -gt 0)
