# AND-0010: Android Needs you screen: agent asks and card decisions answered by a tap

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** android, mobile, ui
- **Blocked by:** AND-0003
- **Created:** 2026-11-21
- **Done:**
- **Moved:** 2026-11-23 13:07:49

## Description

The first screen of the phone app (docs/design/android.md, v1 feature 2), which no card owned: every agent question and permission ask, and every card in For Human Attention with its `## Decision`, across projects, each answered with a tap. AND-0005 lists agents needing you but card decisions have no screen.

**Where:** `src/Darg.Mobile` (a new screen, opened first after connecting); the Darg API's `decide` and agent answer commands; add a pending-decisions list across projects to the API if it isn't there.

**Done when:** with a card waiting on a decision and an agent asking a question, the phone shows both on its first screen and a tap answers each, the PC seeing the answer.

## Decision answered 2026-11-23 13:07

**Question:** Does the Needs you screen look and feel right on a real phone screen?
**Asked by:** agent QA (AND-0010), 2026-11-23
- **Looks right** (recommended) → done: the screen is fine; the card goes to done.
- **Needs changes** → back: something about the layout, taps or feel is wrong; the card is sent back with your notes.
**Context:** The full suite passed (838 unit, 199 integration, including the Needs you tests), but only headless at 412 px; a real device or emulator can't be driven from here. ![Needs you](D:/GitHub/ThePhilipWilson/Workbench/.shots/and-0010/phone-needs-1-needs-you.png)

**Answer:** Looks right (2026-11-23 13:07)

## How to check

**What changed:** the phone app now opens to Needs you, not the projects list. It shows every project agent's pending question and permission ask, then every card in For Human Attention with its decision. A tap answers each one. Projects › is a step on from it, and back returns there. New API commands: `needs-you`, and `answer` (`{id, at, answers | allow}`). `decide` now answers JSON. Fettle's own questions stay in its phone chat; that choice is recorded in the plan's Questions.

**Commit:** `f8ab776` on `android` (pushed).

**Try it:** run `pwsh -File tools/android.ps1 -Emulator` (or with your phone plugged in) against a debug build of Darg. Have something waiting first:
- a card in For Human Attention with a `## Decision`;
- an agent asking an AskUserQuestion;
- if you can, an agent asking for a permission.

**Look for:**
- All of them show on the first screen, asks first. Each row says its project and its agent or card.
- Tapping a question's option answers it. The desktop transcript shows the pick and the agent carries on.
- Allow and Deny work on a permission ask.
- Tapping a decision option writes `**Answer:**` into the card and runs the option's action (done moves the card).
- A card with no decision offers Approve and Send back. Send back with an empty Notes box says why it can't.
- How it looks on a real screen: the headless shot at 412 px wide is ![Needs you](D:/GitHub/ThePhilipWilson/Workbench/.shots/and-0010/phone-needs-1-needs-you.png)

**Tests:** `pwsh -File tools/test.ps1 -Filter NeedsYou` runs `MobileNeedsYouTests` and `NeedsYouTests`.
