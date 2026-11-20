# The life of a card

How a card goes from idea to Done, and what Darg does at each step, in the order a card meets them. All of it is behaviour you can see on the [Board](guide.md#board) and in [Reports](guide.md#reports).

## Making a card

A card comes from + Card on the board, a note's Make it a ticket ([guide.md](guide.md#notes)), a GitHub issue, PR or failed run (→ card, [guide.md](guide.md#github)), the API's `new-card`, or [Fettle](guide.md#fettle) (a described change becomes a Backlog card). It lands in the inbox board; Haiku then moves it to the story board it fits (WOR-0087).

✦ Improve this card starts an agent that reads the card, its source note, the project's CLAUDE.md and the code, asks (AskUserQuestion) if the words are ambiguous, then rewrites the card. Improve and Work on from a card no longer jump to Agents; questions arrive as notifications.

## Work on

▶ Work on starts a card agent with the card, moves a backlog card to In Progress and shows the agent live on the card. The agent works on a branch `wor-NNNN` in its own worktree, and merges it fast-forward when done ([architecture.md](architecture.md#agents)). A Bug card's prompt starts "First check it still happens". The card's `Repo:` picks the repo in a multi-repo project.

How the agent finishes is its call (`ReportStore.Rules`):

- **Sure of it, verified**: move the card to Done and write a report as the very last step (so commit ids are final). The agent is forgotten once its turn ends well.
- **Something it couldn't verify itself**: move to QA Ready, with a How to check section in the card (what changed, branch and commit ids, the exact command or file, what to look for), and write a short QA report (`status: qa`; Changed, Look at, Checked). The agent stays so you can talk to it. A QA report never makes Darg forget its agent, and Reports marks it "to check".
- **A choice that's yours**: move to For Human Attention with a `## Decision` ([below](#for-human-attention-and-decisions)).
- **Investigations** (look into something, no code change): a report with `status: investigation`, then Done.
- **CNR** (WOR-0109): a bug it can't reproduce or finds already fixed goes to Done with a `cnr` label (not a column), a `## Could not reproduce` section (what it tried, what it saw, the fixing commit) and a `status: cnr` report, listed under Investigations. Reading code alone doesn't count unless the agent finds the commit that fixed it; if it couldn't run the bug at all, it asks rather than calling CNR (`ReportStore.Cnr`).

Agents have been sent back more than once for parking cards in QA or Attention when automation could have settled them (mistake category `workflow-skip`, see [guide.md](guide.md#mistakes)). The rule now: a card waits on a human only when it genuinely does. If a check can be a test, write the test; if it can't, say why in the card.

## QA

A card in QA with work to look at:

- **▶ Work on**: on a card already in QA it starts an agent that builds and launches the work to try, changing no code (`ReportStore.CheckPrompt`, WOR-0075).
- **🧪 Run QA**: starts an agent that builds and checks the card from its How to check section, preferring automation and saying in the card why anything couldn't be. The card stays in QA unless the agent moves it; it writes no report (Darg writes the done report if it moves the card to Done). Run QA shows unless a QA agent is already on the card (purpose `qa`, WOR-0110). 🧪 QA Run All does it for every QA card with none.
- **↩ Send back** (WOR-0079): from the panel, Reports or the API's `send-back`. You write what's wrong; it goes into the card as a dated entry under `## QA notes`, the card goes back to In Progress and a rework agent starts (`ReportStore.ReworkPreface`: build on How to check, fix the notes, replace How to check, write a fresh QA report).
- **Automatic Run QA** (WOR-0115): when a reworked card re-enters QA, Darg waits for that agent's turn to end (merge and report done) and starts Run QA by itself. Work on in a needs-more-work column does the same. Ordinary work to QA gets no automatic QA.
- **✓ Done and 🔔 For human attention**: on a QA card with no live agent. ✓ Done moves it (and marks its newest unsettled report approved); 🔔 For human attention moves it to For Human Attention and an optional "why" is written under a dated `## Why it needs attention`.

## For Human Attention and decisions

A card waiting on you has a `## Decision` section (WOR-0124), shaped like an AskUserQuestion:

```markdown
    ## Decision
    **Question:** …
    **Asked by:** agent <id>, <date>
    - **Label** (recommended) → done|back|stay: what it means
    - **Other label** → back: …
    **Context:** …
```

The example is indented inside the fence on purpose (see [Known gaps](#known-gaps)); in a card, write it unindented, right below the description.

- **Shape**: two to four options, exactly one recommended; the heading is matched case-blind.
- **In the panel**: it shows at the top, recommended first, with the description folded, and any markdown images in the card above the options, so a "does this look okay" question has the thing to look at (use a full path).
- **Picking**: a pick (or the API's `decide`) writes `**Answer:**` and renames the heading `## Decision answered <date>`, then runs the action: `done` (to Done), `back` (Send back with the question as notes), `stay`, or, with no arrow, the answer goes to the agent that asked, or a new card agent, and the card goes to In Progress. If the answer can't be written, the pick is refused.
- **No decision yet**: a card that lands in attention without one gets one. Once the mover's turn ends it is sent `CardDecision.Nudge`; with no agent, Haiku drafts one (marked "Drafted by Darg… check it fits"; a draft not in the asked shape is refused).
- **Notifying**: a card landing with a decision notifies (id `card:<id>`, which opens the card). The API's `to-attention` writes one and moves the card.

## Reports Darg writes itself

Darg keeps a card's reports as it moves, whoever moves it (WOR-0092, WOR-0102). `ColumnWatch` says which cards entered attention, QA or Done since the last read; the first read only learns, so moves made while the app was closed make nothing. `ReportFlow` writes:

- **Entering attention or QA**: a decision or QA report (`decision: pending`, unread; Asked, Changed, Look at, Checked, Decision) unless a card agent is at work on it or another was written within 2 minutes.
- **Entering Done**: the card's QA reports go to `reports/archive/` (marked approved) and a done report is written carrying the QA report's Changed and Checked, unless a non-QA card agent still at work will write its own.
- **Completed**: counts as done. Cards approved to Done keep an empty `**Done:**` line, since board moves never fill the date (a known quirk).
- **Agents of a Done card**: a card to Done closes and forgets its agents after the done report is written; one mid-turn goes when its turn ends well and stays listed if the turn fails. Reports → Clean up does the same for Done cards that were missed.

`ReportFlowTests` covers this on real board files; `pwsh -File tools/report-flow.ps1` checks it live in the debug build with fake agents.

### Report format

- **Properties** between `---`: `status` (qa, investigation or cnr; none for a plain finished report), `decision: pending|…` on Darg's decision reports, `card`, `title`, `agent`, `commits`, `repo`, `date`, `read`.
- **Sections**: a finished report has Asked, Done, Left; a QA report has Changed, Look at, Checked.
- **File names**: `<yyyyMMdd-HHmm>-<card>.md`, or `<yyyyMMdd-HHmm>-<card>-qa.md` for QA.
- **Drawing**: reports render markdown pictures (given the report's folder as base, so relative paths work) and links.

## Known gaps

- **Stale pending reports**: each new QA or attention entry writes a fresh report and leaves older pending ones; a new one could replace them.
- **Mark done under a decision**: ✓ Mark done still shows under an open decision and moves the card without recording an answer.
- **Code fences**: every `## Section` reader ignores code fences, so a fenced example `## Decision` counts as a real heading and cuts How to check short. Indent examples, as this doc does.
- **Improve duplicates**: Improve agents aren't tied to their card, so pressing Improve twice starts duplicates and nothing on the card shows it is being improved.
- **Git landing** (WOR-0122, agreed 2026-11-18, not built): a per-repo-folder `landing` setting (`direct` default or `mr` with optional `target`), the same deepest-folder lookup as `commits`; MR creation as a button or automatic; branch-name and commit-message patterns configurable per repo (some work repos have fixed rules); Darg cleans up the worktree and branch when it sees the MR merged. Darg never merges by itself. Also listed in [decisions-and-gotchas.md](decisions-and-gotchas.md#not-built-or-open).
