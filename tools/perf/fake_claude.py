"""A stand-in for claude in stream-json mode, for load-testing Darg (WOR-0076).

FAKE_RATE     tool calls per second per agent (default 5); each is a text, a tool_use and a tool_result
FAKE_SECONDS  how long a turn streams (default 60)
FAKE_RESULT   characters in each tool result (default 4000)
FAKE_TOUCH    1: rewrite the agent's card file every FAKE_TOUCH_EVERY seconds (default 10), as agents edit cards
"""
import json, os, re, sys, threading, time

args = sys.argv[1:]
lock = threading.Lock()

def emit(o):
    with lock:
        sys.stdout.write(json.dumps(o) + "\n")
        sys.stdout.flush()

if "--output-format" in args and args[args.index("--output-format") + 1] == "text":
    sys.stdin.read()  # a Haiku question (model pick, chat title)
    print("Standard | ordinary work")
    sys.exit(0)

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
        text = "".join(c.get("text", "") for c in m["message"]["content"] if isinstance(c, dict))
        threading.Thread(target=work, args=(text,), daemon=True).start()
