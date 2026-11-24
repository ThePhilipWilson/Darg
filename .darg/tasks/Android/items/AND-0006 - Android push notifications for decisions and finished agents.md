# AND-0006: Android: push notifications for decisions and finished agents

- **Status:** Done
- **Priority:**
- **Type:** Task
- **Labels:** android, mobile, notifications
- **Created:** 2026-11-19
- **Done:** 2026-11-23
- **Moved:** 2026-11-23 00:40:00

## Description

Notify when a card lands in For Human Attention or an agent finishes or fails, with the decision options as notification actions so it can be answered without opening the app (uses the decide command). Needs a delivery route that works with the app closed (FCM or a self-hosted alternative). Done when: a decision raised on the desktop can be answered from the lock screen.

## How to check

**What changed** (commit `26a322a` on `android`, https://github.com/ThePhilipWilson/Darg/commit/26a322a): phone notifications through **ntfy** (your pick). Anything that makes a desktop toast (an agent's question, a long turn finished or failed, a card waiting on a decision) also goes to a secret ntfy topic. A card's decision shows up to 3 of its options as buttons, recommended first (★). A tap makes the phone post a one-time key to `<topic>-reply`. Darg listens there and answers the card as `decide` does. The phone needs the ntfy app and internet, not a route to the PC. Answering on the desktop deletes the phone's notification.

**Try it** (needs a build with `26a322a`: the debug exe, or ⟳ Update):
1. Install **ntfy** on the phone (Play Store or F-Droid).
2. In Darg: 🎨 Settings, then **✦ New topic**. Copy the topic it shows (`darg-…`).
3. In the ntfy app: + (subscribe), paste the topic, server `ntfy.sh`.
4. In Darg: **🔔 Send test**. A "Darg: test" notification should reach the phone.
5. Lock the phone. Raise a decision on a throwaway card (or ask Fettle: "to-attention on X with question Ship it? options Yes (recommended) → done, Not yet → back").
6. On the lock screen, expand the notification and tap **Yes ★**. On the desktop the card should move to Done with `**Answer:** Yes` in its file. Settings shows "you picked Yes on the phone".
7. Raise another decision and answer it on the desktop instead. Its phone notification should go away.

**Look for:** whether the buttons show on the lock screen without unlocking (Android may ask you to unlock before it runs a notification action; that's set per phone under lock screen notification settings). Also check the tap reaches Darg within a few seconds.

![Settings, phone notifications](D:/GitHub/ThePhilipWilson/Workbench/.shots/and-0006-settings-push.png)

## QA 2026-11-23

Passed. Built from `android` at `6ffa74f` (contains `26a322a`) in its own worktree (`.shots/qa-and0006-r2`).

- **Whole suite** (`tools/test.ps1`): unit 830/830, integration 161/161, with no failures.
- **Live against ntfy.sh** (`.shots/qa-and0006-live.ps1`: debug exe, a throwaway instance, a random topic, test text only; the phone's button played by POSTing its body to its URL). The script covers New topic, Send test arriving, and a decision going out with `Yes ★ | Not yet`. A tap moves the card to Done 0.3 s later with `**Answer:** Yes`, and status says "you picked Yes on the phone". A second tap does nothing. A desktop answer makes ntfy.sh show a `message_delete` for that card's sequence id, and a stale tap after that does nothing. A tap made while Darg was closed counts when it starts again. A tap on a card moved out of attention by hand is refused, and a "Couldn't answer" notification comes back. Four of five runs were clean. In run 2, decision 4 (right after the restart) never reached ntfy.sh. It came straight after run 1 had hit ntfy.sh's per-IP rate limit (429) from my own polling. I couldn't reproduce it in the 4 runs after that, so rate limiting is the likely cause, but it isn't confirmed. Worth knowing: a refused publish isn't retried. It only shows as a Problem line in Settings.
- **Lock screen, real ntfy app** (F-Droid 1.25.2 on the `darg` emulator, Android 16). Subscribed through the app's + dialog. The decision arrived with the screen off, and expanding it on the lock screen showed **Yes ★** and **Not yet**. Tapping Yes ★ moved the card to Done 1.0 s later with the keyguard still up, both with a swipe lock and with a PIN set (no unlock asked). Settings then showed "Last answer: 00:32 QAT-0004: you picked Yes on the phone". Two things I hit along the way. A subscription made through an `ntfy://` link wasn't instant and delivered nothing until it was re-added through +. My first lock-screen tap opened the app instead of firing the button. The next two taps, at the dumped button bounds, worked.
- Not checked: Philip's own phone. A maker's skin (e.g. Samsung) has its own lock-screen notification settings, which could ask for an unlock that stock Android didn't.

![Lock screen with a PIN set, the decision's buttons](D:/GitHub/ThePhilipWilson/Workbench/.shots/qa-and0006-pin2.png)
