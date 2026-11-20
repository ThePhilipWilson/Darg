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

## Background and history

| Doc | What it is |
|---|---|
| [PLAN.md](PLAN.md) | The original plan (2026-11-16): the ask, the big technical calls and what was built in the first slices. Written when the app was called Workbench. |
| [PHONE-PLAN.md](PHONE-PLAN.md) | Draft plan for phone and tablet. Nothing in it is built. |
| [ANDROID.md](ANDROID.md) | The Android decision: thin remote client, native Avalonia app over the Darg API; v1 features and what stays desktop-only. |
| [MORNING.md](MORNING.md) | A checklist from the first overnight build (2026-11-17). Kept as history. |
| [mockup-v1.html](mockup-v1.html) | The first mockup, opened in a browser. |

## Where the detail is

- `CLAUDE.md` at the repo root is the dense, always-current description of every part, written for agents. These docs are the readable version of it; when they disagree, check the code, then fix both.
- Finished agents write a report per card (see [cards-and-qa.md](cards-and-qa.md)). They are cited here by card id, for example "WOR-0087", and sit in the project's Reports section (`.darg/reports/` in the repo, older ones in the instance's `projects/<project>/reports/`, with archived ones in `archive/`).
