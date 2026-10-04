# In-game self-test framework (vrclient)

How Claude runs **in-game tests itself**: it plays the real V Rising client on the dev machine, joins the local
dedicated server, types commands, presses keys, casts abilities, and reads the results back automatically.
It was built for Beelzebub (2026-10-03/04) and written to be **reused by any server-side V Rising mod**:
the client driver knows nothing about Beelzebub — only the scenario files do.

| Piece | Path | Role |
|---|---|---|
| Client driver + scenario runner | `Beelzebub/tools/vrclient/vrclient.py` | launch, connect, input, OCR, log cursors, `run` |
| Scenarios | `Beelzebub/tools/vrclient/scenarios/*.vrs` | one test per file, plain text, one step per line |
| Reply echo (dev-only plugin) | `Beelzebub/tools/vrclient/DevChatEcho/` | writes every chat-command reply into the server log |

Proven scenarios: `mounted_reset.vrs` (14/14 — grant, summon a horse, mount, reset while riding, dismount message)
and `cast_basic.vrs` (7/7 — grant to Q, cast it, server confirms the cast).

---

## 1. How it works

```
 Claude (shell) ──python vrclient.py run x.vrs──►  VRising.exe client  ──network──►  VRisingServer.exe + mods
       ▲                 keys: pydirectinput            (owner's Steam account,             │
       │                 mouse: pyautogui               character "Chaos")                  │
       │                                                                                   ▼
       ├──── 1. LogCursor on BepInEx/LogOutput.log  ◄──── mod log lines ([Beelz RESET] ...)
       ├──── 2. [CHAT> Chaos] lines (DevChatEcho)   ◄──── exact text of every command reply
       ├──── 3. Windows OCR of screen regions       ◄──── what the client actually shows
       └──── 4. screenshots (PNG) for a look by eye / a multimodal model
```

The rule: **the server log is the source of truth; the screen is the confirmation.** Every check that can be
read from a log line is read from a log line (exact, timestamp-ordered, no OCR guesswork). OCR is used only for
what exists only on the client: a prompt ("Hold to Mount"), a message sent outside VCF, the HUD.

### 1.1 Getting data out automatically

1. **Log cursor (`LogCursor`).** Remembers the file size when the scenario starts (or at `mark`) and reads only
   lines appended after that, so old lines never satisfy a check. Checks consume lines in order: `expect-log A`
   then `expect-log B` requires B to appear *after* A. Lines written in the same burst stay available to the next
   check. A server restart truncates the log; the cursor notices the file shrank and restarts at 0.
   `LogOutput.log` contains binary bytes — read it as bytes (and use `grep -a` when grepping by hand).
2. **DevChatEcho.** A 50-line BepInEx plugin, deployed to the **dev server only**, with a Harmony prefix on
   VampireCommandFramework's `ChatCommandContext.Reply(string)`. Each reply is logged as
   `[Info   :DevChatEcho] [CHAT> <character>] <text>` (newlines become ` \n `). It covers every mod that answers
   through VCF. It does **not** see messages a mod sends directly with `ServerChatUtils.SendSystemMessageToClient`
   (broadcasts, notifications) — check those with `expect-chat` (OCR). Hooking `SendSystemMessageToClient` itself
   was tried and fails: its `ref FixedString512Bytes` arrives through the IL2CPP detour truncated to 8 bytes.
3. **OCR.** Windows' built-in OCR engine (`winocr`) on a cropped, 2x-upscaled region, in two pre-processing modes:
   `bright` (keep pixels whose brightest channel ≥ 175 → black text on white; best for HUD and chat) and `gray`
   (autocontrast + invert; best for menus). Matching is fuzzy: lower-case alphanumerics only, look-alikes folded
   (`l/1/j→i`, `0→o`, `5→s`, `8→b`, `z→s`), sliding `difflib` ratio ≥ 0.8. So `expect-chat` text should be a
   distinctive phrase of ~15+ characters, not a single word.
4. **Screenshots.** `shot <region>` saves `%TEMP%\vrclient\shots\<scenario>-<name>-<time>.png`; every OCR check also
   saves the region it read. Claude opens the PNG with its Read tool to judge icons and effects.
5. **Results JSON.** Each run writes `%TEMP%\vrclient\results\<scenario>-<yyyymmdd-hhmmss>.json`
   (`{scenario, verdict, passed, checks, results:[{step, ok, detail}]}`) and prints
   `SCENARIO PASS|FAIL <name> <passed>/<checks>`; the process exit code is 0 only on PASS. The `detail` of a log
   check is the matched line — paste it straight into a dod evidence line or an audit.

### 1.2 Screen regions

The HUD is anchored to screen edges, so regions are defined against an anchor in units of the screen **height**
(`bc` = bottom centre, `l` = left edge), which keeps them valid on 16:9 and ultrawide. Measured on 3440x1440:
`chat` (bottom-left log + input), `bar` (both ability bars), `spells` (LMB/Q/E/Space/R/C/T), `hp` (the
"1,136 / 1,136" readout, present only in the world). Re-measure from `python vrclient.py shot full` if a check
misses on another resolution.

---

## 2. One-time setup (per machine)

```bash
pip install --user pyautogui pydirectinput winocr pillow pygetwindow
```

- **DevChatEcho:** `dotnet build Beelzebub/tools/vrclient/DevChatEcho/DevChatEcho.csproj -c Release` — its
  `BuildToServer` target copies `DevChatEcho.dll` into the server's `BepInEx/plugins` (stop the server first). The
  server log then prints `DevChatEcho loaded: 1 method(s) patched`. It is not in `Beelzebub.sln` and never ships.
- The owner's character must be an **admin** on the dev server (`adminlist.txt`), and must already exist there.
- The game window should stay on the primary monitor, not minimised. The owner should not touch mouse/keyboard
  during a run (see §6).

---

## 3. Running

```bash
cd Beelzebub/tools/vrclient
python vrclient.py status                         # client running? in world? (JSON)
python vrclient.py ensure                         # launch if needed, join the server, adminauth
python vrclient.py run scenarios/cast_basic.vrs   # a scenario; --var KEY=VALUE, --stop-on-fail
python vrclient.py chat ".beelz bar"              # one-off chat command
python vrclient.py console TPHome                 # one-off client console command
python vrclient.py cast q 0.15 0                  # aim right of the character, press Q
python vrclient.py shot spells | ocr chat         # look / read
python vrclient.py leave | close                  # back to menu / close the client gracefully
```

### 3.1 What `ensure` does (and the hard-won details)

1. **Launch** through Steam (`steam://rungameid/1604030`) if `VRising.exe` is not running; wait for `PLAY` on screen.
2. **Connect** by the menu path: `PLAY → Online Play → Show all Servers → Direct Connect`, type `127.0.0.1:9876`,
   click `Connect`. **"Continue" does not work for a local server** — it times out; always use the menu path.
   Buttons are found by OCR and clicked at their centre.
3. **Wait for the server** to log `Character: 'Chaos' connected` (server log), then for the HP readout (in world).
4. **`adminauth`** in the client console (backtick). Admin status does not survive a reconnect — every connection
   needs it before admin commands work.

### 3.2 Input rules

- **Keys go through `pydirectinput`** (hardware scan codes). The game ignores `pyautogui`'s virtual keys in the
  world. Menu text fields do accept `pyautogui.typewrite` (used for the address).
- **Capitals and symbols** need Shift held explicitly (`type_text` does it; `SHIFTED` maps `| _ : " ?` etc.).
- **Chat is verified open before typing.** `chat()` presses Enter and OCRs for the input hint
  "Tab Cycle Chat Channel"; it retries (Esc between tries) and raises if the box never opens. Without this, a
  command typed into the world is read as movement/ability keys (w/a/s/d/q/e/r/c/t) — once that walked the
  character off a cliff in daylight.
- **Every scenario starts with `console TPHome`** — a known, safe spot (the castle) whatever happened before.
- **Chat fades** after a few seconds. `expect-chat` first checks the OCR transcript taken right after the last
  `chat` step, then reopens the history (Enter), reads it and closes it (Esc).
- **The live bar updates a frame after a grant** — `wait 2` before a `shot` or `cast` of a freshly granted slot.

---

## 4. Scenario grammar (`*.vrs`)

One step per line; `#` starts a comment; `${VAR}` is replaced (`CHARACTER`, `ADDR`, `--var` values, and named
regex groups captured by an earlier `expect-log`). A step ending in `timeout=N` overrides its default wait.

| Step | Does | Check? |
|---|---|---|
| `ensure` | launch + connect + adminauth; a failure aborts the run | yes |
| `chat <text>` | send a chat line (input verified open) | — |
| `console <text>` | client console command (`adminauth`, `TPHome`, `ToggleInvulnerable`, …) | — |
| `key <k>` / `hold <k> <secs>` | press / hold a key (scan code); e.g. `hold f 2.5` to mount | — |
| `cast <k> [dx dy] [hold=S]` | aim at (dx, dy) screen-heights from the character, then use `q e r c t space lmb rmb` | — |
| `aim <dx> <dy>` | move the cursor only | — |
| `wait <secs>` | sleep | — |
| `mark` | move both log cursors to the end (ignore everything so far) | — |
| `click-text <label>` | OCR-find a button and click it | yes |
| `expect-log <regex>` | a NEW plugin-log line matches (default 15 s) | yes |
| `expect-server-log <regex>` | same on the Unity server log | yes |
| `expect-no-log <regex>` | no new plugin-log line matches within the wait (default 3 s) | yes |
| `expect-reply <text>` | a DevChatEcho `[CHAT> CHARACTER]` reply contains the text (exact, case-insensitive) | yes |
| `expect-chat <text>` | the chat box shows it (OCR, fuzzy) | yes |
| `expect-screen <text>` / `expect-bar <text>` | full screen / ability bar shows it (OCR, fuzzy) | yes |
| `shot <region> [name]` | save a screenshot (`full chat bar spells hp`) | — |
| `leave` | back to the main menu | — |

### 4.1 Patterns

- **Prove an ability was used:** turn on the mod's per-cast log (`.beelz admin set VerboseLogging true` makes
  `AbilityCastStartedSystemPatch` log `[Beelz SUMMON][cast] steamId=… ability=<group> guid=<id>` for every
  player cast), `cast q`, `expect-log \[Beelz SUMMON\]\[cast\].*guid=<id>`. For damage, `.beelz admin damage-trace on`
  logs each attributed hit (needs a target in range). Inside a form or on a mount, `[Beelz TRACE]` lines also appear.
- **Prove a reply:** `expect-reply` (exact) — add `expect-chat` only when it matters that the client displayed it.
- **Prove an absence:** `expect-no-log` after the action (e.g. no second destroy of the same buff).
- **Clean up** at the end (resetbar, despawn test units) so the next scenario starts from a known state.
- **Write the header** of each scenario: what it proves and what it needs (weapon held, abilities captured).

---

## 5. Server lifecycle with the client attached

- **Close the client before stopping/restarting the server** (`python vrclient.py close`), so no player is online at
  shutdown — a shutdown with a player online left `Couldn't remap old Modification Id` errors behind (2 after a
  restart with Chaos connected, 0 after restarts with Chaos offline).
- Back up `BepInEx/LogOutput.log` and `logs/NyarDev.log` before any start/restart (CLAUDE.md procedure 6).
- After a restart: `ensure` again (full menu path + adminauth).
- The scenario's results JSON and matched log lines are the evidence for dod `pass` lines and audits.

---

## 6. Limits — what still needs the owner

- **It takes over the machine's mouse and keyboard** for the run and plays as the **owner's Steam account**. The
  owner should not type or move the mouse during a run, and should know a run is happening.
- **Look and feel** — an animation playing correctly, a VFX looking right, a model rendering, sound — is judged
  from screenshots at best. Anything subjective or motion-based goes to the owner.
- **Aiming is coarse:** offsets from screen centre (where the camera keeps the character). There is no reading of
  world positions; hitting a specific moving target is not reliable.
- **Multi-player** checks (a second player, PvP, a clan) need a second client/account — not available.
- **OCR is fuzzy:** short or similar strings can false-match; prefer `expect-reply`/`expect-log`.
- **Non-VCF messages** are only visible through OCR.
- Regions were measured on 3440x1440 (re-measure elsewhere). A Windows dialog or notification stealing focus can
  break a run — `focus()` re-activates the window before each input, but a modal dialog cannot be dismissed.

---

## 7. Porting to another server mod

1. Copy `tools/vrclient/` (driver, `DevChatEcho/`, an empty `scenarios/`) into the other repo.
2. Set environment variables instead of editing code (defaults in `CONFIG`):
   `VR_SERVER_ROOT` (dedicated-server folder), `VR_PLUGIN_LOG` (BepInEx log), `VR_SERVER_LOG` (the `-logFile` path),
   `VR_ADDR` (`ip:port`), `VR_CHARACTER` (the in-game character name, used for connect detection and replies),
   `VR_APPID` (client Steam app id, 1604030), `VR_OUT` (results/screenshots folder).
3. Build and deploy DevChatEcho to that dev server (only if the mod uses VCF; otherwise rely on log lines + OCR).
4. Make sure the mod logs a **greppable line for every state change a test needs** (one tag per subsystem, key=value
   fields). This is the single biggest factor in how much can be tested automatically — add the log line before
   the test, the same way CLAUDE.md's "diagnostic before fix" rule asks.
5. Write scenarios: `ensure` → `console TPHome` → setup commands → action (`chat`/`cast`/`hold`) →
   `expect-log`/`expect-reply` → cleanup.
6. Add the procedure to that project's CLAUDE.md (copy §3, §5, §6).
