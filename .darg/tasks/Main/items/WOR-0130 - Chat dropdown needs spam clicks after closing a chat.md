# WOR-0130: Chat dropdown needs spam clicks after closing a chat

- **Status:** Done
- **Priority:**
- **Type:** Bug
- **Labels:**
- **Created:** 2026-11-17
- **Done:**

## Description

The chats dropdown doesn't work well after closing a previous chat: it took repeated clicking before the previous chat would open.

## Investigation notes (2026-11-18, not resolved)

- Not reproduced through code: tests in 3b1e260 (`FettleChatTests`) forget a chat then pick the next, in Fettle's dropdown and in the Agents view list; both pass first time. They set `SelectedItem` directly, not by pointer.
- A headless pointer click on an open ComboBox's item is ignored even with no chat closed first, so a test of it can't tell anything. The same holds for a live debug build driven by synthetic mouse clicks (SetCursorPos + mouse_event): the item click leaves the dropdown Expanded and the selection unchanged, closed-chat or not. So the real popup click path is unverified either way.
- Suspect, untested: `MascotViewModel.ListChats()` clears and refills `Chats` (new `ChatEntry` objects) on every call, including the late Haiku `Retitle` after a message; if that lands while the dropdown is open, the open list is rebuilt under the pointer and a click is lost.
- Next step needs a person: next time it happens, note whether a message had just been sent in that chat, and which view (Fettle dropdown or Agents list).

## How to check

Round 3 (after "it's on the entire top bar that includes the drop down... it should only be above the dropdown").

What changed, commit `19b9b5a` on main (pushed):

- Fettle's box is now dragged by the part above the chats dropdown only: the title row and the Chat / Notifications row. The dropdown's row (the dropdown, the × and the blank around them) is no longer part of the drag handle, so the four-arrow cursor doesn't show there and a drag from there doesn't move the box.
- The buttons in the handle (–, + New chat, the two tabs) keep the normal arrow (from round 2, `640dfeb`).
- Test: `FettleChatTests.The_chats_dropdown_is_no_part_of_the_drag_handle` drags from the dropdown's row (box stays) and from the handle (box moves).

The click: unchanged since `135c73d`, and the dropdown now sits outside the drag handle altogether, so a press on it or its list can't reach the drag code. Round 2 checked a fresh build against a real Windows popup (one click picks a chat, also right after ×).

- Try it: ⟳ → Restart in the live copy (not only Pull in: the handle is wired at start-up), or `pwsh -File tools/publish.ps1` from a terminal. Open Fettle, close a chat with ×, open Earlier chats and click a chat once.
- Look for: four arrows only over the title row and the tabs row's blank; plain arrow over the dropdown, its list and the edges beside it; one click opens a chat; dragging the title still moves the box and a double-click there puts it back.
- If it still takes several clicks after a restart: say whether the list stays open after the click or closes without switching.

## QA notes (2026-11-18)

Checked at 135c73d: build clean; `FettleChatTests` 4/4; the new test `Clicking_a_chat_in_the_open_dropdown_picks_it_first_click` fails with the old `MainWindow.axaml.cs` and passes with the fix; full suite unit 584/584, integration 40/40 on the second full run (the first had one failure, `DecisionTests.An_answer_of_my_own_goes_to_a_new_card_agent_when_the_asker_is_gone`, which passed alone twice and in the next full run: a flake in decision code, not touched by this change). A throwaway headless test (deleted) dragged the Fettle header and the critter with real pointer events: both still move their box.

Not automatable: the headless click is a simulated pointer, and the card's own notes record that synthetic mouse clicks against a live build's real popup don't register at all (item click leaves the dropdown open), so the real Windows popup path (and "feels like one click") can only be confirmed by a person using the published or live build.

## Decision answered 2026-11-18 11:13

**Question:** Does the Earlier chats dropdown now open a chat on the first click after closing one, in the real app?
**Asked by:** agent QA, 2026-11-18
- **Yes, works** (recommended) -> done: one click opens the chat and the box doesn't move.
- **Still needs repeat clicks** -> back: it still takes more than one click; note which view (Fettle dropdown or Agents list) and whether a message had just been sent.
**Context:** Round 3, commit 19b9b5a: the drag handle (and its four-arrow cursor) now ends above the dropdown. Restart the live copy (⟳ → Restart) before trying, not only Pull in. See How to check.

**Answer:** Yes, works (2026-11-18 11:13)

## QA notes

### 2026-11-18 QA of 640dfeb (round 2)

Build clean; full suite passes (unit 590/590, integration 44/44), including the move-cursor test. Not automatable: whether the real Windows popup takes one click and feels right. Synthetic mouse clicks against the live build's popup don't register at all (recorded above), so only a person using a fresh restart (not a hot-reloaded copy) can confirm. Needs: Restart the live copy, close a chat with x, pick one from Earlier chats once.

### 2026-11-18 02:46

still has the move option on the dropdown, still not working
