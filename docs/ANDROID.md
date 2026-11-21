# Android: approach and scope (AND-0001)

History: the plan for this feature now lives in [design/android.md](design/android.md) (WOR-0180, on 2026-11-21), which the Design section shows beside each card's live column. This doc is the decision as made, and is not updated as cards finish.

Decided 2026-11-20: how Darg reaches Android, what the phone does in v1 and what stays on the desktop. It replaces the open questions in [PHONE-PLAN.md](PHONE-PLAN.md). The desktop half is coming in (phone access in [REMOTE.md](REMOTE.md), AND-0002; AND-0004, AND-0006, AND-0007); the phone app's scaffold and shell are in (AND-0003, below); the cards under Order fill in its screens.

## Decision

**A thin remote client, built as a native Android app with Avalonia, talking to the desktop app's [Darg API](architecture.md#the-darg-api).** The PC stays the hub; the phone holds no agents, repos or boards of its own.

Three options were weighed:

| Option | Verdict | Why |
|---|---|---|
| Wrap the desktop UI (Capacitor/WebView, PWA) | No | Darg's UI is Avalonia, not web. Nothing exists to wrap; a web front end would be a second UI written from scratch, and a PWA can't hold background notifications or a pairing token as well as an app. |
| Rewrite natively (Kotlin/Compose) | No | Throws away `Darg.Core` (card, board, decision and report parsing, `TicketQuality`, themes, markdown links) and doubles every feature in a second language. |
| **Thin client, Avalonia on Android** | **Yes** | Reuses Core's types and parsers, the theme keys and `MarkdownView`; one language; [PHONE-PLAN.md](PHONE-PLAN.md) already assumed it. Phone views are new and narrow-screen, not the desktop views squeezed down. |

"Thin" means: every read and action goes through the desktop's `POST /api/<token>/<command>` (the same code the desktop buttons use; [architecture.md](architecture.md#the-darg-api) lists the commands). The phone caches what it last saw for reading offline but never edits board or note files itself, so there is one source of truth and no sync conflicts.

Risk to check first (AND-0003): that Avalonia's Android target builds against .NET 10 and that `MarkdownView` renders there. If it doesn't, the fallback is native Compose against the same API; the API boundary makes that a swap of the client, not a redesign.

## Where it stands

AND-0003 built the shell, and the risk above did not bite: Avalonia's Android target builds on .NET 10 (`src/Darg.Android`, app id `uk.co.philipwilson.darg`, outside `Darg.sln`; how to build and run it is in [getting-started.md](getting-started.md#the-phone-app)). `src/Darg.Mobile` holds the views and view models: a connection screen for the desktop's address and token (pasting the API address Darg gives fills the token in), the project list from the `projects` command, and a project page with its sections along the bottom (Board, Agents, Notes, Docs, Reports), each a placeholder naming the card that fills it, with a back stack wired to Android's back button. It was checked on the emulator against a running desktop, and `MobileShellTests` drives it headless against the real API.

## What the phone is for

Checking boards, answering what agents and cards are waiting on, and talking to Fettle. Short sessions, reacting to something.

## v1 features

1. **Pairing and connection**: scan a QR shown in Setup, store the token, show connection status. Reachable off the home network (AND-0002 picks the route, Tailscale first).
2. **Needs you** (first screen): every agent question and permission ask, and every card in For Human Attention with its `## Decision` ([cards-and-qa.md](cards-and-qa.md#for-human-attention-and-decisions)), answered with a tap (`decide`, allow/deny).
3. **Board**: a project's boards, one list at a time, swipe between lists; open a card, read it, move it, send it back with notes (`boards`, `card`, `move-card`, `send-back`).
4. **Agents**: list across projects, read a transcript, send a message, close one.
5. **Fettle chat**: with the project and section in view as context ([architecture.md](architecture.md#fettle)); AskUserQuestion shown as tappable options; voice dictation. Done when a card can be made by dictating.
6. **Notifications**: a decision or a finished or failed agent, with the options as actions (AND-0006; delivery with the app closed is the open part).
7. **Read-only** views of notes, docs and reports (AND-0008), and Dark/Light/Girlypop themes (AND-0009).

Not in v1: creating or editing notes and docs, flows, GitHub section, artifacts with `darg.send()`, tablet layout, iOS.

## Stays desktop-only

Terminal and consoles, Code, Preview/Site, Git and GitHub detail, Setup beyond pairing, flow authoring, publish and update. They need a keyboard, a process on the PC, or a screen the phone doesn't have. Anything the phone needs from them arrives as a card or a decision, not a mirrored view.

## What this needs from the desktop

- The API reachable beyond localhost, with an authenticated, encrypted route (AND-0002). Unauthenticated requests are refused.
- Any command the phone needs that isn't there yet (a pending-decisions list across projects, pairing, push registration) is added to the API, not worked around.
- The work instance can leave the link off.

## Order

AND-0002 access, AND-0003 scaffold, then Needs you and Agents (AND-0005), Board (AND-0004), Fettle (AND-0007), notifications (AND-0006), read views and release (AND-0008, AND-0009).
