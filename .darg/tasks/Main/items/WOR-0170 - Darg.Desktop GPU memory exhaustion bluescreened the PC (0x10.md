# WOR-0170: Darg.Desktop GPU memory exhaustion bluescreened the PC (0x10E)

- **Status:** Done
- **Priority:** High
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-20
- **Done:**
- **Moved:** 2026-11-20 21:23:23

## Description

Overnight 2026-11-20 the PC bluescreened at ~05:30 with bugcheck 0x10E (VIDEO_MEMORY_MANAGEMENT_INTERNAL, subtype 0x2d). Dump analysis: fault in Windows dxgmms2.sys, VIDMM_CPU_HOST_APERTURE::MapRange, on a Darg.Desktop process thread; the NVIDIA driver (nvlddmkm) is not in the stack, so not a GPU fault. A minute earlier Darg.Desktop threw System.OutOfMemoryException (05:28:59). From 05:12 a QA agent (qa-and0006, builds under .shots/qa-and0006) was repeatedly launching and crashing Darg builds, and a headless qemu (Android emulator) was leaking memory.

To do:
- Find what in Darg.Desktop leaks or over-allocates GPU or CPU-mapped memory (textures, WebView, repeated locks) and fix it.
- Stop QA and agent runs stacking up Darg and emulator instances unattended (cap concurrent instances, kill on failure, clean up after).

Done when: a long run with repeated Darg launches plus the emulator shows flat GPU memory and no OutOfMemoryException.

Evidence: dump copy and symbols in C:/Users/Philip/AppData/Roaming/Workbench/home/dumpcheck (out.txt has the !analyze output).

## How to check

**What it was.** Darg doesn't leak. Windows ran out of memory, and its video memory manager (dxgmms2) bluescreened while mapping memory for a Darg test host (the dump's process "Darg.Desktop.T"). The event log shows a 0xC000012D "out of virtual memory" popup at 05:28:57 and wor-0161's Darg OutOfMemoryException at 05:28:59. Windows flagged a headless qemu as leaking at 05:22. An agent's transcript shows `android.ps1 -Emulator` started an emulator at 05:09 that never came online. The AND-0003 QA agent killed it and left a headless one running with `nohup` at 05:18, beside five worktrees' builds and test suites. That agent's integration run started at 05:30. Darg was measured with the new `tools/perf/gpu.ps1`: 127 to 131 MB GPU and about 650 MB private, flat over 4 minutes of switching sections and over 8 launches with the emulator up (qemu flat at 4.85 GB), nothing left behind.

**What changed** (commit 3b07fa4 on `android`):
- An agent's session end now ends the Darg copies (and test hosts) and Android emulators it left running, with what those started, and nothing else, so claude-draw and Forge live on (`AgentSession.LeftBehind`, `ChildProcesses.Leftovers`). It follows parent ids every 300 ms rather than using a job, because what the Store pwsh starts escapes the caller's job (tested: such a child survived the job's termination).
- `tools/android.ps1`: never starts a second emulator; one that hasn't booted in 5 minutes is stopped; `-StopEmulator`.
- `tools/test.ps1`: at most 2 runs on the machine at once (`DARG_TEST_SLOTS`), each waiting while committed memory is over 85% (`DARG_TEST_COMMIT_PCT`).
- Darg refuses a 5th agent copy at once (`DARG_MAX_COPIES`, default 4): exit code 3 and a crash.log line. The installed and live copies never count.
- FettleChatTests' shared-folder flake fixed.

**Try it:**
1. `pwsh -File tools/android.ps1 -Emulator -NoBuild -NoPair`, then `pwsh -File tools/perf/gpu.ps1 -Launches 8 -Minutes 1`, then `pwsh -File tools/android.ps1 -StopEmulator`. Look for flat GpuDedMB and DargMB per launch, and "Darg copies left running from this build: 0".
2. Close a QA agent that launched a debug Darg. That copy should close within a second or so.
3. Run `pwsh -File tools/test.ps1` from three worktrees at once. The third says "All 2 test slots on this machine are in use" and waits.

**Choices made for you:** 2 test slots, 85% commit, 4 agent copies, and which exe names count as leftovers (Darg.Desktop, Darg.Desktop.Tests, emulator, qemu-system-*). Two things weren't exercised live: the 5-minute stuck-emulator stop (it calls the same `-StopEmulator` code that was) and a real agent's real Darg copy being ended (the integration test uses a stand-in process).

## QA notes

### 2026-11-20 (QA agent)

Checked at 3b07fa4 (the main folder, on `android`). Full suite `pwsh -File tools/test.ps1`: unit 738/738, integration 110/110, nothing failing. What passed, on the real debug build:

- **GPU soak (Try it 1).** Emulator started with `android.ps1 -Emulator -NoBuild -NoPair`, then `gpu.ps1 -Launches 8 -Minutes 1`: GPU dedicated 127 to 136 MB and Darg private 679 to 688 MB at the end of every launch, commit 30.8 GB before and 31.1 GB after, "Darg copies left running from this build: 0". qemu sat at 4.86 GB. `-StopEmulator` stopped all four emulator processes; a second run says "No emulator running."
- **One emulator at a time.** A second `-Emulator` started while the first was booting printed "already running: waiting for it to boot" and no second qemu appeared.
- **Stuck emulator stop (listed as not exercised).** A fake `DARG_ANDROID_ROOT` (`.shots/qa0170/fakeroot`) whose emulator.exe never boots: after 314 s android.ps1 stopped it, threw "did not finish booting in 5 minutes ... it has been stopped", and nothing was left running.
- **Closing an agent (Try it 2, with a real Darg copy, listed as not exercised).** `.shots/qa0170/leftover.ps1 -Mode close`: a fake claude runs Store pwsh, which `Start-Process`es a real debug Darg.Desktop.exe on a throwaway instance and a stand-in emulator.exe, then exits. Both outlived the turn's end, as they should, and both were gone 0.2 s after `close-agents`.
- **Test slots (Try it 3).** Holding both slot files from another process, `test.ps1` printed "All 2 test slots on this machine are in use (...)" and gave up after `-WaitMinutes 1`. `DARG_TEST_COMMIT_PCT=5` printed "memory is 43% committed (over 5%)" and gave up; both slots were free afterwards.
- **Copy cap.** With `DARG_MAX_COPIES=1` and one debug copy running, another exited with code 3 and wrote the refusal to its instance's crash.log. The installed copy at D:/Apps/Darg wasn't counted.

What fails (same script, same fake claude; logs in `.shots/qa0170/left-*.log`):

- **When claude exits on its own, what it left keeps running.** `leftover.ps1 -Mode crash` kills the fake claude after its turn. The agent shows "exited / Session ended (-1)", and 20 s later the debug Darg copy and the stand-in emulator are both still running. They are never ended: `AgentSession.ReadLoop` raises `SessionExited` and `AgentViewModel` (AgentsViewModel.cs:1080) sets `_session = null` without disposing it, so `_group.Dispose()` never runs. A later close has nothing to end, because `End()` returns when `_session` is null. The group also stays in `ChildProcesses.Watch`, so the 300 ms snapshot keeps running for a session that has gone. The card asked to "kill on failure", and this is that path: a claude that crashes, is killed or hits an auth error.
- **When Darg is closed, what agents left keeps running.** `leftover.ps1 -Mode appclose` closes Darg's window normally (exit code 0) with the agent open. 20 s later both leftovers are still running. On close only `vm.SaveAll()` runs (MainWindow.axaml.cs:34); no session is ended. Darg's job kills claude, but the job never had what Store pwsh started. So the CLAUDE.md line "(so Darg's own job misses those too, if Darg is killed rather than closed)" is wrong: they are missed on a normal close too, including the close before a publish or Update restart. Even if close ended the sessions, `End()` fires `DisposeAsync` without waiting for it, and `DisposeAsync` waits up to 3 + 5 s before `_group.Dispose()`, so the app would probably exit first.
- **Small:** the refusal reads "1 other debug or QA copies are running already" (plural with 1).

The fix wanted: dispose the session's group when `SessionExited` fires (or in `ReadLoop` after the exit), and end every live session's leftovers on app close, synchronously, before the process goes. Each needs an integration test that fails without it; `AgentCleanupTests` can kill the fake claude, or close the app, instead of calling `close-agents`.
