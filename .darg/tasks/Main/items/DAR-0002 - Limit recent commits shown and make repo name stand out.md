# DAR-0002: Limit recent commits shown and make repo name stand out

- **Status:** QA Ready
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-24
- **Done:**
- **Moved:** 2026-11-24 12:30:27

## Description

On the Home page, limit the number of commits shown in each repo's recent commits list, and change the colour of the repo name so it stands out more.

## How to check

- Changed: Home's Recent commits tile shows 3 commits per repo (was 5 for one repo, 3 each for several); the repo name now sits on its own in the Accent colour, bold, before the hash/time/author line (only shown when the project has more than one git repo).
- Commit `33a7851` (on `android` and `mac-path-fixes`).
- Full suite 2026-11-25: unit 849 pass; integration 216/217. `TerminalReadTests.Terminal_command_returns_the_last_lines_a_console_shows` fails (also alone; fails without this card's work too, not investigated).
- Run: `dotnet build` then launch the debug app on a project with two git repos (e.g. Site), open Home.
- Look for: at most 3 commits per repo, and the repo name in the pink accent colour in all three themes (🎨 Settings). I couldn't view it myself.
