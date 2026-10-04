#!/usr/bin/env python3
"""vrclient.py — drive the V Rising game client for in-game self-tests of a server-side mod.

Claude (or any agent with a shell) uses this to launch the client, join the local dev server, type chat / console
commands, press keys, and read results back automatically from three sources:
  1. the server's BepInEx log (LogOutput.log) and the Unity server log, read from a cursor (only NEW lines count);
  2. on-screen text via Windows OCR (winocr) — chat replies, button labels, the HP readout;
  3. screenshots saved for a human / multimodal agent to look at (the bar's icons, effects).

It is mod-agnostic: everything Beelzebub-specific lives in the scenario files (scenarios/*.vrs). See
Beelzebub/Beelzebub/docs/INGAME_SELF_TEST.md for the method, the scenario grammar and the limits.

Requirements (Windows): pip install --user pyautogui pydirectinput winocr pillow
  - keys MUST go through pydirectinput (hardware scan codes); the game ignores pyautogui's virtual keys.
  - mouse clicks work through pyautogui.

Usage:
  python vrclient.py ensure                 # launch if needed, join the server, adminauth
  python vrclient.py run scenarios/mounted_reset.vrs [--var KEY=VALUE ...]
  python vrclient.py chat ".beelz api version"
  python vrclient.py console adminauth
  python vrclient.py shot bar|chat|full [name]
  python vrclient.py ocr chat|bar|hp|full
  python vrclient.py leave | close | status
Configuration (environment variables, defaults in CONFIG below): VR_SERVER_ROOT, VR_SERVER_LOG, VR_PLUGIN_LOG,
VR_ADDR, VR_CHARACTER, VR_APPID, VR_OUT.
"""
from __future__ import annotations

import argparse
import difflib
import json
import os
import re
import shlex
import subprocess
import sys
import time

# ---------------------------------------------------------------------------------------------------- config
SERVER_ROOT = os.environ.get("VR_SERVER_ROOT", r"C:\Program Files (x86)\Steam\steamapps\common\VRisingDedicatedServer")
CONFIG = {
    "plugin_log": os.environ.get("VR_PLUGIN_LOG", os.path.join(SERVER_ROOT, "BepInEx", "LogOutput.log")),
    "server_log": os.environ.get("VR_SERVER_LOG", os.path.join(SERVER_ROOT, "logs", "NyarDev.log")),
    "addr": os.environ.get("VR_ADDR", "127.0.0.1:9876"),
    "character": os.environ.get("VR_CHARACTER", "Chaos"),
    "appid": os.environ.get("VR_APPID", "1604030"),
    "out": os.environ.get("VR_OUT", os.path.join(os.environ.get("TEMP", "."), "vrclient")),
    "client_exe": "VRising.exe",
    "window_title": "VRising",
}

# Screen regions. The HUD is anchored to screen edges, so a region is given against an anchor in units of the
# screen HEIGHT (H) — that keeps it right on 16:9 and ultrawide alike. (x0, y0, x1, y1):
#   anchor "bc" (bottom-centre): x = W/2 + u*H ; anchor "l" (left edge): x = u*H ; y = v*H for both.
# Measured on 3440x1440; re-measure with `shot full` if a region misses on another resolution.
REGIONS = {
    "chat": ("l", 0.0, 0.52, 0.62, 0.87),     # chat log + input line, bottom-left
    "bar": ("bc", -0.53, 0.88, 0.56, 1.0),    # both ability bars (1-8 consumables, LMB/Q/E/Space/R/C/T)
    "spells": ("bc", 0.03, 0.88, 0.56, 1.0),  # the right bar only: LMB, Q, E, Space, R, C, T
    "hp": ("bc", -0.09, 0.85, 0.09, 0.89),    # "1,136 / 1,136" above the blood orb — present only in-world
}


def _out(*parts: str) -> str:
    os.makedirs(CONFIG["out"], exist_ok=True)
    return os.path.join(CONFIG["out"], *parts)


def log(msg: str) -> None:
    print(msg, flush=True)


# ---------------------------------------------------------------------------------------------------- input
def _gui():
    import pyautogui
    import pydirectinput
    pyautogui.FAILSAFE = False
    pydirectinput.FAILSAFE = False
    pydirectinput.PAUSE = 0.02
    return pyautogui, pydirectinput


SHIFTED = {"!": "1", "@": "2", "#": "3", "$": "4", "%": "5", "^": "6", "&": "7", "*": "8", "(": "9", ")": "0",
           "_": "-", "+": "=", ":": ";", '"': "'", "<": ",", ">": ".", "?": "/", "|": chr(92), "{": "[",
           "}": "]", "~": "`"}


def focus() -> bool:
    """Bring the game window to the front (a server console window often steals focus)."""
    try:
        import pygetwindow as gw
        wins = [w for w in gw.getWindowsWithTitle(CONFIG["window_title"]) if w.title.strip() == CONFIG["window_title"]]
        if not wins:
            return False
        w = wins[0]
        if w.isMinimized:
            w.restore()
        try:
            w.activate()
        except Exception:
            # Windows refuses SetForegroundWindow from a background process unless input arrived; a click fixes it.
            pg, _ = _gui()
            pg.click(w.left + w.width // 2, w.top + 10)
        time.sleep(0.4)
        return True
    except Exception as e:  # pragma: no cover - best effort
        log(f"focus: {e}")
        return False


def type_text(s: str, interval: float = 0.03) -> None:
    """Type with scan codes; capitals and shifted symbols get an explicit Shift (pydirectinput drops them)."""
    _, d = _gui()
    for ch in s:
        if ch.isalpha() and ch.isupper():
            d.keyDown("shift"); d.press(ch.lower()); d.keyUp("shift")
        elif ch in SHIFTED:
            d.keyDown("shift"); d.press(SHIFTED[ch]); d.keyUp("shift")
        elif ch == " ":
            d.press("space")
        else:
            d.press(ch)
        time.sleep(interval)


def key(k: str) -> None:
    _, d = _gui()
    d.press(k)


def hold(k: str, seconds: float) -> None:
    _, d = _gui()
    d.keyDown(k); time.sleep(seconds); d.keyUp(k)


def aim(dx: float = 0.12, dy: float = 0.0) -> tuple[int, int]:
    """Put the cursor at an offset from the screen centre — where the camera keeps the character — in units of the
    screen HEIGHT (dx>0 right, dy>0 down). V Rising aims ground-targeted and directional abilities at the cursor."""
    pg, _ = _gui()
    W, H = pg.size()
    x, y = int(W / 2 + dx * H), int(H / 2 + dy * H)
    pg.moveTo(x, y, duration=0.15)
    time.sleep(0.1)
    return x, y


def cast(k: str, dx: float = 0.12, dy: float = 0.0, hold_s: float = 0.0) -> None:
    """Aim, then use the ability on key `k` (q, e, r, c, t, space, or lmb / rmb for the mouse buttons).
    hold_s > 0 holds the input (channelled / charge abilities)."""
    focus()
    aim(dx, dy)
    _, d = _gui()
    if k in ("lmb", "rmb"):
        btn = "left" if k == "lmb" else "right"
        d.mouseDown(button=btn); time.sleep(max(0.05, hold_s)); d.mouseUp(button=btn)
    else:
        d.keyDown(k); time.sleep(max(0.05, hold_s)); d.keyUp(k)


def chat_open() -> bool:
    """The open chat input shows the hint "Tab Cycle Chat Channel" under it."""
    return fuzzy_contains(ocr_text("chat"), "Cycle Chat Channel")


def chat(cmd: str) -> None:
    """Enter opens chat, type, Enter sends. The input is CONFIRMED open first: typing into the world instead would
    send the letters as movement / ability keys (w/a/s/d/q/e/r/c/t) and walk the character away."""
    focus()
    _, d = _gui()
    for attempt in range(3):
        d.press("enter"); time.sleep(0.7)
        if chat_open():
            break
        d.press("esc"); time.sleep(0.5)   # whatever had focus (a menu, the console), close it and retry
    else:
        raise RuntimeError("chat input did not open (OCR saw no 'Cycle Chat Channel' hint)")
    type_text(cmd); time.sleep(0.3)
    d.press("enter"); time.sleep(1.2)


def console(cmd: str) -> None:
    """Backtick opens the client console (e.g. `adminauth`), backtick closes it."""
    focus()
    _, d = _gui()
    d.press("`"); time.sleep(1.2)
    type_text(cmd); time.sleep(0.3)
    d.press("enter"); time.sleep(1.5)
    d.press("`"); time.sleep(0.8)


def click(x: int, y: int, wait: float = 0.5) -> None:
    pg, _ = _gui()
    pg.moveTo(x, y, duration=0.2); time.sleep(0.15); pg.click(); time.sleep(wait)


# ---------------------------------------------------------------------------------------------------- screen
def grab(region: str | None = None):
    from PIL import ImageGrab
    img = ImageGrab.grab()
    if not region or region == "full":
        return img
    return img.crop(region_box(region, img.size))


def region_box(region: str, size: tuple[int, int]) -> tuple[int, int, int, int]:
    W, H = size
    anchor, u0, v0, u1, v1 = REGIONS[region]
    ax = W / 2 if anchor == "bc" else 0
    return (max(0, int(ax + u0 * H)), int(v0 * H), min(W, int(ax + u1 * H)), min(H, int(v1 * H)))


def shot(region: str = "full", name: str | None = None, scale: float | None = None) -> str:
    img = grab(region)
    if scale is None:
        scale = 0.4 if region == "full" else 1.0
    if scale != 1.0:
        img = img.resize((int(img.width * scale), int(img.height * scale)))
    path = _out("shots", f"{name or region}-{time.strftime('%H%M%S')}.png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)
    return path


def _prep(img, mode: str = "bright"):
    """OCR pre-processing. Game text is small and light (white / orange / green) over busy scenery:
    'bright' keeps pixels whose brightest channel is high (the text) as black on white; 'gray' is the softer
    fallback (grayscale, contrast stretch, invert). Both upscale 2x."""
    from PIL import Image, ImageChops, ImageOps
    g = img.convert("RGB").resize((img.width * 2, img.height * 2), Image.LANCZOS)
    if mode == "bright":
        r, gg, b = g.split()
        v = ImageChops.lighter(ImageChops.lighter(r, gg), b)
        return v.point(lambda x: 0 if x >= 175 else 255), 2
    return ImageOps.invert(ImageOps.autocontrast(g.convert("L"))), 2


def ocr_lines(region: str = "full", img=None, mode: str = "bright") -> list[dict]:
    """[{text, x, y, w, h}] in SCREEN coordinates (centre-clickable)."""
    import winocr
    from PIL import ImageGrab
    full = ImageGrab.grab() if img is None else img
    box = (0, 0, full.width, full.height) if region == "full" else region_box(region, full.size)
    pre, k = _prep(full.crop(box), mode)
    res = winocr.recognize_pil_sync(pre, "en")
    out = []
    for line in res.get("lines", []):
        words = line.get("words", [])
        if not words:
            continue
        xs = [w["bounding_rect"]["x"] for w in words]
        ys = [w["bounding_rect"]["y"] for w in words]
        x2 = [w["bounding_rect"]["x"] + w["bounding_rect"]["width"] for w in words]
        y2 = [w["bounding_rect"]["y"] + w["bounding_rect"]["height"] for w in words]
        out.append({"text": line["text"], "x": box[0] + min(xs) / k, "y": box[1] + min(ys) / k,
                    "w": (max(x2) - min(xs)) / k, "h": (max(y2) - min(ys)) / k})
    return out


def ocr_text(region: str = "full", img=None) -> str:
    """Both pre-processing passes joined, so a needle can match either reading."""
    from PIL import ImageGrab
    img = ImageGrab.grab() if img is None else img
    return "\n".join(l["text"] for mode in ("bright", "gray") for l in ocr_lines(region, img, mode))


_CONFUSABLE = str.maketrans({"l": "i", "1": "i", "j": "i", "0": "o", "5": "s", "8": "b", "z": "s"})


def _norm(s: str) -> str:
    """Lower-case alphanumerics with OCR look-alikes folded together (l/1/I/j, 0/O, 5/S, 8/B, z/s)."""
    return re.sub(r"[^a-z0-9]+", "", s.lower()).translate(_CONFUSABLE)


def fuzzy_contains(haystack: str, needle: str, ratio: float = 0.8) -> bool:
    """OCR misreads a few characters (Beelz -> Beetz); compare alphanumerics with a sliding-window ratio."""
    h, n = _norm(haystack), _norm(needle)
    if not n:
        return True
    if n in h:
        return True
    L = len(n)
    best = 0.0
    for i in range(0, max(1, len(h) - L + 1)):
        r = difflib.SequenceMatcher(None, h[i:i + L], n).ratio()
        if r > best:
            best = r
            if best >= ratio:
                return True
    return False


def find_text(label: str, region: str = "full", exact: bool = True) -> dict | None:
    """The on-screen line best matching `label` (exact = whole line equals it, case/space-insensitive)."""
    from PIL import ImageGrab
    img = ImageGrab.grab()
    # menus read best in 'gray', HUD/chat text in 'bright' — search both
    lines = ocr_lines(region, img, "gray") + ocr_lines(region, img, "bright")
    want = _norm(label)
    if exact:
        for l in lines:
            if _norm(l["text"]) == want:
                return l
        best = max(lines, key=lambda l: difflib.SequenceMatcher(None, _norm(l["text"]), want).ratio(), default=None)
        if best and difflib.SequenceMatcher(None, _norm(best["text"]), want).ratio() >= 0.85:
            return best
        return None
    for l in lines:
        if fuzzy_contains(l["text"], label):
            return l
    return None


def click_text(label: str, timeout: float = 20, wait: float = 1.0, exact: bool = True, region: str = "full") -> bool:
    end = time.time() + timeout
    while time.time() < end:
        l = find_text(label, region, exact)
        if l:
            click(int(l["x"] + l["w"] / 2), int(l["y"] + l["h"] / 2), wait)
            return True
        time.sleep(1.0)
    return False


def in_world() -> bool:
    """The HP readout ("1,136 / 1,136") above the blood orb exists only once the character is in the world."""
    # OCR often drops the slash ("1,136 136"): two numbers in the readout area is enough
    return len(re.findall(r"\d[\d,.]*", ocr_text("hp"))) >= 2


# ---------------------------------------------------------------------------------------------------- logs
class LogCursor:
    """Reads only lines appended after mark(). A server restart truncates the file — the cursor then restarts at 0."""

    def __init__(self, path: str):
        self.path = path
        self.pos = 0
        self.pending: list[str] = []
        self.mark()

    def mark(self) -> None:
        self.pos = os.path.getsize(self.path) if os.path.exists(self.path) else 0
        self.pending = []

    def _read(self) -> None:
        self.pending.extend(self.new_lines())

    def new_lines(self) -> list[str]:
        if not os.path.exists(self.path):
            return []
        size = os.path.getsize(self.path)
        if size < self.pos:
            self.pos = 0
        with open(self.path, "rb") as f:
            f.seek(self.pos)
            data = f.read()
        # keep a partial last line for the next read
        cut = data.rfind(b"\n") + 1
        self.pos += cut
        return data[:cut].decode("utf-8", errors="replace").splitlines()

    def wait_for(self, pattern: str, timeout: float = 15, flags: int = 0) -> str | None:
        """First unread line matching `pattern`. Lines before it are consumed (checks run in order); lines after it
        stay unread for the next check — two lines written in one burst are both matchable."""
        rx = re.compile(pattern, flags)
        end = time.time() + timeout
        while True:
            self._read()
            for i, line in enumerate(self.pending):
                if rx.search(line):
                    self.pending = self.pending[i + 1:]
                    return line
            if time.time() >= end:
                return None
            time.sleep(0.5)

    def drain(self) -> list[str]:
        """Every unread line (for expect-no-log)."""
        self._read()
        out, self.pending = self.pending, []
        return out


# ---------------------------------------------------------------------------------------------------- session
def client_running() -> bool:
    out = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {CONFIG['client_exe']}"], capture_output=True, text=True).stdout
    return CONFIG["client_exe"].lower() in out.lower()


def launch(timeout: float = 120) -> bool:
    if not client_running():
        log("launch: starting the client through Steam")
        os.startfile(f"steam://rungameid/{CONFIG['appid']}")
    end = time.time() + timeout
    while time.time() < end:
        if client_running() and (find_text("PLAY") or in_world()):
            return True
        time.sleep(3)
    return False


def leave_to_menu() -> None:
    """From a disconnect / error dialog or the in-game menu, get back to the main menu."""
    focus()
    for label in ("Leave Game", "Back to Menu"):
        if click_text(label, timeout=2, wait=6):
            return
    if in_world():
        key("esc"); time.sleep(1.5)
        if click_text("Exit to Main Menu", timeout=4, wait=2, exact=False):
            click_text("Yes", timeout=4, wait=8)


def connect(timeout: float = 180) -> bool:
    """Main menu -> Play -> Online Play -> Show all Servers -> Direct Connect -> address -> Connect.
    ("Continue" does not work for a local server: it times out.)"""
    focus()
    if in_world():
        return True
    leave_to_menu()
    srv = LogCursor(CONFIG["server_log"])
    steps = [("PLAY", 5), ("Online Play", 5), ("Show all Servers", 6), ("Direct Connect", 3)]
    for label, wait in steps:
        if not click_text(label, timeout=25, wait=wait):
            log(f"connect: FAIL could not find '{label}' on screen ({shot('full', 'connect-fail')})")
            return False
    field = find_text("IP[:Port] / Server ID") or find_text("Server")
    if field:
        fx = int(field["x"] + field["w"] / 2)
        fy = int(field["y"] + field["h"] / 2 + (field["h"] * 1.8 if _norm(field["text"]) == "server" else 0))
        click(fx, fy, 0.4)
    pg, _ = _gui()
    pg.hotkey("ctrl", "a")
    pg.typewrite(CONFIG["addr"], interval=0.04)  # the menu text field accepts virtual keys
    time.sleep(0.4)
    # the dialog's "Connect" (exact) — not the "Direct Connect" button behind it
    if not click_text("Connect", timeout=10, wait=1):
        log("connect: FAIL no Connect button")
        return False
    joined = srv.wait_for(rf"Character: '{re.escape(CONFIG['character'])}' connected", timeout)
    if not joined:
        log(f"connect: FAIL the server never saw '{CONFIG['character']}' connect ({shot('full', 'connect-fail')})")
        return False
    end = time.time() + 90
    while time.time() < end:
        if in_world():
            time.sleep(2)
            return True
        time.sleep(2)
    log("connect: FAIL joined but the HUD never appeared")
    return False


def ensure(admin: bool = True) -> bool:
    if not launch():
        log("ensure: FAIL the client did not reach the main menu")
        return False
    if not connect():
        return False
    if admin:
        console("adminauth")
    log("ensure: ok — in world as " + CONFIG["character"])
    return True


def close_client() -> None:
    """Graceful close (no /F). Do this BEFORE restarting the server so no player is online at shutdown."""
    subprocess.run(["taskkill", "/IM", CONFIG["client_exe"]], capture_output=True)
    for _ in range(30):
        if not client_running():
            return
        time.sleep(2)


# ---------------------------------------------------------------------------------------------------- scenarios
class Scenario:
    """Runs a .vrs file: one step per line, `#` comments, ${VAR} substitution. Each expect-* is a check; the run
    ends with `SCENARIO PASS|FAIL <name> <passed>/<checks>` and writes <out>/results/<name>-<time>.json."""

    def __init__(self, path: str, variables: dict[str, str]):
        self.path = path
        self.name = os.path.splitext(os.path.basename(path))[0]
        self.vars = {"CHARACTER": CONFIG["character"], "ADDR": CONFIG["addr"], **variables}
        self.plugin = LogCursor(CONFIG["plugin_log"])
        self.server = LogCursor(CONFIG["server_log"])
        self.replies = LogCursor(CONFIG["plugin_log"])  # DevChatEcho's [CHAT> name] lines, in their own order
        self.results: list[dict] = []
        self.captured: dict[str, str] = {}
        self.transcript = ""  # OCR of the chat box taken right after the last chat step (chat fades in seconds)

    def sub(self, s: str) -> str:
        return re.sub(r"\$\{(\w+)\}", lambda m: self.vars.get(m[1], self.captured.get(m[1], m[0])), s)

    def check(self, step: str, ok: bool, detail: str) -> None:
        self.results.append({"step": step, "ok": ok, "detail": detail})
        log(f"  {'PASS' if ok else 'FAIL'} {step} :: {detail}")

    def run(self, stop_on_fail: bool = False) -> bool:
        lines = open(self.path, encoding="utf-8").read().splitlines()
        log(f"SCENARIO {self.name} ({self.path})")
        for raw in lines:
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            line = self.sub(line)
            op, _, rest = line.partition(" ")
            rest = rest.strip()
            log(f"> {line}")
            try:
                ok = self.step(op, rest)
            except Exception as e:
                self.check(line, False, f"exception {e!r}")
                ok = False
            if ok is False and (stop_on_fail or op == "ensure"):
                if op == "ensure":
                    log("  (ensure failed: not in the world, so the remaining steps are skipped)")
                break
        passed = sum(1 for r in self.results if r["ok"])
        total = len(self.results)
        verdict = "PASS" if total and passed == total else "FAIL"
        res = _out("results", f"{self.name}-{time.strftime('%Y%m%d-%H%M%S')}.json")
        os.makedirs(os.path.dirname(res), exist_ok=True)
        json.dump({"scenario": self.name, "verdict": verdict, "passed": passed, "checks": total,
                   "results": self.results}, open(res, "w", encoding="utf-8"), indent=2)
        log(f"SCENARIO {verdict} {self.name} {passed}/{total} -> {res}")
        return verdict == "PASS"

    def _timeout(self, rest: str, default: float) -> tuple[str, float]:
        m = re.search(r"\s+timeout=(\d+(?:\.\d+)?)$", rest)
        return (rest[:m.start()], float(m[1])) if m else (rest, default)

    def step(self, op: str, rest: str):
        if op == "chat":
            chat(rest)
            self.transcript = ocr_text("chat")
        elif op == "console":
            console(rest)
        elif op == "key":
            focus(); key(rest)
        elif op == "hold":
            k, secs = rest.split()
            focus(); hold(k, float(secs))
        elif op == "cast":
            # cast <key> [dx dy] [hold=S] — aim at an offset from the character (screen-height units), use the key
            parts = rest.split()
            hold_s = 0.0
            if parts and parts[-1].startswith("hold="):
                hold_s = float(parts.pop()[5:])
            dx, dy = (float(parts[1]), float(parts[2])) if len(parts) >= 3 else (0.12, 0.0)
            cast(parts[0], dx, dy, hold_s)
        elif op == "aim":
            dx, dy = (float(v) for v in rest.split())
            focus(); aim(dx, dy)
        elif op == "wait":
            time.sleep(float(rest))
        elif op == "mark":
            self.plugin.mark(); self.server.mark()
        elif op == "click-text":
            label, t = self._timeout(rest, 10)
            ok = click_text(label, timeout=t)
            self.check(f"click-text {label}", ok, "clicked" if ok else "not on screen")
            return ok
        elif op in ("expect-log", "expect-server-log"):
            pattern, t = self._timeout(rest, 15)
            cur = self.plugin if op == "expect-log" else self.server
            hit = cur.wait_for(pattern, t)
            self.check(f"{op} {pattern}", hit is not None, hit.strip()[:300] if hit else f"no match in {t:.0f}s")
            if hit:
                # named groups become ${vars} for later steps
                m = re.search(pattern, hit)
                if m:
                    self.captured.update({k: v for k, v in m.groupdict().items() if v is not None})
            return hit is not None
        elif op == "expect-no-log":
            pattern, t = self._timeout(rest, 3)
            time.sleep(t)
            bad = [l for l in self.plugin.drain() if re.search(pattern, l)]
            self.check(f"expect-no-log {pattern}", not bad, bad[0].strip()[:300] if bad else "none")
            return not bad
        elif op == "expect-reply":
            # EXACT: the reply as the server sent it, from the DevChatEcho dev plugin's [CHAT> name] log lines.
            text, t = self._timeout(rest, 10)
            pat = r"\[CHAT> " + re.escape(self.vars["CHARACTER"]) + r"\] .*" + re.escape(text)
            hit = self.replies.wait_for(pat, t, re.IGNORECASE)
            self.check(f"expect-reply {text}", hit is not None,
                       hit.split("] ", 2)[-1][:300] if hit else f"no reply containing it in {t:.0f}s (is DevChatEcho loaded?)")
            return hit is not None
        elif op in ("expect-chat", "expect-screen", "expect-bar"):
            # On-screen OCR, fuzzy. Chat fades after a few seconds, so expect-chat first checks the transcript taken
            # right after the last chat step, then reopens the chat history (Enter), reads it and closes it (Esc).
            text, t = self._timeout(rest, 6)
            region = {"expect-chat": "chat", "expect-screen": "full", "expect-bar": "bar"}[op]
            seen = self.transcript if op == "expect-chat" else ""
            ok = bool(seen) and fuzzy_contains(seen, text)
            end = time.time() + t
            while not ok and time.time() < end:
                if op == "expect-chat":
                    focus(); _, d = _gui(); d.press("enter"); time.sleep(0.8)
                    seen = ocr_text(region)
                    d.press("esc"); time.sleep(0.3)
                else:
                    seen = ocr_text(region)
                ok = fuzzy_contains(seen, text)
                if not ok:
                    time.sleep(1)
            p = shot(region, f"{self.name}-{op}")
            self.check(f"{op} {text}", ok, f"ocr={'match' if ok else 'no match'} shot={p}")
            return ok
        elif op == "shot":
            parts = rest.split()
            region = parts[0] if parts else "full"
            p = shot(region, f"{self.name}-{parts[1] if len(parts) > 1 else region}")
            log(f"  shot {p}")
        elif op == "ensure":
            ok = ensure()
            self.check("ensure", ok, "in world" if ok else "could not join")
            return ok
        elif op == "leave":
            leave_to_menu()
        else:
            raise ValueError(f"unknown step '{op}'")
        return True


# ---------------------------------------------------------------------------------------------------- CLI
def main() -> int:
    # mod log lines carry non-ASCII (→, —); a cp1252 console would raise on print and abort the run
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass
    ap = argparse.ArgumentParser(description="Drive the V Rising client for in-game self-tests.")
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("status")
    sub.add_parser("launch")
    sub.add_parser("connect")
    sub.add_parser("ensure")
    sub.add_parser("leave")
    sub.add_parser("close")
    p = sub.add_parser("chat"); p.add_argument("text", nargs="+")
    p = sub.add_parser("console"); p.add_argument("text", nargs="+")
    p = sub.add_parser("key"); p.add_argument("k"); p.add_argument("--hold", type=float)
    p = sub.add_parser("cast"); p.add_argument("k"); p.add_argument("dx", nargs="?", type=float, default=0.12)
    p.add_argument("dy", nargs="?", type=float, default=0.0); p.add_argument("--hold", type=float, default=0.0)
    p = sub.add_parser("shot"); p.add_argument("region", nargs="?", default="full"); p.add_argument("name", nargs="?")
    p = sub.add_parser("ocr"); p.add_argument("region", nargs="?", default="chat")
    p = sub.add_parser("run"); p.add_argument("scenario"); p.add_argument("--var", action="append", default=[])
    p.add_argument("--stop-on-fail", action="store_true")
    a = ap.parse_args()

    if a.cmd == "status":
        print(json.dumps({"client_running": client_running(), "in_world": client_running() and in_world(),
                          **{k: CONFIG[k] for k in ("addr", "character", "plugin_log", "server_log", "out")}}, indent=2))
    elif a.cmd == "launch":
        return 0 if launch() else 1
    elif a.cmd == "connect":
        return 0 if connect() else 1
    elif a.cmd == "ensure":
        return 0 if ensure() else 1
    elif a.cmd == "leave":
        leave_to_menu()
    elif a.cmd == "close":
        close_client()
    elif a.cmd == "chat":
        for t in a.text:
            chat(t)
    elif a.cmd == "console":
        for t in a.text:
            console(t)
    elif a.cmd == "key":
        focus()
        hold(a.k, a.hold) if a.hold else key(a.k)
    elif a.cmd == "cast":
        cast(a.k, a.dx, a.dy, a.hold)
    elif a.cmd == "shot":
        print(shot(a.region, a.name))
    elif a.cmd == "ocr":
        print(ocr_text(a.region))
    elif a.cmd == "run":
        variables = dict(v.split("=", 1) for v in a.var)
        return 0 if Scenario(a.scenario, variables).run(a.stop_on_fail) else 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
