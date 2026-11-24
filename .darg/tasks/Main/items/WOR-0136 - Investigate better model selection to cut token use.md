# WOR-0136: Investigate better model selection to cut token use

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** tokens, models
- **Created:** 2026-11-18
- **Done:** 2026-11-18

## Description

Philip's observation: we seem to be burning through tokens pretty fast. Look at whether choosing models more deliberately (cheaper or smaller models for simple work, bigger ones only where needed) would bring usage down.

**Investigate**
- Where the tokens go: which kinds of session (Fettle chats, card agents, reviews, flows, long-running agents) use the most, and which model each runs on today.
- How Darg currently picks a model for each of those, and whether it can be set per agent type, per card, or per task.
- Which work is simple enough for a smaller model (Fettle quick answers, board edits, lookups, summaries) and which genuinely needs the top model (investigation, large changes, reviews).
- Other levers that sit alongside model choice: reasoning effort, context size and caching, sub-agent fan-out, transcripts re-read each turn.

**Done when**
- A short write-up shows where usage goes, what model each part uses, and a recommended model/effort default per kind of work, with a rough estimate of the saving.
- Philip picks what to change; follow-up cards are raised for that.
