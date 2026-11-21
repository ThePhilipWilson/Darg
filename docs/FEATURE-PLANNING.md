# Feature planning: the Design section

How Darg helps plan a feature, or a whole project, before its cards exist, so a story board starts from a plan rather than a pile of guessed cards (written 2026-11-20 for WOR-0171). It answers four questions: what a plan holds, where it lives, how it becomes a board and cards, and how it stays current as cards finish. Each answer gives the recommendation and the alternatives turned down. Then it replays the Android feature through the design, and lists the cards that build it. Nothing here is built yet.

## Why

The Android board was made by hand on 2026-11-19: nine cards, AND-0001 to AND-0009, written in one go. What happened next shows what a plan is for:

- **The decision came after the cards:** AND-0001 ("decide the approach") sat on the board beside the cards that depended on its answer. Its agent then chose the approach on Philip's behalf and wrote [ANDROID.md](ANDROID.md).
- **No order:** every card was moved to In Progress at the same second, 2026-11-20 01:09:51 (six of the nine `Moved:` lines still read that; AND-0002, AND-0003 and AND-0006 moved again later). Five needed AND-0003's scaffold first: AND-0009 stopped three minutes later as blocked, AND-0004 ended in a work-in-progress commit "waiting on AND-0003's Darg.Mobile shell", and AND-0007 and AND-0008 each did their desktop half and stopped.
- **Choices buried in build cards:** how the phone reaches the PC (AND-0002's agent picked Tailscale for him), how notifications arrive with the app closed (AND-0006 asked, ntfy), the app id (AND-0003's agent picked it for him).
- **Gaps between cards:** the Needs you screen, which [ANDROID.md](ANDROID.md#v1-features) calls the first screen, has no card. Neither has pairing in the phone app: AND-0003's How to check says AND-0002's paired route "isn't wired into the app yet", and no card owns it.
- **The doc went stale the same day:** [ANDROID.md](ANDROID.md) kept saying "the phone app isn't built yet" after AND-0003 built it, until a docs card (WOR-0154) caught it hours later. Nothing tied the doc to the cards made from it.

## The answer in short

- **What a plan holds:** goals, what it isn't doing, the questions only Philip can answer (with his answers), the design, and the cards it makes, each with Where, Done when and what it's blocked by.
- **Where it lives:** a Design section of its own, a page per plan, for anything from a feature to a whole project (Philip's call, 2026-11-20). The plan is a markdown file in the project's `docs/design/`, so it is committed with the code.
- **How it becomes cards:** a planner agent drafts the plan from a line Philip types; he answers its questions on the page and ticks the cards; one press makes the story board and the cards, with their Blocked by lines. Dependencies are a feature of every card, not just planned ones, and Darg holds a blocked card back.
- **How it stays current:** the board holds state and the plan holds intent. The page reads progress from the board and the reports live, and writes nothing about it into the plan. What does change the plan is a choice made on the way: an agent that makes one records it in the plan's Questions for Philip to keep or change.

## What a plan holds

Recommended: six parts, the same for every plan, so the page can show and edit each one and the planner knows what to fill.

- **Goals:** what is different for Philip when it's done, a few lines. Not features: "answer what agents wait on from the sofa", not "an Android app".
- **Not doing:** what is out, so a card agent doesn't drift into it ([ANDROID.md](ANDROID.md#v1-features)'s "Not in v1" list is this).
- **Questions:** each choice only Philip can make, shaped like an AskUserQuestion: the question, two to four options with one recommended, and his answer with its date once given. The planner puts every call it isn't sure of here instead of settling it. An agent's choice made later lands here too, marked as such (see [Keeping it current](#keeping-it-current)).
- **Design:** free markdown: the approach, the parts, what it builds on, risks. [ANDROID.md](ANDROID.md#decision)'s Decision section is this.
- **Cards:** a heading per card, each with its description, `**Where:**`, `**Done when:**` and `**Blocked by:**`. A card that has been made carries its id in its heading. For a whole project this part is Features instead (see [Whole projects](#whole-projects)).
- **Log:** what has happened since it was agreed, built by the page from the cards' reports and moves, never written into the file.

Turned down:

- **Goals, scope, design only, with cards made on the board afterwards:** that is how Android went. The cards are where order and gaps show, so they belong in the plan, where Philip sees them side by side before they exist.
- **A free-form doc with no fixed parts:** the page couldn't offer answers to tap, cards to tick or a live status, and every planner run would invent its own shape.
- **Status written into the plan (done ticks, percentages):** it goes stale the way [ANDROID.md](ANDROID.md) did, and it is a write to a committed file every time a card moves.

### The plan file

One markdown file per plan in the project's docs folder under `design/`, beside the `Stories/` folder the done-column wrap-up writes (WOR-0074), so a feature's plan and its story end up next to each other. Properties between `---` lines, as notes have:

```markdown
---
kind: feature
status: agreed
board: Android
parent: darg
---
# Android

Darg on the phone: a window onto the desktop for answering what waits on Philip, not a second copy of it.

## Goals
- Answer agent questions and card decisions away from the PC.

## Not doing
- Editing notes or docs, flows, the GitHub section, iOS, tablet layout.

## Questions

### How does the phone reach the PC off the home network?
- **Tailscale** (recommended): nothing goes through anyone else's server.
- **A relay**: works without installing anything on the PC.
- **Home network only**: simplest, no use on the move.

**Answer:** Tailscale (Philip, 2026-11-20)

## Design
A thin remote client: a native Avalonia Android app over the Darg API…

## Cards

### AND-0002: Remote access to the desktop API
TLS on its own port, pairing by QR, a token per phone.
**Where:** `src/Darg.Core/Remote.cs`, Settings' Phone part.
**Done when:** a phone calls projects, cards and agents; a request without a token is refused.
**Blocked by:** none

### Pairing in the phone app
…
**Blocked by:** AND-0002, AND-0003
```

`status` is set by people (draft, agreed, dropped); building and built are worked out from the cards. `board` names the story board once made. `parent` makes the tree (a feature under its project plan). A proposed card's Blocked by names other cards in the plan by id when they have one, else by heading.

## Where it lives

Recommended, and Philip's own call when asked (2026-11-20): "a separate page for design, I don't want to have to shoehorn it into the board just because it's what we have. A bespoke design page for managing anything from features to whole projects."

### The Design section

A rail section of its own beside Board and Docs:

- **On the left:** the project's plans as a tree: the project plan at the top, its features under it, drafts and dropped ones marked. A + Plan box at the top takes a line and a kind (feature or project).
- **On the right:** the picked plan as a page in its six parts. Goals, Not doing and Design are edited in place. Each question shows its options as buttons; a tap writes the `**Answer:**` line. Cards show as rows: a proposed card with a tick and its ticket score ([TicketQuality](cards-and-qa.md#making-a-card)'s stars), a made one with its id, column, live agent and open blockers, read from the board each time it shows. A header line gives the status and progress (7 of 12 cards done) and links the story board.
- **Beside the page:** the planner's chat, the agent that drafted this plan, kept per plan so asking it to change a part has the plan's history.

The plan file is the source; the page is a view and an editor of it, as the Notes wall is of its note files. So agents, Obsidian and git all see the same plan.

### Turned down

- **A plan card at the top of its board** (my first recommendation, before Philip's answer): it would reuse [For Human Attention and decisions](cards-and-qa.md#for-human-attention-and-decisions) for the approval and the phone push for free, but a plan isn't a unit of work: it is the thing the work comes from, with parts to answer and tick that a card panel doesn't have. And the board lives in `.darg/`, which isn't committed, so the design history would be lost.
- **A flow** ([guide.md](guide.md#flows)): flows run fully automatic, and the flow generator's own rule is that no stage waits for Philip mid-flow. Planning is mostly his answers.
- **Fettle:** one session for every project, and its replies scroll away. It is a good way in (the `plan` API command lets Fettle and the phone start a plan from a sentence) but not the home.
- **The Docs section:** it reads markdown well but has nowhere to tap an answer, tick a card or see a card's live column. The plan files still show there, as docs.

### The planner

Planning starts from a line Philip types ("Android app: answer agents from the phone"), and the planner drafts every part before asking anything: he judges a draft in seconds, but writing from a blank page is slow, so an interview first would cost him the expensive part. Its prompt (`Planner.Prompt`, built as `FlowGenerator.Prompt` is) tells it to:

- Read the project's CLAUDE.md, docs, boards and existing plans, and the code the feature touches.
- Draft Goals, Not doing and Design; put each call it isn't sure of in Questions with options and a recommendation, rather than choosing quietly.
- Propose cards that each score 3/3 on the ticket score, with Blocked by lines, small enough that one agent finishes one.
- Look for gaps: a part of the Design no card owns, or a card whose Done when needs something no earlier card makes.

It writes only the plan file, and commits it as the project's commit rule says.

Built in WOR-0176: Darg names the file before the planner starts, so the page shows "drafting" and then the plan when its turn ends. Its chat has a Change picker: with a part picked (Goals, Questions, Cards…), anything the planner changes outside that part is put back when its turn ends, since a model told "only that part" still tidies next door now and then. A plan written by hand gets a planner on its first message from the chat.

### Whole projects

A plan with `kind: project` has Goals, Not doing, Questions and Design as a feature plan does, and a Features part in place of Cards: a line per feature, linked to its feature plan once there is one. Ticking features and pressing the button starts the planner on each, with `parent:` set to the project plan. The tree then shows the project with its features under it, each with its progress rolled up from its cards. Darg's own [PLAN.md](PLAN.md) is this shape: the ask, the big calls, then the slices that became its first boards.

## From plan to board

Recommended: Philip ticks the cards on the page (all ticked to start) and one press makes them. Darg does the making in code, not the planner agent, so ids, order and blockers come out right every time:

- **The story board:** made if the plan has none (`BoardStore.CreateBoard`, with the project's columns and a prefix from the plan's name, as AND was), or the one named in `board:`. It joins the project's boards, so the Board section shows it and `StoryBoards` (WOR-0087) can place stray cards on it.
- **The cards:** made with `BoardStore.Create` in Blocked by order, each plan reference turned into the new id, each id written back into its heading in the plan. A card under 3/3 is refused with what it's missing, so the planner fixes it first.
- **Open questions:** listed above the button; making with any still open needs a second press, since a card built on an unanswered question is how AND-0002 came to choose for Philip.
- **Again later:** pressing it again makes only the rows with no id. A row removed from the plan never deletes its card; the page marks the card as no longer in the plan, for Philip to drop by hand.

The `plan-cards` API command does the same, for Fettle.

Turned down:

- **The planner calling `new-card` per card:** ids race with other agents making cards, and the blockers can't name ids that don't exist yet.
- **An approval decision on a card in For Human Attention:** asked as the second question on 2026-11-20; Philip's answer was the Design section itself, so the approval is the page's ticks and its button.

### Blocked by

Philip, 2026-11-20: "we need a dependency system for tickets so that we can block on another ticket, and display this correctly." So dependencies belong to every card on every board, and a plan's Blocked by lines are just the first to use them.

- **On disk:** a `- **Blocked by:** AND-0002, AND-0003` line among the card's properties, after Labels. Tatty and Obsidian ignore a field they don't know, as they do `Moved:`. Ids are found across the project's boards (`CardIndex`), so a card can wait on one elsewhere.
- **Cleared when** every blocker is in the QA column, done or Completed (Philip's call when asked, 2026-11-20). QA counts, because a card agent merges its branch before moving to QA, so the work is there to build on and dependent cards can start while QA waits on him. The cost: a card built on work that QA then sends back may need its own rework. Turned down: clearing only at done, which is safer but leaves dependent cards idle for as long as QA does.
- **On screen:** a tile with an open blocker shows a chip naming it (the `Warn` theme key) until it clears. The card panel lists Blocked by and Blocks as card links, and Blocked by can be edited there. The `boards` command carries both, so the phone shows them too. A cycle or an unknown id shows as a warning.
- **Held back** (Philip's pick when asked, 2026-11-20): ▶ Work All starts only unblocked cards and says how many it left out. Work on a blocked card asks inline first. The API's `work-on` refuses one unless told `force`.
- **Context for the agent:** a card agent's prompt lists its blockers with the path of each one's newest report, so AND-0004's agent would have started from AND-0003's report rather than from a guess.

Turned down: blocked cards started automatically when they clear. It would spend sessions without Philip asking for them; the chip going away is enough for now.

## Keeping it current

Recommended: the board holds state, the plan holds intent, and only a change of intent is written to the plan.

- **Progress is read, never written:** the page asks the board for each made card's column and agent, and `ReportStore` for its reports, every time it shows. The Log part is the plan's cards' reports and moves (what [ColumnWatch and ReportFlow](cards-and-qa.md#reports-darg-writes-itself) already see), newest first. Building and built come from the same reads.
- **Choices made on the way are written:** a card agent working a planned card is given the plan's path, and told that a choice the plan didn't settle goes into its Questions as decided by the agent, with the option it took, in the same commit as the work. The page shows such a question with Keep and Change; Change sends the card back with the new answer as its notes, as ↩ Send back does. AND-0002's Tailscale and AND-0003's app id would each have appeared there the moment they were made.
- **A change to the plan reaches the board:** editing a card's row after it is made updates nothing by itself; the page offers to copy the change into the card (Where, Done when, Blocked by) when the two differ.
- **When it's built:** the done-column wrap-up (WOR-0074) writes the story doc into `Stories/` and links the plan its cards came from, so the plan says what was meant and the story says what was made.

Turned down:

- **Haiku rewriting the plan on each finished card:** it would rewrite Philip's words a little each time, and nobody would see what changed.
- **Ticking rows in the plan file as cards finish:** a commit to the repo for every card move, from the main checkout where other agents' changes sit uncommitted.
- **Leaving the plan as a snapshot:** what happened to [ANDROID.md](ANDROID.md).

## Android, replayed

What the planner would have drafted from "Android app: answer agents from the phone", beside what the board actually has.

### Questions asked before any card

| Question | What happened instead |
|---|---|
| Wrap the UI, rewrite natively, or a thin client over the API? | AND-0001, a card beside the build cards; its agent chose a thin Avalonia client on Philip's behalf. |
| How does the phone reach the PC off the home network? | AND-0002's agent chose Tailscale on his behalf; it isn't installed, so only the home network works. |
| How do notifications arrive with the app closed? | AND-0006 asked, and Philip picked ntfy. The one asked. |
| Where does the Android toolchain go, given the workload needs admin and C: is small? | AND-0009 found no toolchain and stopped; AND-0003 later put it under `D:\Android`. |
| What is the app id, and who holds the release key? | AND-0003's agent picked the id on his behalf and left the key to AND-0009. |

### Cards

| Plan card | Blocked by | On the Android board |
|---|---|---|
| Remote access: TLS, pairing by QR, a token per phone | none | AND-0002 |
| Toolchain, scaffold and app shell | none | AND-0003 |
| Pairing in the phone app: scan, pin the certificate, keep the token | remote access, scaffold | No card. AND-0003 used a USB route with the instance's token instead. |
| Needs you: agent asks and card decisions across projects, answered by a tap | scaffold | No card. AND-0005 lists agents needing you first; card decisions have no screen. |
| Board view | scaffold | AND-0004 |
| Agents and transcripts | scaffold | AND-0005 |
| Fettle from the phone: the API half | none | AND-0007, first half |
| Fettle on the phone, with voice | scaffold, Fettle API half | AND-0007, second half |
| Notifications through ntfy | none | AND-0006 |
| Notes, docs and reports to read | scaffold | AND-0008 (its API half done, screens waiting) |
| Themes and offline | board view, agents | AND-0009, first half |
| Icon, signing and a sideloadable release | themes and offline | AND-0009, second half |

AND-0001 isn't a card in the plan: it is the plan's Questions and Design. With the blockers in place, ▶ Work All would have started four cards (remote access, scaffold, the Fettle API half, notifications) instead of nine, and the rest as the scaffold landed, each agent handed AND-0003's report. The two missing cards would have been on the board from the start.

## Cards to build it

On the Main board, each scoring 3/3, with its Blocked by line already written:

| Card | What | Blocked by |
|---|---|---|
| WOR-0172 | Blocked by: card dependencies shown on tiles and the card panel | none |
| WOR-0173 | Blocked cards don't start: Work All skips them, Work on asks | WOR-0172 |
| WOR-0174 | Plan files: read and write the `docs/design/` plans in Core | none |
| WOR-0175 | The Design section: the plan tree and the plan page | WOR-0174, WOR-0172 |
| WOR-0176 | The planner agent, its chat beside the page, and the `plan` API command | WOR-0174, WOR-0175 |
| WOR-0177 | Make cards from a plan: the story board and its cards, blockers included | WOR-0172, WOR-0174, WOR-0175 |
| WOR-0178 | Plans stay current: choices agents make, the Log, the story link | WOR-0177 |
| WOR-0179 | Whole-project plans with feature plans under them | WOR-0175, WOR-0176 |
| WOR-0180 | Adopt the Android feature as the first plan, with its two missing cards | WOR-0172, WOR-0175 |

WOR-0172 and WOR-0173 stand on their own: they are worth having before any of the rest, on the Android board today.

## Open

- **The section's name and icon:** Design, as Philip called it, unless the rail says otherwise. It shipped as Design with a ◇ (WOR-0175).

Settled since:

- **Who commits an edit made on the page:** nobody, from the page. An answer tapped or a part edited changes the file in the main checkout and is left uncommitted, for Philip or the next agent in the repo to commit (Philip's pick when WOR-0175 asked, 2026-11-20). The status line says so after each edit.
