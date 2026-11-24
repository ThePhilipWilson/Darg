# WOR-0115: Run the QA flow automatically when a reworked card returns to QA

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:** 2026-11-18

## Description

Found on WOR-0102: after QA sends a card to needs more work, nothing sends it on to the QA flow once it has been reworked. Philip expected Work on and then the QA flow, with no manual step.

**Wanted:** when an agent finishes reworking a card in needs more work and the card goes back to QA Ready, Darg starts the QA flow (the 🧪 Run QA agent from WOR-0108) on it without being asked.

**Related:** the Work on button on WOR-0102 was hidden by a finished agent still being counted as on the card; fixed in `fe745a0` (`AgentsViewModel.ForCard`). This card is the separate follow-up for the flow.

## How to check

Nothing for a person to check: Darg verifies this itself. `CardWorkTests` (integration, real MainWindow with fake agents) covers it, run 2026-11-18, passing: a card sent back from QA via `send-back` gets a QA agent only after the rework agent's turn ends, not while it works; a card reaching QA from ordinary Work on gets none. Code in commit `2a44a97`, tests in `5a29b13`, both on main.

## Where

- `src/Darg.Desktop/ViewModels/MainViewModel.cs`: the `Board.EnteredQa` hook (WOR-0102) is where a card arriving in QA is noticed.
- `src/Darg.Desktop/ViewModels/AgentsViewModel.cs`: `QaRunningFor`, so a second QA agent isn't started.

## Done when

- A card moved into QA Ready after rework starts a QA agent automatically, unless one is already running on it.

## Questions left

- Every entry into QA Ready, or only cards coming back from needs more work? Default: only those coming back from needs more work.

## QA notes

- 2026-11-17: build OK; 548/549 tests pass (the failing one is the TicketTitle test, unrelated). Fake-agent run (.shots/wor-0115/run.ps1): reworked card got a (QA) agent only after its turn ended; ordinary card got none. Not exercised: real agents, and the ↩ Send back path (only Work on from needs more work). Your call: the open question (only rework returns vs every QA entry) was answered with the default.

### 2026-11-18 00:08

shouldn't be in this column, this should be something that can be determined by darg
