# Phone access to the Darg API (AND-0002)

How a phone reaches the desktop app. Built 2026-11-20; the phone app itself is AND-0003 onwards. Code: `src/Darg.Core/Remote.cs` (server, devices, pairing, certificate), `src/Darg.Core/QrCode.cs` (the pairing QR), `src/Darg.Desktop/ViewModels/PhoneViewModel.cs` (Settings' Phone part, `RemoteHost`).

## The route

- **A second server, beside the localhost one.** The existing artifact and API server (`ArtifactServer`, port 47817) stays on `localhost` with the instance token in its URLs, for Fettle and agents. Phones get their own: `RemoteServer`, port **47818** (any free port if taken), listening on every network the PC is on.
- **Encrypted with TLS, pinned.** The instance makes a self-signed certificate once (`remote-cert.pfx` in the instance folder, 20 years) and the pairing QR carries its SHA-256 fingerprint. The phone trusts that certificate and no other, so nobody on the Wi-Fi can stand in for the PC, and there is no certificate authority or domain to set up.
- **Off the home network: Tailscale.** The server listens on every interface, so once the PC and the phone are both on a tailnet the phone reaches it at the PC's 100.x address, which the QR includes when Tailscale is running. No relay: nothing goes through a third party's server, and with no Tailscale it works on the home network only. Tailscale isn't installed on the PC yet (2026-11-20).
- **Nothing listens until a phone is paired.** The app starts the server at launch only when the instance has a paired phone, or when a pairing is opened; revoking the last phone stops it. `"remote": false` in instance.json leaves phone access out of an instance altogether (the work machine).

## Pairing

1. Settings → Phone → **📱 Pair a phone** (or ask Fettle: the `phone-pair` command opens Settings the same way). Darg shows a QR and an 8-character code (`ABCD-EFGH`), good for 10 minutes and once; 5 wrong tries end it.
2. The QR holds `darg://pair?v=1&name=<PC>&hosts=<lan>,<tailscale>&port=47818&code=<code>&fp=<sha256>`. The phone tries the hosts in order.
3. The phone sends `POST /pair` with `{"code": "...", "name": "Pixel 9"}` and gets back `{"token", "device", "pc", "commands"}`.
4. Every later call carries `Authorization: Bearer <token>`.

## Tokens

- One per phone: 32 random bytes. Darg keeps only its SHA-256 (`remote-devices.json` in the instance folder: id, name, hash, paired, last seen), so the file can't be used to call the API.
- **Revoke** in Settings (each paired phone has a Revoke button) or with `phone-revoke {device}`. The file is read on every request, so the next call from that phone is a 401.
- No expiry: a phone stays paired until revoked. Re-pairing gives a new token.

## What a phone can call

| Request | Needs a token | Does |
|---|---|---|
| `GET /hello` | no | `{"app": "darg", "pairing": true/false}`: is this Darg, is a pairing open |
| `POST /pair` | no (the code) | trades a pairing code for a token |
| `GET /api` | yes | the commands a phone may call, with their usage lines |
| `GET` or `POST /api/<command>` | yes | runs one, arguments as JSON body or query string, exactly as the desktop's API |
| anything else | | 401 without a good token, 404 with one |

Commands a phone may call (`RemoteAccess.Commands`): `projects`, `overview`, `agents`, `agent`, `cards`, `card`, `flows`, the read-only `notes`, `docs`, `doc`, `reports` and `report` (AND-0008), `say`, `decide`, `send-back`, `new-card`, `work-on`, `close-agents`. Anything else is a 403 even with a token: what drives the PC's own screen (`show`, `where`, `theme`), open-ended work from free text (`start-agent`, `run-command`, `new-flow`, `run-flow`), bulk starts (`work-all`, `qa-all`), `forget-agents`, `improve-card`, `to-attention`, and the `phone-*` commands, so a phone can never pair another. Adding one is a line in `RemoteAccess.Commands`.

Not there yet: answering an agent's question or permission ask (no API command does it so far; AND-0005), push notifications (AND-0006), images and artifacts.

## Checking it

- `pwsh -File tools/test.ps1 -Filter Remote` and `-Filter PhoneAccess`: the server over real TLS (refusals, pairing, revoke, pinning) and the running app paired from Settings.
- The QR is checked against OpenCV's reader when `DARG_TEST_QR_PYTHON` names a Python with `cv2` (Forge's venv has one); without it, only its shape is checked.
- By hand, once paired: `curl -k -H "Authorization: Bearer <token>" https://<pc>:47818/api/projects` (`-k` because the certificate is self-signed; a real client pins its fingerprint).
- The first time the app listens, Windows Firewall asks whether to let Darg accept connections: allow it on private networks, or the phone can't reach it.
