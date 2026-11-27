# WOR-0243: Show a pending decision on In Progress cards

- **Status:** QA Ready
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-26
- **Done:**
- **Moved:** 2026-11-27 10:41:27

## Description

When an agent working on a card in In Progress asks for a decision, the card on the board should show it, just as cards in For Human Attention do (the open decision and its options), instead of the decision being visible only in the agent or needs-you list.

## How to check

**Changed** (commit 1795579 on `android`, branch `wor-0243`): when a card's agent asks you something (an AskUserQuestion or a permission ask), in any column, the card shows it:

- the tile's agent line says "agent needs you" and, under it, the question in the accent colour;
- the card's panel shows the question at the top with the agent's own option buttons (or Allow / Deny for a permission), and a line "Its agent asks · open its chat" that opens the agent. A pick there answers the agent exactly as in its chat, and the box goes once answered.
- A card in For Human Attention with a `## Decision` still shows only its decision, not the asker's chat question too (WOR-0225 settles that one).

**Try it:** restart onto the build (⟳ installed), then Work on a card whose agent will ask you something (or ask a card agent in its chat to "use AskUserQuestion to ask me which colour"). Look at the tile, then open the card.

**Look for:** the question on the tile is readable and not cramped (up to 3 lines); the panel box sits above the description and its buttons answer the agent; nothing shows when the agent isn't asking.

![the tile and panel](D:/GitHub/ThePhilipWilson/Darg/.shots/wor-0243/asks-on-card.png)

Tests: `AgentAskTests` (both fail with the panel's refresh broken), and `DecisionTests.A_card_with_a_decision_shows_it_and_not_its_askers_question_too`.
