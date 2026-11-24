# WOR-0135: Investigate giving Fettle quick access to Darg data

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** fettle
- **Created:** 2026-11-18
- **Done:** 2026-11-18

## Description

Philip's observation: a lot of the time Fettle grasps around (reading files, guessing paths, trial and error) to find information that Darg already holds and should expose through an API. Fettle should have simple, quick access to anything within Darg.

**Investigate**
- Where Fettle currently fumbles: look at recent Fettle transcripts and list the questions it answered by file-hunting or several calls instead of one lookup (cards, board state, agents and their status, notes, docs, what's open in the UI, project config, git state).
- What the Darg API (`GET /api/TOKEN`) already exposes versus what Darg knows internally but doesn't expose.
- Options for closing the gap: new read endpoints (e.g. search across cards/notes/docs, project overview, agent summaries), richer context in the `[Philip is looking at: ...]` line, or a single "state of the project" call.
- Whether the prompt should list the API's commands up front instead of Fettle fetching them each chat.

**Done when**
- A short write-up lists the common lookups Fettle struggles with, what's missing from the API, and a recommended set of endpoints or context changes, with rough effort for each.
- Philip picks what to build; follow-up cards are raised for that.

## Findings

Read all 44 Fettle sessions (Claude Code transcripts for the instance folder, 2026-11-16 to 18), reduced to message → tool calls.

**Where Fettle fumbles, most common first**
1. **Making a card** (about 23 cards in 18 turns). Every time: `cat board.json`, `ls items | tail`, `cat` an existing card to copy its shape, then Write plus one or two Edits or `sed` on board.json to bump nextId and add to the backlog. 4 to 6 calls a card; hooks blocked its Python or sed three times; once it read a guessed path that wasn't there. Its cards also skip the Haiku story-board placement a note's ticket gets.
2. **Finding the API every chat.** `cat artifact-token` + `GET /api/TOKEN` in 10 chats; guessed `list-agents` (no such command) twice; once the auto-mode classifier refused reading the token file, and it fell back to files.
3. **Card state across cards.** `grep Status: items/*.md`, `head -c 2500` over 19 cards to triage QA, a Python join of `agents` against card Status to see which agents sit on Done cards (twice), `ls items | grep WOR-0102` to find a card's file.
4. **Why a button isn't showing.** Needed which agents are on a card and in what state, plus the card's column. It read Darg's source for that; `agents` answered half of it.

No fumbling seen for notes (Organise passes the paths), reports (the context line has the path), docs, mistakes or flows.

**The API now:** 21 commands, nearly all actions. Reads: `projects`, `where`, `agents`, `agent`, `flows`, `theme`. Darg holds but doesn't expose: cards by column with their agents, one card with its decision, QA notes and reports, the Home tiles (cards in flight, agents needing him, unread reports, last commits), creating a card (`BoardStore.Create` + `StoryBoards.Place` exist in Core), git state per repo, notes, reports list, usage.

**Options, rough effort (each with its integration test)**
- A. Put the API in the briefing: `DARG_API` env var on Fettle's process holding the URL with token, and the command list with usage lines (about 1k tokens, once a session). ~45 min.
- B. `new-card {project, title, description, type?, labels?, priority?}`: `BoardStore.Create` on the inbox, then Haiku places it on a story board as a note's ticket is. Returns id, board, file. ~1.5 h.
- C. `cards {project, column?, label?, text?}` (id, title, column, type, labels, file, live agent) and `card {project, id}` (that plus text, decision, QA notes, reports, all agents on it). ~2 h.
- D. `overview {project}`: the Home tiles as JSON, plus uncommitted count per repo. ~2 h.
- E. Context line: when a card is open, add its column and file path. ~15 min.
- F. `search {project, text}` across cards, notes, docs, reports. ~2.5 h. Grep already served it where it searched.

**Recommended:** A, B, C first (they cover 1 to 4), then E. D if a "what's going on" question turns up; F not yet.

**Picked (2026-11-18):** all five. Raised WOR-0138 (A, API in briefing), WOR-0139 (B, new-card), WOR-0140 (C, cards and card), WOR-0141 (D, overview), WOR-0142 (E, context line).
