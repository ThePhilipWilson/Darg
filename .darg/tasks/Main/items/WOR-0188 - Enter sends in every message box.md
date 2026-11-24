# WOR-0188: Enter sends in every message box, Shift+Enter or backslash-Enter for a new line

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-22
- **Done:** 2026-11-22
- **Moved:** 2026-11-22 23:34:05

## Description

> Enter should send the message in every text box in the app; at present it does in some and not in others. Make it consistent everywhere. Newline is Shift+Enter or backslash then Enter.

Every box that sends or submits something (a message, prompt, note, answer) sends on Enter, with Shift+Enter or a typed `\` then Enter making a new line. Multi-line editors that have no send (card Description, Docs and Design editors, the note's paper, Setup's list boxes) are left alone.

**Where:** `src/Darg.Desktop/Views/EnterSends.cs` is the shared attached behaviour (`views:EnterSends.Command`); it has no backslash rule yet. Some boxes use it (`App.axaml` own-answer boxes, `BoardView.axaml` SendBackNotes, `DesignView.axaml` Plan idea / own answer / Ask planner); others have their own code-behind Enter handler (`MainWindow.axaml.cs:92` Fettle, `AgentsView.axaml.cs:21` agent box); others have none and need checking: `BoardView.axaml` DecisionNotes and AttentionWhy, `ReportsView.axaml` DecisionNotes, `AgentsView.axaml` NewPrompt, `FlowsView.axaml` flow task, `ToolsView.axaml` Prompt and Question. Move the code-behind handlers onto EnterSends so there is one rule.

**Done when:**
- Every box listed above sends on Enter, and Shift+Enter only adds a line.
- Typing `\` at the end of the line then Enter removes the backslash and adds a new line instead of sending.
- Enter on an empty box, or one whose command can't run, neither sends nor adds a line.
- The hand-written Enter handlers in MainWindow and AgentsView are gone, using EnterSends.
- An integration test presses Enter, Shift+Enter and backslash-Enter in at least two boxes (checked failing without the change), and `pwsh -File tools/test.ps1` passes.

**Questions:** none open. Assumed the editors above are out of scope; say so if a Description or note box should send too.

## How to check

**What changed:** `EnterSends` (`src/Darg.Desktop/Views/EnterSends.cs`) is now the one Enter rule: Enter sends; Shift+Enter is a new line; a `\` just before the caret then Enter removes the backslash and adds a line; Enter in a blank box (or one whose command can't run) does nothing. Every box the card lists uses it, and the hand-written handlers in `MainWindow.axaml.cs` (Fettle) and `AgentsView.axaml.cs` (agent box) are gone. On branch `android`, commit `9178691`.

Two mappings were my call:
- **Reports' decision notes:** Enter runs **Send back**, not Approve (the notes are what Send back needs).
- **The card panel's "Why it needs your attention" box:** Enter moves the card to **For Human Attention**. Blank, it does nothing, so you can't move a card there by pressing Enter by accident.

**Try it:** update through the header's ⟳ menu, or run `pwsh -File tools/publish.ps1` from a terminal. Then try Enter, Shift+Enter, and `\` then Enter in:
- the new-agent box and an agent's message box (Agents)
- Fettle's box
- the Flow task box (Flows)
- a card's decision notes (For Human Attention), the attention reason (a card in QA) and the Send back notes
- Reports' notes on a pending decision report
- Tools' Forge prompt and Ollama question

**What to look for:** Enter sends at once. Shift+Enter, or `\` then Enter, gives a new line, with no backslash left and nothing sent. Enter in an empty box does nothing.

**Tested:** `EnterSendsTests` (the new-agent box, an agent's message box and Fettle's box). The Board, Reports, Flows and Tools boxes were only wired up, not pressed.

## QA passed 2026-11-22

Built b9e5041 (holds 9178691) in a worktree of its own. `tools/test.ps1`: unit 827/827, integration 159/159 of the committed tests. A scratch integration test (not committed) did blank Enter, Shift+Enter, backslash-Enter, then Enter with real key presses in the six boxes the card had only wired up: the card's decision notes (answers), the attention reason (moves to For Human Attention), Send back notes, Reports' notes (Send back), the Flow task (runs the flow) and Tools' Forge prompt and Ollama question (run against a dead port). All six passed. Five of them failed with the four views' XAML put back to before this card. Send back notes passed either way because it already used EnterSends.

Small things left as they are: the note's paper still has its own Enter handler, with the same Shift and backslash rules, and Enter in it posts the note, so the new CLAUDE.md line calling it an editor "with nothing to send" isn't quite right. The single-line "How to sort them" hint box has no Enter rule. Only the agent box and Send back placeholders mention the Enter keys, and neither mentions backslash.
