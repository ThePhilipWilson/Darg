# WOR-0182: Test runs bind the phone-access server to loopback to stop firewall prompts

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-21
- **Done:**
- **Moved:** 2026-11-22 03:47:07

## Description

> Tests keep triggering Windows Firewall prompts. Make test servers/listeners bind to 127.0.0.1 instead of 0.0.0.0 (or all interfaces) so no prompts appear. Fallback: one admin firewall rule for the runtime exe (node.exe / Electron binary). Done when running the test suite raises no firewall prompts.

**Wanted:** running `tools/test.ps1` should never make Windows ask to allow a program through the firewall. The one listener that binds beyond loopback is the phone-access server, so tests should start it on loopback only. The real app keeps listening on every network, because the phone needs that. (The "node.exe / Electron" in the note is out of date: this is a .NET app, so the exe that would prompt is the test host or Darg.Desktop.exe.)

**Where:**
- `src/Darg.Core/Remote.cs`: `RemoteServer` binds `new TcpListener(IPAddress.IPv6Any, port)` with `DualMode` (line ~275). `RemoteServer.Start(api, instance, port)` needs a way to bind `IPAddress.IPv6Loopback`/`Loopback` instead (a parameter or option, default unchanged).
- `src/Darg.Desktop/ViewModels/PhoneViewModel.cs`: `RemoteHost.PortOverride` (line ~29) and `RemoteServer.Start(Api, port: Port())` (line ~42); tests set the override, so loopback should follow it.
- `tests/Darg.Desktop.Tests/Sandbox.cs` (line ~75, sets `PortOverride = 0`) and `tests/Darg.Core.Tests/RemoteTests.cs` (lines ~95, 147, 148, 158, start `RemoteServer` with `port: 0`).
- Already loopback, nothing to do: `ArtifactServer` (`http://localhost:` prefix), `FakeNtfy`, `FreePort()`. Worth a check: `tools/perf/fake_claude.py`, `fake_gh.py` and anything else a test starts that listens (a Site dev server).

**Done when:**
- `RemoteServer` can be started loopback-only, and the unit and integration tests do so; a test checks the listener's `LocalEndpoint` is loopback in that mode and still any-address by default.
- With the firewall rule for the test host and `Darg.Desktop.Tests` exes removed, `pwsh -File tools/test.ps1` runs to the end with no Windows Security Alert.
- The installed app still pairs a phone over the network (`docs/REMOTE.md`): the default bind is unchanged.
- `pwsh -File tools/test.ps1` passes.

**Questions left:** none. If a prompt still appears after the above, find which exe it names (the alert says) and fix that listener; the admin firewall rule is the last resort, not the plan.

## Decision answered 2026-11-22 03:47

**Question:** Do you want to run the firewall check yourself (admin terminal, steps 1-2 in How to check), or accept the card on the automated evidence?
**Asked by:** agent QA, 2026-11-21
- **Accept on the automated evidence** (recommended) → done: the loopback bind is verified in code and by tests, and the guard test stops regressions; the Windows alert itself is left unchecked.
- **I'll run the firewall check first** → stay: the card stays here until you have removed the rules, run test.ps1 and seen whether an alert appears.
- **Send back** → back: something else needs doing, say what in your notes.
**Context:** All tests pass (unit 791, integration 141) and every test RemoteServer.Start is loopback-only. The Windows Security Alert and a real phone pairing need admin rights and a person watching for a dialog or a phone, so I could not automate them.

**Answer:** Accept on the automated evidence (2026-11-22 03:47)

## How to check

Changed, over two rounds, both on `android` and pushed:
- `fc1d0d6`: `RemoteServer.Start(..., loopbackOnly)` (default false, so the app still binds every address); `RemoteHost` binds loopback whenever `PortOverride` is set, which only the integration tests do.
- `b8a8934` (rework): the certificate-restart unit test that QA found still binding every address now passes `loopbackOnly: true`. A new guard test, `RemoteTests.No_test_listens_beyond_loopback`, scans every test file and fails on a `RemoteServer.Start` without `loopbackOnly: true`, a `TcpListener` not on a loopback address, or an HttpListener prefix on `+` or `*`. I checked it fails on the old line.

What I measured: `.shots/watch-listen.ps1` (in the old worktree) ran the whole suite while polling every 300 ms for TCP listeners and UDP endpoints owned by dotnet, testhost, Darg and python processes. The suite passed (791 + 141), and this branch's test hosts bound only `127.0.0.1`. For comparison, a test run in another worktree from before the fix (`qa0175base`, at `aca3e15`) showed `Darg.Desktop.Tests.exe` on `:::53404` (every address), which is the bind that brings up the prompt.

To check:
1. In an admin terminal, remove the firewall rules for the test exes: `Get-NetFirewallApplicationFilter | Where-Object Program -match 'testhost|Darg.Desktop.Tests' | Get-NetFirewallRule | Remove-NetFirewallRule`
2. `pwsh -File tools/test.ps1` from the repo: no Windows Security Alert should come up. If one does, note the exe it names.
3. In the installed app, open Settings, then Phone, and pair as `docs/REMOTE.md` describes. The default bind is unchanged (`loopbackOnly` defaults to false), but I did not run a real pairing.

## QA notes

- 2026-11-21 (QA agent): `tools/test.ps1`: unit 790/790 pass; integration 135/136. The one failure is `DesignPageTests` (CRLF vs LF in its Goals assertion, WOR-0175's code); it is not related to this card's code, which touches no design code (not re-run without the card).
- **Missed listener:** `tests/Darg.Core.Tests/RemoteTests.cs:145` (`A_phone_pinned_to_another_certificate_...`) still calls `RemoteServer.Start(Api(), () => _home, port: 0)` with no `loopbackOnly: true`, so that unit test binds IPv6Any (every address) and the test host would still raise a firewall prompt. Add `loopbackOnly: true`.
- The default-bind check only asserts the `BindAddress(false)` helper, not a started listener's `LocalEndpoint`. Acceptable since `Start` uses that helper, but it is weaker than the card's "Done when" wording.
- Not checked: the Windows Security Alert itself (needs the firewall rules removed, admin rights and someone watching for the dialog). Re-run How to check steps 1-2 after the fix. Step 3 (pairing in the installed app) not run; in code the default is unchanged (`loopbackOnly = false`; `PortOverride` is only set by tests).
- 2026-11-21 (QA agent, round 2, b8a8934): `tools/test.ps1` all passed: unit 791/791, integration 141/141 (no failures). Read the code: `RemoteServer.Start(loopbackOnly)` defaults false (binds IPv6Any, DualMode); `RemoteHost` passes `loopbackOnly: PortOverride is not null`; every `RemoteServer.Start` in RemoteTests.cs now passes `loopbackOnly: true`, and the guard test `No_test_listens_beyond_loopback` passes. The earlier missed listener (line 146) is fixed.
- Not automated: the Windows Security Alert (needs admin to remove firewall rules plus someone watching for the dialog) and a real phone pairing in the installed app (needs a phone). The default-bind test checks the `BindAddress(false)` helper, not a started listener's endpoint.
