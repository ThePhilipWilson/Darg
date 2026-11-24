# WOR-0077: flow generator. so that for either art flows or basic flows can be cre

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:**
- **Created:** 2026-11-16
- **Done:**

## Description

From a note on the wall (20261116-234344-bfdb).

> flow generator. so that for either art flows or basic flows can be created per project or shared for all

## How to check

What changed: the Agents view has a **+** beside ▶ Run flow. It opens four choices: New flow for this project, New flow for all projects, New art flow for this project, New art flow for all projects. Each starts an agent that writes one flow file from what you typed in the box above (or asks what it's for if the box is empty). A project's own flows go in `%APPDATA%/Workbench/home/projects/<project>/flows/` and show in the Flow list as "Name · this project"; shared ones stay in `home/flows/`. The ✎ beside it opens the picked flow's folder. The API has `new-flow` too.

Commit: `fd68d0a` on main (pushed).

To try it: after ⟳ Update (or `pwsh -File tools/publish.ps1` from a terminal), open Alpha → Agents, type "card art: write prompts for a card, generate candidates in Forge, check hands and eyes, then let me pick", then **+ → New art flow for this project**.

Look for:
- the agent reads the Alpha-art skill and writes a flow that follows it by name rather than restating the recipe;
- it ends with an image review for you to pick from, not a stage waiting on you mid-flow;
- once its turn ends, the new flow is in the Flow list marked "· this project", and only in Alpha's list.

Checked already: the basic, this-project path, live on a throwaway instance (it wrote a writer → editor flow and it showed in the list). The art kind was not run live. My call on your behalf: "art flow" means stages that generate with Forge and finish with Darg's image picker.
