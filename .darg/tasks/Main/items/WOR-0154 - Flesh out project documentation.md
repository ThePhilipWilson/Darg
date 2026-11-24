# WOR-0154: Flesh out project documentation

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** docs
- **Created:** 2026-11-20
- **Done:** 2026-11-20
- **Moved:** 2026-11-20 21:29:56

## Description

Write actual useful docs for Workbench: what it is, how to set it up and run it, how the main parts fit together, and how to use each section. Docs live in D:/GitHub/ThePhilipWilson/Workbench/docs.

Harvest the existing reports: there are a lot of them, and they hold decisions, findings, gotchas and explanations of how things work that never made it into the docs. Go through them (including archived ones) and pull out anything worth keeping, then fold it into the relevant doc and link back to the report where useful.

## Decision answered 2026-11-20 01:58

**Question:** Do docs/cards-and-qa.md and the wording of the new docs match how you actually use Darg, so the docs can ship as they are?
**Asked by:** agent QA (WOR-0154 round 2), 2026-11-20
- **Ship them** (recommended) → done: the facts are fixed and tested (full suite green, new DocsTests guard labels and variables); the wording is good enough.
- **Fix the wording** → back: send it back with your notes on what reads wrong or where cards-and-qa.md differs from how you work the flow.
- **Read them later** → stay: leave it here until you've had time to read them.
**Context:** Every fact QA flagged in round 1 is fixed and checked against the code. Automation can't tell whether cards-and-qa.md describes your real QA flow or whether the docs read right; read docs/README.md, then docs/cards-and-qa.md (70 lines).

**Answer:** mostly fine, can we standardise the formatting of the documentation and make sure that all the links between are correct and all the places there should be links there are. (2026-11-20 01:58)

## How to check

- **Round 4 (QA round 3's findings):** commit 6b4a750 on `android`, pushed. getting-started.md's table gains `DARG_REMOTE_PORT`, `DARG_TEST_QR_PYTHON`, `DARG_ANDROID_ROOT` and the keystore names, plus a new section "The phone app" (how to build and run it, `tools/android-setup.ps1`, `tools/android.ps1`, `adb reverse`, `-StopEmulator`). architecture.md names `src/Darg.Mobile` and `src/Darg.Android`. ANDROID.md (new "Where it stands"), REMOTE.md and decisions-and-gotchas.md no longer say the phone app isn't built. New test `DocsTests.Every_environment_variable_the_code_reads_is_in_getting_started` reads src, tests and tools the other way round.
- **Try it:** `pwsh -File tools/test.ps1 -Unit -Filter DocsTests` (7 tests). To see the new test fail: revert docs/getting-started.md to the commit before 6b4a750 and rerun; it names the missing variables (and the link test fails on the missing `#the-phone-app` heading).
- **Tests:** full suite before the rebase onto the newest `android`: unit 739/739, integration 110/110. Not rerun after the rebase (the only commit pulled in was another agent's).
- **Not done:** the "minor" section names mentioned without a link (judgement call, left); the same-doc anchor links doing nothing in Darg's Docs section is a separate ticket. The R4 harvest of reports was not redone: rounds 1 to 3 covered it.

### Earlier rounds

- **What changed (round 3, from Philip's answer: standardise the formatting, make every link right and add the missing ones):** all 11 docs share one format, written down at the end of `docs/README.md` ("How these docs are written"). Every doc opens with a title and an intro paragraph. Bold lines that stood in for headings (decisions-and-gotchas' Gotchas) are `###` headings. List items lead with a bold name and a colon. Fences name their language. Card ids are written in full. cards-and-qa.md lost its numbered headings, and its Decision example is indented inside the fence. Every mention of another doc is a link, to its section where one fits; the repo's CLAUDE.md is linked from the index. PLAN, MORNING and PHONE-PLAN keep their words under a quoted "History:" line. REMOTE.md (added to `android` meanwhile by AND-0002) is in the index and in the same format.
- **Stale facts fixed while linking:** guide.md's GitHub section as WOR-0151 made it (it still said PRs, issues and CI runs only); 🍃 Optimise tokens (WOR-0159); card links (WOR-0158, a section in architecture.md); the API list with the phone, read-only and push commands; Settings' Phone and ntfy parts; the phone line under Not built.
- **New tests** in `tests/Darg.Core.Tests/DocsTests.cs`: (1) every link reaches a file and every `#part` a heading, using GitHub's slugs, with `card:` and other schemes and code spans skipped; (2) every doc named in the text is a link, README.md excepted; (3) the shared formatting: one title then an intro, no bold line as a heading, a language on every fence, no shortened card id. Run on the old docs, (2) failed on ANDROID.md naming PHONE-PLAN.md unlinked, and (3) failed on the missing intros, unnamed fences, bold headings and "WOR-0139 to 0141". (1) failed on a broken anchor I put in by hand. All three pass now (DocsTests 6/6).
- **Commit:** 7b4569b on `android`, pushed. `wor-0154` was merged and deleted; its worktree folder is held by a build node, safe to delete later.
- **Try it:** `pwsh -File tools/test.ps1 -Unit -Filter DocsTests`. Then open the Docs section and follow the links from `docs/README.md`. In Darg a `doc.md#part` link opens the doc, not the heading (`DocLinks` drops the anchor); on GitHub it goes to the heading.
- **Look for:** whether the format reads well to you. Formatting and links are tested; whether you like the house style isn't.
- **Tests:** full suite on the branch after the first rebase: unit 727/727, integration 97/97. After the last rebase (one phone test commit, no docs) the unit suite reran: 727/727.
- **Not done:** the root `README.md` is untracked in the main folder (not this card's work), so it doesn't link to `docs/`.

## QA notes

- 2026-11-20 QA: built and tested a3fb28c in the main folder, which is on `android` at a3fb28c. The full suite `pwsh -File tools/test.ps1` built clean and passed: unit 619/619, integration 65/65. DocsTests passes, and it fails when it should: a scratch `docs/zz-qa-probe.md` with a dead link failed both tests with the right messages (the file was removed afterwards). No studio name in the new docs.
- Spot-checked about 40 claims against the code. Most hold, including: the Section enum list, the Update ready / Use last build / Rebuild labels, agent purposes, fettle-briefing.md with --append-system-prompt-file, attentionColumn, the 320 px row minimum, 15 titles per board, the Bug prompt's first line, "Drafted by Darg", the known gaps for Mark done and code fences, CLAUDE.local.md in Context, the Tools defaults and the API command names.
- **Wrong:** getting-started.md says "Every `DARG_*` name also works as `WORKBENCH_*`". Only `DARG_HOME` and `DARG_INSTANCE` fall back to the old name (`InstanceConfig.Env`, src/Darg.Core/Instance.cs:43). `DARG_SECTION`, `DARG_AGENT`, `DARG_AGENT_CWD`, `DARG_FLOW`, `DARG_ADD_FOLDER` and `DARG_STALL_MS` read plain `GetEnvironmentVariable` (MainViewModel.cs:282-296, App.axaml.cs:34). Fix the doc so it names only those two.
- **Stale label:** guide.md (Card panel) and section 3 of cards-and-qa.md call the button "🔔 Needs my attention". It is "🔔 For human attention" (BoardView.axaml:362). The panel's done buttons are "✓ Done" and "✓ Mark done", not "Done".
- Minor: the Reports filter labels are "All reports, Finished tasks, Investigations, QA reports" (ReportsViewModel.cs:30), and they are a kind filter, not tabs as guide.md says. Fix it while you're in there.
- Can't be checked by automation: whether cards-and-qa.md matches how Philip actually uses the flow, and the docs' wording and tone. Only he can judge those by reading. Once the facts above are fixed, the next QA round should ask him as a Decision.
- 2026-11-20 QA round 2 (158a70a, in a clean worktree because the main folder had eight test runs queued and another agent had moved src/Darg.Mobile out for its run): the full suite `pwsh -File tools/test.ps1` built clean and passed, unit 672/672 and integration 79/79. `-Unit -Filter DocsTests` passed 4/4.
- Every round 1 finding is fixed, checked against source. getting-started.md names only DARG_HOME / DARG_INSTANCE as falling back (`Env("DARG_HOME")` and `Env("DARG_INSTANCE")`, Instance.cs:31-33). "🔔 For human attention", "✓ Done" and "✓ Mark done" are in BoardView.axaml:376/378/404. The Reports kinds match ReportsViewModel.cs:30, are a TabStrip (ReportsView.axaml:11), and get counts added by RelabelKinds. RestartLabel is "Restart". The only "Needs ... Attention" left in the current docs is the historical rename note in guide.md:17.
- The new tests fail when they should. A scratch doc with "🔔 Needs my attention", `WORKBENCH_SECTION` and `DARG_NOPE` failed both tests, naming "🔔 Needs", WORKBENCH_SECTION and DARG_NOPE. `WORKBENCH_HOME` was correctly allowed. The scratch worktree was removed afterwards.
- Minor, not blocking: the label test matches only the icon plus its first word, so "🔔 For something else" would pass. No studio name in docs/ or DocsTests.cs.
- Can't be automated: whether cards-and-qa.md matches how Philip runs the flow, and the docs' wording and tone. That's a judgement made by reading, so it is the Decision above.
- Still not done (as the card says): the untracked root README.md doesn't link to docs/.
- 2026-11-20 QA round 3 (7b4569b, checked at the branch head b1a0f29 in a clean worktree `.shots/qa154-r3`): the full suite `pwsh -File tools/test.ps1` built clean and passed, unit 727/727 and integration 107/107. `-Unit -Filter DocsTests` passed 6/6.
- **Passed, Philip's round 3 ask:** all 113 links in docs/ reach a file, and every `#part` matches an anchor id in GitHub's own rendering of the docs (fetched as HTML from the repo at b1a0f29, so this is GitHub's slugger, not the test's copy of it). All 113 also resolve through Darg's own `DocLinks.Resolve`, the same call the Docs section makes: none Missing, `../CLAUDE.md` opens as an outside doc, `mockup-v1.html` opens in the browser. Every doc has a title and intro, no bold-line headings, every fence names its language, and headings carry no numbers or icons. 32 labels the docs name (📱 Pair a phone, 🍃 Optimise tokens, ✦ New topic, → card, Use last build, Pull in and others) are all in src/. No studio name in docs/ or DocsTests.cs.
- **The new tests fail when they should:** a scratch `docs/zz-qa-probe.md` with a dead `#part`, an unlinked ANDROID.md, a bold-line heading, an unnamed fence and "WOR-0139 to 0141" failed four DocsTests, each naming its fault. The file was removed afterwards.
- **Wrong:** getting-started.md says it lists "every environment variable it reads", but its table leaves out `DARG_REMOTE_PORT` (the app reads it for the phone server's port, PhoneViewModel.cs:31) and `DARG_TEST_QR_PYTHON` (QrCodeTests.cs:40, named only in REMOTE.md). Both were already in the code at 7b4569b. Add them to the table. Then defend the whole class, not these two: add a DocsTests check that runs the other way, so every `DARG_*` name read in src/ and tests/ has a row in getting-started.md (or is on a short list of names that are set, not read by hand).
- **Stale since AND-0003 (b1a0f29, which came after this card's commit):** ANDROID.md ("the phone app isn't built yet"), REMOTE.md ("the phone app itself is AND-0003 onwards") and decisions-and-gotchas.md's Not built ("The phone app itself isn't built") are now out of date. getting-started.md and architecture.md don't mention `src/Darg.Mobile`, `src/Darg.Android` (outside Darg.sln), `tools/android-setup.ps1`, `tools/android.ps1`, or `DARG_ANDROID_ROOT` / `DARG_ANDROID_KEYSTORE`. The repo's CLAUDE.md already describes all of these. This isn't a fault in round 3, but bring the docs up to date while you're in there.
- **Minor, a judgement call:** a few section names are mentioned without a link, e.g. cards-and-qa.md "no longer jump to Agents" and guide.md's intro "Fettle sits in the corner". The tests can't catch these, since they only check doc file names.
- **For a separate ticket, not this card:** in Darg's Docs section, a link to a heading in the same doc does nothing (`DocLinks.Resolve` answers "That link is to a heading in this doc."). That hits 6 links: guide.md → #setup (twice), #docs, #agents, and cards-and-qa.md → #for-human-attention-and-decisions, #known-gaps. On GitHub they work. docs/README.md's "Darg opens such a link at the doc, not at the heading" holds for links to other docs, but a same-doc link in Darg does nothing at all.
- 2026-11-20 QA round 4 (6b4a750, main folder on `android`): full suite `pwsh -File tools/test.ps1` built clean and passed, unit 762/762 and integration 113/113. DocsTests 7/7. With getting-started.md reverted to 6b4a750~1 the new env-var test failed naming DARG_ANDROID_KEYSTORE(_ALIAS/_PASS), DARG_ANDROID_ROOT, DARG_REMOTE_PORT, DARG_TEST_QR_PYTHON, and the link test failed on the missing #the-phone-app; restored afterwards. Phone-app docs match tools/android*.ps1, no stale 'isn't built' lines left in docs/, architecture.md names Darg.Mobile and Darg.Android, no studio name. Not automated: wording and tone, accepted by Philip's earlier answer.
