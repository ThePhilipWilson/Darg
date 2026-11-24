# WOR-0127: Proper test suite for integration and unit tests

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-17
- **Done:** 2026-11-18

## Description

proper test suite for integration and unit tests

Want to prevent regression and also to give a test suite that QA can use so that so much stuff isn't making it to me.

From a note on the wall (20261117-221309-3df3).

## How to check

### Round 2 (the QA notes' four points, plus two flakes found on the way)

On main, pushed: commit d4d3eb6 (branch wor-0127, merged and deleted).

- **Two runs in one folder:** `tools/test.ps1` holds `.shots/test.lock` for the build and both suites. A second run in the same folder prints "Another test run is going in this folder (pid …, started …); waiting for it to finish." and goes after it (gives up after `-WaitMinutes`, 20 by default). The Sandbox also holds `.shots/integration/.lock` for plain `dotnet test` runs. Choice made on your behalf: wait, not fail, so QA Run All's agents queue rather than error.
- **A filter that matches nothing fails:** `-Filter SendBack` now ends "No test matches -Filter 'SendBack' …" and exits 1. The summary line counts tests: "All passed (integration 23, unit 568 tests)." With no filter, a suite that ran none fails too.
- **Live copy environment:** test.ps1 and the Sandbox clear every inherited `DARG_*` / `WORKBENCH_*` variable (but `DARG_TEST_*` and `DARG_LIVE`), so no HotAvalonia in the build and the app under test isn't "⟳ live".
- **Real timings in the pictures:** new `UserHome` in Core is the one way to ~/.claude (WorkTimer, Claude settings, the user CLAUDE.md, claude-draw's install); the Sandbox points it and `CLAUDE_CONFIG_DIR` at `.shots/integration/_user`. The header shows no "today" in the PNGs.
- **Flake 1:** about one integration run in six failed in Avalonia's per-test app rebuild ("a different thread owns it"). The tests now share one Avalonia application (`AvaloniaTestIsolationLevel.PerAssembly`, in TestApp.cs); 20 runs in a row passed after.
- **Flake 2:** `DargApp.ClaudeCalls` read fake-claude.jsonl with `File.ReadAllLines`, refused while python had it open to append. It now reads with read/write sharing.

To try: from the repo, `pwsh -File tools/test.ps1 -Filter SendBack` (should fail, exit 1); `pwsh -File tools/test.ps1` in two terminals at once (the second waits, both pass); open `.shots/integration/Every_section_draws-Board/Board.png` and check the header has no "today …" and a plain ⟳, not "⟳ live". Taken after the merge: ![Board, round 2](D:/GitHub/ThePhilipWilson/Workbench/.shots/integration/Every_section_draws-Board/Board.png)

Checked here: unit 568/568, integration 23/23, twice on the merged commit; 20 integration runs in a row; two full runs at once in one folder, twice (the second waited, both passed); each new SandboxTests test fails with its fix taken out, run from the live copy. Not checked: QA Run All starting several QA agents that run the suite at once in the real app (the lock is what they'd hit; exercised from two shells instead).

### Round 1

What changed (on main, pushed): commits 6b5d161, 5a29b13, 1c842a6.

- `tools/test.ps1` is the whole suite: builds the solution, then 560 unit tests (tests/Darg.Core.Tests) and 17 integration tests (tests/Darg.Desktop.Tests, new). About 50 s.
- The integration tests run the real Darg window headless on a throwaway instance per test under `.shots/integration/`, with `tools/perf/fake_claude.py` playing claude. They cover: every section draws without a crash (Home, Board, Notes, Docs, Agents, Flows, Reports, Mistakes, Code, a PNG of each), Work on keeps you on the board (WOR-0107), card agents get the stdio protocol and their folders, a short turn notifies nobody (WOR-0078), Send back (WOR-0079), and a reworked card back in QA gets a QA agent only after its turn (WOR-0115). A timed-out wait prints what each agent and the fake claude were doing.
- Not covered yet: Preview, Terminal and Artifacts (a dev server, a shell, a native web view).
- The TicketTitle unit test that failed on every QA pass is fixed: its board sat straight in %TEMP% and a stray board there shifted its card number.
- A choice made on your behalf: every card agent's prompt now says to run the whole suite, never to pass a failing test over as unrelated, and to add a test that fails without its change; QA agents get the first two (`ReportStore.TestSuite`, `AddTest`).

To try it, from the repo: `pwsh -File tools/test.ps1` (or `-Unit`, `-Integration`, `-Filter Send_back`). Look for "All passed." and open `.shots/integration/Every_section_draws-Board/Board.png` (and the other sections') to see what the tests saw.

## QA notes

### 2026-11-17 (QA agent)

Passed: from a worktree of its own at f45ded6, `pwsh -File tools/test.ps1` builds and passes, unit 566/566 and integration 19/19, exit 0 (200 s cold, the first build of a new worktree). `-Unit` alone passes. `-Integration -Filter Send_back` runs both Send back tests. With `Decisions.LongTurn` set to zero (in the throwaway worktree only, then reverted), the short-turn test fails with a readable message listing the toast it got, and the script prints FAILED and exits 1. The nine section PNGs are there; Board.png shows the four columns, the cards and the Work All / QA Run All buttons. All the unit tests that write a board now give it a folder of its own.

Needs fixing:

- **Two runs in one folder break each other.** The first run, in the main folder, failed with BUILD FAILED (MSB4018: Darg.Desktop.Tests.deps.json held by another process) because another agent was running tools/test.ps1 there at the same time (pid 22660, from 23:44:48). QA agents work in the main folder, QA Run All starts several at once, and every one is now told to run the whole suite, so this will happen as a matter of course and read like a broken build. Each integration test also deletes `.shots/integration/<test>` before it starts (DargApp.cs:189), so a second run deletes the first one's instance mid-test. Also, my failed build may have overwritten files under the other run's testhost. Fix: one run per folder at a time (a lock file that waits or says so), or give every run its own output and .shots folder.
- **A filter that matches nothing passes.** `-Filter SendBack`, the card's own example, matches no test ("No test matches the given testcase filter"). Even so, the script prints "All passed." and exits 0. An agent with a mistyped filter gets a green result. Fix: treat zero tests run as a failure, and correct the example: the test names are `Send_back_...`.
- **The sandbox lets the live copy's environment through.** Sandbox clears DARG_SECTION/AGENT/FLOW and others, but not `DARG_LIVE_TREE`, `DARG_LIVE_BRANCH`, `DARG_LIVE_SCRIPT` or `DARG_LIVE_HOST`. An agent started from the live copy (this QA agent was) has all four set. So (a) the solution it builds pulls in HotAvalonia (Darg.Desktop.csproj:46; HotAvalonia.Core is in the test deps.json), which is a different build from the one Philip publishes, and (b) the app under test thinks it is the live copy. Its header shows ⟳ live, pointing at the real `.claude/worktrees/live` and the real host pid. No test presses Pull in or Restart yet, but one that did would act on the real live copy. Fix: clear `DARG_LIVE_*` in tools/test.ps1 before the build and in Sandbox.
- Minor: the header in every PNG shows the real "today 6h 12m" (WorkTimer reads `~/.claude/stats/timings` from the real home folder), so the pictures change from run to run and the tests read Philip's data. Point it at the sandbox, or switch it off there.

### 2026-11-18 (QA agent, round 2): passed

Checked from a worktree of its own at d4d3eb6, from a shell with all four DARG_LIVE_* variables set (started from the live copy):

- -Filter SendBack: both suites run none, it ends "No test matches -Filter 'SendBack' ..." and exits 1. -Filter Send_back: 2 unit and 2 integration, All passed, exit 0.
- Two full runs at once in one folder, 4 s apart: the second printed "Another test run is going in this folder (pid 52868, ...); waiting for it to finish.", went after the first, and both ended "All passed (integration 23, unit 568 tests).", exit 0.
- Plain dotnet test waits on .shots/integration/.lock: with the lock held by hand it was still running after 20 s and finished 3 s after release. Its "Waiting for another integration test run" line doesn't show on the console (testhost's stderr isn't passed through); only test.ps1's message does. Minor.
- No HotAvalonia in the test build's deps.json or bin. Board.png's header has a plain refresh icon and no "today"; .shots/integration/_user/.claude is empty.
- SandboxTests: with ClearInherited and UserHome.Folder taken out of Sandbox (then restored), the live-copy and real-timings tests fail and the third passes; with them in, all three pass.
- Flake: the integration suite 10 times in a row, 23/23 each time.

Not checked: QA Run All starting several QA agents in the real app; the lock they would hit was checked directly instead.
