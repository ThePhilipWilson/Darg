# WOR-0121: Make sure the colour scheme is observed

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** 
- **Created:** 2026-11-17
- **Done:** 2026-11-17

## Description

Check the app actually follows the colour scheme. The pink looks slightly off, and the blue isn't used anywhere.

First step: compare the colours in use against the scheme's definition, list the mismatches and where the blue should appear.

## Findings

Source of the scheme: `assets/logo.svg`, pink `#d74f9d` and teal `#34c4bf` (desaturated in WOR-0072; the old `#e83e9f` / `#22d6d0` survive in the untracked `New Project.png`).

Mismatches (`src/Darg.Core/Themes.cs`):
- Dark `Accent` `#e83e9f`, `FluentAccent` `#e83e9f`, `Ok` / `Picked` `#22d6d0`: the pre-WOR-0072 logo colours, now louder than the logo. Light `Accent` `#d92e8f` is likewise more saturated than `#d74f9d`; Light `Ok` / `Picked` `#0b8b86` / `#12b8b2` are darker teals, not `#34c4bf`.
- Hover, soft and wash tints (`AccentHover`, `AccentSoft`, `AccentWash`, `WallDrop`) derive from the old pink.
- Hard-coded hex outside themes: Fettle's amber (deliberate), post-it paper (deliberate), `SetupViewModel.Palette` (project colour choices).

Blue: the logo has none. The only blue in the themes is `Info` (`#8aa8ff` dark, `#4a5fd0` light; violet `#9a5cf0` in Girlypop), used for links in markdown, tool names, and the working dot/chip in App.axaml (4 uses). It would appear for: links, tool names, working state.
