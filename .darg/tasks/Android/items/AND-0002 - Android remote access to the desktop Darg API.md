# AND-0002: Android: remote access to the desktop Darg API

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** android, mobile, backend
- **Created:** 2026-11-19
- **Done:**
- **Moved:** 2026-11-23 00:44:14

## Description

Agents, repos and files live on the PC, so the phone needs a safe way to reach the Darg API: authenticated, encrypted, working off the home network (e.g. Tailscale or a relay). Define what is exposed, how the token is issued/revoked and how pairing works. Done when: a phone can call projects, cards and agents against the running desktop app, and an unauthenticated request is refused.

## Decision answered 2026-11-23 00:44

**Question:** Phone access passed every check that can run on this PC; only a real phone over Wi-Fi and the firewall prompt are left. Close it now or hold it?
**Asked by:** agent QA (AND-0002 QA pass 3), 2026-11-23

![Settings while pairing, as it opens](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-and0002-r3-pairing.png)

- **Done** (recommended) → done: accept it; the firewall prompt and a real phone get their check the first time you pair the phone app.
- **Hold until a real phone pairs** → stay: it waits in For Human Attention until you've paired from the phone.
- **Send back: show the QR on screen** → back: Pair a phone (and Fettle's phone-pair) should scroll Settings so the QR is in view; at the normal window size it opens half below the fold (screenshot above).

**Context:** Unit 830/830, integration 161/161, plus a live run over real TLS on the LAN address: pair, 401 without a token, projects/cards/agents/boards/move-card with one, 403 for the blocked commands, revoke (API and Settings button), 5 wrong tries, restart, `"remote": false`. The QR off the live screen decodes to the exact pairing link. Detail under QA 2026-11-23.

**Answer:** Done (2026-11-23 00:44)

## How to check

**What changed:** a second server for phones, `RemoteServer` (`src/Darg.Core/Remote.cs`), on port 47818 on every network the PC is on, over TLS with a self-signed certificate the phone pins from the pairing QR. Settings has a new **Phone** part: 📱 Pair a phone shows a QR and an 8-character code (10 minutes, once, 5 wrong tries); the phone trades it at `POST /pair` for a token of its own (only its hash is kept, in `remote-devices.json`); each paired phone has Revoke. Without a good token every request is a 401; a phone may call only the commands in `RemoteAccess.Commands` (projects, cards, agents, boards, move-card, notes/docs/reports, decide, send-back, say, work-on, the fettle-* ones…), anything else is a 403. Nothing listens until a phone is paired; `"remote": false` in instance.json leaves it out. Fettle can run `phone-pair`, `phones` and `phone-revoke` (PC-only). The full write-up is `docs/REMOTE.md`.

**Branch and commits:** on `android`: b9836fa (the feature), 620890e, 3ef6890, aad19c4 (adds the phone commands AND-0008, AND-0004 and AND-0007 brought in), 6438f98 (the tests keep off port 47818).

**Choices I made for you:**
- **Tailscale, no relay.** For off the home network I chose Tailscale over a relay, so nothing goes through anyone else's server. Tailscale isn't installed on the PC yet, so for now it only works on the home network.
- **What a phone can and can't call.** It can't call `start-agent`, `run-command`, `run-flow`, `work-all`/`qa-all`, `forget-agents`, `show`/`theme` or the phone-* commands.
- **No expiry.** Tokens last until you revoke them.

**Try it** (after Update or `pwsh -File tools/publish.ps1` from a terminal):
1. ⚙ Settings → Phone → 📱 Pair a phone. Windows Firewall should ask about Darg the first time: allow **private** networks.
2. Scan the QR with any phone camera app: it should read `darg://pair?...hosts=192.168.68.73&port=47818&code=...&fp=...`. Only your Ethernet address should show; the WSL one is filtered out.
3. Pair from another machine on the Wi-Fi, or from this one: `curl -k -X POST https://192.168.68.73:47818/pair -d "{\"code\":\"ABCD-EFGH\",\"name\":\"curl\"}"` with the code shown. You get a token, and Settings lists "curl".
4. `curl -k https://192.168.68.73:47818/api/projects` should give 401. Add `-H "Authorization: Bearer <token>"` and it should list your projects; `/api/cards?project=workbench` should list cards.
5. Revoke "curl" in Settings, and the same call should give 401 again.

**What to look for:** the firewall prompt and the phone actually reaching the PC over Wi-Fi. I couldn't test either: I had no phone, and I didn't want to trigger a firewall dialog on your screen overnight. The tests cover the rest end to end over real TLS.

Settings while pairing (from the integration test): ![pairing](D:/GitHub/ThePhilipWilson/Workbench/.shots/and-0002/pairing.png)

## QA 2026-11-23

Built `android` at 6ffa74f (has b9836fa, 620890e, 3ef6890, aad19c4, 6438f98) in a clean worktree, `.shots/qa-and0002-r3`.

**Passed**
- `pwsh -File tools/test.ps1`: unit 830/830, integration 161/161 (includes `PhoneAccessTests` and `RemoteTests`). No failures.
- QR test with a real reader: `DARG_TEST_QR_PYTHON` set to a venv with OpenCV 5.1 (`.shots/qr-venv`). `QrCodeTests.Decodes_back_to_the_text_with_a_real_reader` passes.
- Live run of the debug build on a throwaway instance (`.shots/qa-and0002-r3-home`), driven through the local Darg API with `.shots/qa-and0002-r3-live.py`. Phone calls went over TLS to `192.168.68.73:47818`. All 46 checks passed:
  - Before any pairing, nothing listens on 47818. `phone-pair` opens it, on `::`, every address.
  - The pairing link offers only `192.168.68.73`; no WSL or 169.254 address.
  - `/hello` answers; the certificate's SHA-256 on the LAN address and on 127.0.0.1 matches the `fp` in the link.
  - With no token, a wrong token, or a bad token on `/api`, the answer is a 401 with `WWW-Authenticate: Bearer`.
  - 4 wrong codes give 403 "Wrong code". Then the right code, typed in lower case, pairs. The same code a second time gives 403.
  - `remote-devices.json` keeps the token's hash, never the token.
  - With the token: `projects`, `cards`, `card`, `agents` and `boards` all answer 200. `move-card` from the phone moved TST-0003, and board.json changed. Lower-case `bearer` is accepted.
  - `GET /api` lists 25 commands, none of the blocked ones. `start-agent`, `run-command`, `run-flow`, `work-all`, `qa-all`, `forget-agents`, `show`, `theme`, `phones`, `phone-pair` and `phone-revoke` each give 403. An unknown command gives 403, another path 404.
  - Revoking one of two phones: the revoked phone gets 401 and the other still gets 200. Revoking the last phone stops the listener.
  - After 5 wrong tries the right code is refused. A new pairing works.
  - Restarting with a phone paired: it listens at launch and the old token still works.
  - `"remote": false`: it doesn't listen even with a phone paired, and `phone-pair` answers 400 "Phone access is turned off…".
- Settings → Phone, live: the QR taken off a screenshot of the running app (`.shots/qa-and0002-r3-pairing-scrolled.png`) decodes with OpenCV to the exact link `phone-pair` returned. I paired with the code shown, then pressed "Revoke uia-phone" through UI Automation, and the device went. Each Revoke button is named for its phone.

**Can't be automated from this PC, and why** (ticket these if you want them covered):
- **A real phone reaching the PC over Wi-Fi.** All my calls came from this PC. A call from the PC to its own LAN address goes through loopback and the firewall's inbound rules don't apply to it. So nothing here proves a second device gets through. Checking that needs a second device on the network, or the Android emulator going out through the host's NAT, which is still the same host.
- **The firewall prompt.** I ran the build from `.shots/qa-and0002-r2-app`, which already has an inbound-allow rule for Private networks (left from the earlier QA pass at 00:12 today), so no dialog appeared on your screen. That rule shows the prompt was shown and allowed once already. The installed copy (`D:/Apps/Darg/Darg.Desktop.exe`) has no rule yet, so expect the prompt the first time you pair from it. Your Ethernet profile is Private, so allowing Private is enough.

**Worth knowing, not failures**
- **The QR opens below the fold.** At the default window size (1516×929) Settings opens at the top, and the QR is half off-screen until you scroll (decision screenshot).
- **The 403 list doesn't stop a phone from starting work.** `fettle-say`, `work-on`, `new-card` and `plan` are all allowed, and each one starts an agent from text sent by the phone. Fettle can also call the desktop API itself. So the list stops a phone doing something by accident, but anyone holding a token can still start agents. That's in line with "the things worth doing from the sofa", but it should be a choice you make on purpose.
- **Old firewall rules.** Windows Firewall holds about 160 inbound-allow rules on **Private and Public** for `testhost.exe` and `Darg.Desktop.Tests.exe` across worktrees, including `.claude/worktrees/and-0002`. They date from before 6438f98 kept tests on loopback, which `RemoteTests.No_test_listens_beyond_loopback` now enforces. They only matter while one of those exes is listening, but you may want to clear them out.
- **`docs/REMOTE.md` is out of date.** Its list of phone commands leaves out `plan`, which WOR-0176 added to `RemoteAccess.Commands` later.
