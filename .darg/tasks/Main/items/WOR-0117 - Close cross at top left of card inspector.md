# WOR-0117: Close cross at top left of card inspector

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

On a long ticket the card inspector's close button is only reachable by scrolling down, which is tedious.

**Wanted:** a close cross (✕) at the top left of the card inspector that stays visible while the ticket body scrolls, so a long ticket can be closed without scrolling.

## Done when

- The inspector shows a ✕ at its top left that stays in view while scrolling a long ticket.
- Clicking it closes the inspector, the same as the existing close.

## How to check

- **What changed:** in the card editor (the 400 px panel that opens on a card), a ✕ now sits above the scrolling part, at the top left. It calls the same Cancel command as the Close button at the foot of the card.
- **Branch / commit:** main, commit `5fe8070` (pushed to ThePhilipWilson/Darg). The change is 6 lines in `src/Darg.Desktop/Views/BoardView.axaml`.
- **Try it:** `dotnet build` in `D:\GitHub\ThePhilipWilson\Workbench`, then launch `src/Darg.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Darg.Desktop.exe` and open a card with a long description (or add many lines to one).
- **Look for:**
  1. The ✕ is at the top left of the card panel and stays there while you scroll the description down.
  2. Clicking it closes the panel, the same as Close at the bottom.
  3. It looks right in Dark, Light and Girlypop themes (it uses a transparent background, so the text colour is the theme's).

## QA notes

- 2026-11-17 QA: 5fe8070 builds clean (0 warnings). On a throwaway instance the + Card panel shows a ✕ ("Close card") at its top left, above the scroll area, in Girlypop; invoking it closed the panel. In the XAML it sits in a DockPanel outside the ScrollViewer.
- Needs Philip's eyes: scrolling a long ticket live (clicks could not open an existing card here) and the Dark and Light themes.

Not checked by the agent: how it looks (no screenshot was taken, and WebView-free UI could not be driven here), and clicking it live.
