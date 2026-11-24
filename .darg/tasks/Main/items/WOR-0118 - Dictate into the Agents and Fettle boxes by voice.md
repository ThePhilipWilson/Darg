# WOR-0118: Dictate into the Agents and Fettle boxes by voice

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** 
- **Created:** 2026-11-17
- **Done:**
- **Moved:** 2026-11-22 22:58:10

## Description

> Voice mode: talk to Fettle and Workbench by voice instead of typing.
>
> Scope not yet decided (speech in, speech out, or both).
>
> Scope chosen: speech in only, Windows' built-in recogniser (System.Speech, offline). No speech out.

Latest from you (2026-11-18, in the QA notes): "it's working, technically, it's just shit and understanding what I'm saying. can we look at figuring out why? claude code uses a voice model that works pretty well with my voice, can we see what it's using and if we can use it too?" Round 5 answered that: Claude Code's voice streams to Anthropic's own speech service on your claude.ai login, so the mic now runs a local Whisper model (small.en) instead (commit c63bd03). Your voice hasn't been through it yet.

**What's wanted:** the 🎙 button beside the Agents and Fettle message boxes types what you say into the box, accurately enough for your voice. Speech in only; nothing is sent until Send. What's left: fix round 5's QA findings, then you speak into the Whisper build.

**Where:**
- Mic and transcription: `src/Darg.Desktop/Views/Dictation.cs` (`Start`, `Run`, `Stop`; `lastSound` and the 6 s "No sound from the microphone" check in `Run`'s `DataAvailable` handler).
- Phrase cutting and silence filter: `src/Darg.Core/Speech.cs` (`SpeechSegmenter`, 700 ms pause, `Flush()`; `SpeechText.Clean`), tests in `tests/Darg.Core.Tests/SpeechTests.cs`.
- Buttons: `src/Darg.Desktop/MainWindow.axaml` (Fettle, `#MascotBox`) and `src/Darg.Desktop/Views/AgentsView.axaml` (`#ComposeBox`).
- The model downloads to `%APPDATA%\Darg\models`; `dictation.log` is in the instance folder (on this machine still `%APPDATA%\Workbench\home`).

**Done when:**
- First click: "No sound from the microphone" no longer shows a second after "Listening" because the model took 13 s to load (reset `lastSound` when recording starts in `Run`).
- Stopping the mic mid-phrase types that phrase (`Stop` flushes `SpeechSegmenter` and transcribes it), so `Stopping_hands_over_a_phrase_still_being_spoken` tests something the app does.
- An integration test in `tests/Darg.Desktop.Tests` feeds a wave file through a sample source swapped in for `WaveInEvent`: a spoken phrase lands in the box, silence types nothing, the first-click warning doesn't show. It fails with either fix above undone.
- `pwsh -File tools/test.ps1` passes.
- You, on a build with these (live copy ⟳ Pull in, or `tools/publish.ps1` from a terminal): speak into the mic in Agents and in Fettle, and the words come out right, Darg and Fettle included. Can't be automated: no mic input from here.

**Questions left:**
- Is the ~470 MB one-off model download acceptable, or should it default to a smaller model?
- Once something's been typed, should 6 s of quiet still replace "Typed: ..." with the "No sound" warning? (Round 5 called it noisy.)

## Decision answered 2026-11-22 22:58

**Question:** After speaking into the Whisper build, is the dictation accurate enough on your voice to close this card?
**Asked by:** agent QA, 2026-11-20
- **Accurate enough** (recommended) → done: words come out right, Darg and Fettle included; card goes to done.
- **Still misreads me** → back: sent back for more work with your notes on what it got wrong.
- **Too big a download, use a smaller model** → back: sent back to default to a smaller Whisper model.
**Context:** Round 6 QA passed by automation (suite green, 3 dictation tests ran on the real model, both fixes in Dictation.cs). Only your real voice and the 470 MB download question are left; no mic input from here.

**Answer:** Accurate enough (2026-11-22 22:58)

## How to check

- Round 5, commit c63bd03 (main, pushed). You said the dictation works but misreads you. I looked at what Claude Code's voice uses: it is not a local model. It streams your mic over a WebSocket to Anthropic's own speech-to-text service (`/api/ws/speech_to_text/voice_stream`) signed in with your claude.ai token, so Darg can't reuse it without borrowing your login (you picked a local model instead).
- Changed: the mic now runs a local Whisper model (small.en, ~470 MB) in place of Windows' System.Speech. It downloads once to `%APPDATA%\Darg\models` the first time you click the mic (status shows in the box), runs on the GPU through Vulkan (falls back to the CPU), and types each phrase after you pause. The old recogniser got a synthetic test sentence wrong; Whisper got "Please open the board and start a new card for the voice mode fix." exactly, 0.2 s after warm-up. Code: src/Darg.Desktop/Views/Dictation.cs; phrase cutting and silence-hallucination filter in src/Darg.Core/Speech.cs (unit tests in SpeechTests.cs).
- Try: run a build with this commit (live copy: ⟳ Pull in; or `pwsh -File tools/publish.ps1` from a terminal). Click the mic in Agents or Fettle. First time: wait for the download and "Loading the speech model" (first load also compiles GPU shaders, about 15 s). Then speak a sentence and pause: it should appear in the box a moment later. Judge accuracy on your own voice. `dictation.log` in the instance folder records what it said.
- Look for: phrases wrongly cut mid-sentence (pause threshold is 700 ms, `SpeechSegmenter` constructor) or text typed when you said nothing; whether the 470 MB download is acceptable; whether words you use (Darg, Fettle) come out right (they are given to the model as a hint).
- Not checked: your real voice (no microphone from here) and the live mic-to-text path; only the model on synthesised speech, plus unit tests of the phrase cutter. Known gap: stopping the mic mid-phrase drops that phrase.



## How to check (round 6)

- Commit 1f6605b on `android` (pushed). Changed in `src/Darg.Desktop/Views/Dictation.cs`: the "No sound" clock now starts when the mic opens (it started at the click, so a 13 s model load tripped it at once); the warning only shows while nothing has been heard yet, so it no longer replaces "Typed: ..." (my call on your second question); stopping the mic mid-phrase now flushes that phrase and types it. The mic sits behind `ISampleSource`, so tests feed a wave file.
- Tests: `tests/Darg.Desktop.Tests/DictationTests.cs` (real Whisper model, fixture `Fixtures/speech.wav`): a phrase lands in Fettle's box and no "No sound" appears after a simulated slow load, silence types nothing, stop mid-phrase types the phrase. Each fails with its fix undone (checked). They skip, saying so, if the model isn't in `%APPDATA%\Darg\models`. `pwsh -File tools/test.ps1`: unit 670/670, integration 82/82.
- Try: live copy ⟳ Pull in (or `pwsh -File tools/publish.ps1` from a terminal). Click the mic in Agents and in Fettle, say a sentence with "Darg" and "Fettle" in it, pause. Click the mic again while mid-sentence: that sentence should still appear. `dictation.log` in the instance folder records it.
- Not checked: your real voice and the live mic path. The model stays small.en (~470 MB, first click only); I didn't swap in a smaller one, since accuracy on your voice was the complaint. Say if you want a smaller default.

## QA notes

- 2026-11-17 QA: builds clean (0 errors); Core tests 548 pass, 1 fails (TicketTitleTests, unrelated, already known). XAML wiring read: mic buttons beside Fettle box and agent compose box. A en-GB recogniser is installed here, so the button would show and match.
- Card names commit 9973adc, but the dictation commit is f06176a (9973adc is WOR-0120).
- Not checked: speech actually recognised and typed (no microphone from here); needs you to speak into it.
- 2026-11-17 QA (second pass): Dictation.cs confirmed in f06176a. Nothing found wrong; what's left is yours: speak into the mic in Agents and in Fettle and judge the en-GB accuracy.

### 2026-11-17 23:29

I tried but it doesn't seem to be working, are you getting any ambient noise picked up through the mic?

### 2026-11-17 QA round 3 (commit 285aacb)

- Builds clean (0 errors); Core tests 560 pass, 0 fail. Dictation.cs read: level, "Hearing:" tooltip and rejection message are wired as the How to check says; the confidence cut-off is gone.
- Ran the same Windows en-GB recogniser and DictationGrammar over a synthesised spoken sentence (wave file input): hypothesis events fire live, then one SpeechRecognized result. So recognition and event wiring work. Accuracy on synthetic speech was poor ("Please include aboard an starting you can't" for "Please open the board and start a new card", confidence 0.37), so expect rough text from this untrained recogniser.
- AudioLevelUpdated does not fire from a wave file, so the level icon was not exercised.
- Why this can't be automated: the question is whether your default microphone is heard (level was about 3/100 earlier) and whether the icon and words respond to a real voice. No mic input from here, and injecting audio into the default capture device needs a virtual audio cable that isn't installed. Ticketable as: an automated mic-loop test via a virtual audio device.
- For you: build, Agents, click the mic, speak; check the icon changes, "Hearing:" shows, words land in the box; if the icon stays at the quiet symbol, set the right input in Settings > Sound.

### 2026-11-17 23:44

still can't get it to work. am I missing something?

### 2026-11-17 QA round 4 (commit 5b85baa)

- Full suite from main (5b85baa): tools/test.ps1 builds clean, unit 566/566, integration 19/19.
- **Why "nothing happens" (found, checked by automation): you are not running a build with the dictation fix.** The running Darg (pid 2704, started 22:35) is the live copy, .claude/worktrees/live, which is at f45ded6, one commit before 5b85baa; its Darg.Desktop.dll is dated 22:35. The installed D:/Apps/Darg copy is from 17:54, before all three dictation commits, and its DLL contains neither "dictation.log" nor "No sound from the microphone". No dictation.log exists anywhere under %APPDATA%, which is what you would see if the 5b85baa code had never run. Fix: header ⟳ menu, Pull in (live copy), or `pwsh -File tools/publish.ps1` from a terminal; then click the mic in Agents.
- The card's log path is probably wrong: it says %APPDATA%/Darg/<instance>, but your real instance is still %APPDATA%/Workbench/home here (InstanceConfig.Folder() in main uses Darg/<name> after migrating). Look in both.
- Not checked, can't be automated: that your microphone is heard and speech is recognised and typed. No microphone from here, and feeding audio into the default capture device needs a virtual audio cable that isn't installed (ticketable: automated mic-loop test via a virtual audio device). An earlier round measured the input level at about 3/100, so Windows' default input may be the wrong device (only active capture device listed: "USB Advanced Audio Device"); with the new build the box says "No sound from the microphone" after 6 s if so.

### 2026-11-18 11:15

it's working, technically, it's just shit and understanding what I'm saying. can we look at figuring out why? claude code uses a voice model that works pretty well with my voice, can we see what it's using and if we can use it too?

### 2026-11-18 QA round 5 (commit c63bd03, tested on main 1290a59 which contains it)

- Full suite (`pwsh -File tools/test.ps1`): builds clean, unit 608/608, integration 49/49, nothing failing.
- Whisper pipeline, checked by automation (`.shots/qa-0118/harness`: Speech.cs as committed, Whisper.net 1.9.1, the downloaded small.en, the same prompt as Dictation.Run, fed from a wave file in 100 ms chunks like the mic): three synthesised sentences with 1.5-2 s gaps gave 3 phrases, each word for word ("Ask Fettle to move it to QA, then commit the branch." included); the comma pauses in "I think, well, the agent..." didn't split it. With hiss mixed in: still word for word. 20 s of room hiss plus a click: no phrase, nothing typed. GPU path works (Vulkan, RTX 5070 Ti): 0.25-0.35 s a phrase, cold load and warm-up 18 s.
- Live app, checked by automation (`.shots/qa-0118/mic-click.ps1`: debug build, throwaway instance `.shots/home`, Agents section, mic pressed through UI Automation): button is shown, the model loads from `%APPDATA%\Darg\models` without downloading again, "Listening" comes 13 s after the click, the button shows ⏺ and the status shows in the box's placeholder, a second click stops it and puts the button back. Screenshot: `.shots/qa-0118/listening.png`.
- **Wrong: "No sound from the microphone" shows 1 s after "Listening" on the first click.** `lastSound` is set in `Start`, before the model loads (13 s here), so the 6 s timer has already run out when the mic opens; dictation.log: `11:36:27 Listening...` then `11:36:28 No sound from the microphone...`. After rounds 3 and 4 that's exactly the message that would make you think it's broken again. Fix: reset `lastSound` when recording starts in `Run`. It also replaces "Typed: ..." after any 6 s of silence, which is noisy but less important.
- **Wrong: stopping the mic mid-phrase drops the phrase, though the fix is already written.** `SpeechSegmenter.Flush()` exists and has a unit test (`Stopping_hands_over_a_phrase_still_being_spoken`), but `Dictation.Stop` never calls it, so the test covers behaviour the app doesn't have. Either flush and transcribe on Stop, or drop the test.
- **Missing: no integration test of the dictation behaviour** (CLAUDE.md: a change to behaviour the app shows gets an integration test that fails without it). `Dictation` reads `WaveInEvent` directly with no seam; a sample source the test can swap (e.g. a wave file through the same `SpeechSegmenter`/Whisper path) would let one check that a spoken phrase lands in the box, that silence types nothing, and the first-click message above.
- Mic level here: default input "Microphone (3- USB Advanced Aud", ambient RMS 0.00002 (median of 8 s). The app counts sound from 0.01 and starts a phrase at 0.012 (about -38 dBFS), fine for normal speech into a USB mic. Whether your voice clears it couldn't be measured: nobody was speaking.
- Your real instance's dictation.log has only System.Speech entries (last at 11:14 today). The Whisper build hasn't heard your voice yet.
- Can't be automated: accuracy on your own voice and the live mic path with a person speaking. There's no microphone input from here, and feeding audio into the default capture device needs a virtual audio cable, which isn't installed (ticketable: an automated mic-loop test through a virtual audio device). Once the above is fixed, the next round still needs you to speak into it.

### 2026-11-20 QA round 6 (commit 1f6605b, on android 3b07fa4)

- Full suite (`tools/test.ps1`): builds clean, unit 738/738, integration 110/110. `-Filter Dictation`: 3/3 ran against the real small.en model, none skipped.
- Read Dictation.cs: `lastSound` reset when the mic opens in `Run`; the warning only shows while `heardSound` is false; `Finish` flushes `SpeechSegmenter` and queues the tail; mic behind `ISampleSource`/`SourceFactory`. All three round 5 findings are addressed. I did not re-break the code to confirm the tests fail without the fixes; the card says that was checked.
- Can't be automated: accuracy on your own voice and the live mic path. No microphone input from here, and feeding the default capture device needs a virtual audio cable, which isn't installed (ticketable: automated mic-loop test via a virtual audio device).
