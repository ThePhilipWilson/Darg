# WOR-0123: Move all documentation into the project repos

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** 
- **Created:** 2026-11-17
- **Done:**

## Description

Philip wants all project documentation in a Darg-friendly, source-controlled location: inside the project's own repo, by default, especially anything Darg creates. Some projects will make this awkward, but "docs live in the project repo" is the default to aim for.

Today docs are scattered: some projects keep them in the Obsidian vault (`vault/Code/<Project>`), some in a Workspace folder, some under Workbench's own `home/projects/<id>/docs`, none of which is necessarily under version control alongside the code.

Investigate and propose, for Philip to react to:

- Where docs live now for each project (Alpha, Beta, Project C, Site, Claude Draw, Workbench) and whether that location is under source control
- A default docs location inside each repo (e.g. `docs/`), and where Darg-created docs (reports, specs, write-ups) are written
- What breaks on the move: Obsidian `[[links]]`, the vault setup, Darg's docs section and per-project docs path setting, agent prompts that reference doc paths
- A migration plan per project, with the hard cases called out (projects where in-repo docs is difficult) and a fallback for them
- Whether notes and board cards should follow the same rule

Proposal first; no files moved until Philip has picked an approach.

## Decision answered 2026-11-18 11:27

**Question:** QA passed everything it could check. Do the five new docs-move cards (WOR-0144, CD-0001, VOT-0080, CHO-0032, PS-0077) say what you want agents to do?
**Asked by:** agent (QA of WOR-0123), 2026-11-18
- **Yes, close it** (recommended) → done: the cards stand as written and WOR-0123 is finished.
- **Some cards need changes** → back: say which and what in your notes. An agent fixes those cards and nothing else.
- **Not now** → stay: leave it in For Human Attention until you've read them.

**Context:** The move itself checks out: the docs moved unchanged apart from the studio-name swap, everything is pushed, and Docs opens them in a headless run of the app. The full test suite passes. Only the content of the cards is yours to judge.

**Answer:** Yes, close it (2026-11-18 11:27)

## How to check

Proposal and decisions: Artifacts, "Docs into the project repos" (`docs-in-repos.html`). You picked: `docs/` in the repo, `.darg/` gitignored with opt-in, Darg only (no Obsidian junction), commit Alpha's docs now, pilot now and cards for the rest.

What changed:
- **Darg repo** `main` @ `af5c3d0`: `docs/` holds PLAN.md, MORNING.md, PHONE-PLAN.md, mockup-v1.html, moved from the vault. The studio name and account (4 mentions) became `example-studio`, because this repo carries no studio name. CLAUDE.md and CLAUDE.local.md point at `docs/`.
- **Vault**: `906e635` commits Alpha's Art/Design/Tech/UI docs (31 files that git had only as deletions since `17deb00`). `de6cded` removes `Code/Darg`. `49a167b` adds the cards VOT-0080, CHO-0032 and PS-0077.
- **Workspace** (Site) `92984e9`: commits its 5 docs, untracked since June.
- **instance.json**: Workbench's `docs` is now `D:/GitHub/ThePhilipWilson/Workbench/docs` (it pointed at the vault's `Code/Workbench`, which no longer exists).
- **New cards**: WOR-0144 (Darg code: `.darg/` and repo docs first), CD-0001 (Claude Draw, public repo).

To try:
1. Restart Darg (⟳ menu, or close and reopen). The running copy read instance.json at start and still has the old path; I can't restart it from inside.
2. Open Workbench → Docs: the four plan docs should list and open, and PLAN.md's link to MORNING.md (in its last section) should open it.
3. Read `docs/PLAN.md` line 65 and `docs/mockup-v1.html` (search example-studio) and check the replacements read right to you.
4. Look over the five new cards (Backlog on the Workbench, Alpha, Beta, Project C and Claude Draw boards) and check they say what you'd want an agent to do.

The vault board.json files for Alpha, Beta and Project C list the new cards but aren't committed: they also hold uncommitted edits from Darg (the attention column), which I didn't sweep into my commit.

## QA (2026-11-18)

Checked by automation, all passed:
- Darg repo: `af5c3d0` is on `origin/main`. `docs/` holds PLAN.md, MORNING.md, PHONE-PLAN.md and mockup-v1.html, which match the vault copies (`de6cded^:Code/Darg/`) except for line endings and the studio-name swaps. There are 5 swapped lines, not 4: PLAN.md 65 and 96, mockup 321, 441 and 446. All read sensibly. `git grep` finds no studio name in tracked files, and none appears in the log. CLAUDE.md and CLAUDE.local.md point at `docs/`.
- Vault: `906e635` adds 33 files, not 31; they are the same 33 that `17deb00` deleted. `de6cded` removes `Code/Darg`, and `49a167b` adds the three cards. The vault is level with origin. The Alpha, Beta and Project C `board.json` edits are still uncommitted, as the card says.
- Workspace `92984e9` is pushed and its `docs/` has the 5 files. instance.json's Workbench `docs` is `D:/GitHub/ThePhilipWilson/Workbench/docs`.
- Docs section: I ran a throwaway headless integration test (not committed) on a copy of the real `docs/`. The Docs tree lists MORNING, PHONE-PLAN and PLAN, and each opens through the API's `show doc`. A mouse click on PLAN.md's rendered MORNING.md link opened MORNING.md. Nothing went to crash.log. The screenshots looked right.
- Full suite `tools/test.ps1` at `19b9b5a`: unit 590/590, integration 44/44.
- All five new cards exist and are listed in their boards' `board.json`. WOR-0144 is already In Progress.

Notes:
- The Docs section lists only `.md` files, so it shows **three** plan docs, not four. `mockup-v1.html` doesn't appear there; PLAN.md links to it, and that link opens it in the browser. This was already the case in the vault, so it is not a regression.
- Not done by me: restarting your installed Darg, because closing it would kill running agents. The headless run used the same folder contents, and instance.json's path is verified.
- Why automation can't settle this card: whether the five cards ask for what you want is your call, not something a check can decide.
