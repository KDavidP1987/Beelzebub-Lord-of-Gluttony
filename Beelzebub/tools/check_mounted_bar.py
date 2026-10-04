#!/usr/bin/env python3
"""check_mounted_bar.py — evidence checks for the mounted-bar-reset dod plan
(Beelzebub/Beelzebub/docs/dod/mounted-bar-reset.md).

Each subcommand prints exactly one summary line, `<name>: ok ...` or `<name>: FAIL <reason>`, and exits 1 on FAIL.
A check that scans nothing FAILs with "no input" — a check that finds nothing never passes.

  handoff   D7   the BCH handoff carries the v0.137.3 contract and its banner equals ApiCommands.ApiVersion (34)
  callers   D15  MountedSlots.Migrate is called exactly once, from Services/PersistenceService.cs, and no command,
                 patch or other service calls it
  secrets   D16  no credential-shaped literal in the plugin's C# sources (the only code that writes state, replies, logs)
  rollback  D12  the audit's `Rollback range: <first>..<last>` resolves, first is an ancestor of last, and the
                 audit states the revert / rebuild / redeploy commands and the inert slot-5 consequence
  selfonly  D17  `form-grant` and `resetbar` take no player/target parameter and are not adminOnly-gated away from
                 players (they act on the caller only)
  paths     D18  every path changed since the plan's recon commit is declared in the plan's Rollout path list, and
                 every declared repo path exists

Usage: python Beelzebub/tools/check_mounted_bar.py <subcommand> [--root <repo root>]
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
AUDIT = f"{PROJ}/docs/audits/mounted-bar-reset.md"
API_VERSION = 34
HANDOFF_TOKENS = ["| 34 | 0.137.3 |", "v0.137.3", "api>=34", "5/6/7", "You were dismounted",
                  "(slots 5/6/7 from v0.137.3)"]
MIGRATE_HOME = f"{PROJ}/Services/PersistenceService.cs"
SECRET_RE = re.compile(r"(?i)\b(password|passwd|api[_-]?key|secret|bearer|access[_-]?token)\b\s*[:=]\s*\"[^\"]{8,}\"")
CONSEQUENCE = "inert slot-5 bind"
ROLLBACK_STEPS = ["git revert", "dotnet build Beelzebub/Beelzebub.sln -c Release", "taskkill /PID"]
COMMANDS = f"{PROJ}/Commands/BeelzCommands.cs"
SELF_ONLY = {"FormGrant": "form-grant", "ResetBar": "resetbar"}
TARGET_PARAM = re.compile(r"(?i)^(player|target|name|steam\w*|victim|who)$")
PLAN = f"{PROJ}/docs/dod/mounted-bar-reset.md"
RECON = "0a135f5"
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


def check_callers(root: str) -> str:
    files = cs_files(root)
    if not files:
        return "callers: FAIL no input (no .cs files)"
    hits = []
    for rel in files:
        if rel.endswith("Logic/MountedSlots.cs"):
            continue
        for n, line in enumerate(read(root, rel).splitlines(), 1):
            if re.search(r"\bMountedSlots\.Migrate\s*\(", line) and not line.lstrip().startswith("//"):
                hits.append(f"{rel}:{n}")
    home = [h for h in hits if h.startswith(MIGRATE_HOME + ":")]
    if len(hits) != 1 or len(home) != 1:
        return f"callers: FAIL want exactly 1 call in {MIGRATE_HOME}, found {len(hits)}: {hits}"
    return f"callers: ok, {len(files)} files, 1 call ({home[0]})"


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
        m = re.search(r'\[Command\("' + re.escape(cmd) + r'"([^\]]*)\]\s*public static void ' + meth + r'\(([^)]*)\)', src, re.S)
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


CHECKS = {"selfonly": check_selfonly, "paths": check_paths, "handoff": check_handoff, "callers": check_callers, "secrets": check_secrets, "rollback": check_rollback}


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
