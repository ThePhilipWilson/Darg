# WOR-0246: Set a profile or flow to run for a queue

- **Status:** QA Ready
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-27
- **Done:**
- **Moved:** 2026-11-27 12:29:30

## Description

Let a queue have a profile or flow assigned to it, so every card the queue runs is worked using that profile or flow instead of the default agent. Set when creating or editing the queue and shown on the queue. Related: WOR-0244 queue batch size.

## How to check

Branch `android`, commits b2ba6bb and ec52ba9 (pushed). Tests: `pwsh -File tools/test.ps1` (QueueWithTests, CardQueueTests).

In Darg, open a project's Board and press + Queue: the form has a new box beside the name (Default agent / Profile X / Flow Y). Make a queue with a profile and press Run: the agent on the first card notes its profile was "set on the queue", ignoring the usual rules. Choose a flow and its first stage agent starts on the card, and Darg stays on the Board. The lane's own box changes it afterwards (applies to cards started from then on). API: `queue-new {profile|flow}`, `queue-with`, `queues` show it.

Not seen live: how the box looks in the lane (no screenshot taken).
