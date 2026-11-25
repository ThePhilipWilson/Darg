# WOR-0203: Make Optimise tokens on/off state obvious

- **Status:** QA Ready
- **Priority:**
- **Type:** Task
- **Labels:** ui
- **Created:** 2026-11-25
- **Done:**
- **Moved:** 2026-11-25 01:58:53

## Description

Philip says it is really not obvious whether the header Optimise tokens toggle is active or not. Make the on and off states clearly distinguishable (e.g. a lit/filled state, a visible label or badge when on).

## How to check

Changed: the header Optimise tokens toggle is filled with the accent colour when on, bold, and its text reads "🍃 Optimise tokens: ON" / "…: off". Branch `android`, commit f95b906 (`wor-0203`).

Try: `pwsh -File tools/publish.ps1` or build and run, click the header toggle. Look for: clearly filled/ON vs plain/off in Dark, Light and Girlypop. The test OptimiseTokensTests checks the label and fill; I did not see it on screen.
