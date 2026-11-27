# WOR-0236: Decide whether Fettle and Darg's agents run the person's hooks

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:** 2026-11-27
- **Moved:** 2026-11-27 03:50:09

## Description

From WOR-0224: every Darg agent and Fettle chat runs the person's own Claude Code hooks, since it reads their settings (as it must, for permissions). On this machine that is 5 SessionStart, 4 UserPromptSubmit and 6 Stop hooks, each a Windows PowerShell start. Measured with a Haiku one-shot with and without the person's settings: start-up to `init` 2.4 to 4.2 s against 0.9 to 1.0 s, and 0.6 to 1.2 s between the reply and the turn's end (the Stop hooks) against none. In 25 Nov's agents.log the SessionStart hooks alone took 81.6 s over 80 turns, about 1 s for every new session. The UserPromptSubmit and Stop hooks add time to every turn, inside Claude Code's own figure, and `agents.log`'s turn end now logs Claude Code's `request at` and `first token at` so they can be told apart.

Some of the hooks matter to Darg's agents (the timer that feeds Darg's `today` figure, the gates on asking in prose and on logging mistakes), so turning them off is the person's call, not an agent's. `--settings '{"disableAllHooks": true}'` would keep permissions and drop hooks (unverified flag behaviour: check `claude --help` and the settings docs first).

**Where:** the args agents and Fettle start with (`AgentChoices.LaunchArgs`, `MascotViewModel.Args`, `src/Darg.Desktop/ViewModels/`), a per-profile or per-kind switch in `ModelProfiles` (`profiles.md`) if it's wanted per job.

**Questions:**
- Should Fettle's chats skip the person's hooks for speed (about 1 s off a new chat and some off every reply), keeping permissions? Or all agents? Or none?
  **Answer:** Per profile switch (Philip, 2026-11-26): a `Hooks: off` line in a profile in `profiles.md` starts that profile's agents (and Fettle's spare session, when it's Fettle's profile) without hooks; none is off by default. Checked first on Claude Code 2.1.326: `--settings` with `{"disableAllHooks": true}`, inline or as a file, gave no hook events at all (39 without it), and the permission mode stayed the person's `auto`.

**Result (9b09647, 41f5817 on android):** live on a throwaway instance with real Claude, a Haiku chat on a `Hooks: off` profile took 3.3 s to the end of its turn against 5.0 s with hooks; agents.ps1's Hooks column read `-` against 1.0 s, and the turn ended 0.13 s after the reply against 0.6 s. `ProfileHooksTests` checks the flag reaches only the hooks-off profile's agent; `FettleSpareTests` checks a hooks-off Fettle profile's spare is started with it and taken.

**Done when:**
- The question above is answered on this card, and if the answer is to skip any, those sessions start without hooks, an integration test checks the flag is passed only to them, and agents.ps1 shows their Hooks column empty.
