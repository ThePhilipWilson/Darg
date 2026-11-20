"""A stand-in for claude in stream-json mode, for load-testing Darg (WOR-0076).

FAKE_RATE     tool calls per second per agent (default 5); each is a text, a tool_use and a tool_result
FAKE_SECONDS  how long a turn streams (default 60)
FAKE_RESULT   characters in each tool result (default 4000)
FAKE_TOUCH    1: rewrite the agent's card file every FAKE_TOUCH_EVERY seconds (default 10), as agents edit cards
FAKE_LOG      a file: each start (its args and folder), each message it is sent and each one-shot question is
              appended to it as a JSON line, for the integration tests (WOR-0127) to check what an agent was given

A message can steer the turn (AND-0007), for tests of what Fettle does:
FAKE_ASK      asks an AskUserQuestion ("Which board?": Main or Elsewhere) before the work, waits for the answer and
              logs it as "answered"
FAKE_CARD <project>: <title>   calls the Darg API's new-card ($DARG_API) as Fettle does, and replies with its link
FAKE_SPAWN <file>  leaves a process running as agents leave an emulator (WOR-0170): pwsh's Start-Process starts a
              hidden Windows PowerShell sleeping 10 minutes and exits, so nothing alive has it as a child; its id goes in
              the file (not a ping: DevServerTests counts those)
"""
import json, os, re, subprocess, sys, threading, time

args = sys.argv[1:]
lock = threading.Lock()
log_file = os.environ.get("FAKE_LOG")

def log(kind, **fields):
    if not log_file:
        return
    line = json.dumps(dict(kind=kind, pid=os.getpid(), **fields)) + "\n"
    # several fakes (an agent and a model pick) write the log at once. Append mode on Windows seeks to the end and
    # then writes, so two processes' lines overwrote each other: a chat's "start" lost its head to the title
    # question's "ask" and a test waited 30 s for it (WOR-0158). A lock folder (mkdir is atomic) takes turns
    # across processes; one left by a fake killed mid-write is ignored after 2 s. Windows also refuses an open for
    # a moment while another has the file: retry, and never let the log take the fake down with it.
    with lock:
        held = False
        for _ in range(100):
            try:
                os.mkdir(log_file + ".lock")
                held = True
                break
            except FileExistsError:
                time.sleep(0.02)
            except OSError:
                break
        else:
            # 2 s with no turn: the holder died mid-write. Clear its lock so later lines don't wait 2 s each too.
            try:
                os.rmdir(log_file + ".lock")
                os.mkdir(log_file + ".lock")
                held = True
            except OSError:
                pass
        try:
            for _ in range(100):
                try:
                    with open(log_file, "a", encoding="utf-8") as f:
                        f.write(line)
                    return
                except OSError:
                    time.sleep(0.02)
        finally:
            if held:
                try: os.rmdir(log_file + ".lock")
                except OSError: pass

def emit(o):
    with lock:
        sys.stdout.write(json.dumps(o) + "\n")
        sys.stdout.flush()

if "--output-format" in args and args[args.index("--output-format") + 1] == "text":
    question = sys.stdin.read()  # a Haiku question (model pick, chat title)
    log("ask", args=args, text=question)
    print("Standard | ordinary work")
    sys.exit(0)

log("start", args=args, cwd=os.getcwd(), darg_api=os.environ.get("DARG_API"))

rate = float(os.environ.get("FAKE_RATE", "5"))
seconds = float(os.environ.get("FAKE_SECONDS", "60"))
size = int(os.environ.get("FAKE_RESULT", "4000"))
touch = os.environ.get("FAKE_TOUCH") == "1"
touch_every = float(os.environ.get("FAKE_TOUCH_EVERY", "10"))
sid = "fake-%d" % os.getpid()
waiting = {}  # request id -> [event, response]: questions asked of the host, until it answers

def ask(n):
    rid = "ask-%d-%d" % (os.getpid(), n)
    slot = [threading.Event(), None]
    waiting[rid] = slot
    emit({"type": "control_request", "request_id": rid, "request": {"subtype": "can_use_tool", "tool_name": "AskUserQuestion",
          "input": {"questions": [{"question": "Which board?", "header": "Board", "multiSelect": False,
                                   "options": [{"label": "Main", "description": "the inbox"}, {"label": "Elsewhere", "description": "another board"}]}]},
          "tool_use_id": "tq%d" % n, "requires_user_interaction": True}})
    slot[0].wait()
    answer = slot[1] or {}
    log("answered", response=answer)
    return answer

def new_card(project, title):
    import urllib.request
    body = json.dumps({"project": project, "title": title}).encode("utf-8")
    req = urllib.request.Request(os.environ["DARG_API"] + "/new-card", data=body, method="POST")
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.loads(r.read().decode("utf-8"))

asks = 0

def work(text):
    global asks
    card = re.search(r"Card file: (.+)", text)
    card = card.group(1).strip() if card else None
    emit({"type": "system", "subtype": "init", "session_id": sid, "model": "fake", "cwd": os.getcwd(),
          "apiKeySource": "none", "permissionMode": "auto"})
    if "FAKE_ASK" in text:
        asks += 1
        answer = ask(asks)
        picked = ((answer.get("updatedInput") or {}).get("answers") or {}).get("Which board?", "nothing")
        emit({"type": "assistant", "message": {"content": [{"type": "text", "text": "Picked: " + picked}]}})
    made = re.search(r"FAKE_CARD (\S+): (.+)", text)
    if made:
        try:
            got = new_card(made.group(1), made.group(2).strip())
            emit({"type": "assistant", "message": {"content": [{"type": "text", "text": "Made " + got["link"] + "."}]}})
        except Exception as e:
            emit({"type": "assistant", "message": {"content": [{"type": "text", "text": "new-card failed: %s" % e}]}})
    spawn = re.search(r"FAKE_SPAWN (\S+)", text)
    if spawn:
        command = ("(Start-Process -FilePath powershell -ArgumentList '-NoProfile','-Command','Start-Sleep 600' -WindowStyle Hidden -PassThru).Id"
                   " | Set-Content -LiteralPath '%s'" % spawn.group(1))
        subprocess.run(["pwsh", "-NoProfile", "-NonInteractive", "-Command", command], stdin=subprocess.DEVNULL,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        emit({"type": "assistant", "message": {"content": [{"type": "text", "text": "Left a process running."}]}})
    end = time.time() + seconds
    last_touch = time.time()
    n = 0
    while time.time() < end:
        n += 1
        emit({"type": "assistant", "message": {"content": [{"type": "text", "text": f"Step {n}: " + "looking at the code " * 15}]}})
        emit({"type": "assistant", "message": {"content": [{"type": "tool_use", "id": f"t{n}", "name": "Read",
                                                             "input": {"file_path": f"src/File{n}.cs"}}]}})
        emit({"type": "user", "message": {"content": [{"type": "tool_result", "tool_use_id": f"t{n}", "content": "x" * size}]}})
        if touch and card and time.time() - last_touch > touch_every:
            last_touch = time.time()
            try:
                with open(card, encoding="utf-8") as f: body = f.read()
                with open(card, "w", encoding="utf-8") as f: f.write(body)
            except OSError:
                pass
        time.sleep(1 / rate)
    emit({"type": "result", "is_error": False, "result": "done", "total_cost_usd": 0.0})

for line in sys.stdin:
    try:
        m = json.loads(line)
    except ValueError:
        continue
    if m.get("type") == "control_request":
        emit({"type": "control_response", "response": {"subtype": "success", "request_id": m.get("request_id"), "response": {}}})
    elif m.get("type") == "control_response":  # the host answering a question (FAKE_ASK)
        r = m.get("response") or {}
        slot = waiting.pop(r.get("request_id"), None)
        if slot:
            slot[1] = r.get("response")
            slot[0].set()
    elif m.get("type") == "user":
        content = m["message"]["content"]
        text = content if isinstance(content, str) else "".join(c.get("text", "") for c in content if isinstance(c, dict))
        log("message", text=text)
        threading.Thread(target=work, args=(text,), daemon=True).start()
