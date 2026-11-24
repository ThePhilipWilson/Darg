# WOR-0184: Show screenshots alongside agent questions

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-21
- **Done:** 2026-11-21
- **Moved:** 2026-11-21 01:13:22

## Description

> When an agent asks an AskUserQuestion about how something looks and refers to a screenshot, the screenshot is not visible to Philip because it is inside the question. Seen on the agent for WOR-0172. Fix: either show the image as a preview inside the AskUserQuestion panel, or show it directly above the question.

An agent's AskUserQuestion that points at a screenshot should show the picture above that question, so it can be judged without opening the file. This is the same as WOR-0147 did for the card's decision box (card panel), but for the question in the agent's transcript, which Fettle shares.

**Where:** `vm:QuestionItem` template in `src/Darg.Desktop/App.axaml` (~line 92; shared by the agents view and Fettle, so one change covers both); `QuestionItem` / `QuestionGroup` in `src/Darg.Desktop/ViewModels/AgentsViewModel.cs` (~line 1488) to pull image paths out of each question's text, option labels and descriptions. WOR-0147's card-decision image code and `MdImages` (markdown images) can be reused. Clicking to enlarge is WOR-0167's, not this card's.

**Done when:**
- A question whose text or option descriptions hold a local image path (`C:/x.png` or `![x](C:/x.png)`) shows that picture above the question, in the agent transcript and in Fettle.
- A question with no image looks as it does now; a missing file or non-image path shows nothing and doesn't crash.
- An integration test (`tools/test.ps1 -Integration`) sends a question with an image via fake claude, fails without the change, and leaves a screenshot in `.shots/integration`.
- Agents' briefing (`Decisions.Briefing`) tells them to put the full path of a screenshot in the question when it is about how something looks.

**Questions left:** none.
