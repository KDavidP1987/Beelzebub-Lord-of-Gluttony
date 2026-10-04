#!/usr/bin/env python3
"""check_modleak.py — evidence checks for the modid-remap-errors dod plan
(Beelzebub/Beelzebub/docs/dod/modid-remap-errors.md).

Each subcommand prints exactly one summary line, `<name>: ok ...` or `<name>: FAIL <reason>`, and exits 1 on FAIL.
A check that scans nothing FAILs with "no input" — a check that finds nothing never passes.

  wiring    every method in the plugin that pops a slot mod by id (RemoveAbilityGroupModificationOnSlot) calls
            ModLeakService.MarkDue after the pop; Heartbeat.Pulse calls ModLeakService.Tick; Core.TryInitialize calls
            MarkDue after `IsReady = true`; Tick confirms (ModLeak.Confirm) before it cleans; Clean clears the
            holder's mods (ClearLooseSourceModifications) before it destroys it (DestroyUtility.Destroy)
  actors    the `modleak` command is adminOnly, and Clean is called only from ModLeakService.Tick
  secrets   no credential-shaped literal in the plugin's .cs / .json / .toml sources
  tests     every named ModLeak control exists as a [Fact]/[Theory] method
  data      both Militia Leader Whirlwind v2 groups carry FreeMoveAfterSeconds 1.0 in the shipped default
  handoff   ApiVersion stays 35, the banner equals it, and the handoff carries the v0.137.6 modleak note
  rollback  the audit's `Rollback range: <first>..<last>` resolves, first is an ancestor of last, and the audit
            states the revert / rebuild / redeploy commands and the save-restore consequence
  paths     every path changed since the plan's recon commit is declared in the plan's Rollout path list
  backlog   docs/BACKLOG.md's `modid-remap-errors` row says `DONE v0.137.6`
  status    nothing uncommitted but the owner's two private files
  selftest  runs every check above on temp fixtures: a good fixture must pass, a defect fixture must FAIL, and an
            empty tree must FAIL with "no input" (paths/rollback/status on a throwaway git repo)

Usage: python Beelzebub/tools/check_modleak.py <subcommand> [--root <repo root>]
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile

PROJ = "Beelzebub/Beelzebub"
SERVICE = f"{PROJ}/Services/ModLeakService.cs"
HEART = f"{PROJ}/Services/Heartbeat.cs"
CORE = f"{PROJ}/Core.cs"
ACMDS = f"{PROJ}/Commands/AdminCommands.cs"
RULES = f"{PROJ}/Resources/ability_rules.default.json"
HANDOFF = f"{PROJ}/docs/BCH_INTEGRATION_HANDOFF.md"
API = f"{PROJ}/Commands/ApiCommands.cs"
AUDIT = f"{PROJ}/docs/audits/modid-remap-errors.md"
PLAN = f"{PROJ}/docs/dod/modid-remap-errors.md"
BACKLOG = f"{PROJ}/docs/BACKLOG.md"
TESTS = "Beelzebub/Beelzebub.Tests/ModLeakTests.cs"
RECON = "681f313"
OWNER_PATHS = {f"{PROJ}/docs/V0136_ABILITY_TEST_PLAN.xlsx", "_matrix_build.py"}
API_VERSION = 35
HANDOFF_TOKENS = ["v0.137.6", "modleak", "no wire change"]
WHIRLWIND = ["AB_Militia_Leader_Whirlwind_v2_AbilityGroup", "AB_Militia_Leader_Whirlwind_v2_Init_AbilityGroup"]
SECRET_RE = re.compile(r"(?i)\b(password|passwd|api[_-]?key|secret|bearer|access[_-]?token)\b[\"']?\s*[:=]\s*\"[^\"]{8,}\"")
CONSEQUENCE = "save-data-nyardev"
ROLLBACK_STEPS = ["git revert", "dotnet build Beelzebub/Beelzebub.sln -c Release", "taskkill /PID"]
SOURCE_EXT = (".cs", ".json", ".toml")
POP = re.compile(r"\bRemoveAbilityGroupModificationOnSlot\s*\(|\bModifications\.Remove\w*\s*\(")
# (file, outermost method) -> may arm the sweep. Arming only moves WHEN a pass runs; what a pass cleans is decided by
# ModLeak alone, so no route can choose a holder to destroy.
MARK_CALLERS = {(f"{PROJ}/Core.cs", "TryInitialize"), (f"{PROJ}/Services/SlotApply.cs", "PushOnSlot"),
                (f"{PROJ}/Services/TransformBuffService.cs", "PopSlotModifications")}
REMAP = "Couldn't remap old Modification Id"
WHIRL_REJECT = "AB_Militia_Leader_Whirlwind_v2_Cast rejected"
SWEEP_BOOT = re.compile(r"\[Beelz MODLEAK\] sweep \(boot\): cleaned (\d+) stale holder\(s\), (\d+) leftover mod\(s\) removed; (\d+) seen stale once .*?, (\d+) unreadable.*?; (\d+) ms\.")
CLEAN_FAIL = re.compile(r"\[Beelz MODLEAK\] clean holder=\S+ failed")
BOOT_BUDGET_MS = 250
MARK = re.compile(r"\bModLeakService\.MarkDue\s*\(|(?<![.\w])MarkDue\s*\(")
DECL = re.compile(r"(\w+)\s*\([^;{}()]*\)\s*(?:where[^{]*)?\{")
KEYWORDS = {"if", "for", "foreach", "while", "switch", "catch", "using", "lock", "fixed", "else", "return", "new"}

REQUIRED_TESTS = [
    "Row_fails_when_an_id_live_under_another_source_reads_live",
    "Row_fails_when_a_copycooldown_id_counts_as_a_slot_setter",
    "Row_fails_when_an_unreadable_slot_or_empty_id_reads_dangling",
    "Holder_fails_when_a_holder_with_one_live_row_reads_stale",
    "Holder_fails_when_a_dead_holder_is_not_stale",
    "Holder_fails_when_an_unfinished_or_unreadable_holder_is_stale",
    "Confirm_fails_when_a_holder_seen_stale_once_is_cleaned",
    "Select_fails_when_a_holder_that_is_not_stale_now_is_cleaned",
    "Page_fails_when_a_holder_past_the_cap_is_never_read",
    "IdleLine_fails_when_an_idle_boot_is_silent_or_unnamed",
    "Arm_fails_when_repeated_pops_postpone_the_pass",
    "SweepLine_fails_when_a_count_or_the_cap_is_not_named",
    "RowLine_fails_when_a_name_breaks_the_line",
]


def read(root: str, rel: str) -> str | None:
    p = os.path.join(root, rel)
    if not os.path.isfile(p):
        return None
    with open(p, encoding="utf-8", errors="replace") as f:
        return f.read()


def strip(src: str) -> str:
    """Comments and string contents blanked, so a mention in either never counts as a call."""
    st = re.sub(r"//[^\n]*", "", src)
    st = re.sub(r"/\*.*?\*/", "", st, flags=re.S)
    return re.sub(r'"(?:\\.|[^"\\])*"', '""', st)


def method_body(st: str, name: str) -> str | None:
    m = re.search(r"\b" + name + r"\s*\([^;{}()]*\)\s*\{", st)
    if not m:
        return None
    i, depth = m.end() - 1, 0
    for j in range(i, len(st)):
        depth += {"{": 1, "}": -1}.get(st[j], 0)
        if depth == 0:
            return st[i:j + 1]
    return None


def enclosing(st: str, pos: int) -> str | None:
    name = None
    for m in DECL.finditer(st, 0, pos):
        before = st[:m.start()].rstrip()
        if m[1] in KEYWORDS or before.endswith("new") or before.endswith("."):
            continue
        name = m[1]
    return name


def outer_span(st: str, pos: int) -> tuple[str, int, int] | None:
    """The OUTERMOST method whose body contains pos (a local function inside it does not count as its own method)."""
    for m in DECL.finditer(st, 0, pos):
        before = st[:m.start()].rstrip()
        if m[1] in KEYWORDS or before.endswith("new") or before.endswith("."):
            continue
        i, depth = m.end() - 1, 0
        for j in range(i, len(st)):
            depth += {"{": 1, "}": -1}.get(st[j], 0)
            if depth == 0:
                break
        if i < pos < j:
            return m[1], i, j
    return None


def plugin_files(root: str) -> list[str]:
    out = []
    for d, dirs, files in os.walk(os.path.join(root, PROJ)):
        dirs[:] = [x for x in dirs if x not in ("bin", "obj")]
        out += [os.path.relpath(os.path.join(d, f), root).replace("\\", "/") for f in files if f.endswith(SOURCE_EXT)]
    return sorted(out)


def _order(body: str | None, a: str, b: str) -> bool:
    """a occurs in body, and before the first b (b required too)."""
    if body is None:
        return False
    ma, mb = re.search(a, body), re.search(b, body)
    return bool(ma and mb and ma.start() < mb.start())


def check_wiring(root: str) -> str:
    svc, hb, core = read(root, SERVICE), read(root, HEART), read(root, CORE)
    files = [f for f in plugin_files(root) if f.endswith(".cs")]
    if svc is None or hb is None or core is None or not files:
        return "wiring: FAIL no input (ModLeakService.cs, Heartbeat.cs or Core.cs missing)"
    svc, hb, core = strip(svc), strip(hb), strip(core)
    bad, pops = [], 0
    for rel in files:
        st = strip(read(root, rel))
        for m in POP.finditer(st):
            pops += 1
            span = outer_span(st, m.start())
            if span is None or not MARK.search(st, m.end(), span[2]):
                bad.append(f"{rel}:{span[0] if span else None} pops a slot mod without MarkDue after it")
    if pops == 0:
        bad.append("no slot-mod pop found (RemoveAbilityGroupModificationOnSlot)")
    p = method_body(hb, "Pulse")
    if p is None or not re.search(r"\btry\s*\{\s*ModLeakService\.Tick\s*\(\s*\)\s*;\s*\}\s*catch\b", p):
        bad.append("Heartbeat.Pulse: no ModLeakService.Tick inside try/catch (a throw must not stop the next pulse)")
    md = method_body(svc, "MarkDue")
    if md is None or not re.search(r"\bModLeak\.Arm\s*\(", md) or re.search(r"_firstRead\s*=\s*null", md):
        bad.append("ModLeakService.MarkDue: does not coalesce through ModLeak.Arm (or restarts the pass)")
    if not _order(method_body(core, "TryInitialize"), r"\bIsReady\s*=\s*true", r"\bModLeakService\.MarkDue\s*\("):
        bad.append("Core.TryInitialize: no MarkDue after IsReady = true")
    if not _order(method_body(svc, "Tick"), r"\bModLeak\.Select\s*\(", r"\bClean\s*\("):
        bad.append("ModLeakService.Tick: no ModLeak.Select before Clean")
    if not _order(method_body(svc, "Clean"), r"\bClearLooseSourceModifications\s*\(", r"\bDestroyUtility\.Destroy\s*\("):
        bad.append("ModLeakService.Clean: no ClearLooseSourceModifications before DestroyUtility.Destroy")
    cl = method_body(svc, "Clean")
    if cl is None or not re.search(r"\bcatch\b", cl) or not re.search(r"\bDestroyTag\b", cl):
        bad.append("ModLeakService.Clean: no catch (a throw must keep the holder) or no DestroyTag skip")
    rs = method_body(svc, "ReadSlot")
    if rs is None or not re.search(r"\bcatch\b", rs) or "SlotModParse.Failed()" not in rs:
        bad.append("ModLeakService.ReadSlot: a failed dump does not read as SlotModParse.Failed()")
    tk = method_body(svc, "Tick")
    if tk is None or not re.search(r"keptOnce\s*>\s*0\s*\|\|\s*capped\s*\|\|\s*failed\s*>\s*0", tk):
        bad.append("ModLeakService.Tick: does not re-arm when a holder was seen stale once, a clean failed or the pass was capped")
    if tk is None or not re.search(r"ModLeak\.IdleLine\s*\(", tk):
        bad.append("ModLeakService.Tick: an idle boot logs nothing")
    sc = method_body(svc, "Scan")
    if sc is None or not re.search(r"ModLeak\.Page\s*\(", sc):
        bad.append("ModLeakService.Scan: does not page from the cursor (ModLeak.Page)")
    if bad:
        return "wiring: FAIL " + "; ".join(bad[:6])
    return f"wiring: ok, {pops} pop site(s) mark the sweep, heartbeat ticks it, boot marks it, select before clean, clear before destroy, fallbacks kept, idle boot logged, paged, pops coalesce, heartbeat guarded"


def check_actors(root: str) -> str:
    ac = read(root, ACMDS)
    files = [f for f in plugin_files(root) if f.endswith(".cs")]
    if ac is None or not files:
        return "actors: FAIL no input (AdminCommands.cs / .cs files missing)"
    bad = []
    m = re.search(r'\[Command\("modleak"(.*?)\)\]\s*public static void ModLeak\(', ac, re.S)
    if not m:
        bad.append("modleak: not found")
    elif "adminOnly: true" not in m[1]:
        bad.append("modleak: not adminOnly")
    calls = 0
    for rel in files:
        st = strip(read(root, rel))
        for c in re.finditer(r"(?<![\w.])Clean\s*\(|\bModLeakService\.Clean\s*\(", st):
            line = st[:c.start()].rsplit("\n", 1)[-1]
            if re.search(r"\b(bool|void)\s*$", line):   # the declaration: `static bool Clean(`
                continue
            calls += 1
            if rel != SERVICE or enclosing(st, c.start()) != "Tick":
                bad.append(f"{rel}:{enclosing(st, c.start())} calls Clean")
    marks = 0
    for rel in files:
        if rel == SERVICE:
            continue
        st = strip(read(root, rel))
        for c in re.finditer(r"\bModLeakService\.MarkDue\s*\(", st):
            marks += 1
            span = outer_span(st, c.start())
            if (rel, span[0] if span else None) not in MARK_CALLERS:
                bad.append(f"{rel}:{span[0] if span else None} arms the sweep")
    # the pops' own entry points (resetbar/clearbar self-only, admin bar/purge/reset-loadouts adminOnly, every FullReset
    # caller) are guarded by check_clearbar.py actors; this check fails when that one does (skipped on fixtures)
    tool = os.path.join(os.path.dirname(os.path.abspath(__file__)), "check_clearbar.py")
    if os.path.isfile(os.path.join(root, PROJ, "Services", "BarResetService.cs")) and os.path.isfile(tool):
        r = subprocess.run([sys.executable, tool, "actors", "--root", root], capture_output=True, text=True)
        last = (r.stdout.strip().splitlines() or ["actors: FAIL no output"])[-1]
        if ": FAIL" in last:
            bad.append("pop entry points: " + last)
    if bad:
        return f"actors: FAIL {bad[:6]}"
    return (f"actors: ok, modleak adminOnly, {calls} Clean call(s), all inside ModLeakService.Tick, "
            f"{marks} MarkDue call(s) in {len(MARK_CALLERS)} allowed methods, pop entry points guarded")


def check_secrets(root: str) -> str:
    files = plugin_files(root)
    if not files:
        return "secrets: FAIL no input (no .cs/.json/.toml files)"
    hits = [f"{rel}:{n}" for rel in files for n, line in enumerate(read(root, rel).splitlines(), 1)
            if SECRET_RE.search(line)]
    if hits:
        return f"secrets: FAIL {len(hits)} credential-shaped literal(s): {hits[:5]}"
    return f"secrets: ok, {len(files)} files, 0 hits"


def check_tests(root: str) -> str:
    src = read(root, TESTS)
    if src is None:
        return f"tests: FAIL no input ({TESTS} missing)"
    missing = [n for n in REQUIRED_TESTS
               if not re.search(r"\[(Fact|Theory)\][^{;]*?public void " + re.escape(n) + r"\(", src, re.S)]
    if missing:
        return f"tests: FAIL {len(missing)} required control(s) missing: {missing[:4]}"
    return f"tests: ok, {len(REQUIRED_TESTS)} named ModLeak controls present"


def check_data(root: str) -> str:
    t = read(root, RULES)
    if t is None:
        return f"data: FAIL no input ({RULES} missing)"
    try:
        rules = json.loads(t)
    except ValueError as e:
        return f"data: FAIL not JSON ({e})"
    rows = rules.get("AbilityMap") if isinstance(rules, dict) else None
    if not isinstance(rows, dict):
        return "data: FAIL no AbilityMap object"
    vals = {k: (rows.get(k) or {}).get("FreeMoveAfterSeconds") for k in WHIRLWIND}
    if any(v is None for v in vals.values()):
        return f"data: FAIL Whirlwind group(s) or FreeMoveAfterSeconds missing: {vals}"
    if any(float(v) != 1.0 for v in vals.values()):
        return f"data: FAIL FreeMoveAfterSeconds not 1.0 on both groups: {vals}"
    return "data: ok, both Whirlwind v2 groups FreeMoveAfterSeconds 1.0"


def check_handoff(root: str) -> str:
    h, a = read(root, HANDOFF), read(root, API)
    if not h or not a:
        return "handoff: FAIL no input (handoff or ApiCommands.cs missing)"
    m, b = re.search(r"ApiVersion\s*=\s*(\d+)", a), re.search(r"ApiVersion\s*=\s*(\d+)", h)
    if not m or not b:
        return "handoff: FAIL no ApiVersion in " + ("ApiCommands.cs" if not m else "the handoff banner")
    api, banner = int(m[1]), int(b[1])
    note = next((l for l in h.splitlines() if "v0.137.6" in l), "")
    miss = [t for t in HANDOFF_TOKENS if t not in note]
    if api != API_VERSION or banner != api or miss:
        return f"handoff: FAIL api={api} banner={banner} (want {API_VERSION}) v0.137.6 line missing={miss}"
    return f"handoff: ok, api {api}, banner {banner}, v0.137.6 note carries {len(HANDOFF_TOKENS)} tokens"


def check_rollback(root: str) -> str:
    t = read(root, AUDIT)
    if t is None:
        return f"rollback: FAIL no input ({AUDIT} missing)"
    m = re.search(r"Rollback range:\s*`?([0-9a-f]{7,40})\.\.([0-9a-f]{7,40})`?", t)
    if not m:
        return "rollback: FAIL no `Rollback range: <first>..<last>` line"
    for c in (m[1], m[2]):
        if subprocess.run(["git", "-C", root, "cat-file", "-e", c + "^{commit}"], capture_output=True).returncode:
            return f"rollback: FAIL {c} is not a commit"
    if subprocess.run(["git", "-C", root, "merge-base", "--is-ancestor", m[1], m[2]], capture_output=True).returncode:
        return f"rollback: FAIL {m[1]} is not an ancestor of {m[2]}"
    miss = [x for x in ROLLBACK_STEPS if x not in t]
    if miss:
        return f"rollback: FAIL the audit lacks the rollback step(s) {miss}"
    if CONSEQUENCE not in t:
        return f"rollback: FAIL the audit does not name the save to restore ({CONSEQUENCE})"
    return f"rollback: ok, {m[1]}..{m[2]}"


def check_paths(root: str) -> str:
    plan = read(root, PLAN)
    if not plan:
        return "paths: FAIL no input (plan missing)"
    sec = re.search(r"Paths this change ships, writes or regenerates.*?(?=\n- [A-Z]|\n## )", plan, re.S)
    if not sec:
        return "paths: FAIL no Rollout path list"
    declared = set()
    for tok in re.findall(r"`([^`]+)`", sec[0].replace("\n", " ")):
        m = re.match(r"(.*)\{([^}]*)\}(.*)", tok)
        names = [m[1] + n.strip() + m[3] for n in m[2].split(",")] if m else [tok]
        declared |= {n.replace(" ", "") for n in names}
    out = subprocess.run(["git", "-C", root, "diff", "--name-only", RECON], capture_output=True, text=True).stdout.split()
    out += subprocess.run(["git", "-C", root, "ls-files", "--others", "--exclude-standard"], capture_output=True, text=True).stdout.split()
    changed = sorted({p for p in out if p not in OWNER_PATHS})
    if not changed:
        return "paths: FAIL no input (nothing changed since " + RECON + ")"
    undeclared = [p for p in changed if not any(p == d or p.endswith("/" + d) for d in declared)]
    if undeclared:
        return f"paths: FAIL {len(undeclared)} changed path(s) not declared: {undeclared[:8]}"
    return f"paths: ok, {len(changed)} changed, {len(declared)} declared"


def check_backlog(root: str) -> str:
    t = read(root, BACKLOG)
    if t is None:
        return f"backlog: FAIL no input ({BACKLOG} missing)"
    row = next((l for l in t.splitlines() if l.startswith("|") and "modid-remap-errors" in l.split("|")[1]), None)
    if row is None:
        return "backlog: FAIL no modid-remap-errors row"
    if "DONE v0.137.6" not in row:
        return "backlog: FAIL the modid-remap-errors row is not marked `DONE v0.137.6`"
    return "backlog: ok, modid-remap-errors DONE v0.137.6"


def check_status(root: str) -> str:
    r = subprocess.run(["git", "-C", root, "status", "--porcelain"], capture_output=True, text=True)
    if r.returncode:
        return f"status: FAIL no input (git exited {r.returncode}: {r.stderr.strip()[:80]})"
    lines = [l for l in r.stdout.splitlines() if l.strip()]
    other = [l for l in lines if l[3:].strip().strip('"') not in OWNER_PATHS]
    if other:
        return f"status: FAIL {len(other)} uncommitted path(s): {other[:6]}"
    return f"status: ok, nothing uncommitted but {len(lines)} owner file(s)"


def check_bootsweep(root: str, log: str | None = None, minimum: int = 7) -> str:
    """D10: the first boot of the build logged exactly one boot sweep that cleaned >= minimum holders, read none as
    unreadable, stayed under BOOT_BUDGET_MS, and no clean failed."""
    path = log or os.path.join(root, "BepInEx", "LogOutput.log")
    if not os.path.isfile(path):
        return f"bootsweep: FAIL no input ({path} missing)"
    with open(path, encoding="utf-8", errors="replace") as f:
        text = f.read()
    if not text.strip():
        return f"bootsweep: FAIL no input ({path} empty)"
    sweeps = SWEEP_BOOT.findall(text)
    fails = CLEAN_FAIL.findall(text)
    if len(sweeps) != 1:
        return f"bootsweep: FAIL {len(sweeps)} boot sweep line(s), want 1"
    cleaned, mods, once, unread, ms = map(int, sweeps[0])
    if cleaned < minimum or unread or fails or ms > BOOT_BUDGET_MS:
        return (f"bootsweep: FAIL cleaned={cleaned} (want >= {minimum}) unreadable={unread} failed={len(fails)} "
                f"ms={ms} (budget {BOOT_BUDGET_MS})")
    return f"bootsweep: ok, cleaned {cleaned} holder(s), {mods} leftover mod(s), {ms} ms, 0 failed"


def check_restarts(root: str, dirs: list[str] | None = None) -> str:
    """D12: every named restart backup holds both logs of a completed boot, with 0 remap errors and 0 Whirlwind rejects."""
    if not dirs:
        return "restarts: FAIL no input (no backup folders given; pass --dirs <r1> <r2>)"
    out = []
    for d in dirs:
        ny, lo = os.path.join(d, "NyarDev.log"), os.path.join(d, "LogOutput.log")
        if not (os.path.isfile(ny) and os.path.isfile(lo)):
            return f"restarts: FAIL no input ({d} lacks NyarDev.log or LogOutput.log)"
        with open(ny, encoding="utf-8", errors="replace") as f:
            nt = f.read()
        with open(lo, encoding="utf-8", errors="replace") as f:
            lt = f.read()
        if "Server Setup Complete" not in nt:
            return f"restarts: FAIL no input ({ny} is not a completed boot)"
        r, w = nt.count(REMAP), lt.count(WHIRL_REJECT)
        if "[Beelz MODLEAK] sweep (boot)" not in lt:
            return f"restarts: FAIL {os.path.basename(os.path.normpath(d))}: no boot sweep line (the sweep did not run)"
        if r or w:
            return f"restarts: FAIL {os.path.basename(os.path.normpath(d))}: {r} remap error(s), {w} Whirlwind reject(s)"
        out.append(os.path.basename(os.path.normpath(d)))
    return f"restarts: ok, {len(out)} boot(s) ({', '.join(out)}): 0 remap errors, 0 Whirlwind rejects"


CHECKS = {"wiring": check_wiring, "actors": check_actors, "secrets": check_secrets, "tests": check_tests,
          "data": check_data, "handoff": check_handoff, "backlog": check_backlog}

# ---- selftest fixtures ----

GOOD_SVC = '''
internal static class ModLeakService {
    internal static void MarkDue(string why) { _dueAt = ModLeak.Arm(_dueAt, DateTime.UtcNow, ReadGap); }
    internal static void Tick()
    {
        // Clean( in a comment is not a call
        if (none) { Core.Log.LogInfo(ModLeak.IdleLine(_dueWhy, scan.Count, 1)); return; }
        var confirmed = ModLeak.Select(_firstRead, verdicts);
        foreach (var h in scan) if (Clean(h, out int removed)) cleaned++;
        _dueAt = keptOnce > 0 || capped || failed > 0 ? DateTime.UtcNow + ReadGap : DateTime.MaxValue;
    }
    internal static List<HolderScan> Scan(Func<Entity, bool> f, int cap, ref int cursor, out bool capped)
    {
        var (page, next) = ModLeak.Page(sorted, cursor, cap);
    }
    static bool Clean(HolderScan h, out int removed)
    {
        if (!holder.Exists() || em.HasComponent<DestroyTag>(holder)) return false;
        try
        {
            removed = Core.ServerGameManager.Modifications.ClearLooseSourceModifications(holder, ref em);
            DestroyUtility.Destroy(em, holder, DestroyDebugReason.None);
            return true;
        }
        catch (Exception ex) { return false; }
    }
    static SlotModParse ReadSlot(ModificationsRegistry reg, EntityManager em, Entity slotEnt)
    {
        try { return SlotModDump.ParseGroupGuid(Dump(slotEnt)); }
        catch (Exception ex) { return SlotModParse.Failed(); }
    }
}
'''
GOOD_HEART = "static class Heartbeat { static void Pulse() { try { ModLeakService.Tick(); } catch { } } }\n"
GOOD_CORE = "static class Core { static void TryInitialize() { IsReady = true; Services.ModLeakService.MarkDue(\"boot\"); } }\n"
GOOD_POP = '''
static class SlotApply {
    static int PushOnSlot(Entity character, int slot)
    {
        try { sgm.RemoveAbilityGroupModificationOnSlot(character, slot, ModificationId.NewId(id)); popped++; } catch { }
        if (popped > 0) ModLeakService.MarkDue("grant");
        return popped;
    }
}
'''
GOOD_ACMDS = '''
[Command("modleak", description: "Read-only.", adminOnly: true)]
public static void ModLeak(ChatCommandContext ctx, string player = null)
{
    var holders = ModLeakService.Scan(f, int.MaxValue, out _);
}
'''
POPF = f"{PROJ}/Services/SlotApply.cs"


def _rules(a: float, b: float) -> str:
    return json.dumps({"AbilityMap": {WHIRLWIND[0]: {"FreeMoveAfterSeconds": a}, WHIRLWIND[1]: {"FreeMoveAfterSeconds": b}}})


def _write(root: str, files: dict[str, str]) -> None:
    for rel, text in files.items():
        p = os.path.join(root, rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)


def _git(root: str, *args: str) -> str:
    return subprocess.run(["git", "-C", root, "-c", "user.name=t", "-c", "user.email=t@t", *args],
                          capture_output=True, text=True, check=True).stdout.strip()


def _fixtures() -> dict[str, tuple[dict, dict]]:
    code = {SERVICE: GOOD_SVC, HEART: GOOD_HEART, CORE: GOOD_CORE, POPF: GOOD_POP, ACMDS: GOOD_ACMDS}
    tests_good = "".join(f"    [Fact]\n    public void {n}() {{ }}\n" for n in REQUIRED_TESTS)
    handoff_good = "ApiVersion = 35\n- v0.137.6: admin modleak, no wire change.\n"
    return {
        # defect: a new pop site that never arms the sweep
        "wiring": (code, dict(code, **{f"{PROJ}/Services/Other.cs":
            "static class O { static void Pop() { sgm.RemoveAbilityGroupModificationOnSlot(c, 1, m); } }\n"})),
        # defect: Clean destroys before clearing the mods (they would stay leaked, sourced by a dead entity)
        "wiring_order": (code, dict(code, **{SERVICE: GOOD_SVC.replace(
            "            removed = Core.ServerGameManager.Modifications.ClearLooseSourceModifications(holder, ref em);\n            DestroyUtility.Destroy(em, holder, DestroyDebugReason.None);\n",
            "            DestroyUtility.Destroy(em, holder, DestroyDebugReason.None);\n            removed = Core.ServerGameManager.Modifications.ClearLooseSourceModifications(holder, ref em);\n")})),
        # defect: Tick cleans without the two-read confirm
        "wiring_confirm": (code, dict(code, **{SERVICE: GOOD_SVC.replace("var confirmed = ModLeak.Select(_firstRead, verdicts);", "var confirmed = verdicts;")})),
        # defect: the pop sits in a local function and the outer method never marks
        "wiring_local": (dict(code, **{POPF: "static class S { static int P(Entity c) { void Pop(int id) { sgm.RemoveAbilityGroupModificationOnSlot(c, 1, m); } Pop(1); if (popped > 0) ModLeakService.MarkDue(\"bar-reset\"); return 1; } }\n"}),
                         dict(code, **{POPF: "static class S { static int P(Entity c) { void Pop(int id) { sgm.RemoveAbilityGroupModificationOnSlot(c, 1, m); } Pop(1); return 1; } }\n"})),
        # defect: a command arms the sweep (only boot and the two pops may)
        "actors_mark": (code, dict(code, **{ACMDS: GOOD_ACMDS + 'public static void Arm(ChatCommandContext ctx) { ModLeakService.MarkDue("x"); }\n'})),
        # defect: Clean without its catch (a throw would propagate into the heartbeat mid-pass)
        "wiring_catch": (code, dict(code, **{SERVICE: GOOD_SVC.replace("static bool Clean", "static bool CleanX")})),
        # defect: MarkDue restarts the pass on every pop (command spam would starve the sweep)
        "wiring_arm": (code, dict(code, **{SERVICE: GOOD_SVC.replace("_dueAt = ModLeak.Arm(_dueAt, DateTime.UtcNow, ReadGap);", "_dueAt = DateTime.UtcNow + ReadGap; _firstRead = null;")})),
        # defect: the heartbeat calls Tick unguarded
        "wiring_try": (code, dict(code, **{HEART: "static class Heartbeat { static void Pulse() { ModLeakService.Tick(); } }\n"})),
        "actors": (code, dict(code, **{ACMDS: GOOD_ACMDS.replace(", adminOnly: true", "")})),
        # defect: a command cleans holders directly, outside the confirmed sweep
        "actors_clean": (code, dict(code, **{ACMDS: GOOD_ACMDS + "public static void Wipe(ChatCommandContext ctx) { ModLeakService.Clean(h, out _); }\n"})),
        "secrets": ({f"{PROJ}/A.cs": 'var name = "hello world";'}, {f"{PROJ}/c.json": '{ "api_key": "abcdefgh12345678" }'}),
        "tests": ({TESTS: tests_good}, {TESTS: tests_good.replace(REQUIRED_TESTS[0] + "(", "Renamed(")}),
        "data": ({RULES: _rules(1.0, 1.0)}, {RULES: _rules(10.0, 1.0)}),
        "handoff": ({HANDOFF: handoff_good, API: "public const int ApiVersion = 35;"},
                    {HANDOFF: handoff_good.replace("modleak", "x"), API: "public const int ApiVersion = 35;"}),
        "backlog": ({BACKLOG: "| `modid-remap-errors` | DONE v0.137.6 (modid-remap-errors): ... | x |\n"},
                    {BACKLOG: "| `modid-remap-errors` | Startup remap errors | x |\n"}),
    }


def selftest(_root: str) -> str:
    global RECON
    bad = []
    boot_ok = ("[Info   : Beelzebub] [Beelz MODLEAK] sweep (boot): cleaned 7 stale holder(s), 22 leftover mod(s) removed; "
               "0 seen stale once (rechecked next pass), 0 unreadable; 9 ms.\n")
    with tempfile.TemporaryDirectory() as t:
        lg = os.path.join(t, "l.log")
        for text, want in ((boot_ok, "ok"), (boot_ok.replace("cleaned 7", "cleaned 3"), "FAIL"),
                           (boot_ok + "[Warning: Beelzebub] [Beelz MODLEAK] clean holder=1:1 failed: x\n", "FAIL"),
                           (boot_ok + boot_ok, "FAIL"), (boot_ok.replace("9 ms", "900 ms"), "FAIL"), ("", "no input")):
            with open(lg, "w", encoding="utf-8") as f:
                f.write(text)
            line = check_bootsweep(t, lg)
            ok = {"ok": ": ok" in line, "FAIL": ": FAIL" in line and "no input" not in line,
                  "no input": "no input" in line}[want]
            if not ok:
                bad.append(f"bootsweep want {want}, got: {line}")
        r1, r2 = os.path.join(t, "r1"), os.path.join(t, "r2")
        for d in (r1, r2):
            _write(d, {"NyarDev.log": "Server Setup Complete\n",
                       "LogOutput.log": "[Beelz MODLEAK] sweep (boot): nothing stale among 4 player holder(s); 2 ms.\n"})
        line = check_restarts(t, [r1, r2])
        if ": ok" not in line:
            bad.append("restarts want ok: " + line)
        _write(r2, {"NyarDev.log": "Server Setup Complete\n" + REMAP + " 1587\n"})
        line = check_restarts(t, [r1, r2])
        if ": FAIL" not in line or "no input" in line:
            bad.append("restarts want FAIL: " + line)
        _write(r2, {"NyarDev.log": "Server Setup Complete\n", "LogOutput.log": "Beelzebub initialized\n"})
        line = check_restarts(t, [r1, r2])
        if "no boot sweep line" not in line:
            bad.append("restarts want FAIL on a boot without a sweep line: " + line)
        if "no input" not in check_restarts(t, []):
            bad.append("restarts want no input")

    def expect(name: str, line: str, want: str) -> None:
        ok = {"ok": ": ok" in line, "FAIL": ": FAIL" in line and "no input" not in line,
              "no input": ": FAIL no input" in line}[want]
        if not ok:
            bad.append(f"{name} want {want}, got: {line}")
    for name, (good, defect) in _fixtures().items():
        check = CHECKS[name.split("_")[0]]
        for files, want in ((good, "ok"), (defect, "FAIL"), ({}, "no input")):
            with tempfile.TemporaryDirectory() as t:
                _write(t, files)
                expect(name, check(t), want)
    with tempfile.TemporaryDirectory() as t:
        expect("status", check_status(t), "no input")
        expect("rollback", check_rollback(t), "no input")
        _git(t, "init", "-q")
        _write(t, {"f.txt": "1"}); _git(t, "add", "-A"); _git(t, "commit", "-qm", "a")
        a = _git(t, "rev-parse", "--short", "HEAD")
        _write(t, {"f.txt": "2"}); _git(t, "add", "-A"); _git(t, "commit", "-qm", "b")
        b = _git(t, "rev-parse", "--short", "HEAD")
        expect("status", check_status(t), "ok")
        _write(t, {"stray.txt": "x"})
        expect("status", check_status(t), "FAIL")
        os.remove(os.path.join(t, "stray.txt"))
        body = " ".join(ROLLBACK_STEPS) + " " + CONSEQUENCE
        _write(t, {AUDIT: f"Rollback range: `{a}..{b}`\n{body}\n"})
        expect("rollback", check_rollback(t), "ok")
        _write(t, {AUDIT: f"Rollback range: `{b}..{a}`\n{body}\n"})
        expect("rollback", check_rollback(t), "FAIL")
        saved = RECON
        try:
            _git(t, "add", "-A"); _git(t, "commit", "-qm", "c")
            RECON = _git(t, "rev-parse", "--short", "HEAD")
            expect("paths", check_paths(t), "no input")
            _write(t, {PLAN: "## Rollout\n- Paths this change ships, writes or regenerates: `f.txt`, "
                             f"`{PLAN}`.\n- Next\n", "f.txt": "3"})
            expect("paths", check_paths(t), "ok")
            _write(t, {"undeclared.txt": "x"})
            expect("paths", check_paths(t), "FAIL")
        finally:
            RECON = saved
    if bad:
        return f"selftest: FAIL {len(bad)}: " + " | ".join(bad)
    return f"selftest: ok, {len(CHECKS) + 5} checks x good/defect/empty"


CHECKS_ALL = dict(CHECKS, rollback=check_rollback, paths=check_paths, status=check_status, selftest=selftest,
                  bootsweep=check_bootsweep, restarts=check_restarts)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("check", choices=sorted(CHECKS_ALL))
    ap.add_argument("--root", default=".")
    ap.add_argument("--log", help="bootsweep: the LogOutput.log of the build's first boot")
    ap.add_argument("--min", type=int, default=7, help="bootsweep: fewest holders the boot sweep must clean")
    ap.add_argument("--dirs", nargs="*", help="restarts: the log backup folders of each restart")
    a = ap.parse_args()
    if a.check == "bootsweep":
        line = check_bootsweep(a.root, a.log, a.min)
    elif a.check == "restarts":
        line = check_restarts(a.root, a.dirs)
    else:
        line = CHECKS_ALL[a.check](a.root)
    print(line)
    return 1 if ": FAIL" in line else 0


if __name__ == "__main__":
    sys.exit(main())
