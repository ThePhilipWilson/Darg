# Workbench on the phone and tablet (draft, for Philip to react to)

Written 2026-11-16 overnight. Nothing here is built yet.

## The shape

- **The PC stays the hub.** Agents, consoles, repos and tools run on the PC. The phone is a window onto it, not a second copy of everything.
- **Same codebase, same look.** The phone app is the same Avalonia app built for Android (and iOS later), with layouts for a narrow screen. Views that already exist (board, notes, agent transcript, Bench) are reused, not redrawn.
- **Over the home network, on by default.** The desktop app runs a small link the phone connects to. Pair once by scanning a QR code shown in Setup; after that the phone finds the PC by itself whenever both are on the same network. No "turn on remote" step.

## What the phone shows

Portrait (phone): a bottom bar with **Needs you**, **Agents**, **Board**, **Notes**, **Bench**.
- **Needs you** first: every question and permission from every project's agents, one card each, answered with a tap. This is the thing worth having on the sofa.
- **Agents**: the list across projects, tap to read the transcript (images, diffs, markdown as on the PC), send a message.
- **Board**: one list at a time, swipe between lists, tap a card to read and move it.
- **Notes**: the post-it wall as a single column; add a note by typing or by voice-to-text from the phone keyboard.
- **Bench**: the mascot box, full screen.

Landscape (tablet): the board with the agents list beside it, as in the mockup.

## How it works underneath

- The desktop app gets a **link server**: a WebSocket on the LAN, guarded by the token from pairing. It sends what changes (agent events, cards, notes) and takes actions (answer, allow/deny, send, move a card, add a note). Every action goes through the same code the desktop's buttons use.
- **The work instance can leave the link out** (it's a feature tick box like the others), since work won't want a phone talking to it.
- Notifications on the phone: while the app is open first; background notifications need an Android foreground service, which is a later step.

## Build order

1. The link server in the desktop app, plus a tiny test client, so the protocol is proven before any phone UI.
2. The Android app shell: pairing by QR, connection status.
3. **Needs you** and **Agents** (the most useful part).
4. Board and notes.
5. Tablet layout. iOS last (it needs a Mac to build and an Apple developer account).

## Questions for Philip

- Android, iPhone or both? (Android first is much cheaper to build and test from this PC.)
- Away from home too, or home network only? Away from home works through something like Tailscale; it can come later.
- Is **Needs you** the right first screen, or is it the board?
