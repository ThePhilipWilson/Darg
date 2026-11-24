# WOR-0156: Automatic ticket quality score (red/amber/green star) beside ticket code

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-20
- **Done:**
- **Moved:** 2026-11-20 01:56:57

## Description

Every card should automatically get a ticket quality metric shown next to its ticket code: a star scored out of 3, coloured red / amber / green. It is assigned automatically to any ticket; a very bare ticket should be red. Using Improve ticket should re-evaluate the score and improve the ticket if needed.

## Decision answered 2026-11-20 01:56

**Question:** Do the star colours read well enough in all three themes, given amber and green are faint in Girlypop?
**Asked by:** agent QA (WOR-0156 QA run), 2026-11-20

![Dark](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0156/qa-0156-dark.png)
![Light](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0156/qa-0156-light.png)
![Girlypop](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0156/qa-0156-girlypop.png)

- **Fine as it is** (recommended) → done: the colours are readable enough; the card is done.
- **Darken Girlypop's amber and green** → back: sent back to deepen Warn (and Ok) in Girlypop to at least 3:1 against the card tile.
- **Make the star bigger or bolder everywhere** → back: sent back to raise the star's size or weight on the tile in every theme.

**Context:** Everything else passed by automation. Measured contrast of the star against the card tile: Dark all above 5:1; Light amber 3.1:1; Girlypop amber 2.3:1 and green 2.4:1 (below the 3:1 usual for small coloured text). Green there is the theme's existing Ok colour, amber the new Warn key.

**Answer:** Fine as it is (2026-11-20 01:56)

## How to check

**What changed:** every card now shows a star out of 3 beside its id, on the board tile (`★2`) and in the card panel's heading (`★ 2/3`). A point each for saying what's wanted (20 words or more of description), where it lives (a `Where:` label or `## Where` heading, a file or path, or code in backticks) and when it's done (a `Done when:` label or heading, Acceptance, Expected, Steps to reproduce, or a `- [ ]` checklist). Red at 0 or 1, amber at 2, green at 3; hover it for what's there and what's missing. The score reads the description plus the sections after it, but not How to check, QA notes, Decision or Could not reproduce. ✦ Improve's prompt now carries the score and asks for `**Where:**` and `**Done when:**` labels, and the star rescores as soon as the card file changes, so an improved card goes green on its own.

**Choices I made for you:** a fixed rule (instant, free, on every card), not a Haiku judgement; "green" is the theme's existing Ok colour (teal in Dark), amber is a new `Warn` theme key. On the real Workbench board it gives 89 cards at 0, 30 at 1, 21 amber, 19 green: notes turned into tickets are red, improved cards green.

**Branch / commit:** `android`, commit 27143c3 (pushed): https://github.com/ThePhilipWilson/Darg/commit/27143c3

**Try it:** update the installed copy (⟳ menu, or `pwsh -File tools/publish.ps1` from a terminal), open the Board.
1. Old one-line cards (WOR-0001 to WOR-0019) show a red ★0; WOR-0102 or WOR-0146 show a green ★3. Hover a star: the tooltip says what's missing.
2. Open a card: the heading reads `WOR-… ★ n/3`; type into the description and watch it change colour.
3. Press ✦ Improve this card on a red one: once its agent rewrites the card, the star should go green without a reload.
4. Check the colours in Dark, Light and Girlypop (🎨 Settings).

Screenshots from the integration tests (Dark):
![amber on the tile](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0156/board-amber.png)
![green on the tile, red in the panel while typing](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0156/panel-red.png)

**Tests:** `pwsh -File tools/test.ps1 -Filter TicketQuality`. Full suite after rebase: unit 641/641, integration 70/70.

## QA 2026-11-20

Checked on `android` at 158a70a (contains 27143c3), in a worktree of its own.

- Full suite `pwsh -File tools/test.ps1`: unit 672/672, integration 79/79 (integration rerun alone with `-Integration` after my first run hit my own 10-minute limit; nothing failed).
- Debug build on a throwaway instance (`.shots/qa-home`) with four cards written to score 0, 1, 2 and 3: the tiles read ★0 ★1 (red), ★2 (amber), ★3 (green), and the open card's panel reads ★ 2/3 in amber. Shot in Dark, Light and Girlypop through the `theme` API (screenshots in the Decision).
- Improve's prompt carrying the score, a file rewrite turning the star green without a reload, and the panel star following typing: covered by `TicketQualityTests` (integration), which passed.
- **Can't be automated:** whether the star's colours and size read well. Contrast is measurable (numbers in the Decision) but whether it is good enough is a taste call. Also, Avalonia doesn't expose the star TextBlock or its tooltip to UI Automation, so the tooltip text was checked only through the headless integration test, not the running app.
