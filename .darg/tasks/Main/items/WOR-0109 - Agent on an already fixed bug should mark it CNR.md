# WOR-0109: Agent on an already fixed bug should mark it CNR

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

Philip's question: what happens if you run a bug card that has already been fixed, incidentally or otherwise? Does it get marked CNR (could not reproduce)?

**Today:** nothing marks it. "CNR" appears nowhere in Darg's source and the board has no such column. `CardPrompt` (`src/Darg.Desktop/ViewModels/AgentsViewModel.cs:433`) only tells a bug agent to find the cause first and add a failing test where it can. If the bug is already gone, the card stays in In Progress until the agent moves it by hand.

**What's wanted:** when an agent starts on a bug and finds it already fixed or can't reproduce it, it says so and the card lands somewhere that shows that (a CNR status or column), with a note of what it checked and which commit fixed it, if known. It should not go to Done as if work had been done.

**Open:** CNR as a board column, or as a status/label on the card? Decide before building.

**Decided (Philip, 2026-11-17):** Done with a `cnr` label, not a column.

## How to check

What changed (commit 535c739 on main, pushed): every card agent's finishing rules (`ReportStore.Rules`, which now includes the new `ReportStore.Cnr`) say that a bug (whatever the card's Type) it finds already fixed, or can't reproduce after really trying it, gets a `## Could not reproduce` section in the card (what was tried, what was seen, the fixing commit if found), the `cnr` label, goes to Done with a Done line, and gets a report with `status: cnr`. Reading the code alone doesn't count unless the agent finds the fixing commit, and not being able to run it is not CNR (the agent asks instead). A Bug card's prompt now says to check it still happens first. Reports lists a `status: cnr` report under Investigations, marked "could not reproduce".

To try it:
1. Update the installed copy (the header's ⟳ Update, or `pwsh -File tools/publish.ps1` from a terminal outside Darg).
2. Make a Bug card for a bug that's already fixed (for example the symptom of a bug card already in Done) and press Work on.
3. Look for: the card lands in Done with a `cnr` label on its tile; the card file has a `## Could not reproduce` section naming what was tried and, ideally, the fixing commit; Reports shows the report under Investigations with "could not reproduce" in its line.

Tests: `dotnet test tests/Darg.Core.Tests`, new test `A_bug_that_does_not_reproduce_goes_to_done_labelled_cnr_with_a_cnr_report`.
