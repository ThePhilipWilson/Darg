# WOR-0137: Align theme colours with the logo

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** theme, colour
- **Created:** 2026-11-18
- **Done:** 2026-11-18

## Decision answered 2026-11-18 11:29

**Question:** Do Dark and Light look right against the logo when you switch between them in Settings?
**Answer:** Looks right (checked on the swatch page)
**Asked by:** agent QA, 2026-11-18
- **Looks right** (recommended) → done: the card goes to done.
- **Needs tweaking** → back: sent back for more work with your notes on which colours are off.
- **Leave for now** → stay: stays here until you have looked.
**Context:** Automated checks all pass; only the "checked by eye" part of Done when can't be automated, since it is a judgement of how the colours look.

**Answer:** Looks right (2026-11-18 11:29)

## Description

Act on the WOR-0121 investigation (report `20261117-1756-WOR-0121.md`). The themes in `src/Darg.Core/Themes.cs` still use the old logo colours; WOR-0072 desaturated the logo (`assets/logo.svg`: pink `#d74f9d`, teal `#34c4bf`) but not the themes.

**Change**
- Dark `Accent` and `FluentAccent` (`#e83e9f`) become `#d74f9d`.
- Dark `Ok` / `Picked` (`#22d6d0`) become `#34c4bf`.
- Light `Accent` (`#d92e8f`) and its teals (`#0b8b86`, `#12b8b2`) are re-based on the logo pink and teal, darkened as needed to keep contrast on a light background.
- Re-derive `AccentHover`, `AccentSoft`, `AccentWash` and `WallDrop` from the new pink in every theme.
- Leave Fettle's amber, post-it paper and the project colour palette alone (deliberate).

**Blue**
- The logo has no blue, so the scheme doesn't define one. Default: keep `Info` as it is (it only appears in markdown links, tool names and the working dot or chip, which is why it seems absent). Raise a follow-up if Philip wants it used more widely.

**Done when**
- No theme colour in `Themes.cs` is a leftover of the old pink or teal.
- Dark and Light look right against the logo, checked by eye in the app.

## How to check

- Changed: `src/Darg.Core/Themes.cs`, Dark and Light only. Commit `f6db314` on main (branch wor-0137, merged and pushed).
- Dark: Accent/FluentAccent `#d74f9d`, Ok/Picked `#34c4bf`; hover `#e56fb2`, soft `#371f30`, wash `#281a23`, WallDrop `#33d74f9d`; teal tints (OkBg, OkLine, AddInk, DirInk) and user bubble nudged to match.
- Light: pink `#c13c88` (hover `#a52f73`, soft `#f9dfee`, wash `#fcf0f7`, WallDrop `#33c13c88`); teal text `#1a8783`, Picked border `#34c4bf`.
- Try: run Darg, 🎨 Settings, switch Dark and Light. Look at buttons, needs-you rows, Ok dots, picked cards, notes-wall drop, against `assets/logo.svg`. Info blue, Girlypop, Fettle and post-its are unchanged on purpose.

## QA notes (2026-11-18)

- Unit 590/590 and integration 44/44 pass (`tools/test.ps1`); no failing tests.
- No leftover old colours (`#e83e9f`, `#22d6d0`, `#d92e8f`, `#0b8b86`, `#12b8b2`, old hover/soft/wash) anywhere in `src` or `tests`. Dark Accent/FluentAccent = logo pink `#d74f9d`, Ok/Picked = logo teal `#34c4bf`, matching `assets/logo.svg`. Girlypop, Info, Fettle untouched in the diff.
- Contrast (WCAG): Light white-on-accent 4.92, hover 6.45, accent on bg 4.61, teal text `#1a8783` on white 4.34 (slightly under 4.5 for small text); Dark button text 5.22, accent on panel 4.58, teal on panel 8.16.
- Not automatable: whether the palette looks right against the logo is an eye judgement. The WebView/popup surfaces don't show in shot captures either.
