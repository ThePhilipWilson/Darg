# AND-0011: Android pairing and TLS in the phone app: scan, pin the certificate, keep the token

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** android, mobile, backend
- **Blocked by:** AND-0002, AND-0003
- **Created:** 2026-11-21
- **Done:**
- **Moved:** 2026-11-23 11:06:27

## Description

The phone side of AND-0002's phone access (docs/REMOTE.md): AND-0003 used a USB route with the instance's token, and its How to check says the paired route isn't wired into the app. Scan the QR Darg's Settings shows, pin the certificate by the SHA-256 in it, trade the pairing code for a token, keep both, and show connection status.

**Where:** `src/Darg.Mobile` (`Connection`, `ConnectionStore`, `DargClient`) and `src/Darg.Android`.

**Done when:** a phone that scans the pairing QR reaches the desktop over port 47818 with the pinned certificate and its own token, refuses a different certificate, and a Revoke in Settings cuts it off.

## Decision answered 2026-11-23 11:06

**Question:** Everything I could automate passed, including pairing on the emulator from the desktop's real QR link. Is that enough for Done, or do you want to scan the QR with your own phone first?
**Asked by:** agent workbench-be (AND-0011 QA), 2026-11-23
- **Done as it is** (recommended) → done: the emulator run covered the link, the pinning, the kept token and the revoke. Only the camera scan is left, and that is Google's scanner, not our code.
- **I'll scan it on my phone first** → stay: the card waits until you've done steps 1 to 3 of How to check on a real phone.
- **Fix the status line first** → back: on the project list the line is cut at "certificate pinned (391ce7…", so the address never shows (second picture). Sent back to wrap it or shorten it.

**Context:** 830/830 unit and 181/181 integration tests pass. On the emulator: a link naming another certificate was refused with nothing kept, the real link paired, and a revoke showed Pair again. The in-app Scan button and the phone's camera opening the link need a camera pointed at a real QR, so I couldn't automate them.

![paired: project list with the cut-off status line](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-and0011-e2e/e2e-2-paired.png)
![connection screen while paired](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-and0011-e2e/e2e-6-settings.png)

**Answer:** Done as it is (2026-11-23 11:06)

## How to check

**Changed:** the phone app pairs from the QR's `darg://pair` link, pins the certificate by its SHA-256, keeps its own token and shows how it's connected. Branch `android`, commit `8be9318` (branch `and-0011` merged in).

**Automatic (done):** `pwsh -File tools/test.ps1 -Filter PhonePairing`: pairing with a host fallback, the saved pairing reopening, a Revoke giving "Pair again", the last revoke stopping the server, a different certificate refused with the code never sent, a wrong code and bad links. Not run on a real phone.

**By hand on a phone (not done by me):**
1. On the PC: Settings → Phone → 📱 Pair a phone (allow the Windows firewall prompt on private networks).
2. Build and install: `pwsh -File tools/android.ps1 -NoPair` (phone on the same Wi-Fi as the PC).
3. In the app, Settings (header) → **Scan the QR** and scan the one on the PC. Or point the phone's own camera at it and tap the link: it opens Darg and pairs.
4. Look for: the project list, with a line "Paired with <PC> · certificate pinned (xxxxxxxx…) · https://<ip>:47818".
5. In Darg's Settings → Phone press **Revoke** on the phone, then ⟳ in the app: it should say Darg no longer knows this phone, with a **Pair again** button.

**Choices I made:** scan with Google's code scanner (no camera permission) plus the camera-app link; the token is kept in the app's private files folder as plain JSON; the old USB route still works. Recorded in docs/design/android.md.

## QA 2026-11-23 (workbench-be)

Checked at `868a751` (branch `android`, holds `8be9318`) in a worktree of its own (`.shots/qa-and0011`).

- **Whole suite** `pwsh -File tools/test.ps1`: unit 830/830, integration 181/181, nothing failed.
- **The tests can fail:** with the certificate check made to accept anything (`PinnedTls.Handler`, a temporary edit in the QA worktree, since reverted), `-Filter PhonePairing` fails exactly the two pin tests (wrong certificate refused, changed certificate refused) and passes the other 4.
- **Android head builds** with the Android toolchain (`D:\Android\dotnet`). The merged manifest has the `darg://pair` VIEW / BROWSABLE intent filter.
- **On the darg emulator against a throwaway debug desktop** (`DARG_HOME` in `.shots/qa-and0011-e2e/home`, `DARG_REMOTE_PORT=47828`), driven by `.shots/qa-and0011-e2e/e2e.ps1`, with the link handed over by `am start -a VIEW -d darg://pair…` the way a camera app does. All 5 steps passed, screenshots `e2e-1` to `e2e-6` beside the script:
  1. A link with another fingerprint, cold start (OnCreate): "That isn't the PC in the QR…", no connection.json, the desktop paired nothing.
  2. The desktop's **real, unmodified** `phone-pair` link (LAN host 192.168.68.73, port 47828), app already running (OnNewIntent): paired. connection.json holds the https address, the desktop's fingerprint, a token that isn't the instance token and the PC name. Settings lists the phone as `sdk_gphone64_x86_64`. So Android's own TLS stack accepts the pinned self-signed certificate.
  3. Cold start with the saved pairing: the project list opens over the pinned route.
  4. Revoked through `phone-revoke` (a second device kept the server up), then a reopen: "Darg no longer knows this phone…" with **Pair again**.
  5. A new link opened while the app was stopped: paired again.

**Couldn't automate:** the in-app **Scan the QR** button (Google's code scanner from Play services), and the phone's camera app turning the QR into the link. Both need a camera looking at a real QR on the PC screen. The emulator's camera shows a fixed virtual scene that can't be pointed at the PC screen. A real phone on the Wi-Fi also goes through the Windows firewall, which the emulator gets round because its traffic starts on the PC itself. Steps 1 to 3 of How to check on a real phone are what's left.

**Worth knowing (not failures):**
- The project list's status line trims to one line, so on a phone it ends at the fingerprint and the address never shows. The connection screen wraps the same line in full.
- When paired, the "Or over USB" boxes come pre-filled with the paired https address and its token in plain text. Pressing Connect there would try the unpinned route, which fails on the self-signed certificate, so nothing is weakened. It is the token on show, though.
- A 401 shows **Pair again** only on the project list. Other pages show the message without the button.
- `Pairer` keeps one cert-mismatch flag across all hosts. If an earlier host shows another certificate and a later one only times out, you get the "isn't the PC in the QR" message rather than "couldn't reach".
