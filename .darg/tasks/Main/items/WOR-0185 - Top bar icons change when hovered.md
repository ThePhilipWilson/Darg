# WOR-0185: Top bar icons change when hovered

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-21
- **Done:** 2026-11-21
- **Moved:** 2026-11-21 08:25:07

## Description

> Hovering over the tools, refresh, setup and settings icons in the top bar changes them. Philip reports this as a bug; check what changes (size, colour, icon swap, layout shift) and stop it if unintended.

**Wanted:** the four top bar buttons (🔧 Tools, ⟳ Restart/update, ⚙ Setup, 🎨 Settings) look exactly the same when the pointer is over them. No hover feedback at all (Philip's pick); the tooltip and pointer cursor stay.

**Where:** the header in `src/Darg.Desktop/MainWindow.axaml` (lines ~28-66: the `Button`s with `Background="Transparent"`, `OpenSettingsCommand`, `OpenSetupCommand`, `RestartButton`, `OpenToolsCommand`). Likely cause: they are plain Buttons, so Fluent's `:pointerover` template style (on `ContentPresenter#PART_ContentPresenter`) overrides the local Transparent background and/or foreground. Fix with a style in `src/Darg.Desktop/App.axaml` (like the existing `Button.rail` / `Button.chip` ones), e.g. a `Button.header` class that pins background and foreground on `:pointerover` and `:pressed`, then add the class to those four buttons. Use `{DynamicResource}` theme keys, no hex (all three themes).

**Done when:**
- Hovering each of the four buttons in the Dark, Light and Girlypop themes changes nothing visible (no background, colour, size or position change).
- Clicking still opens Tools, the Restart flyout, Setup and Settings, and tooltips still show.
- An integration test or screenshot check in `tests/Darg.Desktop.Tests` shows the style applied (fails without it); `pwsh -File tools/test.ps1` passes.


## How to check

- **Changed:** a `Button.header` style in `src/Darg.Desktop/App.axaml` pins a transparent background and the `Ink` colour on normal, hover and press; the four top bar buttons in `MainWindow.axaml` use it. `tests/Darg.Desktop.Tests/HeaderHoverTests.cs` hovers each one in Dark, Light and Girlypop and checks background, colour and size don't change.
- **Where:** branch `android`, commit `bc72f11` (pushed).
- **Try it:** run the debug build (`dotnet build`, then `src/Darg.Desktop/bin/Debug/net10.0-windows10.0.19041.0/Darg.Desktop.exe`) and hover 🔧 Tools, ⟳, ⚙ Setup and 🎨 Settings in each theme. Nothing should change; click each, tooltips should still show.
- **Not seen by me:** how it looks on screen. In Light the text is now the theme's Ink instead of Fluent's plain black, a tiny shift in normal state too. The test ran headless only.
