# WOR-0159: Optimise tokens toggle next to the token bar

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-20
- **Done:** 2026-11-20
- **Moved:** 2026-11-20 18:49:00

## Description

Add an "Optimise tokens" button next to the token bar in the top menu. It is a toggle, so Philip can switch it on quickly when nearing the usage limit to slow token use.

Minimum: while on, agents drop down to lower models. Research is needed on what else is worth doing (lower effort, shorter context, fewer parallel agents, and so on) before building beyond that.

Done when: the toggle shows its on/off state, and turning it on lowers the model for agents; turning it off restores the previous behaviour.

## Decision answered 2026-11-20 18:42

![the header with Optimise tokens on](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa0159/header-on.png)

**Question:** Everything in Done when passed QA. Take WOR-0159 as it is, or send it back for one of the two loose ends?
**Asked by:** agent (QA, Claude Code session), 2026-11-20
- **Take it as it is** (recommended) → done: the card goes to Done; the restart gap below stays as a known edge.
- **Keep hand picks across a restart** → back: send it back to save the "picked by hand" flag with the agent, so a resumed agent's own pick isn't lowered after Darg restarts.
- **Drop the effort cap** → back: send it back so the toggle only lowers the model, as the card first asked, and leaves effort alone.

**Context:** The toggle works live: agents start one model down, running ones switch on their next turn, turning it off puts them back, and the setting survives a restart. Two loose ends: after a restart Darg forgets which picks were made by hand, so the toggle lowers those too; and capping effort at medium was the implementer's choice, not yours.

**Answer:** Keep hand picks across a restart (2026-11-20 18:42)

## How to check

**What changed:** 🍃 Optimise tokens toggle in the header, right of the usage bar (`TokenSaver` in Core). While it's on, every agent runs one model down (Fable→Opus, Opus→Sonnet, Sonnet→Haiku; Haiku stays) and at no more than medium effort. Agents already running switch from their next turn, with a line in their transcript. Turning it off puts each back to what it was given. A model or effort you picked by hand on an agent is left alone. It's saved in settings.json, so it survives a restart. Fettle can switch it through the API's `optimise-tokens` command.

**Commit:** a538b44 on `android` (branch wor-0159, merged and deleted).

**Try it:** run ⟳ Update or `pwsh -File tools/publish.ps1`, then:
1. Look at the header: the toggle sits beside the usage bar and goes pink when on. This shot is from the integration test, which has no usage data, so the bar isn't drawn there: ![toggle on](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0159-on.png)
2. Turn it on, then start Work on a card. The transcript should say "Optimise tokens is on: starting on sonnet, effort medium (it was given opus, effort high)" or similar.
3. With an agent running, turn it off. Its transcript should say "Optimise tokens off: back to …", and its next turn runs on the original model (the agent's header shows the model).

**A choice made for you:** the card asked only for a model drop. This also caps effort at medium, which was the cheapest of the research ideas to add. Not built: fewer parallel agents (e.g. holding ▶ Work All) and shorter context.

## QA notes

### 2026-11-20 QA pass (commit a538b44, checked on `android` at b1a0f29)

- **Test suite:** `pwsh -File tools/test.ps1` all passed: unit 727/727, integration 107/107. No failing tests.
- **Live check, debug build, throwaway instance, fake agents** (`.shots/qa0159/live.ps1`, with a shim that logs the control requests Darg sends): 0 of 24 checks failed.
  - The toggle is found by UI Automation as "Optimise tokens" and is Off at first start. Pressing it through TogglePattern turns it On and writes `"optimiseTokens": true` to settings.json.
  - A card agent on the Deep profile started while it was off runs `--model opus --effort high`. Turning the toggle on sends that running session `set_model sonnet` and `effortLevel medium`, and its transcript says "Optimise tokens on: sonnet, effort medium from the next turn (it was given opus, effort high)".
  - A second agent started while it was on runs `--model sonnet --effort medium`, with the "Optimise tokens is on: starting on…" line.
  - `optimise-tokens {on:false}` through the API turns the toggle Off in the UI and sends both agents `set_model opus` and `effortLevel high`. Both transcripts say "off: back to opus, effort high". A bad value is refused with a 400.
  - Left on, then the app killed and restarted: the toggle is still On and a new agent starts on sonnet/medium.
- **Look:** with a usage reading fed in, the toggle sits right of the usage bar (`today · bar · % · reset · wk`) and before 🔧 Tools: grey when off, pink when on. See `.shots/qa0159/header-on.png` and `header-off.png`.
- **Found by reading the code (not a test failure):** `AgentRecord` saves Model and Effort but not whether they were picked by hand (`_modelByHand` and `_effortByHand` in `AgentViewModel` are memory only). After Darg restarts, a resumed agent's hand-picked model is lowered while the toggle is on, despite "a model or effort you picked by hand on an agent is left alone".
- **The model picker in the agent header** keeps showing what the agent was given (Opus, Effort: high) while it runs lowered. The transcript line and the header's model label from the session are what show the change. This matches the design ("what it was given stays") but may read oddly.
- **What automation can't check:** whether the real Claude Code CLI applies `set_model` and `apply_flag_settings` mid-session. Fake agents don't run a model, and a real check costs a paid session. These are the same control requests the agent's own model and effort pickers already send, so the path isn't new. Whether the look is right is your call.

### 2026-11-20 18:42

Everything in Done when passed QA. Take WOR-0159 as it is, or send it back for one of the two loose ends? I picked: Keep hand picks across a restart.
