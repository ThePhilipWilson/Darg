# WOR-0157: Fettle: turns that do work but end with no reply

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:** fettle
- **Created:** 2026-11-20
- **Done:** 2026-11-20
- **Moved:** 2026-11-20 01:29:55

## Description

Some Fettle chats do the work (e.g. make a card) but end with no reply shown, so Philip cannot tell whether the request was understood.

Wanted: Fettle sends a quick first message saying the request was understood, then a second once the ticket exists, saying it was made and linking to the card. If Fettle cannot do it (cut off in Philip's message: "If fettle can'..."), it should say so rather than end silently; confirm the rest of that sentence with him.

Seen: Philip asked for a GitHub page ticket and the card (WOR-0151) was created with no reply.

## How to check

**Cause:** + New chat (and picking another chat) ended the open Fettle chat's session even mid-turn. A session's events are dropped once it ends, so Fettle got cut off after the work and before the reply. The saved chats show it: four of the last eight stop at a tool call with no Turn done, and their Claude session logs show the reply written, or the tool call never run, at the second the next chat began. The GitHub page chat (WOR-0151) was one of them. The improve-ticket chat (00:39) was cut before its curl even ran; that request became a card anyway (WOR-0155).

**Changed** (commit 234ed87 on android, from branch wor-0157):
- `MascotViewModel.LetGo`: a chat left mid-turn keeps running and saving, and its session closes once the turn ends, unless it is back on screen.
- `FettleRules.SayWhatYouDo` in Fettle's briefing: one line saying what it understood before the first tool call, and a last line naming the card it made, or saying why it could not.
- Linking to the card is WOR-0158, as Philip asked in the same chat ("If fettle can't give links then we need a ticket for that too").

**To try:** update the installed copy (the header's ⟳ Update, or `pwsh -File tools/publish.ps1` from a terminal outside Darg). Then in Fettle:
1. Ask for a ticket, e.g. "ticket: the board tabs should show a count".
2. Straight away, press + New chat and type something else.
3. After a few seconds, open the first chat from the dropdown.

**Look for:** the first chat has an "On it: ..." line before its tool step, then a "Made WOR-xxxx ..." line and Turn done. Before the fix it stopped at the tool step with nothing said. An ordinary single request should also get the "understood" line first.

**Tests:** `FettleChatTests.New_chat_mid_turn_lets_the_turn_finish_and_reply` and `Picking_another_chat_mid_turn_lets_the_turn_finish_and_reply` (both fail without the fix), plus `FettleRulesTests`. Full suite on 234ed87: unit 647/647, integration 75/75.

## QA result

QA agent, 2026-11-20: passed.
- Full suite (tools/test.ps1) on android at bcb41ae, which contains 234ed87: unit 670/670, integration 79/79.
- With MascotViewModel.cs put back to its version before 234ed87 (in a scratch worktree), both new FettleChatTests fail (the chat left mid-turn was Saved, not still at work), so the tests catch the bug.
- Live briefing check: real claude -p turns with ChangeRequests and SayWhatYouDo on a throwaway board. Asked for a ticket, 4 of 4 (2 Haiku, 2 Sonnet) wrote an On it line before the first tool call, made the card and ended with a Made T-0002 line. Asked for a card in a project that does not exist, 2 of 2 said On it first, then said they could not make it and why.
