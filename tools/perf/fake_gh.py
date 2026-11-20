"""A stand-in for the gh CLI, for the integration tests of the GitHub page (WOR-0151). Darg runs it when
GH_PATH names it (the tests point GH_PATH at a .cmd that runs this). It answers the calls the page makes for
one made-up repo, example-studio/proj: a GraphQL repo query, a pull request, an issue, workflow runs, a
failed run's jobs, annotations and log, a commit, notifications, and marking one read.

FAKE_GH_LOG   a file: each call's arguments are appended to it as a JSON line
"""
import json, os, re, sys

args = sys.argv[1:]
log_file = os.environ.get("FAKE_GH_LOG")
if log_file:
    with open(log_file, "a", encoding="utf-8") as f:
        f.write(json.dumps(args) + "\n")

def out(o):
    sys.stdout.write(json.dumps(o) if not isinstance(o, str) else o)
    sys.exit(0)

def fail(msg):
    sys.stderr.write(msg + "\n")
    sys.exit(1)

if args[:2] == ["auth", "token"]:
    out("fake-token")
if args[:2] == ["auth", "status"]:
    out("")
if not args or args[0] != "api":
    fail("fake gh: unknown command " + " ".join(args))

RUNS = [
    {"id": 902, "name": "CI", "display_title": "Count the coins right", "event": "push", "status": "completed", "conclusion": "failure",
     "head_branch": "main", "head_sha": "c0ffee1234567", "run_number": 12, "html_url": "https://github.com/example-studio/proj/actions/runs/902",
     "run_started_at": "2026-11-19T10:00:00Z", "updated_at": "2026-11-19T10:04:10Z"},
    {"id": 901, "name": "CI", "display_title": "First", "event": "push", "status": "completed", "conclusion": "success",
     "head_branch": "main", "head_sha": "abc1234567890", "run_number": 11, "html_url": "https://github.com/example-studio/proj/actions/runs/901",
     "run_started_at": "2026-11-18T10:00:00Z", "updated_at": "2026-11-18T10:03:00Z"},
]

def commit(oid, head, state, date):
    return {"oid": oid, "messageHeadline": head, "messageBody": "", "committedDate": date, "url": f"https://github.com/example-studio/proj/commit/{oid}",
            "author": {"name": "Pat", "user": {"login": "pat"}}, "statusCheckRollup": {"state": state}}

REPO = {"repository": {
    "description": "A throwaway repo for Darg's tests", "url": "https://github.com/example-studio/proj", "isPrivate": True,
    "defaultBranchRef": {"name": "main", "target": {"history": {"nodes": [
        commit("c0ffee1234567", "Count the coins right", "FAILURE", "2026-11-19T09:59:00Z"),
        commit("abc1234567890", "First", "SUCCESS", "2026-11-18T09:59:00Z")]}}},
    "pullRequests": {"totalCount": 1, "nodes": [{"number": 7, "title": "Shiny new coin art", "url": "https://github.com/example-studio/proj/pull/7",
        "isDraft": False, "updatedAt": "2026-11-19T08:00:00Z", "headRefName": "coin-art", "baseRefName": "main", "additions": 40, "deletions": 3,
        "reviewDecision": "CHANGES_REQUESTED", "author": {"login": "sam"}, "labels": {"nodes": [{"name": "art"}]}, "comments": {"totalCount": 2},
        "assignees": {"nodes": []}, "commits": {"nodes": [{"commit": {"statusCheckRollup": {"state": "SUCCESS"}}}]}}]},
    "issues": {"totalCount": 1, "nodes": [{"number": 3, "title": "Coins vanish on reload", "url": "https://github.com/example-studio/proj/issues/3",
        "updatedAt": "2026-11-19T07:00:00Z", "author": {"login": "kim"}, "labels": {"nodes": [{"name": "bug"}]}, "comments": {"totalCount": 1},
        "assignees": {"nodes": [{"login": "pat"}]}}]},
    "releases": {"totalCount": 1, "nodes": [{"tagName": "v0.1", "name": "First playable", "url": "https://github.com/example-studio/proj/releases/tag/v0.1",
        "isDraft": False, "isPrerelease": True, "publishedAt": "2026-11-10T12:00:00Z", "createdAt": "2026-11-10T12:00:00Z", "author": {"login": "pat"},
        "description": "## What's in it\n\n- coins\n- a shop", "releaseAssets": {"nodes": [{"name": "proj-win.zip", "downloadCount": 5, "size": 3145728,
        "downloadUrl": "https://github.com/example-studio/proj/releases/download/v0.1/proj-win.zip"}]}}]},
    "refs": {"totalCount": 2, "nodes": [
        {"name": "coin-art", "target": commit("d00d1234567", "Coin art", "SUCCESS", "2026-11-19T08:00:00Z"), "associatedPullRequests": {"nodes": [{"number": 7}]}},
        {"name": "main", "target": commit("c0ffee1234567", "Count the coins right", "FAILURE", "2026-11-19T09:59:00Z"), "associatedPullRequests": {"nodes": []}}]},
}}

PULL = {"repository": {"pullRequest": {"number": 7, "title": "Shiny new coin art", "url": "https://github.com/example-studio/proj/pull/7",
    "body": "Swaps the placeholder coins for **painted** ones.", "state": "OPEN", "isDraft": False, "createdAt": "2026-11-18T08:00:00Z",
    "headRefName": "coin-art", "baseRefName": "main", "additions": 40, "deletions": 3, "changedFiles": 2, "reviewDecision": "CHANGES_REQUESTED",
    "mergeable": "MERGEABLE", "author": {"login": "sam"}, "labels": {"nodes": [{"name": "art"}]},
    "files": {"nodes": [{"path": "art/coin.png", "additions": 0, "deletions": 0, "changeType": "MODIFIED"},
                        {"path": "src/Coins.cs", "additions": 40, "deletions": 3, "changeType": "MODIFIED"}]},
    "commits": {"nodes": [{"commit": {"statusCheckRollup": {"state": "SUCCESS", "contexts": {"nodes": [
        {"__typename": "CheckRun", "name": "build", "conclusion": "SUCCESS", "status": "COMPLETED", "detailsUrl": "https://github.com/example-studio/proj/runs/1"}]}}}}]},
    "comments": {"nodes": [{"author": {"login": "pat"}, "body": "Lovely shine on these.", "createdAt": "2026-11-18T09:00:00Z", "url": ""}]},
    "reviews": {"nodes": [{"author": {"login": "kim"}, "body": "The gold is too orange.", "state": "CHANGES_REQUESTED", "submittedAt": "2026-11-18T10:00:00Z",
        "url": "", "comments": {"nodes": [{"path": "src/Coins.cs", "line": 12, "originalLine": 12, "body": "Off by one here", "createdAt": "2026-11-18T10:00:00Z",
        "author": {"login": "kim"}}]}}]}}}}

ISSUE = {"repository": {"issue": {"number": 3, "title": "Coins vanish on reload", "url": "https://github.com/example-studio/proj/issues/3",
    "body": "Save, reload: the purse is empty.", "state": "OPEN", "createdAt": "2026-11-17T07:00:00Z", "author": {"login": "kim"},
    "labels": {"nodes": [{"name": "bug"}]}, "assignees": {"nodes": [{"login": "pat"}]},
    "comments": {"nodes": [{"author": {"login": "pat"}, "body": "Seen it too, on the second slot only.", "createdAt": "2026-11-18T07:00:00Z", "url": ""}]}}}}

JOBS = {"jobs": [{"id": 5001, "name": "test", "status": "completed", "conclusion": "failure",
    "started_at": "2026-11-19T10:00:05Z", "completed_at": "2026-11-19T10:04:05Z", "steps": [
        {"number": 1, "name": "Set up job", "status": "completed", "conclusion": "success", "started_at": "2026-11-19T10:00:05Z", "completed_at": "2026-11-19T10:00:09Z"},
        {"number": 2, "name": "Run tests", "status": "completed", "conclusion": "failure", "started_at": "2026-11-19T10:00:09Z", "completed_at": "2026-11-19T10:04:00Z"},
        {"number": 3, "name": "Upload results", "status": "completed", "conclusion": "skipped", "started_at": None, "completed_at": None}]}]}

ANNOTATIONS = [{"path": ".github", "start_line": 0, "annotation_level": "failure", "title": "", "message": "Process completed with exit code 1."}]

LOG = "\n".join(
    ["2026-11-19T10:00:05.0000000Z ##[group]Run dotnet test"] +
    [f"2026-11-19T10:01:{i:02d}.0000000Z   Passed Proj.Tests.Case{i}" for i in range(40)] +
    ["2026-11-19T10:02:00.0000000Z   Failed Proj.Tests.Coins_add_up [12 ms]",
     "2026-11-19T10:02:00.1000000Z   Expected: 10",
     "2026-11-19T10:02:00.2000000Z   Actual:   9"] +
    [f"2026-11-19T10:03:{i:02d}.0000000Z   Passed Proj.Tests.Later{i}" for i in range(30)] +
    ["2026-11-19T10:04:00.0000000Z Failed!  - Failed: 1, Passed: 70",
     "2026-11-19T10:04:00.5000000Z ##[error]Process completed with exit code 1."])

COMMIT = {"sha": "c0ffee1234567", "html_url": "https://github.com/example-studio/proj/commit/c0ffee1234567",
    "commit": {"message": "Count the coins right\n\nThe purse rounded down.", "author": {"name": "Pat", "date": "2026-11-19T09:59:00Z"}},
    "author": {"login": "pat"}, "stats": {"additions": 2, "deletions": 1},
    "files": [{"filename": "src/Coins.cs", "status": "modified", "additions": 2, "deletions": 1, "patch": "@@ -1,3 +1,4 @@\n class Coins\n-  int n;\n+  long n;\n+  // the purse"}]}

NOTICES = [{"id": "77", "unread": True, "reason": "ci_activity", "updated_at": "2026-11-19T10:04:20Z",
            "subject": {"title": "CI workflow run failed for main branch", "url": None, "type": "CheckSuite"}},
           {"id": "78", "unread": False, "reason": "review_requested", "updated_at": "2026-11-18T08:00:00Z",
            "subject": {"title": "Shiny new coin art", "url": "https://api.github.com/repos/example-studio/proj/pulls/7", "type": "PullRequest"}}]

if args[1] == "graphql":
    query = next((a[len("query="):] for a in args if a.startswith("query=")), "")
    if "pullRequest(number" in query:
        out({"data": PULL})
    if "issue(number" in query:
        out({"data": ISSUE})
    out({"data": REPO})

if "-X" in args and "PATCH" in args:
    out("")

path = args[-1]
routes = [
    (r"actions/runs\?", {"total_count": len(RUNS), "workflow_runs": RUNS}),
    (r"actions/runs/\d+/jobs", JOBS),
    (r"check-runs/\d+/annotations", ANNOTATIONS),
    (r"actions/jobs/\d+/logs", LOG),
    (r"commits/[0-9a-f]+/check-runs", {"check_runs": [{"name": "test", "status": "completed", "conclusion": "failure", "html_url": "https://github.com/example-studio/proj/runs/5001"}]}),
    (r"commits/[0-9a-f]+$", COMMIT),
    (r"notifications", NOTICES),
]
for pattern, answer in routes:
    if re.search(pattern, path):
        out(answer)
fail("gh: Not Found (HTTP 404)")
