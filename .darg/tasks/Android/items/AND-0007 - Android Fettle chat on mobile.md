# AND-0007: Android: Fettle chat on mobile

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** android, mobile, ui
- **Blocked by:** AND-0003
- **Created:** 2026-11-19
- **Done:**
- **Moved:** 2026-11-23 11:12:48

## Description

A bottom-sheet or full-screen chat with Fettle on the phone, aware of the project and section being viewed, with AskUserQuestion prompts shown as tappable options. Include voice input since typing is slow on a phone. Done when: a card can be created by dictating to Fettle.

## Decision answered 2026-11-23 11:12

**Question:** Dictating a card to Fettle works end to end on the emulator, with a stand-in recogniser in place of real speech. Is that enough to close AND-0007, or do you want to try real speech on your phone first?
**Asked by:** agent (AND-0007 QA, workbench-a6), 2026-11-23
- **Close it** (recommended) → done: the wiring is proven on a real Android build. Only Google turning your voice into words is untried, and that part isn't Darg's code.
- **Try it on my phone first** → stay: it stays here until you've run `pwsh -File tools/android.ps1`, tapped 🎤 and said "make a card to …".
- **Send it back** → back: something in the QA notes below needs more work. Say what in your notes.

**Context:** On the emulator the mic shows, Google's listening sheet opens and a cancel leaves the box empty. A stand-in recogniser's words landed in the box, Send made card T-0001, and the reply "Made T-0001." came back. Real speech can't be automated: the emulator has no voice to give Google's recogniser.

![dictated words in the box](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-and0007-live/shots/12-dictated.png)
![the reply naming the card](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-and0007-live/shots/14-top.png)
![the card on the phone's board](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-and0007-live/shots/20-board.png)

**Answer:** Close it (2026-11-23 11:12)

## How to check

**What changed:** commit `0aed1f8` on `android` (pushed). Every project page on the phone has a pink **✦ Fettle** button, bottom right. It opens Fettle full screen:
- A "Fettle sees: Project › Section" line sits above the box, and each message carries it.
- A 🎤 button starts Android's speech recogniser (Google's listening sheet). What you say lands in the box; Send sends it marked as dictated.
- Fettle's questions show as option buttons. A permission ask shows Allow and Deny.
- Back returns to the page, and ✦ Fettle opens the same chat again.

**Try it on your phone** (USB debugging on, Darg running on the PC):
1. `pwsh -File tools/android.ps1` from the repo.
2. Open a project, tap **✦ Fettle**, tap 🎤 and say "make a card to try dictation on the phone". Glance at the box, then tap Send.
3. Look for:
   - The card on the project's inbox board.
   - Fettle's reply naming its id.
   - The mic is there at all. If it isn't, the phone has no speech recogniser that the app can see.
4. Ask something that makes Fettle ask you a question, and tap an option.

**Screenshots** (headless, from `MobileFettleTests`; the headless font draws the mic as a pen):
![project page](D:/GitHub/ThePhilipWilson/Workbench/.shots/and-0007/phone-fettle-1-project.png)
![empty chat](D:/GitHub/ThePhilipWilson/Workbench/.shots/and-0007/phone-fettle-2-empty.png)
![question](D:/GitHub/ThePhilipWilson/Workbench/.shots/and-0007/phone-fettle-4-question.png)

## QA notes

**2026-11-23, agent workbench-a6, on `868a751` (android HEAD, which includes `0aed1f8`)**

Passed:
- Whole suite: `pwsh -File tools/test.ps1` in a worktree at 868a751 gave unit 830/830 and integration 181/181, `MobileFettleTests` included. No failures.
- On the darg emulator (Android 16, google_apis image), the 868a751 app was built and installed with `tools/android.ps1 -Port`. It was paired with a throwaway desktop (debug build, fake claude, its own instance in `.shots/qa-and0007-live`), never the real one.
  - The ✦ Fettle button is on the project page. It opens "Fettle · Proj" full screen, with "Fettle sees: Proj › Home" above the box.
  - **The mic shows on a real Android build.** The image has a recogniser (Google's speech service), and the manifest's `<queries>` lets the app see it.
  - Tapping 🎤 opens **Google's real listening sheet**. With no voice it says "Didn't catch that". Back cancels, the box stays empty and the mic works again.
  - **Dictation end to end.** A stand-in recogniser (a tiny test-only app answering `RECOGNIZE_SPEECH`, installed and removed again) returned "FAKE_CARD proj: Try dictation on the phone". The words landed in the box for a glance, and the prompt "Say it to Fettle" was passed on. Send reached Fettle as `[Philip is looking at: Proj › Home, on his phone]` with the dictation note. Card **T-0001 Try dictation on the phone** was made on the inbox board, the phone showed "Made T-0001.", and the card shows on the phone's Board section.
  - A recogniser that crashes (my first stand-in did) leaves Darg unharmed: the box stays empty and nothing crashes.
  - **Question flow.** A typed message made Fettle ask "Which board?". Main and Elsewhere showed as buttons, one tap sent it, and Fettle got `Which board?: Elsewhere`.
  - Back goes to the page. After switching to Board, ✦ Fettle opens **the same chat**, now saying "Fettle sees: Proj › Board".
  - No crash in logcat or in the desktop's crash.log.

Not checked, and why:
- **Real speech.** Google turning a spoken voice into words can't be automated here: the emulator has no microphone input to give the recogniser, and no phone is attached. Everything after the recogniser hands back its words is checked above.
- **Allow / Deny for a permission ask.** No test or device run covers the phone's buttons. Fake claude can only raise an AskUserQuestion, never a tool permission ask, so nothing can make one appear. The buttons call `FettleRemote.Allow` straight through, and the desktop side is covered by Core's `PhoneFettleTests`. To automate it, fake claude needs a `FAKE_PERM`-style hook that asks `can_use_tool` for an ordinary tool (ticketable).

Small things seen (not failures):
- Fettle's bubbles are `SelectableTextBlock`s. My first scroll swipe left a blue text selection in one, which stayed there (screenshot `.shots/qa-and0007-live/shots/15-swipe-on-bubble.png`). Scrolling by dragging on a bubble still works.

## Progress

**Part 1, desktop half: done** (2026-11-20, `e818d7c` on `android`, agent 2139877b6d). The Darg API has `fettle-say`, `fettle`, `fettle-answer` and `fettle-chats` for the phone; `PhoneFettle` and `FettleRemote` in Core are the context line and the phone's client (see CLAUDE.md, "Fettle from the phone"). Integration tests `PhoneFettleTests` make a card from a dictated phone message end to end through fake claude, and answer a Fettle question by tapped label.

**Part 2, phone half: done** (2026-11-23, `0aed1f8` on `android`, agent 83486313d7). The items below are what it built. Not tried on a device yet (see How to check).

**Part 2 as planned:**
- A Fettle chat view in `Darg.Mobile` (full screen from the bottom bar), driven by `FettleRemote` through the app's `DargClient`: poll `fettle` from `chat.NextFrom` while `busy`; a pending `question` entry shows its options as buttons (`Answer`), and a `permission` entry shows Allow and Deny.
- Send what the phone shows: project, section, open card or doc.
- Voice: a mic button using Android's `SpeechRecognizer` (`RecognizerIntent`) in `Darg.Android`, sending with `voice: true`. Gboard's own mic in the text box works with no code meanwhile.
- A headless test in Darg.Desktop.Tests (as `MobileShellTests` does) and a try on a device: dictate "make a card for X" and see the card made.
