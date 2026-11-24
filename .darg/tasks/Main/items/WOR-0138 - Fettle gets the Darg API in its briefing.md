# WOR-0138: Fettle gets the Darg API in its briefing

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** fettle
- **Created:** 2026-11-18
- **Done:** 2026-11-18

## Description

From the WOR-0135 investigation (see its Findings section). In 10 of 44 Fettle chats it ran `cat artifact-token` and `GET /api/TOKEN` before its first call, guessed `list-agents` (no such command) twice, and once the auto-mode classifier refused reading the token file.

**Want**
- Darg sets `DARG_API` on Fettle's process (`AgentSession.Start` / `MascotViewModel.Args`): the API's URL with the token, so a call is `curl -s -X POST $DARG_API/agents -d ...`.
- The briefing lists every command with its usage line (built from `DargApi`'s registrations, not a copy), and drops "read it before your first call".
- Card agents could get the same variable; decide while doing it.

**Done when**
- A new Fettle chat calls a command first time with no token read and no GET of the list (integration test: fake claude sees `DARG_API` in its env and the command names in its briefing).

Effort: about 45 min.

## How to check

Changed (commit a3d0d16 on main, pushed): every agent's process gets `DARG_API` (the API address with its token); Fettle's briefing lists every command from `DargApi` and tells it to `POST $DARG_API/NAME`, with no token file and no GET of the list. The briefing now goes in `<instance>/fettle-briefing.md`, passed with `--append-system-prompt-file`, because the command list took the command line past the 8191 characters a `.cmd` shim allows (a test caught it). Card agents get `DARG_API` too (same code path); their briefings don't mention it.

Try: run the app (`pwsh -File tools/publish.ps1` or debug build), start a new Fettle chat and ask "which agents are running?". Look for: first call is a POST with no `cat artifact-token` and no GET of the list; no invented command names. Open `fettle-briefing.md` in the instance folder to read what it was given.

Not seen live: a real Fettle turn with the file flag (I checked `claude --append-system-prompt-file` works on haiku, and the integration test reads the file, but fake claude never obeys it).
