# AND-0001: Android: decide the approach and scope

- **Status:** Done
- **Priority:**
- **Type:** Story
- **Labels:** android, mobile
- **Created:** 2026-11-19
- **Done:**
- **Moved:** 2026-11-23 00:32:16

## Description

Decide how the Android version is built before any code: wrap the existing UI (Capacitor/WebView or PWA), rewrite natively, or make it a thin remote client to the desktop app. Write down what the mobile app is for (checking boards, answering agent decisions, talking to Fettle) versus what stays desktop-only (Terminal, Code, Preview). Done when: a short doc in docs/ records the choice, the reasons and the v1 feature list.


## Decision answered 2026-11-23 00:32

**Question:** Do you accept the approach docs/ANDROID.md records: a thin remote client, a native Avalonia Android app over the Darg API, with the v1 list (Needs you, Board, Agents, Fettle with voice, notifications, read-only notes/docs/reports)?
**Asked by:** agent QA, 2026-11-23
- **Accept** (recommended) → done: the decision stands; AND-0002 to AND-0009 were already built on it.
- **Change the scope** → back: sent back with your notes to adjust the v1 list or the desktop-only list.
- **Rethink the approach** → back: sent back with your notes to reconsider (web wrap or native Kotlin).
**Context:** The doc, its commits (85740ab, c72e11b), the README line and the whole suite (unit 830, integration 161) all check out; the only open item is that the approach was chosen on your behalf without asking.

**Answer:** Accept (2026-11-23 00:32)

## How to check

- **Changed:** new `docs/ANDROID.md` (decision, reasons, v1 feature list, desktop-only list) and a line for it in `docs/README.md`.
- **Branch/commits:** `android`, 85740ab and c72e11b (pushed; `and-0001` merged fast-forward and removed).
- **Open:** `docs/ANDROID.md`.
- **Look for:** I chose on your behalf (no question asked): thin remote client, native Avalonia Android app over the Darg API, rather than a web wrap or Kotlin rewrite. Check you agree, and the v1 list (Needs you first, Board, Agents, Fettle with voice, notifications, read-only notes/docs/reports).
