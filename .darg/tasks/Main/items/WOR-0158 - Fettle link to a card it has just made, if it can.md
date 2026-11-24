# WOR-0158: Fettle: link to a card it has just made, if it can

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** fettle
- **Created:** 2026-11-20
- **Done:** 2026-11-20
- **Moved:** 2026-11-20 21:22:11

## Description

Related to WOR-0157. When Fettle makes a ticket it should reply with a clickable link to the card. If Fettle replies cannot currently render or give links to cards, make that possible (e.g. a card link in the reply that opens its panel).

## How to check

**What changed:** a card ID in Fettle's replies (and in any agent transcript or doc) is now a link that opens the card's panel on the Board. Fettle is told to write `[WOR-0160](card:WOR-0160)` for a card it made, and the API's new-card hands it that link ready-made. A card ID written bare (WOR-0160, or **WOR-0160** in bold) is linked too, as long as its prefix is one of your boards'. IDs in backticks, inside another link, or in lower case (branch names like wor-0160) stay as they are.

**Commits** on `android`: 01f8e0c (the links), bcb41ae (WOR-0157's "say what you did" rule now uses the link form in its example). Also in 01f8e0c: a fix to `tools/perf/fake_claude.py`, whose log lost lines when two fakes wrote at once. That made my new test time out in the full run.

**To try it:** update Darg (⟳ Update, or `pwsh -File tools/publish.ps1` from a terminal), then ask Fettle to make a throwaway card ("make a card: test link, delete me").
- Fettle's last line should name the card as an underlined link; hovering it says "Open WOR-xxxx".
- Clicking it should switch to Board with that card's panel open, even if you were on another section.
- Also try an older Fettle chat or an agent transcript that mentions a WOR-number: it should now be a link too.

**Screenshots** from the integration test (fake chat on a throwaway instance):
![Fettle's reply with the link](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0158/fettle-reply.png)
![after the click](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0158/card-open.png)

**Couldn't check:** a real Fettle (a live Claude session) actually writing the link. The tests use a fake Claude and a seeded reply, so whether the live model follows the briefing is what to look at. If it writes the ID bare, it is still a link.

## QA notes

### 2026-11-20 (QA agent)

Checked at bcb41ae in a worktree of its own. Build clean; full suite `tools/test.ps1`: unit 670/670, integration 79/79, nothing failing. With `MarkdownView.OpenCard` unwired, the three `CardLinkTests` reply cases fail ("collection was empty") and pass again with it wired, so the test guards the change. The unit tests cover bare IDs, bold, branch names, code, existing links and prefixes from board.json. The screenshots show the underlined link, and after the click the Board opens with the card's panel.

- **Missing space in Fettle's briefing (bcb41ae).** `src/Darg.Core/FettleRules.cs:21` now ends `...in the backlog.)." +` with no trailing space, so the briefing Fettle gets reads `...(Made [WOR-0151](card:WOR-0151) in the backlog.).If you could not do it, say so and why.` The old line ended `.). " +`. Put the space back (the doubled `.).` could be just `).` too). A one-character fix; no test reads `SayWhatYouDo` closely enough to catch it.
- For your eyes, not a failure: after the click, the Fettle box stays open over the lower half of the card panel (type, priority and the fields below are hidden; see card-open.png above). The link still works. Whether Fettle should fold down when a link opens a card is a judgement call.
- Couldn't check: whether a live Fettle (a real Claude session) actually writes `[ID](card:ID)`. The tests use fake Claude and a seeded reply. A bare ID still links, so the risk is small.
