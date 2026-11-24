# WOR-0181: Ticket quality star black at 0, no digits

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-20
- **Done:**
- **Moved:** 2026-11-21 00:56:52

## Description

**Your words:** "Ticket quality stars: at 0 they should be black. Remove the numbers next to them; colour alone is enough."

**What's wanted:** The ticket quality star (WOR-0156) at 0 becomes black instead of red, and no card shows a digit beside its star: on the board tile or in the card panel. The colour alone says the score. Hovering still gives the number.

**Where:**
- `src/Darg.Core/TicketQuality.cs`: `TicketScore.Text` (tile, now `★2`) and `TicketScore.Long` (panel, now `★ 2/3`) add the digit; `Colour` names red, amber or green.
- `src/Darg.Desktop/Views/BoardView.axaml`: the quality styles at lines 51-54 (`TextBlock.quality` is `Bad` red, `.amber` is `Warn`, `.green` is `Ok`); the tile's star at about line 231 and the panel's at about line 297.
- `src/Darg.Core/Themes.cs`: a new theme key for the 0 star, given in all three themes (views take colours with `{DynamicResource}`, never a hex).
- Tests that pin the text: `tests/Darg.Core.Tests/TicketQualityTests.cs` (asserts `★3` and `★ 3/3`) and `tests/Darg.Desktop.Tests/TicketQualityTests.cs`.

**Done when:**
- A card at 0 stars shows a black star on its tile and in its panel, with no digit.
- Cards at 1, 2 and 3 stars show a star with no digit, red, amber and green.
- The hover tip still says the score, e.g. "Ticket quality 0/3".
- The black star is visible on the tile in Dark, Light and Girlypop (at least 3:1 contrast, as WOR-0156 measured).
- `pwsh -File tools/test.ps1` passes, with the two test files updated and a 0-star case added.
- The API's `quality` field (`MainViewModel.Api.cs`, line 453) still returns the number. It is not changed.

**Questions left:**
- Black on the Dark theme's dark tile is nearly invisible. Should 0 be black in every theme, or a key that is black in Light and a light colour in Dark?
- The tooltip keeps the number. I assumed that is wanted.

## Decision answered 2026-11-21 00:56

**Question:** On the Dark theme the 0-star is pale (#c9c4d9) rather than black, because black can't reach 3:1 there. Is that how you want it?
**Asked by:** agent QA, 2026-11-21
- **Keep pale on Dark** (recommended) → done: black on Light and Girlypop, pale on Dark; the card goes to done.
- **Black on every theme** → back: sent back to make the 0 star #000000 in Dark too, accepting it is nearly invisible there.
- **Something else on Dark** → back: sent back with your note saying what colour or shape you want for the 0 star on Dark.
**Context:** QA found no faults (unit 788/788, integration 131/131; contrast of Zero on Bg/Panel/Panel2 is 9.4 to 21:1 in all themes; no digit on tile or panel; tooltip and API `quality` unchanged). Only the Dark look is yours to judge; the picture is the Girlypop panel, where the star is black. I could not screenshot Dark: the integration shots are Girlypop and a WebView-free capture of another theme needs the app driven by hand.

![the panel in Girlypop, 0 star black](D:/GitHub/ThePhilipWilson/Workbench/.shots/integration/A_bare_card_is_red_and_Improve_s_rewrite_turns_it_green/panel-red.png)

**Answer:** Keep pale on Dark (2026-11-21 00:56)

## How to check

**Changed:** the star is now a bare ★ on the tile and in the panel. A new theme key `Zero` colours the 0 star: `#000000` in Light and Girlypop, pale `#c9c4d9` in Dark (black can't reach 3:1 on Dark's panels, so this is my call on your question). 1 is red, 2 amber, 3 green. The tooltip still says "Ticket quality n/3"; the API's `quality` is untouched. The Improve prompt now says "(black)" for a 0 card.

**Commit:** 2c9bc45 on `android` (branch wor-0181, merged and pushed).

**Try it:** `dotnet build`, then run `src/Darg.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Darg.Desktop.exe` and open the Board; switch theme under 🎨 Settings. A card with a bare description should show a black star (pale in Dark) with no digit; hover shows "Ticket quality 0/3". No screenshot kept (the worktree holding it is removed).

**Look for:** whether the pale star on Dark reads as "black enough" or whether you want something else there.
