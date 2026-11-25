# WOR-0211: TerminalReadTests fails: the console's last line is the PowerShell prompt, not the output

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:** tests
- **Created:** 2026-11-25
- **Done:** 2026-11-25
- **Moved:** 2026-11-25 04:12:03

## Description

TerminalReadTests.Terminal_command_returns_the_last_lines_a_console_shows (DAR-0001) fails on every run: the API's terminal command with lines = 1 returns the PowerShell prompt ("PS D:...>") that comes back after the command, so "marker-omega-9152" isn't found. At least six card agents' reports since 2026-11-24 name it and say it needs a card of its own (WOR-0199, WOR-0200, WOR-0201, WOR-0203, DAR-0002, DAR-0003); WOR-0207 saw it fail on b9eb9ac too, without its change. A failing test passed over in every QA run is the thing CLAUDE.md warns about.

Decide what the terminal command should return: the last lines of output with the trailing empty prompt left out (what Fettle wants when it asks what a console shows), or the screen as it is, and fix the test or the command to match.

**Where:** tests/Darg.Desktop.Tests/TerminalReadTests.cs, the terminal API command in src/Darg.Desktop/ViewModels/MainViewModel.Api.cs and the console's Read.

**Done when:** the test passes on three runs in a row of pwsh -File tools/test.ps1 -Integration -Filter Terminal_command_returns, and the whole suite is green.
