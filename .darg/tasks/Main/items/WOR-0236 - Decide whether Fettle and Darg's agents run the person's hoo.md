# WOR-0236: Decide whether Fettle and Darg's agents run the person's hooks

- **Status:** Backlog
- **Priority:**
- **Type:** Task
- **Labels:** performance
- **Created:** 2026-11-25
- **Done:**

## Description

From WOR-0224: every Darg agent and Fettle chat runs the person's own Claude Code hooks, since it reads their settings (as it must, for permissions). On this machine that is 5 SessionStart, 4 UserPromptSubmit and 6 Stop hooks, each a Windows PowerShell start. Measured with a Haiku one-shot with and without the person's settings: start-up to `init` 2.4 to 4.2 s against 0.9 to 1.0 s, and 0.6 to 1.2 s between the reply and the turn's end (the Stop hooks) against none. In 25 Nov's agents.log the SessionStart hooks alone took 81.6 s over 80 turns, about 1 s for every new session. The UserPromptSubmit and Stop hooks add time to every turn, inside Claude Code's own figure, and `agents.log`'s turn end now logs Claude Code's `request at` and `first token at` so they can be told apart.

Some of the hooks matter to Darg's agents (the timer that feeds Darg's `today` figure, the gates on asking in prose and on logging mistakes), so turning them off is the person's call, not an agent's. `--settings '{"disableAllHooks": true}'` would keep permissions and drop hooks (unverified flag behaviour: check `claude --help` and the settings docs first).

**Where:** the args agents and Fettle start with (`AgentChoices.LaunchArgs`, `MascotViewModel.Args`, `src/Darg.Desktop/ViewModels/`), a per-profile or per-kind switch in `ModelProfiles` (`profiles.md`) if it's wanted per job.

**Questions:**
- Should Fettle's chats skip the person's hooks for speed (about 1 s off a new chat and some off every reply), keeping permissions? Or all agents? Or none?

**Done when:**
- The question above is answered on this card, and if the answer is to skip any, those sessions start without hooks, an integration test checks the flag is passed only to them, and agents.ps1 shows their Hooks column empty.
