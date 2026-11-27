# Summarise an instance's agents.log (WOR-0224): where each agent turn's time went, split into Darg's part (model pick,
# process spawn, the transcript showing what was read), Claude Code's start-up (node, hooks, resume, MCP: before its own
# clock starts) and Claude's part (the model, tools, waits on the person). Read-only; it never starts the app.
#   pwsh -File tools/perf/agents.ps1                                   # the real instance
#   pwsh -File tools/perf/agents.ps1 -InstanceHome .shots/home -Since '2026-11-25 09:00' -Top 20
#   pwsh -File tools/perf/agents.ps1 -Turns                            # every turn, not just the slowest
# The lines are AgentTimings' (src/Darg.Core/AgentTimings.cs). What each agent was comes from its "agent <kind>" line
# (logs since WOR-0224), else its saved file (agents/~bench is Fettle's folder), else its pick rule (kind: fettle).
param([string]$InstanceHome = '', [string]$Since = '', [int]$Top = 15, [switch]$Turns, [string]$Log = '')
$ErrorActionPreference = 'Stop'
if (-not $InstanceHome) {
    $InstanceHome = @("$env:APPDATA/Darg/home", "$env:APPDATA/Workbench/home") | Where-Object { Test-Path -LiteralPath "$_/instance.json" } | Select-Object -First 1
    if (-not $InstanceHome) { $InstanceHome = "$env:APPDATA/Darg/home" }
}
if (-not $Log) { $Log = Join-Path $InstanceHome 'agents.log' }
if (-not (Test-Path -LiteralPath $Log)) { "== agents.log: none at $Log"; return }

# what each saved agent was: Fettle's chats live in agents/~bench, the rest say their purpose (work, rework, qa, plan…)
$kindOf = @{}
$saved = Join-Path $InstanceHome 'agents'
if (Test-Path -LiteralPath $saved) {
    foreach ($f in Get-ChildItem -LiteralPath $saved -Recurse -Filter '*.json' -File) {
        $id = $f.BaseName
        if ($f.Directory.Name -eq '~bench') { $kindOf[$id] = 'fettle'; continue }
        try { $p = (Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json).purpose } catch { $p = $null }
        $kindOf[$id] = if ($p) { "$p" } else { 'chat' }
    }
}

$culture = [Globalization.CultureInfo]::InvariantCulture
function D([string]$s) { [double]::Parse($s, $culture) }
$sessions = [Collections.Generic.List[object]]::new()
$byAgent = @{}
function SessionFor($agent, [datetime]$at, [double]$elapsed) {
    # a session is one clock: its lines' wall time less their elapsed time is the same instant (a resumed session's clock
    # starts again at 0 while the old one may still log its exit, so the agent id alone can't tell them apart)
    $origin = $at.AddSeconds(-$elapsed)
    foreach ($s in @($byAgent[$agent])) { if ($s -and [math]::Abs(($s.Origin - $origin).TotalSeconds) -lt 1.5) { return $s } }
    $s = [pscustomobject]@{ Agent = $agent; Origin = $origin; Kind = $kindOf[$agent]; Logged = $null; Resume = $false; Pick = $null; PickRule = ''
        Spawn = $null; Spare = $false; Turns = [Collections.Generic.List[object]]::new() }
    $byAgent[$agent] = @(@($byAgent[$agent]) + $s | Where-Object { $_ })
    $sessions.Add($s)
    return $s
}
function TurnOf($s, [int]$n, [datetime]$at) {
    $t = $s.Turns | Where-Object N -eq $n | Select-Object -Last 1
    if (-not $t) {
        $t = [pscustomobject]@{ N = $n; At = $at; HookStart = $null; HookEnd = $null; Init = $null; Reply = $null; Tool = $null; ToolName = ''
            ShownLag = $null; UiLagMax = $null; Queued = $false; Seen = $false; Offset = 0.0; Perm = 0.0; PermOpen = @{}; End = $null; Cc = $null; ApiTotal = $null; Request = $null; Ttft = $null; Thinking = 0 }
        $s.Turns.Add($t)
    }
    $t
}

$rx = [regex]'^(?<at>\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3}) (?<id>\S+) \+(?<el>[\d.]+) s(?: turn (?<turn>\d+) \+(?<tt>[\d.]+) s)? · (?<what>.*)$'
$lines = 0
foreach ($l in [IO.File]::ReadLines((Resolve-Path -LiteralPath $Log).Path)) {
    $m = $rx.Match($l)
    if (-not $m.Success) { continue }
    if ($Since -and $m.Groups['at'].Value.Substring(0, 19) -lt $Since) { continue }
    $lines++
    $at = [datetime]::ParseExact($m.Groups['at'].Value, 'yyyy-MM-dd HH:mm:ss.fff', $culture)
    $what = $m.Groups['what'].Value
    $s = SessionFor $m.Groups['id'].Value $at (D $m.Groups['el'].Value)
    # lines that say which turn or session they're about themselves, in a turn or not
    if ($what -match '^agent (?<k>\S+)$') { $s.Logged = $Matches['k']; continue }
    if ($what -match '^turn (?<n>\d+) shown \(\d+ ms after it was read, the turn''s longest wait (?<ms>\d+) ms\)') {
        (TurnOf $s ([int]$Matches['n']) $at).UiLagMax = [int]$Matches['ms'] / 1000.0; continue
    }
    if (-not $m.Groups['turn'].Success) {
        if ($what -match '^model pick end \((?<p>[\d.]+) s\) · (?<rule>.*)$') { $s.Pick = D $Matches['p']; $s.PickRule = $Matches['rule'] }
        elseif ($what -match '^process start \(spawn (?<ms>\d+) ms, \d+ args(?<r>, resume)?\)') { $s.Spawn = [int]$Matches['ms'] / 1000.0; if ($Matches['r']) { $s.Resume = $true } }
        elseif ($what -match '^took spare session') { $s.Spare = $true } # WOR-0235: started ahead, so no spawn or start-up of its own
        continue
    }
    $t = TurnOf $s ([int]$m.Groups['turn'].Value) $at
    $tt = D $m.Groups['tt'].Value
    # before WOR-0224 a message Claude Code folded into the turn running still got a "turn" from that turn's end, which
    # ran until the person's next message (logged as sent mid-turn): count such a turn from that message instead
    if ($what -eq "next message's turn") { $t.Queued = $true; continue }
    if ($t.Queued -and -not $t.Seen -and $what -eq 'message sent mid-turn') { $t.Offset = $tt; $t.At = $at; $t.Seen = $true; continue }
    $t.Seen = $true
    $tt -= $t.Offset
    switch -regex ($what) {
        '^hook start SessionStart:(\w+)' { if ($null -eq $t.HookStart) { $t.HookStart = $tt }; if ($Matches[1] -eq 'resume') { $s.Resume = $true }; break }
        '^hook end SessionStart' { $t.HookEnd = $tt; break }
        '^init\b' { if ($null -eq $t.Init) { $t.Init = $tt }; break }
        '^first reply' { $t.Reply = $tt; break }
        '^first tool call (\S+)' { $t.Tool = $tt; $t.ToolName = $Matches[1]; break }
        '^first text shown \((\d+) ms' { $t.ShownLag = [int]$Matches[1] / 1000.0; break }
        '^permission asked \S+ \((?<r>[^)]+)\)' { $t.PermOpen[$Matches['r']] = $tt; break }
        '^permission answered \((?<r>[^)]+)\)' { if ($t.PermOpen.ContainsKey($Matches['r'])) { $t.Perm += $tt - $t.PermOpen[$Matches['r']]; $t.PermOpen.Remove($Matches['r']) }; break }
        '^(system thinking_tokens|thinking)$' { $t.Thinking++; break }
        '^turn end \((?<o>[\d.]+) s' {
            $t.End = (D $Matches['o']) - $t.Offset
            if ($what -match 'Claude Code says (?<cc>[\d.]+) s, API (?<api>[\d.]+) s') { $t.Cc = D $Matches['cc']; $t.ApiTotal = D $Matches['api'] }
            if ($what -match 'request at (?<r>[\d.]+) s') { $t.Request = D $Matches['r'] }
            if ($what -match 'first token at (?<f>[\d.]+) s') { $t.Ttft = D $Matches['f'] }
            break
        }
    }
}

function KindOf($s) {
    if ($s.Logged) { return $s.Logged }
    if ($s.Kind) { return $s.Kind }
    if ($s.PickRule -match 'rule kind: (\w+)') { return $Matches[1] }
    # forgotten since, from a log before WOR-0224: a Haiku pick means it wasn't Fettle (Fettle's is a rule), so a card
    # agent, planner or chat
    if ($s.PickRule -match 'Haiku') { return 'agent' }
    '?'
}

# per turn: Darg's part, Claude Code's start-up and Claude's part
$rows = foreach ($s in $sessions) {
    $prevApi = 0.0
    $kind = KindOf $s
    foreach ($t in $s.Turns | Sort-Object N) {
        if ($null -eq $t.End) { continue } # still running, or cut off (a crash, a close)
        # duration_api_ms counts the whole session's API time so far: this turn's is the step from the last
        $api = if ($null -ne $t.ApiTotal) { [math]::Max(0.0, $t.ApiTotal - $prevApi) } else { $null }
        if ($null -ne $t.ApiTotal) { $prevApi = $t.ApiTotal }
        $first = $t.N -eq 1
        $pick = if ($first -and $s.Pick) { $s.Pick } else { 0.0 }
        $spawn = if ($first -and $s.Spawn) { $s.Spawn } else { 0.0 }
        # Claude Code's own clock starts once it has started up: what ours has beyond it is start-up (and pipe time)
        $startup = if ($null -ne $t.Cc) { [math]::Max(0.0, $t.End - $t.Cc) } elseif ($null -ne $t.Init) { $t.Init } else { $null }
        $hooks = if ($null -ne $t.HookStart -and $null -ne $t.HookEnd) { $t.HookEnd - $t.HookStart } else { $null }
        $other = if ($null -ne $t.Cc -and $null -ne $api) { [math]::Max(0.0, $t.Cc - $api - $t.Perm) } else { $null }
        [pscustomobject]@{
            At = $t.At; Agent = $s.Agent; Kind = $kind; Session = if ($s.Resume) { 'resume' } elseif ($s.Spare) { 'spare' } else { 'new' }
            Turn = $t.N; Pick = $pick; Spawn = $spawn; Startup = $startup; Hooks = $hooks; Init = $t.Init; Reply = $t.Reply; Tool = $t.Tool; ToolName = $t.ToolName
            Shown = $t.ShownLag; UiLag = $t.UiLagMax; Cc = $t.Cc; Api = $api; Other = $other; Perm = $t.Perm; Thinking = $t.Thinking; Ttft = $t.Ttft
            Total = $pick + $t.End; Waited = $pick + $t.End - $t.Perm
        }
    }
}
$rows = @($rows)

function F($v) { if ($null -eq $v) { '-' } else { '{0:0.0}' -f $v } }
function Med($xs) { $xs = @($xs | Where-Object { $null -ne $_ } | Sort-Object); if (-not $xs.Count) { $null } else { $xs[[int][math]::Floor(($xs.Count - 1) / 2)] } }
$head = '{0,-14} {1,-10} {2,-7} {3,-6} {4,2} {5,7} {6,7} {7,5} {8,7} {9,5} {10,6} {11,-14} {12,7} {13,7} {14,6}'
function Table($set) {
    $head -f 'When', 'Agent', 'Kind', 'Sess', 'T', 'Waited', 'Person', 'Pick', 'Startup', 'Hooks', 'Reply', 'Tool', 'Model', 'Tools+', 'Shown'
    foreach ($r in $set) {
        $tool = if ($null -ne $r.Tool) { "$(F $r.Tool) $($r.ToolName)" } else { '-' }
        if ($tool.Length -gt 14) { $tool = $tool.Substring(0, 14) }
        $head -f $r.At.ToString('MM-dd HH:mm:ss'), $r.Agent, $r.Kind, $r.Session, $r.Turn, (F $r.Waited), (F $r.Perm), (F $r.Pick), (F $r.Startup), (F $r.Hooks),
            (F $r.Reply), $tool, (F $r.Api), (F $r.Other), (F $r.Shown)
    }
    ''
}

"== agents.log: $lines lines, $($sessions.Count) sessions, $($rows.Count) finished turns$(if ($Since) { " since $Since" }) in $Log"
if (-not $rows.Count) { return }
"   ($(($rows | Measure-Object Thinking -Sum).Sum) thinking lines: before WOR-0224 one every second or so the model thought, since then a turn's first)"
""
"In seconds. Waited: the turn less Person (waiting on a question or permission), from the agent being made for a"
"first turn. Darg's part: Pick (the model pick) and Shown (the first text's wait between being read and being in the"
"transcript; logs since WOR-0224). Startup: Claude Code starting before its own clock (node, SessionStart Hooks, MCP"
"servers, a resume's load). Reply / Tool: from the message to the first reply / tool call. Claude's part: Model (its"
"API time this turn) and Tools+ (the rest of its turn: tools, subagents, its own work). Since WOR-0233 a new agent's"
"claude starts while Haiku picks its model, so its first turn's Startup (and Hooks) is only what was left once the pick"
"ended and its message went."
""
"-- the sessions: what they were and how they started"
$sessions | Where-Object { $_.Turns.Count } | ForEach-Object { [pscustomobject]@{ Name = "$(KindOf $_) $(if ($_.Resume) { 'resumed' } elseif ($_.Spare) { 'spare' } else { 'new' })"; S = $_ } } |
    Group-Object Name | Sort-Object Count -Descending | ForEach-Object {
        $ids = @($_.Group.S.Agent | Select-Object -Unique)
        '{0,-16} {1,3} sessions, {2,3} turns: {3}{4}' -f $_.Name, $_.Count, ($_.Group | ForEach-Object { $_.S.Turns.Count } | Measure-Object -Sum).Sum,
            (($ids | Select-Object -First 8) -join ' '), $(if ($ids.Count -gt 8) { " +$($ids.Count - 8) more" })
    }
""
"-- medians (first turns of new sessions, of spare ones started ahead, of resumed ones, and later turns)"
$mhead = '{0,-20} {1,-7} {2,3} {3,7} {4,5} {5,7} {6,5} {7,6} {8,6} {9,7} {10,7} {11,6}'
$mhead -f 'Turns', 'Kind', 'N', 'Waited', 'Pick', 'Startup', 'Hooks', 'Reply', 'Tool', 'Model', 'Tools+', 'Shown'
$groups = @(
    @{ Name = 'first turn, new'; Set = @($rows | Where-Object { $_.Turn -eq 1 -and $_.Session -eq 'new' }) }
    @{ Name = 'first turn, spare'; Set = @($rows | Where-Object { $_.Turn -eq 1 -and $_.Session -eq 'spare' }) } # WOR-0235: a session started ahead
    @{ Name = 'first turn, resumed'; Set = @($rows | Where-Object { $_.Turn -eq 1 -and $_.Session -eq 'resume' }) }
    @{ Name = 'later turns'; Set = @($rows | Where-Object { $_.Turn -gt 1 }) }
)
foreach ($g in $groups) {
    foreach ($k in ($g.Set | Group-Object Kind)) {
        $mhead -f $g.Name, $k.Name, $k.Count, (F (Med $k.Group.Waited)), (F (Med $k.Group.Pick)), (F (Med $k.Group.Startup)), (F (Med $k.Group.Hooks)),
            (F (Med $k.Group.Reply)), (F (Med $k.Group.Tool)), (F (Med $k.Group.Api)), (F (Med $k.Group.Other)), (F (Med $k.Group.Shown))
    }
}
""
"-- where the time went, added up over every turn (s)"
$sum = { param($p) [math]::Round((($rows | ForEach-Object { $_.$p } | Where-Object { $null -ne $_ }) | Measure-Object -Sum).Sum, 1) }
"Darg: model pick $(& $sum 'Pick'), spawn $(& $sum 'Spawn'), transcript showing the first text $(& $sum 'Shown') (worst wait in a turn $(F (($rows.UiLag | Where-Object { $null -ne $_ } | Measure-Object -Maximum).Maximum)))"
"Claude Code start-up: $(& $sum 'Startup') (SessionStart hooks $(& $sum 'Hooks'))"
"Claude: model $(& $sum 'Api'), tools and the rest $(& $sum 'Other')  ·  waiting on the person $(& $sum 'Perm')"
""
"-- the $Top slowest turns (Waited)"
Table ($rows | Sort-Object Waited -Descending | Select-Object -First $Top)
"-- the $Top slowest to first answer (pick plus message to first reply)"
Table ($rows | Where-Object { $null -ne $_.Reply } | Sort-Object { $_.Pick + $_.Reply } -Descending | Select-Object -First $Top)
"-- Fettle's replies (newest $Top)"
Table ($rows | Where-Object Kind -eq 'fettle' | Select-Object -Last $Top)
if ($Turns) { "-- every turn"; Table $rows }
