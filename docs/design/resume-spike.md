---
kind: feature
status: agreed
parent: seamless-machine-switching
board: Resume spike
---
# Resume spike

A throwaway experiment, not a feature: copy a finished agent's Claude conversation to another path (and then to the Mac), `--resume` it there, and write down whether it carries on and exactly what had to be copied.

## Goals
- Know, with a recorded run rather than a guess, whether an agent carries on from a conversation copied under another path.
- Have a short written list of what must travel (the session file only, or its subagents and tool-result folders too) and what the agent makes of Windows paths in its old history.
- Leave the agents feature a rule it can build on: resume, or the fresh-with-transcript fallback, decided from evidence.

## Not doing
- Any change to Darg's app code: no copying inside `AgentsViewModel`, no Carry on button, no sync. That is the agents feature's job.
- Moving a live agent or one mid-turn.
- Testing every saved agent or the 800 MB of old conversations; one or two throwaway sessions are the whole sample.
- Hand-editing the copied conversation to rewrite paths, unless the plain copy fails and the Question below says to try it.
- Syncing anything through git. The copy is by hand or a script.

## Questions

### Where is the first proof run?
- **On this PC, into a second folder with a different path key, then the Mac as the final check** (recommended): the PC run proves the key mapping and `--resume` in minutes without Philip; the Mac run, which needs him at the Mac, adds the Windows-paths-in-history case.
- **On the Mac only**: the one true test, but nothing can start until he's at the Mac with `claude` signed in.
- **On this PC only**: fast, but a different OS's path style is exactly the risk, and it would go untested.

**Context:** the Mac has a `claude` and a Darg build (`docs/MAC-NOTES.md` says starting a real agent there is untried).

**Answer:** On this PC, into a second folder with a different path key, then the Mac as the final check (Philip, 2026-11-24)

### Which conversation is the guinea pig?
- **A throwaway session the spike makes itself** (recommended): a short Haiku session that reads a file, makes a note and is told a secret word, so a resume can be checked ("what was the word?") and costs nothing real.
- **One of Philip's real saved agents**: shows the real shape (long, with subagents and large tool results) but a failed or odd resume touches something he may want.
- **Both, throwaway first**: the most evidence, a second run to do.

**Context:** a throwaway proves the mechanism; a real long one shows whether size or subagent folders matter.

**Answer:** A throwaway session the spike makes itself (Philip, 2026-11-24)

### If the plain copy doesn't resume, how far does the spike go?
- **Stop and report what Claude Code said** (recommended): a failed resume is a finding. The agents feature then plans the fresh-with-transcript fallback only.
- **Try rewriting the paths in the copied file and resume again**: may rescue it, but editing Claude's own file format is fragile and ties Darg to it.
- **Try both, and report which of them worked**: a fuller answer for one more card.

**Context:** the project plan already treats the fresh-with-transcript fallback as wanted either way.

**Answer:** Stop and report what Claude Code said (Philip, 2026-11-24)

## Design

### What it builds on
- Claude Code keeps a session as `~/.claude/projects/<cwd key>/<session id>.jsonl`, the key being the folder it ran in with every character that isn't a letter or digit turned into `-` (this machine shows `C--Users-Philip--claude`). Darg stores the session id on each agent (`AgentRecord.SessionId`, `AgentStore.cs`) and starts a resume with `--resume <id>` from the agent's `Cwd` (`AgentsViewModel.Begin`).
- The project plan's agents section says what the spike must answer: the file only, or its folder too, and what the agent makes of Windows paths in its history.

### The experiment
1. **Make the sample.** From a folder A (say `D:\spike\a`), a short Haiku session via `claude -p --output-format stream-json`: it reads a small file, writes a note, and is told a secret word. Keep the session id.
2. **Copy.** To a folder B with a different path (`D:\spike\b-other`), copy the `.jsonl` into B's key folder, and make B hold the same small file. Variants: the file alone; the file plus its sibling folder `<session id>/` if there is one.
3. **Resume.** From B, `claude -p --resume <id>` and ask for the secret word and what folder it is in. Record the exit code, the output, and any error.
4. **The Mac.** The same, with the session copied from the PC and B the Mac's own path. Also ask what the agent thinks the working folder is and whether it tries a `D:\` path.
5. **Edge checks:** the session id resumed from a folder with no copy at all (what error comes back, so the agents feature can detect it); the same id resumed from folder A while it also exists in B.

A script, `tools/resume-spike.ps1`, does 1 to 3 and 5 so the PC run can be repeated; the Mac steps are the same commands by hand, written out in the findings file.

### What it produces
`docs/resume-spike-findings.md`: the claude version and OS for each run, the commands, a table of what was copied against what happened, the exact error when resume fails, and a short "the agents feature should" list (what to copy, how to find the key, how to tell a failed resume).

### Risks
- A Claude Code update could change the key or resume lookup; the findings name the version so they date.
- Real conversations can hold secrets, so the throwaway is the default and nothing copied goes into git.
- Needs Philip at the Mac for step 4; the card says so and the PC result stands alone meanwhile.

## Cards

### RES-0001: Spike script: make a session, copy it under another path, resume it
Write `tools/resume-spike.ps1`, which makes a short Haiku session in one folder with a secret word in it, copies the session file into the key folder for a second path, resumes from there and prints the result. It takes the two folders, whether to copy the session's sibling folder, and writes its commands and outputs to a log so a run can be repeated or compared.
**Where:** `tools/resume-spike.ps1`, `.shots/resume-spike/` for its output
**Done when:** running the script prints whether the resumed session knew the secret word, shows the key folder it used for each path, and leaves a log of every command and Claude's output.
**Blocked by:** none

### RES-0002: Run the spike on this PC and record what has to be copied
Run the script from the spike card: file only, then file plus sibling folder, then the edge checks (resume from a folder with no copy, and the exact error). Note the Claude Code version and which variants resumed. Write the results as the PC part of the findings file.
**Where:** `docs/resume-spike-findings.md`, `tools/resume-spike.ps1`
**Done when:** the findings file has a table of what was copied against whether resume worked, the exact error text when it didn't, and the Claude Code version.
**Blocked by:** Spike script: make a session, copy it under another path, resume it

### RES-0003: Run the Mac step and write what the agent makes of Windows paths
With the PC's sample session copied to the Mac, resume it under the Mac's path key and ask the agent its working folder and the secret word; note whether it tries `D:\` paths from its old history. This needs Philip at the Mac, so the card writes the exact commands first and leaves the results for him to paste in, then adds them.
**Where:** `docs/resume-spike-findings.md`
**Done when:** the findings file's Mac part lists the commands, the Mac's Claude Code version, whether resume worked, and what the agent said about the working folder and Windows paths.
**Blocked by:** Run the spike on this PC and record what has to be copied

### RES-0004: State the rule for the agents feature
From the findings, add a short section to the findings file saying what the agents feature must copy, how it derives the key for a folder, how it detects a failed resume, and whether the fresh-with-transcript fallback is the only path. Link the findings from the project plan's agents section.
**Where:** `docs/resume-spike-findings.md`, `docs/design/seamless-machine-switching.md`
**Done when:** the findings end with a "the agents feature should" list of at least four lines, and the project plan's spike bullet links to the file.
**Blocked by:** Run the Mac step and write what the agent makes of Windows paths
