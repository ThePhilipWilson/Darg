# WOR-0163: Mark notifications that need a decision

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-20
- **Done:**
- **Moved:** 2026-11-20 22:37:15

## Description

Notifications should carry a small marking to highlight when they are waiting on a decision from Philip.

## Decision answered 2026-11-20 22:37

**Question:** Do the accent bar and "needs you" pill in Fettle's Notifications list look right to you (easy to spot, not loud) in Dark, Light and Girlypop?
**Asked by:** agent QA, 2026-11-20

![Dark](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0163/qa-dark.png)
![Light](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0163/qa-light.png)
![Girlypop](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0163/qa-girlypop.png)

- **Looks right** (recommended) → done: the marking stays as it is and the card goes to done.
- **Too loud** → back: sent back to tone it down (e.g. a softer pill or just the bar), with your notes.
- **Too quiet** → back: sent back to make it stand out more, with your notes.
**Context:** Everything automated passed: full suite green, the pill shows on questions, permission asks and decisions and not on finished turns, in all three themes, and both toast paths get the flag. What's left is whether it looks right, plus the real Windows toast's extra line, which no test can see (see QA notes).

**Answer:** Looks right (2026-11-20 22:37)

## How to check

**What changed:** in Fettle's Notifications list, an entry waiting on your answer (an agent's question, a permission ask, a card's decision) now has an accent bar down its left edge and a small "needs you" pill after its title. A finished turn has neither. The Windows toast for one of these carries a third line, "● Waiting on your decision". The phone's ntfy notifications already marked these (question tag, high priority) and are unchanged.

**Where:** commit `5cb681c` on `android` (pushed). Files: `src/Darg.Desktop/MainWindow.axaml` (the list template), `src/Darg.Desktop/Notifier.cs` (the toast), `MainViewModel.cs` (`NotificationEntry.Asks`).

**Try it:** publish or ⟳ Update, then close or minimise Darg's window while an agent asks a question (or a card lands in For Human Attention with a decision).
- Look at the toast: it should show "● Waiting on your decision" in small text at the bottom. I couldn't see a real toast, because the tests capture them.
- Open Fettle → Notifications: questions, permission asks and decisions have the bar and pill; "finished after n min" entries don't.

Here's the list as the integration test draws it:

![the marked list](D:/GitHub/ThePhilipWilson/Workbench/.shots/wor-0163/notifications-marked.png)

**Done when:** the marking is easy to spot without being loud, in all three themes.

## QA notes

- 2026-11-20, QA agent, on `android` at 0b342d5 / e1f4dd7 (both contain 5cb681c):
  - Full suite `pwsh -File tools/test.ps1`: unit 762/762, integration 119/119, no failures.
  - `Notifications_waiting_on_a_decision_are_marked` and the decision toast assert (`DecisionTests`) pass. Reading the test, it would fail on the old XAML: it looks for a Border named "Needs your decision", which the old template didn't have.
  - Scratch test (not committed) switched the theme through the API to dark, light and girlypop. In each one, 3 pills showed (decision, permission ask, question) and none on the finished turn. No pill was squeezed. A long agent name is cut short with "…" and its pill stays visible. Screenshots are in the Decision section.
  - Scratch test (not committed): an agent started with fake claude's `FAKE_ASK` produced a captured toast with `NeedsYou = true`. So the agent-question path is marked as well as the card-decision path, which was the only one with a test.
  - Not automated: the real Windows toast's "● Waiting on your decision" line. The tests send toasts to `Notifier.Capture`, never to Windows. Seeing the real line would mean posting a real notification to Philip's Action Center and reading the Windows shell's UI, so it needs your eyes once. Could be ticketed: a dev-only API command that builds the toast's XML (`ToastContentBuilder.GetToastContent().GetContent()`) so a test can check for the attribution element without showing anything.
