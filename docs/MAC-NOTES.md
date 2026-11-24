# Running Darg on a Mac: things to fix

First Mac build on 2026-11-24, at commit `c78bb71`, on an Apple Silicon Mac with the .NET 10.0.404 SDK installed user-locally in `~/.dotnet` (the machine only had .NET 9 before, so `DOTNET_ROOT=$HOME/.dotnet` and `PATH=$HOME/.dotnet:$PATH` are needed in the shell until a system install replaces it).

`dotnet build` succeeds with 0 errors and 4 warnings. `dotnet test` gave 61 of 61 on the integration suite, which drives the real MainWindow headless, and 608 of 617 on the unit suite before the fixes below.

## Fixed on branch `mac-path-fixes`

These made 9 of 617 unit tests fail on the Mac, and all 618 (with the new one) pass after it.

- **Full paths in doc links resolved as relative.** The rooted-path branch in `DocLinks.cs` only took a drive path (`D:/x`), so `/var/.../shot.png` lost its leading slash. Off Windows a `/x` link is now taken as a full path when it is under the folder or something exists there, and otherwise stays a path from the folder's root as before (new test `A_path_from_the_folders_root_is_not_taken_for_a_full_path`).
- **File names made on a Mac kept a `:`.** The five `Safe` helpers (reports, report archive, artifacts, mistakes, project data) used `Path.GetInvalidFileNameChars()`, which on a Mac is only `/` and NUL. They all call `FileNames.Safe` now, which uses Windows' set on every OS, so a `.darg` folder made on either machine opens on the other. Nothing changes on Windows.
- **Shell names read from a Windows path on a Mac.** `ShellInfo.RunArgs` (and its test) now treat `\` as a separator before taking the file name.
- **`ProjectDetectTests.Prefixes` expected `VOT` and `PS` for `Alpha` and `Project C`.** The code gives `ALP` and `PC` on any OS, so the expectations are updated; they look left over from the names before they were made placeholders.

## Not tried yet

- Starting a real agent against the Mac `claude`. The app does launch (`src/Darg.Desktop/bin/Debug/net10.0/Darg.Desktop`) and stays up with nothing on stderr; the window has not been looked at yet.
- Notifications. On a Mac `Notifier` builds without `WINDOWS`, and the build warns that `Notifier.Opened` is never used, so clicking a notification probably can't open the agent yet.
- Dictation is Windows-only (Whisper and NAudio are only referenced on Windows), so there is no voice input on the Mac.
- No Mac install path. `tools/publish.ps1` installs to `DARG_INSTALL`, else `%LOCALAPPDATA%\Programs\Darg` (no such folder on a Mac), and there is no `.app` bundle. The updater and the live copy also need `pwsh` on PATH (`brew install powershell`).
- Instance data goes to `~/Library/Application Support/Darg/<instance>` (the default instance is `home`), which is where .NET puts `ApplicationData` on a Mac. CLAUDE.md still says `%APPDATA%`.

## Build warnings

- `NotesViewModel.cs(486)` CS9124: primary-constructor parameter `note` is captured and also used to initialize a member.
- `Dictation.cs(40,41)` CS0649/CS0169: `savedHint` and `lastSound` are unused outside the Windows path, so they need their own `#if WINDOWS` guard.
- `Notifier.cs(10)` CS0067: `Opened` is never raised off Windows (see notifications above).
