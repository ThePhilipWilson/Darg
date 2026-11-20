# Android: approach and scope (AND-0001)

Decided 2026-11-20. Supersedes the open questions in PHONE-PLAN.md. No code yet.

## Decision

**A thin remote client, built as a native Android app with Avalonia, talking to the desktop app's Darg API.** The PC stays the hub; the phone holds no agents, repos or boards of its own.

Three options were weighed:

| Option | Verdict | Why |
|---|---|---|
| Wrap the desktop UI (Capacitor/WebView, PWA) | No | Darg's UI is Avalonia, not web. Nothing exists to wrap; a web front end would be a second UI written from scratch, and a PWA can't hold background notifications or a pairing token as well as an app. |
| Rewrite natively (Kotlin/Compose) | No | Throws away `Darg.Core` (card, board, decision and report parsing, `TicketQuality`, themes, markdown links) and doubles every feature in a second language. |
| **Thin client, Avalonia on Android** | **Yes** | Reuses Core's types and parsers, the theme keys and `MarkdownView`; one language; PHONE-PLAN already assumed it. Phone views are new and narrow-screen, not the desktop views squeezed down. |

"Thin" means: every read and action goes through the desktop's `POST /api/<token>/<command>` (the same code the desktop buttons use). The phone caches what it last saw for reading offline but never edits board or note files itself, so there is one source of truth and no sync conflicts.

Risk to check first (AND-0003): that Avalonia's Android target builds against .NET 10 and that `MarkdownView` renders there. If it doesn't, the fallback is native Compose against the same API; the API boundary makes that a swap of the client, not a redesign.

## What the phone is for

Checking boards, answering what agents and cards are waiting on, and talking to Fettle. Short sessions, reacting to something.

## v1 features

1. **Pairing and connection**: scan a QR shown in Setup, store the token, show connection status. Reachable off the home network (AND-0002 picks the route, Tailscale first).
2. **Needs you** (first screen): every agent question and permission ask, and every card in For Human Attention with its `## Decision`, answered with a tap (`decide`, allow/deny).
3. **Board**: a project's boards, one list at a time, swipe between lists; open a card, read it, move it, send it back with notes (`cards`, `card`, `send-back`).
4. **Agents**: list across projects, read a transcript, send a message, close one.
5. **Fettle chat**: with the project and section in view as context; AskUserQuestion shown as tappable options; voice dictation. Done when a card can be made by dictating.
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
