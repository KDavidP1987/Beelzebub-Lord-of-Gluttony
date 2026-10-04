#!/usr/bin/env python3
"""check_clearbar.py — evidence checks for the clearbar-fullreset dod plan
(Beelzebub/Beelzebub/docs/dod/clearbar-fullreset.md).

Each subcommand prints exactly one summary line, `<name>: ok ...` or `<name>: FAIL <reason>`, and exits 1 on FAIL.
A check that scans nothing FAILs with "no input" — a check that finds nothing never passes.

  handoff   the BCH handoff carries the v0.137.4 contract and its banner equals ApiCommands.ApiVersion (35)
  secrets   no credential-shaped literal in the plugin's C# sources (the only code that writes state, replies, logs)
  rollback  the audit's `Rollback range: <first>..<last>` resolves, first is an ancestor of last, and the audit
            states the revert / rebuild / redeploy commands and the no-migration consequence
  selfonly  `clearbar` and `resetbar` take no player/target parameter and are not adminOnly (caller only)
  wiring    the ClearBar handler calls BarResetService.FullReset(..., BarResetScope.ClearSet, ...) exactly once and
            none of the calls the old handler used to change binds or the bar (business rule 6 of the plan)
  paths     every path changed since the plan's recon commit is declared in the plan's Rollout path list

Usage: python Beelzebub/tools/check_clearbar.py <subcommand> [--root <repo root>]
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys

PROJ = "Beelzebub/Beelzebub"
HANDOFF = f"{PROJ}/docs/BCH_INTEGRATION_HANDOFF.md"
API = f"{PROJ}/Commands/ApiCommands.cs"
AUDIT = f"{PROJ}/docs/audits/clearbar-fullreset.md"
API_VERSION = 35
HANDOFF_TOKENS = ["| 35 | 0.137.4 |", "v0.137.4", "api>=35", "clearbar", "scope=ClearSet",
                  "Your transform was ended to clear the bar."]
SECRET_RE = re.compile(r"(?i)\b(password|passwd|api[_-]?key|secret|bearer|access[_-]?token)\b\s*[:=]\s*\"[^\"]{8,}\"")
CONSEQUENCE = "no saved-data migration"
ROLLBACK_STEPS = ["git revert", "dotnet build Beelzebub/Beelzebub.sln -c Release", "taskkill /PID"]
COMMANDS = f"{PROJ}/Commands/BeelzCommands.cs"
SELF_ONLY = {"ClearBar": "clearbar", "ResetBar": "resetbar"}
TARGET_PARAM = re.compile(r"(?i)^(player|target|name|steam\w*|victim|who)$")
PLAN = f"{PROJ}/docs/dod/clearbar-fullreset.md"
RECON = "265e98c"
OWNER_PATHS = {f"{PROJ}/docs/V0136_ABILITY_TEST_PLAN.xlsx", "_matrix_build.py"}


def read(root: str, rel: str) -> str | None:
    p = os.path.join(root, rel)
    if not os.path.isfile(p):
        return None
    with open(p, encoding="utf-8", errors="replace") as f:
        return f.read()


def cs_files(root: str) -> list[str]:
    out = []
    base = os.path.join(root, PROJ)
    for d, dirs, files in os.walk(base):
        dirs[:] = [x for x in dirs if x not in ("bin", "obj")]
        out += [os.path.relpath(os.path.join(d, f), root).replace("\\", "/") for f in files if f.endswith(".cs")]
    return sorted(out)


def check_handoff(root: str) -> str:
    h, a = read(root, HANDOFF), read(root, API)
    if not h or not a:
        return "handoff: FAIL no input (handoff or ApiCommands.cs missing)"
    m = re.search(r"ApiVersion\s*=\s*(\d+)", a)
    b = re.search(r"ApiVersion\s*=\s*(\d+)", h)
    if not m or not b:
        return "handoff: FAIL no ApiVersion in " + ("ApiCommands.cs" if not m else "the handoff banner")
    api, banner = int(m[1]), int(b[1])
    miss = [t for t in HANDOFF_TOKENS if t not in h]
    if api != API_VERSION or banner != api or miss:
        return f"handoff: FAIL api={api} banner={banner} (want {API_VERSION}) missing={miss}"
    return f"handoff: ok, api {api}, banner {banner}, {len(HANDOFF_TOKENS)} tokens"


def check_secrets(root: str) -> str:
    files = cs_files(root)
    if not files:
        return "secrets: FAIL no input (no .cs files)"
    hits = [f"{rel}:{n}" for rel in files for n, line in enumerate(read(root, rel).splitlines(), 1)
            if SECRET_RE.search(line)]
    if hits:
        return f"secrets: FAIL {len(hits)} credential-shaped literal(s): {hits[:5]}"
    return f"secrets: ok, {len(files)} files, 0 hits"


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
        return f"rollback: FAIL the audit does not state the '{CONSEQUENCE}' consequence"
    return f"rollback: ok, {m[1]}..{m[2]}"


def check_selfonly(root: str) -> str:
    src = read(root, COMMANDS)
    if not src:
        return "selfonly: FAIL no input (BeelzCommands.cs missing)"
    bad, seen = [], 0
    for meth, cmd in SELF_ONLY.items():
        # the description may itself contain brackets (`[all|universal|...]`): match lazily up to the attribute's `)]`
        m = re.search(r'\[Command\("' + re.escape(cmd) + r'"(.*?)\)\]\s*public static void ' + meth + r'\(([^)]*)\)', src, re.S)
        if not m:
            bad.append(f"{cmd}: not found")
            continue
        seen += 1
        if "adminOnly: true" in m[1]:
            bad.append(f"{cmd}: adminOnly")
        params = [x.strip() for x in m[2].split(",")[1:]]
        tp = [x for x in params if x and TARGET_PARAM.search(x.split("=")[0].split()[-1])]
        if tp:
            bad.append(f"{cmd}: target parameter {tp}")
    if bad:
        return f"selfonly: FAIL {bad}"
    return f"selfonly: ok, {seen} self-only commands"


BYPASS = ["ClearGrant", "RestoreResolvedGrants", "ClearAllSlots", "ClearUniversalBucket", "ClearWeaponBucket",
          "ClearFormBucket", "ClearAllLoadouts", "Transforms.Revert"]


def method_body(src: str, name: str) -> str | None:
    """The brace-balanced body of `public static void <name>(` (comments and strings blanked first)."""
    st = re.sub(r'//[^\n]*', '', src)
    st = re.sub(r'"(?:\\.|[^"\\])*"', '""', st)
    m = re.search(r"public static void " + name + r"\(", st)
    if not m:
        return None
    i = st.index("{", m.end())
    depth = 0
    for j in range(i, len(st)):
        depth += {"{": 1, "}": -1}.get(st[j], 0)
        if depth == 0:
            return st[i:j + 1]
    return None


def check_wiring(root: str) -> str:
    src = read(root, COMMANDS)
    if not src:
        return "wiring: FAIL no input (BeelzCommands.cs missing)"
    body = method_body(src, "ClearBar")
    if body is None:
        return "wiring: FAIL no input (no ClearBar handler)"
    bypass = [b for b in BYPASS if re.search(r"\b" + re.escape(b) + r"\s*\(", body)]
    calls = re.findall(r"\bFullReset\s*\(([^;]*)\)\s*;", body)
    bad = []
    if bypass:
        bad.append(f"bypass call(s) {bypass}")
    if len(calls) != 1:
        bad.append(f"{len(calls)} FullReset call(s), want 1")
    elif "BarResetScope.ClearSet" not in calls[0]:
        bad.append("FullReset is not called with BarResetScope.ClearSet")
    if bad:
        return "wiring: FAIL " + "; ".join(bad)
    return "wiring: ok, ClearBar calls FullReset(ClearSet) once, 0 bypass calls"


def check_paths(root: str) -> str:
    plan = read(root, PLAN)
    if not plan:
        return "paths: FAIL no input (plan missing)"
    sec = re.search(r"Paths this change ships, writes or regenerates.*?(?=\n- [A-Z]|\n## )", plan, re.S)
    if not sec:
        return "paths: FAIL no Rollout path list"
    text = sec[0].replace("\n", " ")
    declared = set()
    for tok in re.findall(r"`([^`]+)`", text):
        m = re.match(r"(.*)\{([^}]*)\}(.*)", tok)
        names = [m[1] + n.strip() + m[3] for n in m[2].split(",")] if m else [tok]
        declared |= {n.replace(" ", "") for n in names}
    def is_declared(path: str) -> bool:
        return any(path == d or path.endswith("/" + d) for d in declared)
    out = subprocess.run(["git", "-C", root, "diff", "--name-only", RECON], capture_output=True, text=True).stdout.split()
    out += subprocess.run(["git", "-C", root, "ls-files", "--others", "--exclude-standard"], capture_output=True, text=True).stdout.split()
    changed = sorted({p for p in out if p not in OWNER_PATHS})
    if not changed:
        return "paths: FAIL no input (nothing changed since " + RECON + ")"
    undeclared = [p for p in changed if not is_declared(p)]
    if undeclared:
        return f"paths: FAIL {len(undeclared)} changed path(s) not declared: {undeclared[:8]}"
    return f"paths: ok, {len(changed)} changed, {len(declared)} declared"


CHECKS = {"wiring": check_wiring, "selfonly": check_selfonly, "paths": check_paths, "handoff": check_handoff, "secrets": check_secrets, "rollback": check_rollback}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("check", choices=sorted(CHECKS))
    ap.add_argument("--root", default=".")
    a = ap.parse_args()
    line = CHECKS[a.check](a.root)
    print(line)
    return 1 if ": FAIL" in line else 0


if __name__ == "__main__":
    sys.exit(main())
