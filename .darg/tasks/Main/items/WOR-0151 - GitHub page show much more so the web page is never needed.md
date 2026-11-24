# WOR-0151: GitHub page: show much more so the web page is never needed

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** github
- **Created:** 2026-11-20
- **Done:**
- **Moved:** 2026-11-20 21:39:40

## Description

Philip wants to see a lot more from GitHub inside the GitHub section, without opening the web page.

Figure out what would be useful to show (e.g. PRs, issues, CI/check runs, reviews and comments, releases, branches, commits, notifications) and how to lay the page out properly. Write up a proposal first, then build it.

## Decision answered 2026-11-20 21:39

**Question:** Round 2 passes every check; the one thing untried is ✓ Mark read on your real account, since it writes to GitHub. Is that enough to call WOR-0151 done?
**Asked by:** agent cec837a71a, 2026-11-20
- **Done** (recommended) → done: take it as it is; Mark read gets its first real try the next time you clear a "CI workflow run failed" notification.
- **Fix the two small things first** → back: send it back for the "main is failing" label that can name another branch's run, and the clipped log lines in the Overview box (QA notes, 2026-11-20 21:35).
- **Leave it for now** → stay: it stays here until you've tried Mark read yourself.

**Context:** On real Project C data, the Overview box says why main fails without a click (below). The Site tabs worked, and so did the log cut on Alpha run 144. Full suite: unit 762/762, integration 113/113.

![Overview on real Project C data](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0151-home/ps-overview2.png)

**Answer:** Done (2026-11-20 21:39)

## Proposal

What was on the page: per repo, three columns of open PRs, open issues and the last 8 CI runs, each a title and an Open ↗ to the browser. Checked against the real repos (2026-11-20): Philip's have almost no PRs or issues; what sends him to the web is **CI** (145 runs on one repo, failures he can't read without the browser), **notifications** (nearly all "workflow run failed"), and commits. So CI gets the most depth.

Layout: the Reports layout. Left, a list; right, the picked thing in full, so nothing needs Open ↗.

- **Top bar:** a tab per repo when the project has several (Site: Site, Workspace), ↻ Refresh, when read and as which account.
- **Left tabs:** Overview · Actions · Pull requests (n) · Issues (n) · Commits · Branches (n) · Releases (n) · Notifications (n unread).
- **Overview:** description, public/private, default branch; the default branch's latest CI and, if it failed, why; the newest of each other tab. A row there opens it in its tab.
- **Actions:** every run (state, title, workflow, branch, event, run number, how long, when). Picked: its jobs and their steps with ✓ ✗, the failure annotations GitHub shows, and for a failed job the lines of its log that matter (errors, failed tests, the end), with the whole log a click away. → card makes a card from a failed run with that excerpt in it.
- **Pull requests:** draft, checks, review decision, +/−, comments, branches. Picked: the description, the checks, the files changed with +/−, and the conversation (comments, reviews, line comments) in order.
- **Issues:** labels, assignees, comments. Picked: the description and every comment. → card stays.
- **Commits:** the default branch's last 30 with each one's CI. Picked: the whole message, the files with +/− and the diff.
- **Branches:** each with its last commit, CI and open PR. Picked: its last commit, as Commits shows it.
- **Releases:** tag, name, date. Picked: the notes and the assets with size and downloads.
- **Notifications:** this repo's, unread first, with why (ci_activity, review_requested…). Picked: the thing it's about; ✓ Mark read.

Read-only apart from → card and Mark read. GitLab repos keep what they had (MRs, issues, pipelines) in the same tabs. Not in this card: writing (commenting, merging, re-running CI), and the board showing PRs and issues (PLAN.md's other line).

## How to check

**Round 2 (QA notes of 01:45):** commit af6b964 on `android` (pushed): https://github.com/ThePhilipWilson/Darg/commit/af6b964. Round 1 was 9434764.

**What changed this round:**
- Cards made from an issue or PR carry its description again (the list query asks for `body`).
- The log cut ignores counts of none (dotnet's padded `Failed:     0`, `0 failed`, `failures: 0`, `failures="0"`, `0 Error(s)`) and leaves out passing tests, build output, restores and banners. On the real Alpha run 144 the cut is now 10 lines and the → card excerpt 655 characters (was about 80 lines and 5,948).
- PR, issue and comment text drops a template's `<!-- … -->` comments.
- Overview: when the default branch's latest run failed, a red box on the right says so with the log cut and a See the run button; a row picked on Overview opens in its own tab.

**Try it:** update the app (header ⟳ → Update, or `pwsh -File tools/publish.ps1` from a terminal), open Alpha, click GitHub on the rail.

**Look for:**
- Overview: if Alpha's main is red, the box on the right says why without a click. Pick any row: it should jump to its tab with that row picked.
- Actions → run 144: "the lines that matter" is the failing DocsPathTests test, its message, the `Failed!` summary and the exit code, nothing else.
- Site (two repos): a tab per repo in the top bar; switching keeps the tab you're on.
- Still never tried on your real account: ✓ Mark read (it writes to GitHub). Only the fake gh.

**Screenshots** (headless, fake gh): ![overview](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0151-shots/overview-failing.png) ![two repos](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0151-shots/two-repos.png)

**Tests:** `pwsh -File tools/test.ps1 -Filter GitHub`. Whole suite with this work on 3b07fa4: unit 760/760, integration 113/113.

## QA notes

### 2026-11-20 01:45

QA by a QA agent. Built and tested `android` at bcb41ae (holds 9434764). Full suite `pwsh -File tools/test.ps1`: unit 670/670, integration 79/79, nothing failed. I also ran Darg.Core's `GitHub.Load` and `GitHubDetail` read-only against the real repos through a probe (`.shots/qa-0151-probe`, not committed). The repos were Alpha, Project C, Site, Darg, Beta and claude-draw, plus the public `cli/cli`, because none of Philip's repos has a PR, issue or release.

**What works on real data:** the repo read (3 s, 9 s for cli/cli). Alpha run 144 shows its jobs and steps with ✓ ✗, and "What GitHub says" lists the failure first. Its log cut contains the failing `DocsPathTests.NoTrackedFile_PointsAtTheVaultsAlphaFolder` and its message. Project C run 94's cut is a clean 11 lines ending at the artifact-quota error. The commit diff and checks show, and so do branches with the default first. Notifications read 50 with all 50 unread, which is where the "50+" comes from. On cli/cli, a PR shows its checks, files, and reviews with their line comments in order, and an issue shows all its comments.

**Needs fixing:**

- **Cards made from a GitHub issue or PR lost their description (regression).** The new GraphQL list query (`GitHub.RepoQuery`, issues and pullRequests nodes) doesn't ask for `body`, so `HubItem.Body` is always "". On cli/cli every issue and PR came back with bodyLen=0. `GitHubViewModel.ToCard` builds the card from `i.Body`, so the card gets only "From GitHub issue …: url". Before 9434764 the REST list carried the body and the card had it. The fix is to add `body` to the list query, or build the card from the picked detail's body. No test covers → card from an issue or PR. The fake's REPO has no body either, so a test written now would show the gap.
- **The log cut is mostly noise on a real dotnet CI log.** `GitHubDetail.Harmless` has `Failed: 0\b`, but dotnet test pads its summary as `Passed!  - Failed:     0, Passed:     7, …`. Every passing test project's summary line therefore counts as a failure line. Alpha run 144's "lines that matter" is about 80 lines, mostly passing projects' summaries and build output with ±2 lines around each, and the → card excerpt is 5,948 characters. The real failure is at the top, so it's still readable, but it isn't the cut the card promises. The fake log (`tools/perf/fake_gh.py` LOG) uses a single space (`Failed: 1, Passed: 70`), which is why the tests pass. By the "defend against the class" rule, cover zero-count summaries in their padded and other shapes: `Failed:     0`, `0 failed`, `failures: 0`, `Failed=0`, `0 Error(s)`. Then add a padded line to the fake log, or a unit case taken from run 144's real log.

**Minor (your call whether to fix in this card):**

- PR descriptions keep HTML comments (`<!-- … -->`, PR templates). `Markdown.cs` has nothing that drops them, so I expect them to show as raw text in the PR's body. I saw this in cli/cli's PR #15100 text, not on screen.
- The Proposal says Overview shows, for the default branch's latest CI, "if it failed, why", and that "a row there opens it in its tab". On screen, Overview rows open in place and the summary on the right gives no reason for a failure. The How to check section doesn't claim either, so this may be a deliberate cut.
- In the fake's failed-run shot, the cut's last 10 lines before the final error are passing tests (`Passed Proj.Tests.Later21…29`). That's the `tail` rule working as written, but in a dotnet log they're always noise.

**Not checked, and why:**

- ✓ Mark read on Philip's real account. It writes to GitHub and changes his notification state, so I only saw it against the fake gh (the integration test passes).
- The repo tabs for a two-repo project (Site: Site and Workspace). Both repos read fine through Core, but no integration test has two repos and I didn't drive the real UI. This could be automated by giving `DargApp` a second repo with a GitHub origin.

### 2026-11-20 21:35

QA round 2 by agent cec837a71a. Built `android` at 6b4a750 (holds af6b964) in a worktree of its own (`.shots/qa-0151-r2`).

**Checked, and passed:**
- Full suite `pwsh -File tools/test.ps1`: unit 762/762, integration 113/113, nothing failed.
- I checked the log cut on real data with last round's read-only probe, rebuilt against this Core. Alpha run 144's "lines that matter" is now 10 lines: the failing `DocsPathTests.NoTrackedFile_PointsAtTheVaultsAlphaFolder`, its message, `Failed!  - Failed:     1, Passed:    66…` and the exit code. The → card excerpt is 655 characters, as claimed. Project C run 94's cut ends at the artifact-quota error.
- I ran the real UI on real GitHub data (no writes). I launched the debug build on a throwaway instance (`.shots/qa-0151-home`) whose projects are empty local repos with the real remotes (Project C as example-studio-eng; Site with Site and Workspace as ThePhilipWilson). I drove it with UI Automation.
  - **Overview:** Project C's main is red, and the box says "✗ main is failing: …" with the job, GitHub's annotation and the log cut, with no click (`D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-0151-home/ps-overview2.png`).
  - **Rows open in their tabs:** an Overview run row opened Actions with run #8 picked, and a notification row opened Notifications with it picked (`ps-row-opened.png`). See the run opened Actions with run 94 picked (`ps-see-run.png`).
  - **Site:** a tab per repo. With Commits picked on Site, switching to Workspace kept Commits, and so did switching back (`site-workspace.png`).
- Cards made from an issue or PR keep the body, and template comments are dropped. Both are covered by the new tests in the passing suite (the fake gh now leaves out fields that weren't asked for). None of Philip's repos has an issue or PR to try it on.

**Small things, not blocking (Philip's call):**
- If none of the last 30 runs is on the default branch, `HubRepoView.Main` falls back to the newest run on any branch. The box then reads "✗ main is failing: <another branch's run>". On Philip's repos it can't happen today: every run is on main. A PR-heavy repo would hit it.
- The Overview box's log block doesn't wrap. The last line of Project C's cut is clipped at "Usage is recal…". The annotation above it shows the full text.
- On a run with several jobs (Project C run 94), the failed job is listed last, below every passing job's steps, so you scroll to reach it. This layout comes from round 1.

**Not checked, and why:**
- ✓ Mark read on the real account. It changes Philip's GitHub notification state, which is a write to an outside service on his account. I didn't do that without his OK. The code calls `gh api -X PATCH notifications/threads/<id>`, which as far as I know is GitHub's mark-as-read endpoint. The fake-gh integration test passes. Automating it for real would need a throwaway notification on a test account.
