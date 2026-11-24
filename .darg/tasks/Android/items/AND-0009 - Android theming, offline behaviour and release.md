# AND-0009: Android: theming, offline behaviour and release

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** android, mobile
- **Blocked by:** AND-0004, AND-0005
- **Created:** 2026-11-19
- **Done:**
- **Moved:** 2026-11-23 22:33:13

## Description

Match the desktop themes (dark, light, girlypop), show last-known data with a clear offline state when the PC is unreachable, add an app icon from Fettle, and produce a signed release APK/AAB for sideloading. Done when: the app is installable without adb and fails gracefully with the PC off.

## Decision answered 2026-11-23 22:33

![the release APK with the PC off, after a cold start](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa9/10-off-16s.png)

**Question:** QA passed on the emulator, and the only part left is installing `D:\Android\release\darg-0.1.apk` on your own phone. Close the card now, or hold it until you've sideloaded it?
**Asked by:** agent (QA of AND-0009), 2026-11-23

- **Done** (recommended) → done: QA is enough; you'll sideload it when you next pick the phone up (back up `D:\Android\keys` first).
- **Hold until I've installed it** → stay: it stays here until you've installed it from Files and looked at the icon and themes on the real phone.
- **Polish first** → back: fix the small things in the QA notes below (banner time, blank first seconds, Fettle over the error line) before closing.

**Context:** Every test passes, and on the emulator the release APK installs, pairs, shows kept screens under the banner with the PC off and recovers by itself when it comes back. No phone is attached to this PC, so the real-phone install can't be automated.

**Answer:** Done (2026-11-23 22:33)

## How to check

**What changed** (commit [6c4b993](https://github.com/ThePhilipWilson/Darg/commit/6c4b993) on `android`):
- **PC off:** the phone keeps the PC's last answer to every read. When the PC doesn't answer, each screen shows what it read last, under a banner: "Offline: the PC isn't answering. What shows is from 21:48; nothing can be sent." It has a Try again button. Anything that does something (a decision, a message) still goes to the PC and says nothing was sent. The banner goes by itself once the PC answers again.
- **Themes:** Dark, Light and Girlypop are at the top of the phone's ⚙ Settings. The phone keeps its own pick, separate from the desktop's.
- **Icon:** Fettle, in its default orange.
- **Release:** `pwsh -File tools/android-release.ps1` builds a signed APK into `D:\Android\release`. It signs with the key in `D:\Android\keys` (`darg-release.keystore` plus `darg-release.pass`), which is never in the repo.

**To try on your phone:**
1. **Back up `D:\Android\keys` somewhere safe.** Android only installs an update over a copy signed with the same key.
2. Copy `D:\Android\release\darg-0.1.apk` to the phone (cloud drive, a message to yourself, USB). Open it, allow "install unknown apps" for whatever opened it, and tap Install. If a debug copy from `tools/android.ps1` is on the phone, uninstall that first: it's signed with a different key, so the release can't install over it.
3. Pair from the PC: Settings → Phone → Pair a phone, then Scan the QR on the phone.
4. Look at the icon in the launcher. Switch themes in ⚙ Settings.
5. Open Needs you, Projects, a project and its Board. Then close Darg on the PC, or take the phone off Wi-Fi, and tap ↻ (or close and reopen the app). Look for the banner and the last-read screens. Then turn the PC back on and tap Try again.

**Emulator screenshots** (release APK installed through Files and the package installer, not adb):
![installer](D:/GitHub/ThePhilipWilson/Workbench/.shots/and9/e6.png)
![offline, cold start](D:/GitHub/ThePhilipWilson/Workbench/.shots/and9/off-cold.png)
![offline project home](D:/GitHub/ThePhilipWilson/Workbench/.shots/and9/off-home2.png)
![Girlypop](D:/GitHub/ThePhilipWilson/Workbench/.shots/and9/theme-girlypop.png)

## Blocked (agent e024c39b23, 2026-11-20)

Nothing to theme, cache or sign yet, and no way to build it here. Checked 2026-11-20: no Android project in the repo (`src/` is Darg.Core/Darg.Desktop only; the `android` branch is `main` plus Fettle commits; AND-0003's scaffold hasn't landed), no Android SDK (no `ANDROID_HOME`, no `%LOCALAPPDATA%\Android\Sdk`), no .NET `android` workload (`dotnet workload list` is empty); JDK 21 and adb are present.

Pieces, to do once AND-0003 lands:
- Install Android SDK + `dotnet workload install android` (~15 min, needs Philip's OK on disk: C: is small).
- Themes: `Themes.All` in Darg.Core already holds Dark/Light/Girlypop key to colour maps; the Android app needs to read them (shared Core) and a picker. Small.
- Offline: cache last-known projects/cards/agents (depends on AND-0002 API client and AND-0004/5 views), banner when the PC is unreachable.
- Icon: `assets/logo.png` exists; Fettle's own art would need a source image (adaptive icon foreground + background).
- Release: keystore (Philip must hold it; never commit it), signed APK/AAB, sideload check on a device.

## QA (2026-11-23)

Checked at 6c4b993 in its own worktree (`.shots/qa-and0009`); no code changed.

**Passed**
- Whole suite, `pwsh -File tools/test.ps1`: unit 838/838, integration 206/206 (MobileOfflineTests' 4 included). No failures.
- **Release APK** (`D:\Android\release\darg-0.1.apk`): apksigner verifies it (v2 and v3) with signer `CN=Darg, O=ThePhilipWilson`, not the debug key. It isn't debuggable and has the assemblies embedded, so it runs with nothing deployed by the IDE. minSdk 26, icon `mipmap-anydpi-v26/ic_launcher` with PNGs for every density. Nothing key-like is in git. `tools/android-release.ps1` rebuilt it from the commit with the same signer, and `-Aab` built `darg-0.1.aab` signed with the same key (keytool -printcert).
- **On the emulator, with the release APK on a clean install:** it paired over the real TLS route from a `darg://pair` link (the same intent a camera scan sends) with a throwaway debug desktop (`.shots/qa9home`). It read Needs you, Projects, the project home and the Board. I then killed the desktop and cold-started the app:
  - About 10 s later the banner showed ("…What shows is from 22:23; nothing can be sent", Try again), along with the kept Needs you, Projects, project home and Board.
  - Tapping a decision said "Nothing was sent: Can't reach https://…:47828: Connection refused…". Add card said the same, and no card file was made.
  - Picking Girlypop in Settings restyled the app at once, and it was still Girlypop after a force-stop and restart.
  - I added QAP-0003 to the board and restarted the desktop. Try again cleared the banner, and Needs you re-read to show the new card.
  - Shots are in `D:\GitHub\ThePhilipWilson\Workbench\.shots\qa9\` (01 to 19).

**QA notes** (small, none blocking)
- The banner's time is the newest of the kept answers shown (`DargClient.Showing` only moves forward). After reading Needs you today and the Board yesterday, the Board would show under "What shows is from <today>". This is the code's stated intent, but the banner's wording promises more than that.
- On a cold start with the PC off, Needs you is blank (no "Reading…" line, no banner) until the first call fails: about 10 s here (`10-off-16s.png` vs `09-off-4s.png`). With the PC asleep on Wi-Fi it's the full 10 s timeout.
- On the Board, the ✦ Fettle button sits over the middle of the "Nothing was sent: …" line, hiding part of the address (`15-off-add.png`).

**Why the rest can't be automated:** installing through the phone's own Files app and package installer, and judging the icon in your launcher, needs your phone. None is attached to this PC, and this agent can't reach it. On the emulator the install went through `adb install`. The dev agent's `.shots/and9/e6.png` shows the Files/installer route on the emulator. Backing up `D:\Android\keys` is yours to do.

**Snag outside this card:** the first `tools/android.ps1 -Emulator -NoBuild -NoPair` run, from Git Bash, failed with "The emulator closed before it finished booting". I then started the emulator directly, and the script's second run waited for it, used it and stopped it at the end (`-StopEmulator`).
