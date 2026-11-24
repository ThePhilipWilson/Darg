# Darg docs

Darg is one app for everything in a project: boards, notes, docs, code, agents, consoles and tools. It is a native Avalonia app (C#, .NET 10) whose agents are real Claude Code sessions run headless. It was called Workbench until 2026-11-16, so older files, folders and reports still say Workbench or Bench (Fettle, the mascot, was Bench).

## Read in this order

| Doc | What it answers |
|---|---|
| [getting-started.md](getting-started.md) | Install, build, run, test, publish, the live copy, every environment variable. |
| [architecture.md](architecture.md) | How the parts fit: Core and Desktop, instances and projects, where files live, how agents and the API work. |
| [guide.md](guide.md) | What each section of the app is for and how to use it. |
| [cards-and-qa.md](cards-and-qa.md) | The life of a card: Work on, QA, For Human Attention, decisions, reports, Done, CNR. |
| [decisions-and-gotchas.md](decisions-and-gotchas.md) | Decisions, findings and traps collected from the finished-agent reports, with the card each came from. |
| [performance.md](performance.md) | How to profile Darg (stalls.log with stacks, perf.log, analyze.ps1) and what the first trace found (WOR-0161). |
| [REMOTE.md](REMOTE.md) | How a phone reaches the desktop API (AND-0002): TLS with a pinned certificate, pairing by QR, per-phone tokens, what's exposed. |
| [MAC-NOTES.md](MAC-NOTES.md) | The first Mac build (2026-11-24): what was fixed for it, what hasn't been tried yet, and its build warnings. |

## Plans and history

| Doc | What it is |
|---|---|
| [ANDROID.md](ANDROID.md) | The Android decision (2026-11-20): a thin remote client, a native Avalonia app over the Darg API; v1 features and what stays desktop-only. Being built. |
| [design/android.md](design/android.md) | The Android feature as a plan (WOR-0180, on 2026-11-21): goals, what it isn't doing, the choices made, the design and its eleven cards with what each is blocked by. The first plan in the Design section, so it shows each card's live column. |
| [design/seamless-machine-switching.md](design/seamless-machine-switching.md) | Draft project plan (2026-11-24): close Darg on the PC, open it on the Mac and the project is there as left, carried by git on a `darg` branch of each repo. Its feature plans are the next rows. |
| [design/merge-safe-boards.md](design/merge-safe-boards.md) | Draft: boards two machines can both change between syncs and still merge (ids from the card files, a Rank line per card, clashing ids renumbered). |
| [design/project-settings-in-the-repo.md](design/project-settings-in-the-repo.md) | Draft: a project's own settings in `.darg/project.json`, instance.json keeping only where things are on this machine. |
| [design/per-user-settings-sync.md](design/per-user-settings-sync.md) | Draft: what is about Philip rather than a project or machine, in one folder a private settings repo carries between machines. |
| [design/agents-and-fettle-chats-travel.md](design/agents-and-fettle-chats-travel.md) | Draft: saved agents and Fettle chats in `.darg`, readable on either machine and carried on from there. |
| [design/auto-sync.md](design/auto-sync.md) | Draft: Darg pulls and pushes each project's `darg` branch by itself, with a footer line and a Sync panel for conflicts. |
| [design/leaving-check.md](design/leaving-check.md) | Draft: before leaving a machine, what won't be on the other one, pushing what it safely can. |
| [design/resume-spike.md](design/resume-spike.md) | Draft spike: copy a finished agent's conversation to another path and machine and see whether `--resume` carries on. |
| [FEATURE-PLANNING.md](FEATURE-PLANNING.md) | The design for planning a feature or a whole project before its cards exist (2026-11-20, WOR-0171): the Design section, plan files, Blocked by on every card, and the Android feature replayed through it. Not built yet; WOR-0172 onwards build it. |
| [PLAN.md](PLAN.md) | The original plan (2026-11-16): the ask, the big technical calls and what was built in the first slices. Written when the app was called Workbench. |
| [PHONE-PLAN.md](PHONE-PLAN.md) | The first draft for phone and tablet (2026-11-16), replaced by [ANDROID.md](ANDROID.md). |
| [MORNING.md](MORNING.md) | A checklist from the first overnight build (2026-11-17). |
| [mockup-v1.html](mockup-v1.html) | The first mockup, opened in a browser. |

## Where the detail is

- [CLAUDE.md](../CLAUDE.md) at the repo root is the dense, always-current description of every part, written for agents. These docs are the readable version of it; when they disagree, check the code, then fix both.
- Finished agents write a report per card (see [cards-and-qa.md](cards-and-qa.md#reports-darg-writes-itself)). These docs cite them by card id, for example (WOR-0087), and they sit in the project's [Reports section](guide.md#reports): `.darg/reports/` in the repo, older ones in the instance's `projects/<project>/reports/`, archived ones in `archive/`.

## How these docs are written

Keep to this when adding to them; `DocsTests` checks the parts marked *(tested)*.

- **Shape**: a doc opens with one `# Title` and a short paragraph saying what it covers *(tested)*. `##` for sections and `###` below them; never a bold line standing in for a heading *(tested)*. Headings are plain words, with no numbers or icons, so their links stay stable.
- **Lists of named things**: each item leads with the name in bold, then a colon: `- **Board tabs** (WOR-0149): …`.
- **Buttons and labels**: written as the app shows them, icon included (▶ Work on this, 🔔 For human attention), and they must exist in `src/` *(tested)*.
- **Code**: identifiers, files, commands and settings go in backticks. Fenced blocks name their language (`powershell`, `text`, `markdown`) *(tested)*.
- **Cards**: cited by full id in brackets after the claim, (WOR-0087), never shortened to a bare number as in `WOR-0139 to 0141` *(tested)*. Darg links the id to its card; on GitHub it reads as plain text.
- **Links**: every mention of another doc in this folder is a link to it *(tested)*, to the section where one fits, e.g. [cards-and-qa.md](cards-and-qa.md#qa). Each link must reach a file and each `#part` a heading *(tested)*. Darg opens such a link at the doc, not at the heading.
- **History docs** ([PLAN.md](PLAN.md), [MORNING.md](MORNING.md), [PHONE-PLAN.md](PHONE-PLAN.md)) keep their words as written, under a quoted line saying when and what replaced them.
