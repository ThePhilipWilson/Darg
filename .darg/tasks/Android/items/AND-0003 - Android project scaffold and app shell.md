# AND-0003: Android: project scaffold and app shell

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** android, mobile, ui
- **Created:** 2026-11-19
- **Done:** 2026-11-23
- **Moved:** 2026-11-23 00:30:03

## Description

Create the Android project (build, signing, debug install over adb), with navigation between projects and sections and a connection/settings screen for the desktop address and token. Done when: a debug build installs on a device, connects to the desktop and lists projects.

## How to check

**What changed** (commit `b1a0f29` on branch `android`, pushed): a new phone app.
- `src/Darg.Mobile`: the shared screens. A connection screen, the projects list, and a project page with Board / Agents / Notes / Docs / Reports tabs. The tabs are placeholders for AND-0004, AND-0005 and AND-0008.
- `src/Darg.Android`: the Android app, `uk.co.philipwilson.darg`, signed with the debug key.
- `tools/android-setup.ps1`: run it once (I already have). It puts a dotnet with the android workload, an Android SDK and a `darg` emulator under `D:\Android`, because adding the workload to the system dotnet needs admin.
- `tools/android.ps1`: builds the app and installs it over adb. It also forwards the phone's `localhost:47817` to the PC (`adb reverse`), writes your instance's token into the app and starts it.

**Try it on your phone** (the part I couldn't do: no phone was plugged in):
1. Plug the phone in over USB with USB debugging on, and accept the prompt on it.
2. With Darg running on the PC, run `pwsh -File tools/android.ps1` from the repo.
3. Look for: Darg opens on **Projects** listing your six projects. Tapping one shows the bottom bar. Android's back gesture returns to the list. ⚙ opens the connection screen.
4. Re-plugging the cable drops `adb reverse`; `pwsh -File tools/android.ps1 -NoBuild` sets it up again.

No phone? `pwsh -File tools/android.ps1 -Emulator` boots the `darg` emulator first. This is what I checked:

![the emulator listing the desktop's projects](D:/GitHub/ThePhilipWilson/Workbench/.darg/reports/AND-0003-projects.png)

**Choices I made for you:**
- The app id is `uk.co.philipwilson.darg`. It can't change once the app is on the Play Store, so AND-0009 is the time to change it.
- The toolchain lives under `D:\Android`, not in the system dotnet.
- This is a USB development route using the instance's own token. AND-0002's paired route isn't wired into the app yet: QR, TLS on 47818, a token per phone and an allow-list of commands. That's the next step for using it off the cable.
