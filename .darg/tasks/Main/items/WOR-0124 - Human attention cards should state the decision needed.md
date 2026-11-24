# WOR-0124: Human attention cards should state the decision needed

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** 
- **Created:** 2026-11-17
- **Done:**

## Description

When Philip clicks a card in the Needs Phil Attention column (becoming For Human Attention, see WOR-0120), the inspector should show clearly what he has to decide, so he doesn't have to go investigating.

Wherever possible it should be a decision, not a process: a specific question with options he can pick from, ideally rendered in the same form as an AskUserQuestion (question, a few labelled options, a recommended default), rather than a wall of description or a list of steps to follow.

Work out and propose:

- Where the decision lives on the card (a dedicated section or field, shown at the top of the inspector in place of the description)
- How an agent or flow records the decision when it moves a card to human attention: question, options, recommendation, and the context needed to answer it
- How Philip's answer is captured and acted on (moves the card on, or is passed to the agent that asked)
- What to show when a card needs attention but no decision was recorded (fallback, and a nudge to the agent to supply one)
- Making the move to human attention require a decision, so cards don't land there without one

Proposal first, for Philip to react to.

## Decision answered 2026-11-18 11:14

**Question:** The fix passed QA. Is WOR-0124 done as built, or should Mark done be hidden while a decision is open?
**Asked by:** agent 6d455e8b03, 2026-11-18 03:05

- **Done as built** → done: the four differences from the proposal, the themes and Mark done are all fine as they are.
- **Hide Mark done** (recommended) → back: hide Mark done while a decision is open, so a card can't go to Done with its decision still unanswered.
- **Something else** → back: say in the notes which proposal difference or theme to change.
- **Look later** → stay: leave it here until the installed copy runs this build.

**Context:** Round 2 QA passed: suite 590/590 unit and 42/42 integration, both new tests fail without the fix, and round 1's seven extra tests all pass now. Theme shots of the panel are in the artifact "WOR-0124 QA round 2: decision panel in every theme". Mark done moves the card without writing an answer or settling the decision report, so if the card comes back it still asks the old question. That's why Hide is recommended.

**Answer:** Done as built (2026-11-18 11:14)

## Proposal

Full version with a mockup: the artifact "WOR-0124 proposal" (agent a009a97566). In short:

- **Where:** a `## Decision` section in the card file below the description: `**Question:**`, `**Asked by:**`, 2 to 4 options as `- **Label** (recommended) → done|back|stay: what it means`, and a `**Context:**` line. While the card is in For Human Attention, the inspector shows it at the top in place of the description, drawn like an AskUserQuestion (option buttons, recommended first, a box for your own answer), with the description and How to check folded underneath. The decision report and Reports show the same options.
- **Recording:** card, QA, rework and flow agents are told the format wherever they're told about the attention column; a `to-attention` API command takes question, options and recommendation.
- **Answering:** a click writes `**Answer:**` into the section, settles the decision report, then does the option's action: done (to the done column), back (Send back with the pick as notes), stay. With no action, the answer goes to the agent that asked if it's still open, else a card agent starts with question and answer at the top. A `decide` API command lets Fettle answer from the phone.
- **No decision:** an amber "No decision recorded" box with the default question "Is it right as it stands?" (Approve / Send back) and the reason pulled from the card. Darg nudges the agent that moved it (a message asking for a Decision section), or with no agent around has Haiku draft one, marked as drafted.
- **Required:** told up front in every prompt; checked on landing after the mover's turn ends; the 🔔 button and the API take a question and options; a card landing without one is a mistake-log entry, so three flag red.

## How to check

Round 2. The decision feature itself is commit 749aeb6 (https://github.com/ThePhilipWilson/Darg/commit/749aeb6); the QA failure is fixed in fff650c on main (https://github.com/ThePhilipWilson/Darg/commit/fff650c).

**What the fix changed:** a `## decision` heading (any case) used to show its options and move the card on a pick, but the answer was never written, so the decision stayed open. Now reading, answering and replacing a decision all find the section through one case-blind regex, so they can't disagree again. If an answer can't be written, the pick is refused and the card doesn't move. Tests: `Any_heading_read_as_a_decision_is_answered_and_replaced_too` (unit: four heading variants, LF and CRLF) and QA's `A_lowercase_decision_heading_is_answered_like_any_other` (integration). Both fail without the fix; I checked. Full suite: unit 590/590, integration 42/42 (`pwsh -File tools/test.ps1`).

**To try it:** update Darg (⟳ Update, or `pwsh -File tools/publish.ps1` from a terminal). Then put this below the description of a card in For Human Attention, with the heading in lowercase on purpose:

    ## decision

    **Question:** Does dictation work for you?

    - **Works** (recommended) → done: words land in the box.
    - **Still fails** → back: say what the box showed.
    - **Not tried yet** → stay: leave it here.

Click the card and pick Works. It goes to Done, and its file now has `## Decision answered <date>` and `**Answer:** Works`. The round 1 steps (fallback, nudge, Haiku draft, notification) are unchanged, and QA passed them.

**Still yours to judge (from round 1's QA):**
- Where the build differs from the proposal:
  - the 🔔 button takes a "why", not a question and options;
  - Reports shows the options as text, not buttons;
  - an answered decision stays where it was under a renamed heading;
  - the nudged agent logs its own mistake entry.
- **Mark done** still shows under an open decision. It moves the card without recording an answer. Keep it or hide it?
- Light and Girlypop themes: only Dark has been captured.
- Real agents following the Decision rules, and real Haiku drafts. You'll only see these once the installed copy runs this build.

## QA notes

### 2026-11-18 QA

Checked on main at 5578b47, which includes 749aeb6. Full suite (`pwsh -File tools/test.ps1`): unit 584/584, integration 39/39. I wrote seven more integration tests in a throwaway worktree. They are kept at `D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0124/QaDecisionExtraTests.cs`, with a fake_claude patch beside them so Haiku's draft can be scripted. Six pass and one fails.

- **Fails: a `## decision` heading (any case other than `## Decision`) is shown and acted on, but the answer is never recorded.** `CardDecision.Read` goes through `DecisionReport.Section`, which ignores case. `OpenSectionRx`, used by `Answer` and `WriteTo`, is case-sensitive. So on a card with `## decision`, the panel shows the options and picking Works moves the card to Done. But `Answer` quietly returns null, so the section stays open with no `**Answer:**` line. If the card comes back, it still carries the open decision, and `WriteTo` would add a second section instead of replacing the first. The fix: make the two regexes agree (`OpenSectionRx` case-blind, or `Read` case-sensitive), and have `DecideOn` refuse with an error when `Answer` returns null for a recorded decision. Test: `Qa_a_lowercase_decision_heading_is_answered_like_any_other` in the QA file above.
- Passed in the extra tests:
  - Clicking the card's notification (`OpenAgent` with `card:<id>`) opens the Board on the card's decision panel.
  - A card that lands with no agent around gets a Haiku draft. A well-formed draft is written into the card, marked "Drafted by Darg from the card: check it fits", and notifies.
  - A refused draft (fake Haiku's default reply) writes nothing and notifies nothing.
  - An answer in your own words on a card with no decision sends it back with those words as QA notes and starts a rework agent.
  - Notes typed before picking Still fails reach both the QA notes and the `**Notes:**` line.
  - Still fails picked while an agent is at work on the card goes to that agent as a message. No second agent starts.

These need Philip's judgement, not automation (after the fix):
- The four places where the build differs from the proposal, listed under How to check.
- **Mark done** still shows on the panel under an open decision. It moves the card without recording an answer or settling the decision report. Keep it, or hide it while a decision is open?
- The Light and Girlypop themes: only Dark was captured. The integration tests don't switch theme before `Shot`, so this needs a screenshot test that sets the theme first, or Philip's eye.
- Real agents following `CardDecision.Rules`, and real Haiku drafts. These can only be seen once the installed copy runs this build. The installed copy in D:/Apps/Darg was built 2026-11-17 17:54, before 749aeb6, and this QA agent's own prompt didn't include the Decision rules.

### 2026-11-18 QA round 2

Checked on main at fff650c, from the main folder and a throwaway worktree (since removed).

- Passed: full suite (`pwsh -File tools/test.ps1`): unit 590/590, integration 42/42.
- Passed: both new tests fail with only the fix's source files put back to fff650c~1. In `Any_heading_read_as_a_decision_is_answered_and_replaced_too`, 3 of 4 variants fail; the 4th is the `##   Decision  ` control. `A_lowercase_decision_heading_is_answered_like_any_other` fails too, and so does round 1's own copy of it.
- Passed: round 1's seven extra integration tests (`.shots/qa-0124/QaDecisionExtraTests.cs`, with the fake_claude draft patch) all pass on fff650c, including the lowercase one that failed before. Notification click, Haiku draft written and marked, refused draft writes nothing, own answer with no decision, typed notes on Still fails, Still fails to the working agent.
- Passed: the How to check scenario as written. A `## decision` card in For Human Attention, Works picked: Status Done, heading renamed `## Decision answered 2026-11-18 02:51`, `**Answer:** Works (2026-11-18 02:51)`, and How to check kept below it.
- Read: `Read`, `Answer` and `WriteTo` now share `OpenSectionRx` (case-blind). That is the same pattern `DecisionReport.Section` used, so no heading `Read` took before is lost. The new refusal in `DecideOn` is defensive only: `DecisionOf` reads the file fresh, so it can only fire if the file changes between the read and the write. I didn't test it.
- New this round: Light and Girlypop. Test `.shots/qa-0124/round2/QaThemeShotTests.cs` switches theme through the API and captures the panel with a decision and the fallback in all three themes (PNGs beside it). All are legible. How they look is yours to judge.
- Side note, not this card: a card approved to Done keeps an empty `**Done:**` line. Board moves in Darg never fill the date (`Approve` → `Move`).

Can't be automated, so it's for you (ticket candidates):
- The four proposal differences and Mark done under an open decision are product choices, so no test can settle them. The Decision above asks.
- Real agents following `CardDecision.Rules`, and real Haiku drafts. Fake claude plays every agent in the tests. A live run needs `DARG_LIVE=1`-style tests with real model turns, and none exist for this. The installed copy (D:/Apps/Darg, built 2026-11-17 17:54) predates 749aeb6, so it shows nothing until it's updated. A live test that has a real Haiku draft a decision from a card and checks `ParseDraft` accepts it could be ticketed.
