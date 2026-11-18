"""A stand-in for claude in stream-json mode, for load-testing Darg (WOR-0076).

FAKE_RATE     tool calls per second per agent (default 5); each is a text, a tool_use and a tool_result
FAKE_SECONDS  how long a turn streams (default 60)
FAKE_RESULT   characters in each tool result (default 4000)
FAKE_TOUCH    1: rewrite the agent's card file every FAKE_TOUCH_EVERY seconds (default 10), as agents edit cards
FAKE_LOG      a file: each start (its args and folder), each message it is sent and each one-shot question is
              appended to it as a JSON line, for the integration tests (WOR-0127) to check what an agent was given
"""
import json, os, re, sys, threading, time

args = sys.argv[1:]
lock = threading.Lock()
log_file = os.environ.get("FAKE_LOG")

def log(kind, **fields):
    if not log_file:
        return
    line = json.dumps(dict(kind=kind, pid=os.getpid(), **fields)) + "\n"
    # several fakes (an agent and a model pick) open the log at once, and Windows refuses the second open
    # for a moment: retry, and never let the log take the fake down with it
    with lock:
        for _ in range(100):
            try:
                with open(log_file, "a", encoding="utf-8") as f:
                    f.write(line)
                return
            except OSError:
                time.sleep(0.02)

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

def work(text):
    card = re.search(r"Card file: (.+)", text)
    card = card.group(1).strip() if card else None
    emit({"type": "system", "subtype": "init", "session_id": sid, "model": "fake", "cwd": os.getcwd(),
          "apiKeySource": "none", "permissionMode": "auto"})
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
    elif m.get("type") == "user":
        content = m["message"]["content"]
        text = content if isinstance(content, str) else "".join(c.get("text", "") for c in content if isinstance(c, dict))
        log("message", text=text)
        threading.Thread(target=work, args=(text,), daemon=True).start()
