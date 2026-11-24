# Getting started

How to build, run, test and install Darg on this machine, and every environment variable it reads. For how the parts fit together once it runs, see [architecture.md](architecture.md).

## What you need

- **.NET 10 SDK**: this machine also has 7, 8 and 9; the repo targets 10.
- **Claude Code** (`claude`) on PATH, signed in. Agents are real Claude Code sessions, so Darg runs without it but has no agents.
- **Windows**: the primary target. Mac and Linux build; toasts and a few Windows-only things (the job object that kills children, WebView2) differ.
- **Optional**: `gh` for the [GitHub section](guide.md#github), `glab` for GitLab (untested here), Python on PATH for the integration tests' fake claude, and Ollama, Forge, ComfyUI or Pinokio for the [Tools page](guide.md#tools).

## Build and run

```powershell
dotnet build
dotnet test
```

The app is `src/Darg.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Darg.Desktop.exe` on Windows. `dotnet test` rebuilds only Core and the tests, not the app, so run `dotnet build` before launching the exe or you will be looking at the old build. Launch it with a visible window (`Start-Process -WindowStyle Normal`); from an agent's shell the process otherwise runs with no window.

If restore fails with `Unable to find fallback package folder ... Microsoft Visual Studio\Shared\NuGetPackages`, the repo's `NuGet.config` already clears the fallback folders; keep it next to the `.csproj` and solution. On this machine the NuGet cache lives on D: (`NUGET_PACKAGES=D:/nuget/packages`) because C: is small.

## First run and instances

An **instance** is one install's data folder: its projects, settings and saved agents. By default it is `%APPDATA%\Darg\home` (older installs: `%APPDATA%\Workbench\home`, which Darg moves into the Darg folder the first time it starts with no older copy holding it open, leaving a link at the old path; until then it keeps using it). Inside, `instance.json` lists the projects. [Setup](guide.md#setup) edits it: add a folder as a project, tick its features, name its repos, boards and docs. What else the folder holds is in [architecture.md](architecture.md#instances-and-projects).

Instances cannot see each other, by design: the work PC and home PC each run their own ([decisions-and-gotchas.md](decisions-and-gotchas.md#decisions)).

## Environment variables

`DARG_HOME` and `DARG_INSTANCE` also work under their old names, `WORKBENCH_HOME` and `WORKBENCH_INSTANCE` (the app was renamed; `InstanceConfig.Env`). The others answer to their `DARG_` name only.

| Variable | Effect |
|---|---|
| `DARG_HOME` | Use this folder as the instance. Use a throwaway one (`.shots/home`) for anything that writes cards or notes. |
| `DARG_INSTANCE` | Instance name under `%APPDATA%\Darg\` (default `home`). |
| `DARG_SECTION` | Open this section on start: Home, Board, Notes, Docs, Design, Agents, Flows, Reports, Artifacts, Mistakes, Context, Code, GitHub, Preview, Terminal (each described in [guide.md](guide.md)). |
| `DARG_AGENT` | Start an agent with this prompt (in `DARG_AGENT_CWD` if set). |
| `DARG_FLOW` | With `DARG_AGENT`, run this [flow](guide.md#flows) instead. |
| `DARG_ADD_FOLDER` | Open Setup with this folder added as a project (unsaved). |
| `DARG_API` | Set by Darg on every agent process: the address of [the Darg API](architecture.md#the-darg-api) with its token. Agents `POST $DARG_API/<command>`. |
| `DARG_STALL_MS` | Lower the freeze-log threshold (default 200 ms) for `stalls.log`. |
| `DARG_PERF_SECONDS` | Seconds between `perf.log` lines (default 60), see [performance.md](performance.md). |
| `DARG_STACK_MS` | How long the UI thread must be held before its stack is logged (default 1000, minimum 100). |
| `DARG_STACKS` | `0` turns the stack sampling off. |
| `DARG_LIVE=1` | Makes the unit tests also run the live Claude tests (a real Haiku turn). |
| `DARG_LIVE_TREE`, `_BRANCH`, `_SCRIPT`, `_HOST`, `_REPO`, `_GH`, `_FORGE` | Set by `tools/live.ps1` and the live tests. Don't set by hand; `tools/test.ps1` clears them. |
| `DARG_TEST_PYTHON` | Name the Python for the fake claude in integration tests. |
| `DARG_TEST_SLOTS`, `DARG_TEST_COMMIT_PCT` | How many `tools/test.ps1` runs go at once on the machine (default 2), and the committed-memory percentage a run waits below before it builds (default 85). |
| `DARG_REMOTE_PORT` | The port of the [phone server](REMOTE.md) (default 47818; `0` for any free one). |
| `DARG_TEST_QR_PYTHON` | A Python with OpenCV (`cv2`) so the pairing-QR test decodes the QR; without it only its shape is checked ([REMOTE.md](REMOTE.md)). |
| `DARG_ANDROID_ROOT` | Where `tools/android-setup.ps1` and `tools/android.ps1` keep the Android dotnet, SDK and emulator (default `D:\Android`); see [The phone app](#the-phone-app). |
| `DARG_ANDROID_KEYSTORE`, `_ALIAS`, `_PASS` | Sign a Release build of the Android app with this keystore, alias and password (`Darg.Android.csproj`). Unset, Release is debug-signed. |
| `DARG_MAX_COPIES` | How many agents' debug copies of Darg may run at once (default 4; 0 for no cap). The installed and live copies never count. A copy over the cap exits with code 3 and says why in `crash.log`. |

## Tests

```powershell
pwsh -File tools/test.ps1                      # build + unit + integration, about 45 s
pwsh -File tools/test.ps1 -Unit                # Core only, seconds
pwsh -File tools/test.ps1 -Integration -Filter Send_back
```

- **Unit tests**: `tests/Darg.Core.Tests` (xunit, no UI).
- **Integration tests**: `tests/Darg.Desktop.Tests` (xunit v3 with Avalonia.Headless: the real window on a throwaway instance, agents played by `tools/perf/fake_claude.py`). Use `[AvaloniaFact]`, not `[Fact]`.
- **One run per folder**: a second run waits and names the first (`.shots/test.lock`). Run in a worktree of your own when other agents share the main folder.
- **Two runs per machine**: worktrees are separate folders, so `tools/test.ps1` also takes one of two machine-wide slots and waits while memory is short (WOR-0170). Five suites at once beside two emulators ran the PC out of memory.
- **Filters**: `-Filter` matches `Class.Method`, and a filter that matches no test fails.
- **Isolation**: `tools/test.ps1` clears inherited `DARG_*` variables and the Sandbox points `UserHome` and `CLAUDE_CONFIG_DIR` into `.shots/integration/_user`, so tests never read your real timings or settings.
- **What a test leaves**: each integration test leaves `.shots/integration/<test>/`: its instance, board files, `fake-claude.jsonl` (what each agent was started with and sent) and PNGs of what the window showed. That is how you see a view without a running app.
- **No failing test is "unrelated"**: one was called that in every QA pass for a week while it hid a real bug ([decisions-and-gotchas.md](decisions-and-gotchas.md#tests-and-build)).
- **Test boards**: a test board goes in its own folder inside the temp folder. `BoardStore.Create` numbers past every sibling board, so a stray one next to it bumps ids and fails the test.
- **Known flake**: `DevServerTests.Reads_the_address…` counts PING processes machine-wide and fails when other agents are running; it passes alone.

## Installing the copy you use

```powershell
pwsh -File tools/publish.ps1
```

Builds Release into the install folder (`-To`, else `DARG_INSTALL`, else `%LOCALAPPDATA%\Programs\Darg`), makes Start menu and desktop shortcuts and writes `source.txt` (the repo) beside the exe. **Never run it from an agent inside Darg**: it closes the installed copy, which kills the agent's own host (it did, twice). The script refuses; run it from a terminal, or use the header's ⟳ menu.

⟳ Update builds the repo into `<install>.next` while the app runs, then a helper (`Updater`, started outside the app's job) waits for the app to close, copies the build in and restarts it. If the repo moved on after a build, the button reads Rebuild, the header shows `⟳ Update ready •` and Use last build restarts into the existing one. A copy without `source.txt` (a debug build) can only restart. Agents started before an update keep the old instructions until they are restarted.

## The phone app

`src/Darg.Mobile` (plain net10.0: views, view models, `DargClient` over the [Darg API](architecture.md#the-darg-api)) is in `Darg.sln` and tested headless against the real desktop API by `MobileShellTests`, so `tools/test.ps1` covers it. `src/Darg.Android` is the `net10.0-android` head and is **not** in the solution: the system dotnet has no Android workload and installing one needs admin.

```powershell
pwsh -File tools/android-setup.ps1 [-Emulator]        # once: a dotnet with the workload, an Android SDK and a "darg" AVD under D:\Android
pwsh -File tools/android.ps1 [-Emulator] [-NoBuild]   # build, install over adb, adb reverse tcp:47817, write the token in, start it
pwsh -File tools/android.ps1 -StopEmulator            # when done
```

`adb reverse` makes the phone's `localhost:47817` the desktop, so an emulator or a USB phone needs no pairing; a real phone away from the PC pairs through Settings' Phone part ([REMOTE.md](REMOTE.md)). `-Emulator` never starts a second emulator (it waits for the one running) and stops one that hasn't booted in 5 minutes. An agent that starts an emulator stops it with `-StopEmulator` once done: one left running beside several test suites helped run the PC out of memory ([architecture.md](architecture.md#agents)). The approach and scope are in [ANDROID.md](ANDROID.md).

### A release to install without adb

```powershell
pwsh -File tools/android-release.ps1 [-Version 0.2] [-Aab]   # a signed APK (or AAB) in D:\Android\release
```

It signs with `D:\Android\keys\darg-release.keystore`, which the first run makes, with its password beside it in `darg-release.pass`. Both stay out of the repo. **Back that folder up**: Android only installs an update over a copy signed with the same key, so a lost key means uninstalling Darg on the phone (and pairing again) to move to a new one. The version code is the build time in minutes since 2026-01-01, so each build installs over the one before. `apksigner` checks the APK is signed with that key and not the debug key. To install, copy the APK to the phone (USB, a cloud drive, a message to yourself), open it, and let the app that opened it install unknown apps when Android asks. Then pair from Settings → Phone on the PC ([REMOTE.md](REMOTE.md)). An AAB is only for an app store; sideloading takes the APK.

### With the PC off

The phone keeps the desktop's last answer to every read (`AnswerCache`, in an `answers` folder beside its connection file). When the PC doesn't answer, each screen shows what it read last, under a banner saying when that was from, with a Try again button. Anything that does something (a tap on a decision, a message to an agent) still goes to the PC, and with the PC off it says nothing was sent. While offline, a read tries the PC in the background every 5 seconds, so the banner goes by itself when the PC is back and the screen is read again. Forget this connection deletes what was kept.

## The live copy

An experiment (WOR-0126):

```powershell
pwsh -File tools/live.ps1 [-Branch main] [-Shortcut]
```

Runs Darg under `dotnet watch` from its own worktree (`.claude/worktrees/live`, detached at the repo's branch), so agents' half-made edits never reach it. The ⟳ menu reads "⟳ live" and offers Pull in. C# changes apply by .NET hot reload, `.axaml` by HotAvalonia (Debug live builds only; the SDK disables hot reload in Release). An edit hot reload can't take makes `watch` close and reopen Darg, which stops agents mid-turn. It refuses while the installed copy runs on the same instance. Wiring a view's named parts in code-behind goes in `InitializeComponentState()`, which HotAvalonia re-runs after reloading that view; only `MainWindow` does so far. Hot reload doesn't rewire handlers set at startup, so Restart after a Pull in ([decisions-and-gotchas.md](decisions-and-gotchas.md#ui)).

## Seeing the app without clicking

- **Screenshots**: `tools/shot.ps1 -ProcessId <pid> -Out x.png` captures only the app's window (PrintWindow, works when covered). It does not capture WebView2 (Artifacts, Preview) or popup windows (the ▾ shell menu, the ⟳ flyout); use a screen copy or UI Automation for those.
- **UI Automation**: it can press Avalonia buttons by name (look among the window's `Button` descendants). Synthetic clicks don't register on real Windows popups, and a flyout can't be opened while the window is behind another Darg.
- **Crashes and freezes**: a crash in a view goes to `crash.log` in the instance folder instead of closing the app. Freezes go to `stalls.log` (a UI-thread heartbeat logs gaps over 200 ms with the named sections that ran); measure there before guessing at a slowdown. `tools/perf/load.ps1` starts N fake agents on a throwaway board to reproduce load; `perf.log` and `tools/perf/analyze.ps1` are in [performance.md](performance.md).
- **Scratch output**: goes in `.shots/` (git-ignored).
