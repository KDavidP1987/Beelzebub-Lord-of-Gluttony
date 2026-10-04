# vrclient: in-game self-testing for server-side V Rising mods

`vrclient` lets an AI agent (Claude Code) test a server-side V Rising mod **in the real game, by itself**. It launches
the V Rising client, joins your local dedicated server, types chat and console commands, presses keys, casts
abilities, and then checks the results:

- from the **server log** (exact lines your mod writes),
- from **command replies** (captured by a small dev-only plugin, DevChatEcho),
- from the **screen** (Windows OCR, plus screenshots the agent can look at).

Each test is a plain-text **scenario** file. A run prints `SCENARIO PASS <name> n/n` or `FAIL` and writes a results
JSON file. That line and the matched log lines are the evidence for "this works in game".

The tool knows nothing about any particular mod; everything mod-specific lives in the scenario files. It was built
for Beelzebub (October 2026) and is meant to be copied into your other server-side mods.

---

## 1. What is in this folder

| Path | What it is |
|---|---|
| `vrclient.py` | The client driver and scenario runner (one Python file) |
| `scenarios/*.vrs` | One test per file. `_template.vrs` is the starting point for new ones |
| `DevChatEcho/` | A tiny BepInEx plugin for the **dev server only**: it writes every chat-command reply into the server log, so tests can check the exact text. Never ship it |

```
 agent (shell) ── python vrclient.py run x.vrs ──►  VRising.exe client ──network──►  VRisingServer.exe + your mod
      ▲              keys: pydirectinput              (owner's Steam account)                  │
      ├── 1. new lines in BepInEx/LogOutput.log   ◄──────── your mod's log lines ─────────────┤
      ├── 2. [CHAT> <character>] lines             ◄──────── DevChatEcho (exact replies) ──────┘
      ├── 3. Windows OCR of screen regions         ◄──────── what the client shows
      └── 4. screenshots (PNG) for the agent to look at
```

**Rule: the server log is the source of truth; the screen is only confirmation.** Anything a log line can prove is
checked from the log. OCR is for things that exist only on the client (a prompt, the HUD, a message the mod sends
outside the command framework).

---

## 2. Porting to another mod: checklist

Do these once per mod. Steps 1 to 4 are setup, step 5 is the part that decides how much you can test.

1. **Copy this folder** into the other repo, for example as `tools/vrclient/`. Keep `vrclient.py`, `DevChatEcho/`,
   `README.md` and `scenarios/_template.vrs`. Delete the Beelzebub scenarios, or keep one as an example.
2. **Install the Python packages** (once per machine):
   ```bash
   pip install --user pyautogui pydirectinput winocr pillow pygetwindow
   ```
3. **Point it at your server** with environment variables (no code edits). Defaults are in `CONFIG` at the top of
   `vrclient.py`:

   | Variable | Meaning | Default |
   |---|---|---|
   | `VR_SERVER_ROOT` | Dedicated server folder | `C:\Program Files (x86)\Steam\steamapps\common\VRisingDedicatedServer` |
   | `VR_PLUGIN_LOG` | BepInEx log | `<root>\BepInEx\LogOutput.log` |
   | `VR_SERVER_LOG` | Unity server log (the `-logFile` path) | `<root>\logs\NyarDev.log` |
   | `VR_ADDR` | Server address | `127.0.0.1:9876` |
   | `VR_CHARACTER` | Your in-game character name | `Chaos` |
   | `VR_APPID` | Client Steam app id | `1604030` |
   | `VR_OUT` | Results and screenshots folder | `%TEMP%\vrclient` |

   If the other mod runs on the **same** dev server as Beelzebub, the defaults already fit.
4. **Deploy DevChatEcho** to the dev server, if your mod answers commands through VampireCommandFramework (VCF):
   ```bash
   dotnet build tools/vrclient/DevChatEcho/DevChatEcho.csproj -c Release
   ```
   Stop the server first; the build copies `DevChatEcho.dll` into `BepInEx/plugins`. The log then shows
   `DevChatEcho loaded: 1 method(s) patched`. **It is already deployed on the shared dev server**, and one copy
   serves every mod on that server, so skip this step there. Don't add it to your mod's solution.
5. **Make your mod testable.** For every state change a test needs to see, the mod should write one greppable log
   line: one tag per subsystem plus `key=value` fields, for example
   `[Nyar SPAWN] target=Chaos unit=-1905691330 ok=1`. A read-only status command (like Beelzebub's
   `.beelz admin bar <player>`) helps too. This matters more than anything else: if no log line shows something,
   the agent can only guess at it from the screen.
6. **Make sure your character is an admin** on the dev server (`adminlist.txt`) and already exists there.
7. **Paste the CLAUDE.md block** from section 9 into the mod's `CLAUDE.md`, with that mod's values filled in.
8. **Smoke test:** `python vrclient.py ensure`, then `python vrclient.py run scenarios/_template.vrs` after editing
   the template to use one of your mod's commands.

---

## 3. Commands

```bash
python vrclient.py status                         # is the client running? in the world? (JSON)
python vrclient.py ensure                         # launch if needed, join the server, adminauth
python vrclient.py run scenarios/x.vrs            # run a scenario; --var KEY=VALUE, --stop-on-fail
python vrclient.py chat ".mymod status"           # one chat command
python vrclient.py console TPHome                 # one client console command
python vrclient.py key esc                        # press a key (--hold SECONDS)
python vrclient.py cast q 0.15 0                  # aim right of the character, press Q
python vrclient.py shot full                      # screenshot: full | chat | bar | spells | hp
python vrclient.py ocr chat                       # read a screen region
python vrclient.py leave                          # back to the main menu
python vrclient.py close                          # close the client cleanly
```

What `ensure` does:
1. Starts the game through Steam if it is not running, then waits for `PLAY`.
2. Connects through the menus: PLAY → Online Play → Show all Servers → Direct Connect → address → Connect.
   **"Continue" never works for a local server**; it times out.
3. Waits until the server logs `Character: '<name>' connected`, then until the HP readout is on screen.
4. Runs `adminauth` in the console. Admin rights do not survive a reconnect, so every connection needs it.

---

## 4. Writing scenarios

One step per line. A `#` comment must be on its own line; anything after a step is part of the step. `${VAR}` is replaced by `CHARACTER`, `ADDR`, any `--var` value, or a named
regex group captured by an earlier `expect-log`. Ending a step with `timeout=N` changes how long it waits.

| Step | Does | Counts as a check |
|---|---|---|
| `ensure` | Launch, connect, adminauth. A failure stops the run | yes |
| `chat <text>` | Send a chat line (checks the chat box opened first) | no |
| `console <text>` | Client console command (`TPHome`, `ToggleInvulnerable`, …) | no |
| `key <k>` / `hold <k> <secs>` | Press or hold a key, e.g. `hold f 2.5` to mount | no |
| `cast <k> [dx dy] [hold=S]` | Aim at an offset from the character, then press `q e r c t space lmb rmb` | no |
| `aim <dx> <dy>` | Move the cursor only | no |
| `wait <secs>` | Pause | no |
| `mark` | Ignore every log line written so far | no |
| `click-text <label>` | Find a button by OCR and click it | yes |
| `expect-log <regex>` | A new BepInEx log line matches (default wait 15 s) | yes |
| `expect-server-log <regex>` | A new Unity server log line matches | yes |
| `expect-no-log <regex>` | No new line matches within the wait (default 3 s) | yes |
| `expect-reply <text>` | A command reply contains the text (exact, case-insensitive; needs DevChatEcho) | yes |
| `expect-chat <text>` | The chat box shows the text (OCR, fuzzy; use 15+ characters) | yes |
| `expect-screen <text>` / `expect-bar <text>` | The screen or the ability bar shows the text (OCR, fuzzy) | yes |
| `shot <region> [name]` | Save a screenshot | no |
| `leave` | Back to the main menu | no |

Log checks only see lines written **after** the run started (or after `mark`), and they consume lines in order:
`expect-log A` then `expect-log B` means B appeared after A.

### Shape of a good scenario

```
# what this proves, and what it needs (a weapon held, an item owned, a config value)
ensure
# always start at a known, safe place, whatever happened before
console TPHome
wait 3
# set up a known state
chat .mymod reset
expect-log \[MyMod RESET\].*target=${CHARACTER}
# the action under test: exact reply text, then the server-side proof
chat .mymod do-the-thing
expect-reply Done: the thing
expect-log \[MyMod THING\].*ok=1
# a picture for anything a human might want to see
shot full after-thing
# clean up, so the next scenario starts clean
chat .mymod reset
```

### Lessons learned the hard way

- **Use names, not list positions.** A Beelzebub scenario said `transform 0`; another test added an unlock, the list
  shifted, and `0` became a different boss. Refer to things by name or ID.
- **Wait a moment after a change before checking the screen.** The bar updates a frame after a grant: `wait 2`.
- **Things that take time in game can't always be caught from chat.** A form that finishes appearing in under a
  second finishes before the next chat command arrives. Say so in the scenario header rather than calling the run a
  failure.
- **Prove absences with `expect-no-log`**, e.g. "no second destroy of the same buff".
- **Put the reason in the header**: what the scenario proves and what state it needs.

---

## 5. The test loop (how the agent works on its own)

1. Write or change code and its unit tests. Build.
2. **Deploy:** close the client, back up the logs, stop the server, build (the mod's build copies the DLL), confirm
   the deployed DLL matches the build output, start the server. Section 6 has the exact commands.
3. `python vrclient.py ensure`, then `run` the feature's scenario **and** one or two older scenarios as a
   regression check.
4. Read the result: `SCENARIO PASS`, or the failing step's `detail` in the results JSON plus its screenshot.
5. If something fails and the cause isn't proven, **add a log line or read-only command that names the reason
   first**, redeploy and rerun. Fix only once the cause is visible.
6. After the session, before any restart: read every `[Error` and `[Warning` in **both** logs. Unity errors don't
   appear in the BepInEx log.
7. Record the evidence: the `SCENARIO PASS` line, the results JSON path and the matched log lines.

Tell the owner before a run: it takes over the mouse and keyboard and plays as their Steam account.

---

## 6. Running the dev server

On the shared dev server the agent starts, stops and restarts the server **without asking** (owner's rule: it's a
dev server, and players just see a disconnect). Because it is shared, a few rules always apply.

- **Back up both logs before any start or restart**; starting overwrites them:
  copy `BepInEx\LogOutput.log` and `logs\<name>.log` to `%TEMP%\<mod>-logs-<date>-<label>\`.
- **Close the client first** (`python vrclient.py close`). Shutting down with a player online left
  `Couldn't remap old Modification Id` errors after the next start.
- **Stop gracefully:** `taskkill /PID <pid>` **without** `/F`, so the world saves. Force only if it is still running
  after 60 seconds.
- **Start** (PowerShell, from the server folder):
  ```powershell
  $env:SteamAppId="1604030"
  Start-Process VRisingServer.exe -WorkingDirectory "C:\Program Files (x86)\Steam\steamapps\common\VRisingDedicatedServer" `
    -ArgumentList '-persistentDataPath .\save-data-nyardev -serverName "Nyar Dev" -saveName nyardev -logFile .\logs\NyarDev.log'
  ```
  It is ready when the server log prints `Server Setup Complete` and the BepInEx log shows your plugin's
  "initialized" line.
- **The server locks every plugin DLL while it runs**, so a deploy always means stop, build, start. Every mod on a
  shared server goes down with it.
- **Watch live:** follow `BepInEx\LogOutput.log` filtered to your mod's tags plus `[Error`, `Exception` and your
  namespace (`at MyMod.`).

---

## 7. What still needs a human

- **Look and feel:** whether an animation, effect, model or sound is *right*. Screenshots help, but motion and taste
  go to the owner.
- **Precise aiming:** aim is an offset from the screen centre; hitting a specific moving target isn't reliable.
- **Several players:** PvP, clans and a second player need a second account.
- **Messages sent outside VCF** (broadcasts, system notices) are only visible through OCR, which is fuzzy.
- **Other screen sizes:** regions were measured on 3440x1440. On another resolution, take `shot full` and
  re-measure `REGIONS` in `vrclient.py`.

When handing a check to the owner: start with "connect to **127.0.0.1:9876**", give numbered steps with the exact
commands (real IDs, no `<placeholders>`), and one line saying what PASS looks like.

---

## 8. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Server exits right after "Steam GameServer Initialized!" | Bitdefender kills the BepInEx-patched server | Check Bitdefender first; allow the server folder |
| `ensure` times out on connect | "Continue" was used, or the address is wrong | Use the menu path (it does); check `VR_ADDR` |
| `ensure` fails while already in the world | The in-game Esc/System menu is open | `python vrclient.py key esc`, then `ensure` again |
| Admin commands are refused | `adminauth` is lost on every reconnect | `ensure` runs it; after a manual reconnect run `console adminauth` |
| Character walks off by itself | Text was typed into the world instead of chat | Already guarded (chat is verified open); keep `console TPHome` first |
| Keys do nothing in the world | Virtual keys instead of scan codes | Keys must go through `pydirectinput` (they do) |
| `expect-reply` never matches | DevChatEcho isn't deployed, or the reply bypasses VCF | Check `DevChatEcho loaded` in the log; else use `expect-chat` |
| A check passes on old output | Log line from before the step | Add `mark` before the action |
| Check tool prints "no input" | Run from the wrong folder | Run tools from the repo root |
| The server quit when `close` ran | **Open question (2026-10-04):** the server shut down cleanly the moment `close` ran; the client had already left 15 minutes earlier. Cause not yet proven | Until it is checked, run `close` only when stopping the server anyway, and check the server is still up afterwards |
| Python heredoc corrupts `\b` | Bash heredocs turn `\b` into a backspace | Write scripts to a file first |

---

## 9. Block to paste into the other mod's CLAUDE.md

```markdown
## In-game tests: Claude runs them itself (vrclient)

Tool: `tools/vrclient/` (guide: `tools/vrclient/README.md`). Claude plays the real client as the owner's character
**`Chaos`** (SteamID 76561198039548286) on the local dev server **127.0.0.1:9876**.

- Every feature gets a scenario `tools/vrclient/scenarios/<feature>.vrs`; its `SCENARIO PASS n/n` line and results
  JSON are the evidence that it works in game. Every scenario starts with `console TPHome`.
- The mod writes one greppable log line per state change a test needs (`[<Tag> <SUBSYSTEM>] key=value …`).
  Diagnostic before fix: if a failure's cause isn't proven, the next build adds the log line that names it.
- Claude starts, stops and redeploys the shared dev server without asking. It always backs up `BepInEx/LogOutput.log`
  and `logs/NyarDev.log` first, closes the client first (`python tools/vrclient/vrclient.py close`), and stops with
  `taskkill /PID <pid>` without `/F`. Start command and readiness lines: `tools/vrclient/README.md` section 6.
- Tell the owner before a run (it takes over mouse and keyboard). After the session, read every `[Error`/`[Warning`
  in both logs.
- Hand the owner only what the tool can't judge (look and feel, sound, multiplayer, precise aim): start with
  "connect to 127.0.0.1:9876", numbered steps, exact commands with real IDs, and a one-line PASS condition each.
```
