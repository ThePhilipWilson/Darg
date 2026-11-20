# Workbench: morning checklist (2026-11-17)

> History: the checklist from the first overnight build, when Darg was called Workbench, kept as written. How the app works now is in [README.md](README.md) and the docs it lists.

Open it from the **Workbench** shortcut on the desktop or in the Start menu. It runs from `D:\Apps\Workbench` on your real projects (Alpha, Beta, Project C).

## Built overnight

- **Agents survive restarts.** Close Workbench (or let it crash) and the agents are still listed as "saved". Send one a message and it carries on in the same Claude conversation. Tested: an agent told to remember PINEAPPLE still knew it after a hard kill and restart. × on an agent forgets it. Bench keeps its chat too ("New chat" starts over).
- **Consoles** (Term): tabs of shells. Saved commands appear as ▶ buttons: Alpha has **Web** (dotnet watch on the web build), **Tests** and **Git**; the others have **Git**. Add your own in Setup ("Name: command" per line). Nothing runs until you press it.
- **Docs**: each project's vault folder (`vault/Code/<Project>`), rendered, with a finder; `[[wikilinks]]` open the doc they name; Edit, then Save or Ctrl+S.
- **Code view edits**: type in a file, the tab shows ●, Ctrl+S saves; closing an edited tab warns once.
- **GitLab** on the same page as GitHub (for work; needs `glab` installed there).
- **A crash in a view no longer closes the app**: it's written to `%APPDATA%\Workbench\home\crash.log`.

## Please try (needs a real hand)

1. Drag a card between columns, and drag a post-it between columns.
2. Click a card: the panel opens; change something and Save.
3. Click a post-it, then **Make it a ticket**.
4. Code: open a file, type, Ctrl+S (try it on something harmless).
5. Docs: Edit and Save a doc you don't mind touching.
6. Term: type in a shell; press ▶ Git.
7. Start an agent, switch to another tab, wait for its toast, click the toast: it should open that agent.
8. Bench (the face, bottom right): ask it to do something to a board.

## Worth knowing

- **Alpha's latest CI run failed** (GitHub page). The seven before it passed.
- Ollama's 14B models don't fit on the GPU while Forge has SDXL loaded; the 7B ones do. Ollama is running now (started from the Tools page).
- The code editor can't be driven by Windows' accessibility interface, so typing in it is untested; everything else above was checked from screenshots or by pressing its buttons through that interface.

## Next, your call

- The phone and tablet plan: [PHONE-PLAN.md](PHONE-PLAN.md) has three questions for you.
- Jira for the work instance.
- Starting Forge and ComfyUI from Workbench (Forge needs Pinokio's start; ComfyUI's install path).
