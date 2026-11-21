---
kind: feature
status: agreed
board: Android
parent: darg
---
# Android

Darg on the phone: a window onto the desktop for answering what waits on Philip, not a second copy of it. This is the first plan adopted into the Design section (WOR-0180), written from [ANDROID.md](../ANDROID.md) and the Android board after the cards already existed; [FEATURE-PLANNING.md](../FEATURE-PLANNING.md#android-replayed) shows what planning first would have changed.

## Goals
- Answer what agents and cards wait on (questions, permission asks, card decisions) from the phone, with a tap.
- Check a project's boards and agents, and talk to Fettle, in short sessions away from the PC.
- Get a notification when something needs him, even with the app closed.

## Not doing
- Creating or editing notes and docs, flows, the GitHub section, artifacts with `darg.send()`.
- Tablet layout and iOS.
- Terminal, Code, Preview, Git detail, Setup beyond pairing, publish and update: they stay desktop-only.

## Questions

### How does the phone reach Darg: wrap the desktop UI, rewrite natively, or a thin client over the API?
- **Thin client, Avalonia on Android** (recommended): reuses Core's parsers, theme keys and markdown view; one language.
- **Wrap the desktop UI**: Darg's UI isn't web, so there is nothing to wrap.
- **Rewrite natively in Kotlin**: throws away Darg.Core and doubles every feature.

**Answer:** Thin client, Avalonia on Android (agent for AND-0001, 2026-11-20, on Philip's behalf; keep or change)

### How does the phone reach the PC off the home network?
- **Tailscale** (recommended): nothing goes through anyone else's server.
- **A relay**: works without installing anything on the PC.
- **Home network only**: simplest, no use on the move.

**Answer:** Tailscale (agent for AND-0002, 2026-11-20, on Philip's behalf; not installed, so only the home network works for now; keep or change)

### How do notifications arrive with the app closed?
- **ntfy** (recommended): the phone needs only the ntfy app and internet, not a route to the PC.
- **Firebase push**: needs a Google project and a server of our own.
- **Polling**: drains the battery and misses what happens between polls.

**Answer:** ntfy (Philip, 2026-11-20, asked by AND-0006)

### Where does the Android toolchain go, given the workload needs admin and C: is small?
- **Under `D:\Android`** (recommended): a dotnet with the workload, an SDK and an emulator image, set up by `tools/android-setup.ps1`.
- **The system dotnet and C:**: needs admin and fills C:.

**Answer:** Under `D:\Android` (agent for AND-0003, 2026-11-20, on Philip's behalf; keep or change)

### What is the app id, and who holds the release key?
- **`uk.co.philipwilson.darg`, with Philip holding the key** (recommended): the key is never committed.
- **An id under the studio's name**: Darg carries no studio reference.

**Answer:** `uk.co.philipwilson.darg` (agent for AND-0003, 2026-11-20, on Philip's behalf; the key is left to AND-0009; keep or change)

## Design
A thin remote client: a native Avalonia Android app over the desktop's Darg API, with the PC as the hub. The phone holds no agents, repos or boards of its own and never edits board or note files; it caches what it last saw for reading offline.

- **Reaching the PC:** the API's phone commands on port 47818, TLS with a pinned self-signed certificate, pairing by QR and a token per phone ([REMOTE.md](../REMOTE.md)).
- **The app:** `src/Darg.Mobile` (views, view models, `DargClient` over the API) and `src/Darg.Android` (the head, app id `uk.co.philipwilson.darg`). Phone screens are new and narrow, not the desktop views squeezed down, and use the Core theme keys.
- **Notifications:** what makes a desktop toast also goes to the instance's ntfy topic, a decision's options as buttons.
- **Risk:** Avalonia's Android target on .NET 10 was the one to check first; AND-0003 built it and it held.

The first screen is Needs you: every agent question and permission ask, and every card in For Human Attention with its decision. The full reasoning is in [ANDROID.md](../ANDROID.md#decision).

## Cards

### AND-0001: Decide the approach and scope
The decision, now this plan's Questions and Design. It is a card here only because the board has it.
**Where:** [ANDROID.md](../ANDROID.md).
**Done when:** the approach and v1 scope are written down.
**Blocked by:** none

### AND-0002: Remote access to the desktop Darg API
TLS on its own port, pairing by QR, a token per phone.
**Where:** `src/Darg.Core/Remote.cs` and Settings' Phone part.
**Done when:** a phone calls projects, cards and agents; a request without a token is refused.
**Blocked by:** none

### AND-0003: Project scaffold and app shell
The toolchain, the app project and a shell with a connection screen, project list and project sections.
**Where:** `src/Darg.Mobile`, `src/Darg.Android`, `tools/android-setup.ps1`.
**Done when:** the app builds, installs on the emulator and shows the desktop's projects.
**Blocked by:** none

### AND-0011: Pairing and TLS in the phone app
Scan the pairing QR, pin the certificate, keep the token, show connection status. AND-0003 used a USB route with the instance's token instead.
**Where:** `src/Darg.Mobile` (`Connection`, `ConnectionStore`, `DargClient`) and `src/Darg.Android`.
**Done when:** a phone that scans the pairing QR reaches the desktop with the pinned certificate and its own token, refuses a different certificate, and a Revoke in Settings cuts it off.
**Blocked by:** AND-0002, AND-0003

### AND-0010: Needs you screen
Agent questions and permission asks, and cards in For Human Attention with their decisions, across projects, answered by a tap.
**Where:** `src/Darg.Mobile` and the Darg API's `decide` and agent answer commands.
**Done when:** a waiting card decision and an agent's question both show on the phone's first screen and a tap answers each.
**Blocked by:** AND-0003

### AND-0004: Board view with touch-friendly cards
A project's boards by column, a card opened to read, moved or created by touch.
**Where:** `src/Darg.Mobile` board section.
**Done when:** Philip can triage a backlog from the phone.
**Blocked by:** AND-0003

### AND-0005: Agents list and transcript viewer
A project's agents with state, a transcript to read, a message to send.
**Where:** `src/Darg.Mobile` agents section.
**Done when:** an agent can be watched and nudged from the phone.
**Blocked by:** AND-0003

### AND-0006: Push notifications for decisions and finished agents
A decision or a finished or failed agent reaches the phone through ntfy, with options as actions.
**Where:** `src/Darg.Core` (`Push`), Settings.
**Done when:** a card decision's options can be tapped on the phone's notification and the card is answered.
**Blocked by:** none

### AND-0007: Fettle chat on mobile
The API half first (`fettle-say` and its siblings), then the chat screen with options to tap and voice dictation.
**Where:** `PhoneFettle` in Core, `src/Darg.Mobile`.
**Done when:** a card can be made by dictating to Fettle on the phone.
**Blocked by:** AND-0003

### AND-0008: Notes, docs and reports reading views
Read-only views of notes, docs and reports; the API half is done.
**Where:** `src/Darg.Mobile` notes, docs and reports sections.
**Done when:** a note, a doc and a report can each be opened and read on the phone.
**Blocked by:** AND-0003

### AND-0009: Theming, offline behaviour and release
Dark, Light and Girlypop on the phone, last-known data with a clear offline state, an icon, and a signed release for sideloading.
**Where:** `src/Darg.Mobile`, `src/Darg.Android`.
**Done when:** the app installs without adb and fails gracefully with the PC off.
**Blocked by:** AND-0004, AND-0005
