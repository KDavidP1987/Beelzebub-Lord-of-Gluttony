#!/usr/bin/env python3
"""check_bar_reset.py — evidence checks for the bar-reset dod plan (Beelzebub/Beelzebub/docs/dod/bar-reset.md).

Each subcommand prints exactly one summary line, `<name>: ok ...` or `<name>: FAIL <reason>`, and exits 1 on FAIL.
Every subcommand FAILs with "no input" when it parses nothing — a check that finds nothing never passes.

  config    D5   Forms_AutoFillFromCaptures is bound in section Forms with default false
  commands  D13  Commands/ reaches the reset only through BarResetService.FullReset / ReadBar (allowlist)
  text      D14  no ForceAbilityBarReinit call, no ".beelz slot" reply, LEGACY: prefixes present
  auth      D15  targeting commands are adminOnly; FullReset / ReadBar callers are admin-only or self-only
  docs      D17 D18 D26  recovery guide flow, ApiVersion + handoff entry, backlog slugs
  audit     D21  docs/audits/bar-reset.md markers + the fault-harness output and patch-tree hash
  paths     D23  every changed / untracked path is declared in the plan's Rollout > Paths walked
  data      D28  every artifact row has owner/location/retention/copies; every producer has a row
  rollback  D22  the recorded rollback range resolves and ends at chore(release): v0.137.0
  session   D30  a copied session LogOutput.log proves the in-game steps D6-D10
  selftest  D29  runs the ten subcommands above on generated empty / defect / good fixture trees
  all            every subcommand except rollback, session and selftest (called by tools/preflight.ps1)

Usage: python Beelzebub/tools/check_bar_reset.py <subcommand> [--root <repo root>] [session: <log path>]
"""
from __future__ import annotations

import argparse
import os
import re
import shutil
import subprocess
import sys
import tempfile

PROJ = "Beelzebub/Beelzebub"
PLAN = f"{PROJ}/docs/dod/bar-reset.md"
AUDIT = f"{PROJ}/docs/audits/bar-reset.md"
FAULTS = "Beelzebub/tools/faults/bar-reset"
BASE_COMMIT = "3fbc2d4"
RELEASE_SUBJECT = "chore(release): v0.137.0"
API_VERSION = 33
OWNER_UNTRACKED = {f"{PROJ}/docs/V0136_ABILITY_TEST_PLAN.xlsx", "_matrix_build.py"}

# D13: reset-layer symbols a command may never call (on top of every IBarResetOps member and every other public
# BarResetService member). Exempt: the LEGACY recovery handlers and clearbar (one chosen set; backlog clearbar-fullreset).
LAYER_SYMBOLS = ["RemoveInjectedRows", "ReapplyEquipRows", "PopSlotModifications", "ForceResetAbilitySlots",
                 "PurgeAbilitySlotModifications", "ClearAllLoadouts", "ClearAllSlots", "RestoreSlotBaseValue",
                 "TrySaveSync"]
ALLOWED_ENTRY = {"FullReset", "ReadBar"}
EXEMPT_COMMANDS = {"clearslotmods", "rebuildslots", "clearbar"}


class CheckFail(Exception):
    pass


def read(root: str, rel: str) -> str:
    p = os.path.join(root, rel)
    if not os.path.isfile(p):
        raise CheckFail(f"no input: {rel} missing")
    with open(p, encoding="utf-8-sig", errors="replace") as f:
        return f.read()


def git(root: str, *args: str) -> str:
    r = subprocess.run(["git", "-C", root, *args], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if r.returncode != 0:
        raise CheckFail(f"git {' '.join(args)}: {r.stderr.strip() or r.returncode}")
    return r.stdout


def base_ref(root: str) -> str:
    """The plan's base commit; a selftest fixture repo marks its own base with the tag fixture-base."""
    r = subprocess.run(["git", "-C", root, "rev-parse", "--verify", "-q", "fixture-base"], capture_output=True, text=True)
    return "fixture-base" if r.returncode == 0 else BASE_COMMIT


def added_text(root: str, rel: str) -> str:
    """Lines this feature added to <rel> since the base commit (working tree included); the whole file if untracked."""
    tracked = subprocess.run(["git", "-C", root, "ls-files", "--error-unmatch", rel], capture_output=True).returncode == 0
    if not tracked:
        return read(root, rel)
    diff = git(root, "diff", base_ref(root), "--", rel)
    return "\n".join(l[1:] for l in diff.splitlines() if l.startswith("+") and not l.startswith("+++"))


# ── C# source helpers ───────────────────────────────────────────────────────────────────────────────────────────

def strip_cs(src: str) -> str:
    """Blank comments and string/char literal CONTENTS with spaces (same length, newlines kept) so positions map
    1:1 onto the raw text and braces / names inside strings are invisible."""
    out = list(src)
    i, n = 0, len(src)

    def blank(a: int, b: int) -> None:
        for k in range(a, min(b, n)):
            if out[k] != "\n":
                out[k] = " "

    while i < n:
        c = src[i]
        two = src[i:i + 2]
        if two == "//":
            j = src.find("\n", i)
            j = n if j < 0 else j
            blank(i, j)
            i = j
        elif two == "/*":
            j = src.find("*/", i + 2)
            j = n if j < 0 else j + 2
            blank(i, j)
            i = j
        elif src.startswith('"""', i):
            j = src.find('"""', i + 3)
            j = n if j < 0 else j + 3
            blank(i + 1, j - 1)
            i = j
        elif c == "@" and src[i + 1:i + 2] == '"' or src[i:i + 3] in ('$@"', '@$"'):
            start = src.index('"', i) + 1
            j = start
            while j < n:
                if src[j] == '"' and src[j + 1:j + 2] == '"':
                    j += 2
                    continue
                if src[j] == '"':
                    break
                j += 1
            blank(start, j)
            i = j + 1
        elif c == '"':
            j = i + 1
            while j < n and src[j] != '"' and src[j] != "\n":
                j += 2 if src[j] == "\\" else 1
            blank(i + 1, j)
            i = j + 1
        elif c == "'":
            j = i + 1
            while j < n and src[j] != "'" and src[j] != "\n":
                j += 2 if src[j] == "\\" else 1
            blank(i + 1, j)
            i = j + 1
        else:
            i += 1
    return "".join(out)


_KEYWORDS = {"if", "for", "foreach", "while", "switch", "catch", "using", "lock", "fixed", "return", "new", "else",
             "nameof", "typeof", "sizeof", "default", "when", "checked", "unchecked", "base", "this", "throw", "await",
             "stackalloc"}
_SIG = re.compile(r"\b([A-Za-z_]\w*)(?:<[^()]*?>)?\s*\(")


def match_brace(s: str, open_pos: int) -> int:
    depth = 0
    for k in range(open_pos, len(s)):
        if s[k] == "{":
            depth += 1
        elif s[k] == "}":
            depth -= 1
            if depth == 0:
                return k
    return len(s) - 1


def methods(stripped: str) -> list[tuple[str, int, int, int]]:
    """(name, signature start, body start, body end) of every method-like declaration with a { } or => body."""
    found = []
    for m in _SIG.finditer(stripped):
        name = m.group(1)
        if name in _KEYWORDS:
            continue
        # the token before the name must be a type-ish token (identifier, >, ], ?) — rules out calls like Foo.Bar(
        before = stripped[:m.start()].rstrip()
        if not before or not (before[-1].isalnum() or before[-1] in "_>]?"):
            continue
        prev_word = re.search(r"([A-Za-z_]\w*)\W*$", before)
        if prev_word and prev_word.group(1) in _KEYWORDS | {"return", "await", "is", "as", "in", "out", "ref"}:
            continue
        # parameter list, then { or =>
        depth, k = 0, m.end() - 1
        while k < len(stripped):
            if stripped[k] == "(":
                depth += 1
            elif stripped[k] == ")":
                depth -= 1
                if depth == 0:
                    break
            k += 1
        rest = stripped[k + 1:k + 400]
        mm = re.match(r"\s*(?:where\s[^{;]*)?(\{|=>)", rest)
        if not mm:
            continue
        body_start = k + 1 + mm.start(1)
        if mm.group(1) == "{":
            body_end = match_brace(stripped, body_start)
        else:
            semi = stripped.find(";", body_start)
            body_end = len(stripped) - 1 if semi < 0 else semi
        found.append((name, m.start(), body_start, body_end))
    return found


def outermost(meths, pos: int):
    best = None
    for mt in meths:
        if mt[1] <= pos <= mt[3] and (best is None or mt[1] < best[1]):
            best = mt
    return best


def command_attrs(raw: str) -> list[dict]:
    """Every [Command(...)] attribute: its name, whether adminOnly: true, its description, and the method it decorates."""
    res = []
    for m in re.finditer(r"\[Command\(", raw):
        k, depth, instr = m.end() - 1, 0, False
        while k < len(raw):
            ch = raw[k]
            if instr:
                if ch == "\\":
                    k += 2
                    continue
                if ch == '"':
                    instr = False
            elif ch == '"':
                instr = True
            elif ch == "(":
                depth += 1
            elif ch == ")":
                depth -= 1
                if depth == 0:
                    break
            k += 1
        args = raw[m.end():k]
        name_m = re.match(r'\s*"([^"]*)"', args)
        desc_m = re.search(r'description:\s*"((?:[^"\\]|\\.)*)"', args)
        sig = re.search(r"\]\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|internal|protected|static|async)\s+)+[\w<>\[\],.?]+\s+(\w+)\s*\(([^)]*)\)",
                        raw[k:k + 600])
        res.append({
            "name": name_m.group(1) if name_m else "",
            "admin": bool(re.search(r"adminOnly:\s*true", args)),
            "desc": desc_m.group(1) if desc_m else "",
            "method": sig.group(1) if sig else "",
            "params": sig.group(2) if sig else "",
            "pos": m.start(),
        })
    return res


def cs_files(root: str, rel_dir: str) -> list[str]:
    base = os.path.join(root, rel_dir)
    out = []
    for dp, dns, fns in os.walk(base):
        dns[:] = [d for d in dns if d not in ("bin", "obj")]
        for fn in fns:
            if fn.endswith(".cs"):
                out.append(os.path.relpath(os.path.join(dp, fn), root).replace("\\", "/"))
    return sorted(out)


# ── plan helpers ────────────────────────────────────────────────────────────────────────────────────────────────

def plan_section(plan: str, heading_re: str) -> str:
    m = re.search(heading_re, plan, re.M)
    if not m:
        return ""
    nxt = re.search(r"^##+ ", plan[m.end():], re.M)
    return plan[m.end(): m.end() + nxt.start()] if nxt else plan[m.end():]


def paths_walked(plan: str) -> list[str]:
    sec = plan_section(plan, r"^- Paths walked.*$")
    out = []
    for line in sec.splitlines():
        if "Declared, not diffed" in line:
            break
        m = re.match(r"\s+- `([^`]+)`", line)
        if m:
            out.append(m.group(1))
    return out


def data_rows(plan: str) -> list[dict]:
    sec = plan_section(plan, r"^### Data\s*$")
    rows = []
    for line in sec.splitlines():
        if not line.startswith("- artifact:"):
            continue
        fields = {}
        for part in line[2:].split(" · "):
            if ":" in part:
                k, v = part.split(":", 1)
                fields[k.strip()] = v.strip()
        fields["_line"] = line
        rows.append(fields)
    return rows


# ── checks ──────────────────────────────────────────────────────────────────────────────────────────────────────

def check_config(root: str) -> str:
    src = read(root, f"{PROJ}/Config/Settings.cs")
    m = re.search(r"Forms_AutoFillFromCaptures\s*=\s*config\.Bind\(\s*\"(\w+)\"\s*,\s*nameof\(Forms_AutoFillFromCaptures\)\s*,\s*(\w+)", src)
    if not m:
        raise CheckFail("no input: no Bind call for Forms_AutoFillFromCaptures")
    if m.group(1) != "Forms":
        raise CheckFail(f"bound in section {m.group(1)}, not Forms")
    if m.group(2) != "false":
        raise CheckFail(f"default is {m.group(2)}, not false")
    return "config: ok"


def layer_symbols(root: str) -> set[str]:
    syms = set(LAYER_SYMBOLS)
    logic = read(root, f"{PROJ}/Logic/BarReset.cs")
    im = re.search(r"interface\s+IBarResetOps\s*\{(.*?)\n\}", logic, re.S)
    if not im:
        raise CheckFail("no input: IBarResetOps not found in Logic/BarReset.cs")
    members = re.findall(r"\b(\w+)\s*\(\s*\)\s*;", strip_cs(im.group(1)))
    if not members:
        raise CheckFail("no input: IBarResetOps has no members")
    syms.update(members)
    svc = os.path.join(root, PROJ, "Services/BarResetService.cs")
    if os.path.isfile(svc):
        s = read(root, f"{PROJ}/Services/BarResetService.cs")
        syms.update(re.findall(r"\bpublic\s+(?:static\s+)?[\w<>\[\](),.? ]+?\s+(\w+)\s*\(", s))
    return syms - ALLOWED_ENTRY


def check_commands(root: str) -> str:
    syms = layer_symbols(root)
    files = cs_files(root, f"{PROJ}/Commands")
    if not files:
        raise CheckFail("no input: no Commands/*.cs")
    sym_re = re.compile(r"\b(" + "|".join(sorted(map(re.escape, syms))) + r")\s*\(")
    full_calls, bad = 0, []
    for rel in files:
        raw = read(root, rel)
        st = strip_cs(raw)
        meths = methods(st)
        attrs = {a["method"]: a for a in command_attrs(raw)}
        full_calls += len(re.findall(r"\bFullReset\s*\(", st))
        for m in sym_re.finditer(st):
            host = outermost(meths, m.start())
            cmd = attrs.get(host[0]) if host else None
            if cmd and cmd["name"] in EXEMPT_COMMANDS:
                continue
            line = raw.count("\n", 0, m.start()) + 1
            bad.append(f"{rel}:{line} {m.group(1)} in {host[0] if host else '<no method>'}")
    if bad:
        raise CheckFail("reset-layer symbol outside the allowlist: " + "; ".join(bad[:6]))
    if full_calls == 0:
        raise CheckFail("no input: no FullReset call site under Commands/")
    if full_calls < 3:
        raise CheckFail(f"FullReset called {full_calls} time(s); resetbar, reset-loadouts and purge need 3")
    return f"commands: ok, {len(syms)} symbols, {full_calls} FullReset call sites"


def check_text(root: str) -> str:
    files = cs_files(root, f"{PROJ}/Commands")
    if not files:
        raise CheckFail("no input: no Commands/*.cs")
    seen, bad = set(), []
    for rel in files:
        raw = read(root, rel)
        st = strip_cs(raw)
        if re.search(r"\bForceAbilityBarReinit\s*\(", st):
            bad.append(f"{rel} calls ForceAbilityBarReinit")
        for m in re.finditer(r"\.beelz slot[ .]", raw):
            bad.append(f"{rel}:{raw.count(chr(10), 0, m.start()) + 1} names .beelz slot")
        for a in command_attrs(raw):
            if a["name"] in ("clearslotmods", "rebuildslots"):
                seen.add(a["name"])
                if not a["desc"].startswith("LEGACY:"):
                    bad.append(f"{a['name']} description lacks LEGACY:")
    missing = {"clearslotmods", "rebuildslots"} - seen
    if len(seen) == 0:
        raise CheckFail("no input: neither clearslotmods nor rebuildslots found")
    if missing:
        bad.append("missing command(s): " + ", ".join(sorted(missing)))
    if bad:
        raise CheckFail("; ".join(bad[:6]))
    return "text: ok"


def check_auth(root: str) -> str:
    files = cs_files(root, f"{PROJ}/Commands")
    if not files:
        raise CheckFail("no input: no Commands/*.cs")
    admin_classes = set()
    for rel in files:
        raw = read(root, rel)
        for m in re.finditer(r'\[CommandGroup\("beelz admin"\)\]\s*(?:\[[^\]]*\]\s*)*[\w\s]*?class\s+(\w+)', raw):
            admin_classes.add(m.group(1))
    admin_count, bad, handlers = 0, [], {}
    for rel in files:
        raw = read(root, rel)
        cls = re.search(r"\bclass\s+(\w+)", strip_cs(raw))
        in_admin = bool(cls and cls.group(1) in admin_classes)
        for a in command_attrs(raw):
            params = [p.strip().split()[-1].split("=")[0].strip() for p in a["params"].split(",")[1:] if p.strip()]
            targets = any(p in ("player", "target") for p in params)
            if in_admin:
                admin_count += 1
            if (in_admin or targets) and not a["admin"]:
                bad.append(f"{rel}: [Command(\"{a['name']}\")] lacks adminOnly: true")
            if a["name"] in ("resetbar", "clearbar") and targets:
                bad.append(f"{a['name']} takes a player/target parameter")
            handlers[(rel, a["method"])] = a
    if admin_count == 0:
        raise CheckFail("no input: no beelz admin commands parsed")
    if admin_count < 50:
        raise CheckFail(f"only {admin_count} admin commands parsed (expected >= 50)")
    # every caller of FullReset / ReadBar anywhere in the plugin
    callers = 0
    for rel in cs_files(root, PROJ):
        if rel.endswith("Services/BarResetService.cs"):
            continue
        raw = read(root, rel)
        st = strip_cs(raw)
        meths = methods(st)
        for m in re.finditer(r"\b(FullReset|ReadBar)\s*\(", st):
            callers += 1
            host = outermost(meths, m.start())
            a = handlers.get((rel, host[0])) if host else None
            if a is None:
                bad.append(f"{rel}:{raw.count(chr(10), 0, m.start()) + 1} {m.group(1)} called outside a command handler")
            elif not (a["admin"] or a["method"] == "ResetBar"):
                bad.append(f"{rel}: {a['name']} calls {m.group(1)} without adminOnly")
    if bad:
        raise CheckFail("; ".join(bad[:6]))
    return f"auth: ok, {admin_count} admin commands, {callers} reset callers"


def check_docs(root: str) -> str:
    guide = read(root, f"{PROJ}/docs/RECOVERY_GUIDE.md")
    if not guide.strip():
        raise CheckFail("no input: RECOVERY_GUIDE.md is empty")
    bad = []
    order = [".beelz admin bar", ".beelz resetbar CONFIRM", ".beelz admin reset-character"]
    pos = [guide.find(s) for s in order]
    for s, p in zip(order, pos):
        if p < 0:
            bad.append(f"guide lacks {s}")
    if all(p >= 0 for p in pos) and not pos[0] < pos[1] < pos[2]:
        bad.append("guide lists admin bar / resetbar / reset-character out of order")
    if ".beelz admin purge" not in guide:
        bad.append("guide lacks .beelz admin purge")
    if re.search(r"guaranteed clean", guide, re.I):
        bad.append('guide still says "guaranteed clean"')
    api = re.search(r"const int ApiVersion\s*=\s*(\d+)", read(root, f"{PROJ}/Commands/ApiCommands.cs"))
    handoff = read(root, f"{PROJ}/docs/BCH_INTEGRATION_HANDOFF.md")
    banner = re.search(r"ApiVersion = (\d+)", handoff)
    if not api or not banner:
        raise CheckFail("no input: ApiVersion or the handoff banner not found")
    if api.group(1) != banner.group(1):
        bad.append(f"ApiCommands.cs {api.group(1)} != handoff banner {banner.group(1)}")
    if int(api.group(1)) != API_VERSION:
        bad.append(f"ApiVersion is {api.group(1)}, expected {API_VERSION}")
    entry = re.search(r"v0\.137[^\n]*\n(.*?)(?=\n#{1,3} |\Z)", handoff, re.S)
    if not entry:
        bad.append("handoff has no v0.137 entry")
    else:
        body = entry.group(0)
        for need in ("Forms_AutoFillFromCaptures", "admin bar", f"api>={API_VERSION}"):
            if need not in body:
                bad.append(f"handoff v0.137 entry lacks {need}")
    backlog = read(root, f"{PROJ}/docs/BACKLOG.md")
    for slug in ("clearbar-fullreset", "transform-chain-guard", "docs-consolidation", "dev-snapshot"):
        if slug not in backlog:
            bad.append(f"BACKLOG.md lacks {slug}")
    if bad:
        raise CheckFail("; ".join(bad[:6]))
    return "docs: ok"


def check_audit(root: str) -> str:
    rec = read(root, AUDIT)
    if not rec.strip():
        raise CheckFail("no input: audit record is empty")
    bad = [f"lacks '{m}'" for m in ("## Pre-audit", "## Post-audit", "Codex verdict:", "planted faults:") if m not in rec]
    h = re.search(r"planted faults:.*?patch tree ([0-9a-f]{40})", rec, re.S)
    if not re.search(r"harness: ok, \d+ faults", rec):
        bad.append("no 'harness: ok' output recorded")
    if not h:
        bad.append("no patch tree hash recorded")
    else:
        try:
            cur = git(root, "rev-parse", f"HEAD:{FAULTS}").strip()
            if cur != h.group(1):
                bad.append(f"recorded patch tree {h.group(1)[:12]} != HEAD {cur[:12]} (re-run the harness)")
        except CheckFail as e:
            bad.append(str(e))
    if bad:
        raise CheckFail("; ".join(bad[:6]))
    return "audit: ok"


def changed_paths(root: str) -> list[str]:
    out = set(p for p in git(root, "diff", "--name-only", f"{base_ref(root)}..HEAD").splitlines() if p)
    for line in git(root, "status", "--porcelain=v1", "--untracked-files=all").splitlines():
        if len(line) > 3:
            p = line[3:].strip().strip('"')
            if " -> " in p:
                p = p.split(" -> ", 1)[1]
            out.add(p)
    return sorted(out - OWNER_UNTRACKED)


def check_paths(root: str) -> str:
    declared = paths_walked(read(root, PLAN))
    if not declared:
        raise CheckFail("no input: no Paths walked entries parsed from the plan")
    undeclared = []
    for p in changed_paths(root):
        if not any(p == d or (d.endswith("/") and p.startswith(d)) for d in declared):
            undeclared.append(p)
    if undeclared:
        raise CheckFail("undeclared path(s): " + ", ".join(undeclared[:8]))
    return f"paths: ok, {len(declared)} paths"


def check_data(root: str) -> str:
    plan = read(root, PLAN)
    rows = data_rows(plan)
    if not rows:
        raise CheckFail("no input: no artifact rows in Design > Data")
    bad = []
    for r in rows:
        for f in ("owner", "location", "retention", "copies"):
            if not r.get(f):
                bad.append(f"row '{r.get('artifact', '?')}' lacks {f}")
    blob = "\n".join(r["_line"] for r in rows)
    for rel in paths_walked(plan):
        p = os.path.join(root, rel)
        if not os.path.isfile(p):
            continue
        whole = open(p, encoding="utf-8-sig", errors="replace").read()
        txt = added_text(root, rel)   # producers THIS feature added; pre-existing tags are not its artifacts
        if rel.endswith(".cs"):
            for tag in sorted(set(re.findall(r"\[Beelz ([A-Z]+)\]", txt))):
                if f"[Beelz {tag}]" not in blob:
                    bad.append(f"{rel} logs [Beelz {tag}] with no row")
            for key in re.findall(r"config\.Bind\(\s*\"\w+\"\s*,\s*nameof\((\w+)\)", txt, re.I):
                if key == "Forms_AutoFillFromCaptures" and key not in blob:
                    bad.append(f"{rel} binds {key} with no row")
            if re.search(r"File\.(?:Write\w*|Replace|Move)\(", txt):
                names = set(re.findall(r"\"([\w.-]+\.(?:json|txt|log|cfg))\"", whole))
                if names and not any(n in blob for n in names):
                    bad.append(f"{rel} writes {', '.join(sorted(names))} with no row")
        elif rel.endswith(".py"):
            if re.search(r"open\([^)]*['\"]w", txt) or "mkdtemp" in txt:
                if os.path.basename(rel) not in blob and "mkdtemp" in txt:
                    bad.append(f"{rel} writes files with no row naming it")
        elif rel.endswith(".csproj") and "bin/" not in blob:
            bad.append(f"{rel} builds outputs with no bin/ row")
    if bad:
        raise CheckFail("; ".join(bad[:6]))
    return f"data: ok, {len(rows)} artifacts"


def check_rollback(root: str) -> str:
    rec = read(root, AUDIT)
    m = re.search(r"rollback: git revert --no-edit ([0-9a-f]{7,40})\^\.\.([0-9a-f]{7,40})", rec)
    if not m:
        raise CheckFail("no input: no rollback line in the audit record")
    first = git(root, "rev-parse", "--verify", m.group(1) + "^{commit}").strip()
    release = git(root, "rev-parse", "--verify", m.group(2) + "^{commit}").strip()
    subject = git(root, "log", "-1", "--format=%s", release).strip()
    if subject != RELEASE_SUBJECT:
        raise CheckFail(f"release commit subject is '{subject}'")
    for anc, desc, what in ((first, release, "first is not an ancestor of release"),
                            (base_ref(root), first, f"first is not a descendant of {base_ref(root)}")):
        r = subprocess.run(["git", "-C", root, "merge-base", "--is-ancestor", anc, desc])
        if r.returncode != 0:
            raise CheckFail(what)
    if "Version = 8" not in read(root, f"{PROJ}/Services/PersistenceService.cs"):
        raise CheckFail("PersistenceService no longer writes Version = 8")
    return "rollback: ok"


# ── session log (D30) ───────────────────────────────────────────────────────────────────────────────────────────

_BAR = re.compile(r"\[Beelz BAR\] target=(?P<t>.*?) \((?P<id>\d+)\) binds=(?P<binds>\d+) rows=(?P<rows>\d+) "
                  r"gear=(?P<gear>\d+) other=(?P<other>\w+) slots=(?P<slots>\S*)(?: part=(?P<k>\d+)/(?P<n>\d+))?")
_RESET = re.compile(r"\[Beelz RESET\] run=(?P<run>\d+) scope=(?P<scope>\w+) target=(?P<t>.*?) \((?P<id>\d+)\) "
                    r"ms=(?P<ms>\d+) steps=(?P<steps>\S+) survivors=(?P<surv>\S+)")
_LATE = re.compile(r"\[Beelz RESET\] late-survivor target=(?P<t>.*?) \((?P<id>\d+)\) run=(?P<run>\d+) slot=(?P<slot>\d+)")
_FORM = re.compile(r"\[Beelz FORM\] form=(?P<form>\w+) source=(?P<src>\w+)")


def check_session(root: str, log: str | None) -> str:
    if not log or not os.path.isfile(log) or os.path.getsize(log) == 0:
        raise CheckFail(f"no input: session log missing or empty ({log})")
    target = "PerpetualChaos"
    events = []
    with open(log, encoding="utf-8", errors="replace") as f:
        for line in f:
            for kind, rx in (("bar", _BAR), ("late", _LATE), ("reset", _RESET), ("form", _FORM)):
                m = rx.search(line)
                if m:
                    events.append((kind, m.groupdict(), line.strip()))
                    break
    if not events:
        raise CheckFail("no input: no [Beelz BAR/RESET/FORM] lines in the log")
    mine = [e for e in events if e[0] == "form" or e[1].get("t") == target]
    # join BAR continuation parts into one reading
    joined = []
    for kind, d, line in mine:
        if kind == "bar" and d.get("k") and int(d["k"]) > 1 and joined and joined[-1][0] == "bar":
            joined[-1][1]["slots"] += "," + d["slots"]
            continue
        joined.append((kind, dict(d), line))
    bad = []

    # D6 — two consecutive identical BAR readings with slots 1 and 4 bound
    d6 = None
    for i in range(len(joined) - 1):
        a, b = joined[i], joined[i + 1]
        if a[0] == b[0] == "bar" and a[1] == b[1]:
            cells = {c.split(":")[0]: c.split(":") for c in a[1]["slots"].split(",") if c and c != "none"}
            if cells.get("1", [None, None])[1] == "bind" and cells.get("4", [None, None])[1] == "bind":
                d6 = i
                break
    if d6 is None:
        bad.append("D6: no two identical [Beelz BAR] lines with slots 1 and 4 bound")

    # D7 — a PlayerReset with survivors=none, then a clean BAR, and no late-survivor for that run
    d7_idx, d7_run = None, None
    for i, (kind, d, _) in enumerate(joined):
        if kind == "reset" and d["scope"] == "PlayerReset" and d["surv"] == "none":
            nxt = next((e for e in joined[i + 1:] if e[0] == "bar"), None)
            if nxt and nxt[1]["binds"] == "0" and nxt[1]["rows"] == "0" and nxt[1]["other"] == "0":
                d7_idx, d7_run = i, d["run"]
                break
    if d7_idx is None:
        bad.append("D7: no PlayerReset with survivors=none followed by a BAR line binds=0 rows=0 other=0")
    elif any(k == "late" and d["run"] == d7_run for k, d, _ in joined):
        bad.append(f"D7: late-survivor after reset run {d7_run}")

    # D8 — a Purge with survivors=none and ClearHotkeys:1
    if not any(k == "reset" and d["scope"] == "Purge" and d["surv"] == "none" and "ClearHotkeys:1" in d["steps"]
               for k, d, _ in joined):
        bad.append("D8: no Purge line with survivors=none and ClearHotkeys:1")

    # D9 — Wolf enters with its native kit after the D7 reset
    if d7_idx is not None and not any(k == "form" and d["form"] == "Wolf" and d["src"] == "native"
                                      for k, d, _ in joined[d7_idx + 1:]):
        bad.append("D9: no [Beelz FORM] form=Wolf source=native after the D7 reset")
    elif d7_idx is None:
        bad.append("D9: no D7 reset to anchor the Wolf check")

    # D10 — three consecutive PlayerReset runs, each BAR after it other=0 with the same gear=
    triples, streak = [], []
    for i, (kind, d, _) in enumerate(joined):
        if kind == "reset":
            if d["scope"] != "PlayerReset":
                streak = []
                continue
            nxt = next((e for e in joined[i + 1:] if e[0] in ("bar", "reset")), None)
            streak.append(nxt[1] if nxt and nxt[0] == "bar" else None)
            if len(streak) >= 3:
                triples.append(streak[-3:])
    ok10 = any(all(b is not None and b["other"] == "0" for b in t) and len({b["gear"] for b in t}) == 1 for t in triples)
    if not ok10:
        bad.append("D10: no three consecutive PlayerReset runs with other=0 and a constant gear= after each")
    if bad:
        raise CheckFail("; ".join(bad))
    return "session: ok D6 D7 D8 D9 D10"


# ── selftest (D29) ──────────────────────────────────────────────────────────────────────────────────────────────

SUBS = ["config", "commands", "text", "auth", "docs", "audit", "paths", "data", "rollback", "session"]


def _w(root: str, rel: str, text: str) -> None:
    p = os.path.join(root, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def _git(root: str, *args: str) -> str:
    env = dict(os.environ, GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@t", GIT_COMMITTER_NAME="t", GIT_COMMITTER_EMAIL="t@t")
    r = subprocess.run(["git", "-C", root, *args], capture_output=True, text=True, env=env)
    if r.returncode != 0:
        raise RuntimeError(f"fixture git {' '.join(args)}: {r.stderr}")
    return r.stdout.strip()


GOOD_SESSION = "\n".join([
    "[Info   :Beelzebub] [Beelz BAR] target=PerpetualChaos (7) binds=2 rows=2 gear=2 other=0 slots=1:bind:1:0,4:bind:1:0",
    "[Info   :Beelzebub] [Beelz BAR] target=PerpetualChaos (7) binds=2 rows=2 gear=2 other=0 slots=1:bind:1:0,4:bind:1:0",
    "[Info   :Beelzebub] [Beelz RESET] run=1 scope=PlayerReset target=PerpetualChaos (7) ms=12 steps=ClearSavedBindings:2 survivors=none",
    "[Info   :Beelzebub] [Beelz BAR] target=PerpetualChaos (7) binds=0 rows=0 gear=9 other=0 slots=none",
    "[Info   :Beelzebub] [Beelz FORM] form=Wolf source=native",
    "[Info   :Beelzebub] [Beelz RESET] run=2 scope=PlayerReset target=PerpetualChaos (7) ms=9 steps=ClearSavedBindings:0 survivors=none",
    "[Info   :Beelzebub] [Beelz BAR] target=PerpetualChaos (7) binds=0 rows=0 gear=9 other=0 slots=none",
    "[Info   :Beelzebub] [Beelz RESET] run=3 scope=PlayerReset target=PerpetualChaos (7) ms=9 steps=ClearSavedBindings:0 survivors=none",
    "[Info   :Beelzebub] [Beelz BAR] target=PerpetualChaos (7) binds=0 rows=0 gear=9 other=0 slots=none",
    "[Info   :Beelzebub] [Beelz RESET] run=4 scope=Purge target=PerpetualChaos (7) ms=9 steps=ClearSavedBindings:2,ClearHotkeys:1 survivors=none",
    "",
])

GOOD_FILES = {
    f"{PROJ}/Config/Settings.cs":
        'class S { void B() { Forms_AutoFillFromCaptures = config.Bind(\n "Forms", nameof(Forms_AutoFillFromCaptures), false, "x"); } }\n',
    f"{PROJ}/Logic/BarReset.cs": "public interface IBarResetOps\n{\n    int Reapply();\n    bool SaveBindings();\n}\n",
    f"{PROJ}/Services/BarResetService.cs":
        "static class BarResetService {\n public static int FullReset(int c) { return 0; }\n public static int ReadBar(int c) { return 0; }\n"
        " public static int RemoveInjectedRows(int c) { return 0; }\n}\n",
    f"{PROJ}/Services/PersistenceService.cs": 'class P { int Version = 8; void S() { File.WriteAllText("state.json", ""); } }\n',
    f"{PROJ}/Commands/BeelzCommands.cs":
        '[CommandGroup("beelz")]\ninternal static class BeelzCommands\n{\n'
        '    [Command("resetbar", description: "Reset your bar.")]\n'
        '    public static void ResetBar(ChatCommandContext ctx, string confirm = null)\n    { BarResetService.FullReset(1); }\n\n'
        '    [Command("clearbar", description: "Clear one set.")]\n'
        '    public static void ClearBar(ChatCommandContext ctx, string bucket = null)\n    { Core.AbilityRegistry.ClearAllSlots(1); }\n}\n',
    f"{PROJ}/Commands/AdminCommands.cs":
        '[CommandGroup("beelz admin")]\ninternal static partial class AdminCommands\n{\n'
        '    [Command("purge", adminOnly: true, description: "Purge.")]\n'
        '    public static void Purge(ChatCommandContext ctx, string player = null, string confirm = null)\n    { BarResetService.FullReset(1); }\n\n'
        '    [Command("reset-loadouts", adminOnly: true, description: "Reset loadouts.")]\n'
        '    public static void ResetLoadouts(ChatCommandContext ctx, string player, string confirm = null)\n    { BarResetService.FullReset(1); }\n\n'
        '    [Command("bar", adminOnly: true, description: "Show a bar.")]\n'
        '    public static void Bar(ChatCommandContext ctx, string player = null)\n    { BarResetService.ReadBar(1); }\n\n'
        '    [Command("clearslotmods", adminOnly: true, description: "LEGACY: clear slot mods.")]\n'
        '    public static void ClearSlotMods(ChatCommandContext ctx, string player = null)\n    { TransformBuffService.PurgeAbilitySlotModifications(1); }\n\n'
        '    [Command("rebuildslots", adminOnly: true, description: "LEGACY: rebuild slots.")]\n'
        '    public static void RebuildSlots(ChatCommandContext ctx, string player = null)\n    { TransformBuffService.ForceResetAbilitySlots(1); }\n'
        + "".join(f'\n    [Command("x{i}", adminOnly: true, description: "filler {i}")]\n'
                  f'    public static void X{i}(ChatCommandContext ctx, string player = null) {{ }}\n' for i in range(46))
        + "}\n",
    f"{PROJ}/Commands/ApiCommands.cs": f"class A {{ const int ApiVersion = {API_VERSION}; }}\n",
    f"{PROJ}/docs/RECOVERY_GUIDE.md":
        "1. `.beelz admin bar <player>`\n2. `.beelz resetbar CONFIRM` or `.beelz admin purge <player> CONFIRM`\n"
        "3. Only if survivors remain: `.beelz admin reset-character <player>`\n",
    f"{PROJ}/docs/BCH_INTEGRATION_HANDOFF.md":
        f"ApiVersion = {API_VERSION}\n\n## v0.137 bar reset\nAdds Forms_AutoFillFromCaptures and admin bar; gate api>={API_VERSION}.\n",
    f"{PROJ}/docs/BACKLOG.md": "- clearbar-fullreset\n- transform-chain-guard\n- docs-consolidation\n- dev-snapshot\n",
    f"{FAULTS}/D1-x.patch": "fixture patch\n",
}


def _good_plan(extra_paths: list[str]) -> str:
    rows = [
        "- artifact: state.json binds · owner: the server · location: state.json · retention: r · copies: one",
        "- artifact: [Beelz RESET] line · owner: the server · location: log · retention: r · copies: one",
        "- artifact: Forms_AutoFillFromCaptures · owner: admin · location: cfg · retention: r · copies: one",
    ]
    paths = sorted(set(GOOD_FILES) | set(extra_paths) | {PLAN, AUDIT, f"{FAULTS}/", "session.log"})
    return ("# plan\n\n## Design\n### Data\nrows:\n" + "\n".join(rows) + "\n### States\n\n## Rollout\n- Paths walked (x):\n"
            + "\n".join(f"  - `{p}`" for p in paths) + "\n  - Declared, not diffed: nothing\n\n## Out of scope\n")


def _build_good(root: str) -> None:
    for rel, text in GOOD_FILES.items():
        _w(root, rel, text)
    _w(root, PLAN, _good_plan([]))
    _git(root, "init", "-q")
    _w(root, "seed.txt", "seed\n")
    _git(root, "add", "-A")
    _git(root, "commit", "-q", "-m", "base")
    _git(root, "tag", "fixture-base")
    _w(root, f"{PROJ}/Services/BarResetService.cs", GOOD_FILES[f"{PROJ}/Services/BarResetService.cs"] + "// step\n")
    _git(root, "add", "-A")
    _git(root, "commit", "-q", "-m", "feat: step")
    first = _git(root, "rev-parse", "HEAD")
    _git(root, "commit", "-q", "--allow-empty", "-m", RELEASE_SUBJECT)
    release = _git(root, "rev-parse", "HEAD")
    tree = _git(root, "rev-parse", f"HEAD:{FAULTS}")
    _w(root, AUDIT, "## Pre-audit\n## Post-audit\nCodex verdict: clean\nplanted faults:\nharness: ok, 1 faults, 1 clauses\n"
                    f"patch tree {tree}\nrollback: git revert --no-edit {first}^..{release}\n")
    _w(root, "session.log", GOOD_SESSION)
    _git(root, "add", "-A")
    _git(root, "commit", "-q", "-m", "docs(audit): rollback range")


def _defect(sub: str, root: str) -> None:
    def sub_in(rel: str, a: str, b: str) -> None:
        p = os.path.join(root, rel)
        s = open(p, encoding="utf-8").read()
        assert a in s, (sub, a)
        open(p, "w", encoding="utf-8", newline="\n").write(s.replace(a, b, 1))
    if sub == "config":
        sub_in(f"{PROJ}/Config/Settings.cs", "false", "true")
    elif sub == "commands":
        sub_in(f"{PROJ}/Commands/BeelzCommands.cs", "{ BarResetService.FullReset(1); }", "{ BarResetService.FullReset(1); BarResetService.RemoveInjectedRows(1); }")
    elif sub == "text":
        sub_in(f"{PROJ}/Commands/AdminCommands.cs", '"LEGACY: rebuild slots."', '"Rebuild slots."')
    elif sub == "auth":
        sub_in(f"{PROJ}/Commands/AdminCommands.cs", '[Command("purge", adminOnly: true,', '[Command("purge",')
    elif sub == "docs":
        sub_in(f"{PROJ}/docs/BACKLOG.md", "- dev-snapshot\n", "")
    elif sub == "audit":
        sub_in(AUDIT, "harness: ok, 1 faults", "harness: FAIL")
    elif sub == "paths":
        _w(root, "Beelzebub/undeclared.txt", "x\n")
    elif sub == "data":
        sub_in(f"{PROJ}/Services/BarResetService.cs", "static class BarResetService {", 'static class BarResetService { const string T = "[Beelz BAR] x";')
        _git(root, "add", "-A")
        _git(root, "commit", "-q", "-m", "defect")
    elif sub == "rollback":
        sub_in(AUDIT, "rollback: git revert --no-edit ", "rollback: git revert --no-edit 0000000")
    elif sub == "session":
        sub_in("session.log", "run=1 scope=PlayerReset target=PerpetualChaos (7) ms=12 steps=ClearSavedBindings:2 survivors=none",
               "run=1 scope=PlayerReset target=PerpetualChaos (7) ms=12 steps=ClearSavedBindings:2 survivors=4")


def _run_sub(sub: str, root: str) -> tuple[bool, str]:
    try:
        if sub == "session":
            return True, check_session(root, os.path.join(root, "session.log"))
        return True, CHECKS[sub](root)
    except CheckFail as e:
        return False, f"{sub}: FAIL {e}"
    except Exception as e:  # a crash on a fixture is a FAIL, never an ok
        return False, f"{sub}: FAIL crashed: {type(e).__name__}: {e}"


def check_selftest(_root: str) -> str:
    cases, bad = 0, []
    tmp = tempfile.mkdtemp(prefix="bar-reset-selftest-")
    try:
        for sub in SUBS:
            empty = os.path.join(tmp, f"{sub}-empty")
            os.makedirs(empty)
            _git(empty, "init", "-q")
            passed, line = _run_sub(sub, empty)
            cases += 1
            if passed or "no input" not in line:
                bad.append(f"{sub} on the empty tree: {line}")

            good = os.path.join(tmp, f"{sub}-good")
            os.makedirs(good)
            _build_good(good)
            passed, line = _run_sub(sub, good)
            cases += 1
            if not passed:
                bad.append(f"{sub} on the good tree: {line}")

            defect = os.path.join(tmp, f"{sub}-defect")
            os.makedirs(defect)
            _build_good(defect)
            _defect(sub, defect)
            passed, line = _run_sub(sub, defect)
            cases += 1
            if passed:
                bad.append(f"{sub} ok on its defect tree")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
        if os.path.isdir(tmp):  # git object files can be read-only on Windows
            for dp, _, fns in os.walk(tmp):
                for fn in fns:
                    os.chmod(os.path.join(dp, fn), 0o666)
            shutil.rmtree(tmp, ignore_errors=True)
    if bad:
        raise CheckFail("; ".join(bad[:6]))
    return f"selftest: ok, {cases} cases"


CHECKS = {
    "config": check_config, "commands": check_commands, "text": check_text, "auth": check_auth,
    "docs": check_docs, "audit": check_audit, "paths": check_paths, "data": check_data,
    "rollback": check_rollback, "selftest": check_selftest,
}
ALL = ["config", "commands", "text", "auth", "docs", "audit", "paths", "data"]


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("sub", choices=sorted(list(CHECKS) + ["session", "all"]))
    ap.add_argument("log", nargs="?", help="session: the copied LogOutput.log")
    ap.add_argument("--root", default=os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
    a = ap.parse_args(argv)
    subs = ALL if a.sub == "all" else [a.sub]
    rc = 0
    for sub in subs:
        try:
            line = check_session(a.root, a.log) if sub == "session" else CHECKS[sub](a.root)
        except CheckFail as e:
            line, rc = f"{sub}: FAIL {e}", 1
        print(line)
    return rc


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
