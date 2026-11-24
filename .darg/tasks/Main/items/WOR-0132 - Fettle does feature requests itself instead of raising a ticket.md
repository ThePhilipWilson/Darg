# WOR-0132: Fettle does feature requests itself instead of raising a ticket

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:** fettle
- **Created:** 2026-11-18
- **Done:** 2026-11-18

## Description

Philip, looking at the Workbench board, said "pressing enter in the send back text box should send the ticket back with the note". Fettle edited BoardView.axaml, built, committed and pushed it, with no card. Philip expected a ticket.

**Why it happened:** Fettle's prompt (`MascotViewModel.cs`, ~line 271) only says to raise a ticket when he asks for a change while looking at an *agent*. For a change request made from the board or anywhere else, it says "quick answers and instructions followed", and says to offer an agent only for long jobs. So a small code change reads as an instruction to carry out, and nothing says a feature or bug request belongs on a card.

**Wanted:** a rule in Fettle's prompt: when Philip describes a change to the app or a project's code ("X should do Y", "pressing Z should..."), make a card on the board he's looking at, saying what he asked, and don't edit code. Edit code only when he says to do it now ("just do it", "fix it", "work on it"). If it's unclear which, ask with AskUserQuestion (New ticket / Do it now). Cards and notes edits he asks for directly are unaffected.

**Done when**
- From the Board, "X should do Y" gets a card in Backlog and no code edit.
- "Just fix it" or "do it now" still edits code.
- The agent-view rule (new ticket vs amend) is unchanged.

## How to check

Checked without Philip (round 2): live tests run real Haiku turns with the rule in the briefing, in a throwaway folder with a board and a source file. "pressing enter in the send back text box should send the ticket back with the note" made card T-0002 in Backlog and left the source untouched; the same plus "just fix it, do it now" edited the source. With the rule removed the first phrase edited code. Commits 3fece8a (rule) and 868a035 (live tests) on main. Run: `$env:DARG_LIVE='1'; dotnet test tests/Darg.Core.Tests --filter FettleRules`.

## QA notes (2026-11-18)

Passed: commit 3fece8a is on main; `FettleRules.ChangeRequests` is appended to Fettle's briefing for every context (MascotViewModel.cs line 242), outside the agent-view block, which is unchanged. Text has no shell-special characters. FettleRulesTests 2/2. Full `tools/test.ps1`: unit 584 all passed; integration 38/39, with `DecisionTests.An_answer_of_my_own_goes_to_a_new_card_agent_when_the_asker_is_gone` failing once in the full run and passing twice alone (not touched by this card; looks flaky, worth its own ticket).

Needs Philip: the Done-when items are about what the model does with the prompt (card in Backlog and no edit on "X should do Y", code edit on "just fix it", agent-view New ticket / Amend). That can't be asserted by automation: it is a live, non-deterministic model reply, and a real Fettle turn would edit real files and commit. Suggested check: ask Fettle the three phrases in the real app, once each. Also note the new rule's own question offers New ticket / Do it now while the agent-view rule offers New ticket / Amend; the agent-view rule is more specific, but the model may blur them.

## QA notes

### 2026-11-18 01:59

why is this in needs human attention? feels like it could be tetsted without me
