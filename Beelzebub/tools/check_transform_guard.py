#!/usr/bin/env python3
"""check_transform_guard.py — evidence checks for the transform-chain-guard dod plan
(Beelzebub/Beelzebub/docs/dod/transform-chain-guard.md).

Each subcommand prints exactly one summary line, `<name>: ok ...` or `<name>: FAIL <reason>`, and exits 1 on FAIL.
A check that scans nothing FAILs with "no input" — a check that finds nothing never passes.

  wiring    TryActivate, ApplyPhase, ReapplyActiveTransform and ApplyNativeFormTest ask the gate
            (TransformGate.Decide, or PhaseGate for the phase routes) before any state-changing call;
            ApplyNativeFormTest never calls Revert; the `phase` command asks PhaseGate before ApplyPhase
  actors    transform / phase / revert stay self-only, testform / force-transform stay adminOnly, and no file outside
            the known four calls TryActivate / ApplyNativeFormTest / ApplyPhase
  secrets   no credential-shaped literal in the plugin's .cs / .json / .toml sources
  tests     every named TransformGate control (D1-D3) exists as a [Fact]/[Theory] method
  handoff   ApiVersion stays 35, the banner equals it, and the handoff carries the v0.137.5 note
  rollback  the audit's `Rollback range: <first>..<last>` resolves, first is an ancestor of last, and the audit
            states the revert / rebuild / redeploy commands and the no-migration consequence
  paths     every path changed since the plan's recon commit is declared in the plan's Rollout path list
  backlog   docs/BACKLOG.md's `transform-chain-guard` row says `DONE v0.137.5`
  profile   docs/dod/profile.md carries the Probe 6.2 note (how a collaborator reports failure; deferred effects)
  selftest  runs every check above on temp fixtures: a good fixture must pass, a defect fixture must FAIL, and an
            empty tree must FAIL with "no input" (paths/rollback on a throwaway git repo)

Usage: python Beelzebub/tools/check_transform_guard.py <subcommand> [--root <repo root>]
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
import tempfile

PROJ = "Beelzebub/Beelzebub"
SERVICE = f"{PROJ}/Services/TransformService.cs"
TCMDS = f"{PROJ}/Commands/TransformCommands.cs"
ACMDS = f"{PROJ}/Commands/AdminCommands.cs"
HANDOFF = f"{PROJ}/docs/BCH_INTEGRATION_HANDOFF.md"
API = f"{PROJ}/Commands/ApiCommands.cs"
AUDIT = f"{PROJ}/docs/audits/transform-chain-guard.md"
PLAN = f"{PROJ}/docs/dod/transform-chain-guard.md"
BACKLOG = f"{PROJ}/docs/BACKLOG.md"
PROFILE = f"{PROJ}/docs/dod/profile.md"
TESTS = "Beelzebub/Beelzebub.Tests/TransformGateTests.cs"
RECON = "a38c168"
OWNER_PATHS = {f"{PROJ}/docs/V0136_ABILITY_TEST_PLAN.xlsx", "_matrix_build.py"}
API_VERSION = 35
HANDOFF_TOKENS = ["v0.137.5", "transform-phase-shift", "Still transforming", "no wire change"]
SECRET_RE = re.compile(r"(?i)\b(password|passwd|api[_-]?key|secret|bearer|access[_-]?token)\b[\"']?\s*[:=]\s*\"[^\"]{8,}\"")
CONSEQUENCE = "no saved-data migration"
ROLLBACK_STEPS = ["git revert", "dotnet build Beelzebub/Beelzebub.sln -c Release", "taskkill /PID"]
SOURCE_EXT = (".cs", ".json", ".toml")
TARGET_PARAM = re.compile(r"(?i)^(player|target|name|steam\w*|victim|who)$")

# route method -> the gate tokens it may use
ROUTES = {
    "TryActivate": [r"TransformGate\.Decide\s*\("],
    "ApplyPhase": [r"TransformGate\.Decide\s*\(", r"\bPhaseGate\s*\("],
    "ReapplyActiveTransform": [r"TransformGate\.Decide\s*\(", r"\bPhaseGate\s*\("],
    "ApplyNativeFormTest": [r"TransformGate\.Decide\s*\("],
}
# any of these before the gate changes the bar, the form or the transform record
STATE = [r"\bApplyForm\s*\(", r"TransformBuffService\.Apply\s*\(", r"\bReapplyFormAbilitiesInPlace\s*\(",
         r"TransformBuffService\.Reapply\s*\(", r"\bSetActiveTransform\s*\(", r"\bCurrentPhase\s*=(?!=)",
         r"\bRevert\s*\(", r"\bApplyPhase\s*\(", r"\bSendEvent\s*\("]
SELF_ONLY = {"Transform": "transform", "Phase": "phase", "Revert": "revert"}
ADMIN_ONLY = {"TestForm": "testform", "ForceTransform": "force-transform"}
ROUTE_CALL = re.compile(r"\.(TryActivate|ApplyNativeFormTest|ApplyPhase)\s*\(")
ALLOWED_CALLERS = {SERVICE, TCMDS, ACMDS, f"{PROJ}/Patches/UpdateBuffsBufferDestroyPatch.cs"}

REQUIRED_TESTS = [
    "Decide_fails_when_a_pending_form_allows_any_route",
    "Decide_fails_when_an_active_transform_allows_a_second_activate",
    "Decide_fails_when_the_same_unit_reads_as_another_unit",
    "Decide_fails_when_a_free_player_is_refused",
    "Decide_fails_when_formtest_chains_over_an_active_transform",
    "Decide_fails_when_an_unknown_route_is_allowed",
    "Message_fails_when_a_refusal_text_drifts",
    "Message_fails_when_a_text_exceeds_the_chat_cap",
    "LogLine_fails_when_route_or_reason_is_missing",
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
    """The brace-balanced body of the method declared as `<modifiers> <type> <name>(...)` in stripped source."""
    m = re.search(r"\b(?:public|internal|private|protected)\b[^;{}=]*?\b" + name + r"\s*\([^)]*\)\s*\{", st)
    if not m:
        return None
    i = m.end() - 1
    depth = 0
    for j in range(i, len(st)):
        depth += {"{": 1, "}": -1}.get(st[j], 0)
        if depth == 0:
            return st[i:j + 1]
    return None


def first(body: str, pats: list[str]) -> int | None:
    hits = [m.start() for p in pats for m in [re.search(p, body)] if m]
    return min(hits) if hits else None


def check_wiring(root: str) -> str:
    svc, cmd = read(root, SERVICE), read(root, TCMDS)
    if svc is None or cmd is None:
        return "wiring: FAIL no input (TransformService.cs or TransformCommands.cs missing)"
    svc, cmd = strip(svc), strip(cmd)
    bad = []
    for meth, gates in ROUTES.items():
        body = method_body(svc, meth)
        if body is None:
            bad.append(f"{meth}: not found")
            continue
        g, s = first(body, gates), first(body, STATE)
        if g is None:
            bad.append(f"{meth}: no gate call")
        elif s is not None and s < g:
            bad.append(f"{meth}: state change before the gate ({body[s:s + 30].split(chr(10))[0].strip()})")
    nf = method_body(svc, "ApplyNativeFormTest")
    if nf is not None and re.search(r"\bRevert\s*\(", nf):
        bad.append("ApplyNativeFormTest: calls Revert")
    ph = method_body(cmd, "Phase")
    if ph is None:
        bad.append("Phase command: not found")
    else:
        g, a = first(ph, [r"\bPhaseGate\s*\("]), first(ph, [r"\bApplyPhase\s*\("])
        if g is None or (a is not None and a < g):
            bad.append("Phase command: ApplyPhase without or before PhaseGate")
    if bad:
        return "wiring: FAIL " + "; ".join(bad)
    return "wiring: ok, 4 routes gated, testform never reverts, phase command gated"


def _attr(src: str, meth: str, cmd: str):
    return re.search(r'\[Command\("' + re.escape(cmd) + r'"(.*?)\)\]\s*public static void ' + meth + r'\(([^)]*)\)', src, re.S)


def plugin_files(root: str) -> list[str]:
    out = []
    for d, dirs, files in os.walk(os.path.join(root, PROJ)):
        dirs[:] = [x for x in dirs if x not in ("bin", "obj")]
        out += [os.path.relpath(os.path.join(d, f), root).replace("\\", "/") for f in files if f.endswith(SOURCE_EXT)]
    return sorted(out)


def check_actors(root: str) -> str:
    tc, ac = read(root, TCMDS), read(root, ACMDS)
    files = [f for f in plugin_files(root) if f.endswith(".cs")]
    if tc is None or ac is None or not files:
        return "actors: FAIL no input (TransformCommands.cs / AdminCommands.cs / .cs files missing)"
    bad = []
    for meth, cmd in SELF_ONLY.items():
        m = _attr(tc, meth, cmd)
        if not m:
            bad.append(f"{cmd}: not found"); continue
        if "adminOnly: true" in m[1]:
            bad.append(f"{cmd}: adminOnly")
        params = [x.strip() for x in m[2].split(",")[1:]]
        tp = [x for x in params if x and TARGET_PARAM.search(x.split("=")[0].split()[-1])]
        if tp:
            bad.append(f"{cmd}: target parameter {tp}")
    for meth, cmd in ADMIN_ONLY.items():
        m = _attr(ac, meth, cmd)
        if not m:
            bad.append(f"{cmd}: not found")
        elif "adminOnly: true" not in m[1]:
            bad.append(f"{cmd}: not adminOnly")
    calls = 0
    for rel in files:
        for m in ROUTE_CALL.finditer(strip(read(root, rel))):
            calls += 1
            if rel not in ALLOWED_CALLERS:
                bad.append(f"{rel}: calls {m[1]}")
    if bad:
        return f"actors: FAIL {bad[:6]}"
    return f"actors: ok, {len(SELF_ONLY)} self-only, {len(ADMIN_ONLY)} admin-only, {calls} route callers in {len(ALLOWED_CALLERS)} allowed files"


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
    return f"tests: ok, {len(REQUIRED_TESTS)} named TransformGate controls present"


def check_handoff(root: str) -> str:
    h, a = read(root, HANDOFF), read(root, API)
    if not h or not a:
        return "handoff: FAIL no input (handoff or ApiCommands.cs missing)"
    m, b = re.search(r"ApiVersion\s*=\s*(\d+)", a), re.search(r"ApiVersion\s*=\s*(\d+)", h)
    if not m or not b:
        return "handoff: FAIL no ApiVersion in " + ("ApiCommands.cs" if not m else "the handoff banner")
    api, banner = int(m[1]), int(b[1])
    miss = [t for t in HANDOFF_TOKENS if t not in h]
    if api != API_VERSION or banner != api or miss:
        return f"handoff: FAIL api={api} banner={banner} (want {API_VERSION}) missing={miss}"
    return f"handoff: ok, api {api}, banner {banner}, {len(HANDOFF_TOKENS)} tokens"


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
    row = next((l for l in t.splitlines() if l.startswith("|") and "transform-chain-guard" in l.split("|")[1]), None)
    if row is None:
        return "backlog: FAIL no transform-chain-guard row"
    if "DONE v0.137.5" not in row:
        return "backlog: FAIL the transform-chain-guard row is not marked `DONE v0.137.5`"
    return "backlog: ok, transform-chain-guard DONE v0.137.5"


def check_profile(root: str) -> str:
    t = read(root, PROFILE)
    if t is None:
        return f"profile: FAIL no input ({PROFILE} missing)"
    line = next((l for l in t.splitlines() if l.lstrip("- ").startswith("Probe 6.2")), None)
    if line is None:
        return "profile: FAIL no `Probe 6.2` line"
    miss = [w for w in ("reports failure", "deferred") if w not in line]
    if miss:
        return f"profile: FAIL the Probe 6.2 line lacks {miss}"
    return "profile: ok, probe 6.2 note present"


CHECKS = {"wiring": check_wiring, "actors": check_actors, "secrets": check_secrets, "tests": check_tests,
          "handoff": check_handoff, "backlog": check_backlog, "profile": check_profile}

# ---- selftest fixtures ----

GOOD_SVC = '''
public class TransformService {
    public (bool ok, string message) TryActivate(ulong steamId, int unit)
    {
        // ApplyForm( in a comment is not a call
        var v = TransformGate.Decide(TransformRoute.Activate, pending, active, unit);
        if (v != TransformGateVerdict.Allow) return (false, TransformGate.Message(v, TransformRoute.Activate, "x"));
        TransformBuffService.ApplyForm(c, 1, set, null);
        Core.AbilityRegistry.SetActiveTransform(steamId, a);
        return (true, "ok");
    }
    public bool ApplyPhase(ulong steamId, ActiveTransform active, Entity character, int phase)
    {
        if (PhaseGate(steamId, active) != TransformGateVerdict.Allow) return false;
        active.CurrentPhase = phase;
        return TransformBuffService.ApplyForm(character, 1, set, null);
    }
    public bool ReapplyActiveTransform(ulong steamId, ActiveTransform active, Entity character)
    {
        if (TransformGate.Decide(TransformRoute.Reapply, p, a, a) != TransformGateVerdict.Allow) return false;
        return ApplyPhase(steamId, active, character, active.CurrentPhase);
    }
    public (bool ok, string message) ApplyNativeFormTest(ulong steamId, Entity character, int form, int[] set, string label)
    {
        var v = TransformGate.Decide(TransformRoute.FormTest, p, a, form);
        if (v != TransformGateVerdict.Allow) return (false, "Revert( in a string");
        TransformBuffService.ApplyForm(character, form, set, null);
        return (true, "ok");
    }
}
'''
GOOD_TCMDS = '''
[Command("transform", description: "Activate. Usage: .beelz transform <index|unitName>.")]
public static void Transform(ChatCommandContext ctx, string unitOrIndex)
{
    var (ok, message) = Core.Transforms.TryActivate(steamId, unitGuid);
}
[Command("phase", description: "Switch phase [n].")]
public static void Phase(ChatCommandContext ctx, int n = -1)
{
    if (Core.Transforms.PhaseGate(steamId, active) != TransformGateVerdict.Allow) { return; }
    bool appliedNow = Core.Transforms.ApplyPhase(steamId, active, character, n);
}
[Command("revert", description: "Revert.")]
public static void Revert(ChatCommandContext ctx)
{
}
'''
GOOD_ACMDS = '''
[Command("testform", description: "Test [wolf|bear|off].", adminOnly: true)]
public static void TestForm(ChatCommandContext ctx, string form)
{
    var (ok, message) = Core.Transforms.ApplyNativeFormTest(steamId, character, g, set, "Wolf");
}
[Command("force-transform", description: "Force.", adminOnly: true)]
public static void ForceTransform(ChatCommandContext ctx, string player, int unitGuid)
{
}
'''


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
    code = {SERVICE: GOOD_SVC, TCMDS: GOOD_TCMDS, ACMDS: GOOD_ACMDS}
    tests_good = "".join(f"    [Fact]\n    public void {n}() {{ }}\n" for n in REQUIRED_TESTS)
    handoff_good = "ApiVersion = 35\n" + "\n".join(HANDOFF_TOKENS)
    return {
        # defect: ApplyPhase writes CurrentPhase before asking the gate (D5)
        "wiring": (code, dict(code, **{SERVICE: GOOD_SVC.replace(
            "        if (PhaseGate(steamId, active) != TransformGateVerdict.Allow) return false;\n        active.CurrentPhase = phase;\n",
            "        active.CurrentPhase = phase;\n        if (PhaseGate(steamId, active) != TransformGateVerdict.Allow) return false;\n")})),
        # defect: a timer service calls ApplyPhase from a file outside the allowed callers
        "actors": (code, dict(code, **{f"{PROJ}/Services/Timer.cs": "class T { void Go() { Core.Transforms.ApplyPhase(s, a, c, 2); } }"})),
        "secrets": ({f"{PROJ}/A.cs": 'var name = "hello world";'}, {f"{PROJ}/c.json": '{ "api_key": "abcdefgh12345678" }'}),
        "tests": ({TESTS: tests_good}, {TESTS: tests_good.replace(REQUIRED_TESTS[0] + "(", "Renamed(")}),
        "handoff": ({HANDOFF: handoff_good, API: "public const int ApiVersion = 35;"},
                    {HANDOFF: handoff_good.replace("Still transforming", ""), API: "public const int ApiVersion = 35;"}),
        "backlog": ({BACKLOG: "| `transform-chain-guard` | DONE v0.137.5 (transform-chain-guard): ... | x |\n"},
                    {BACKLOG: "| `transform-chain-guard` | A recurrence guard in TryActivate | x |\n"}),
        "profile": ({PROFILE: "- Probe 6.2: for each collaborator, how it reports failure; how a deferred effect is seen.\n"},
                    {PROFILE: "- Probe 6.1: layered slots.\n"}),
    }


def selftest(_root: str) -> str:
    global RECON
    bad = []
    def expect(name: str, line: str, want: str) -> None:
        ok = {"ok": ": ok" in line, "FAIL": ": FAIL" in line and "no input" not in line,
              "no input": ": FAIL no input" in line}[want]
        if not ok:
            bad.append(f"{name} want {want}, got: {line}")
    for name, (good, defect) in _fixtures().items():
        for files, want in ((good, "ok"), (defect, "FAIL"), ({}, "no input")):
            with tempfile.TemporaryDirectory() as t:
                _write(t, files)
                expect(name, CHECKS[name](t), want)
    with tempfile.TemporaryDirectory() as t:
        expect("rollback", check_rollback(t), "no input")
        _git(t, "init", "-q")
        _write(t, {"f.txt": "1"}); _git(t, "add", "-A"); _git(t, "commit", "-qm", "a")
        a = _git(t, "rev-parse", "--short", "HEAD")
        _write(t, {"f.txt": "2"}); _git(t, "add", "-A"); _git(t, "commit", "-qm", "b")
        b = _git(t, "rev-parse", "--short", "HEAD")
        body = " ".join(ROLLBACK_STEPS) + " " + CONSEQUENCE
        _write(t, {AUDIT: f"Rollback range: `{a}..{b}`\n{body}\n"})
        expect("rollback", check_rollback(t), "ok")
        _write(t, {AUDIT: f"Rollback range: `{b}..{a}`\n{body}\n"})
        expect("rollback", check_rollback(t), "FAIL")
        saved = RECON
        try:
            RECON = a
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
    return f"selftest: ok, {len(CHECKS) + 2} checks x good/defect/empty"


CHECKS_ALL = dict(CHECKS, rollback=check_rollback, paths=check_paths, selftest=selftest)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("check", choices=sorted(CHECKS_ALL))
    ap.add_argument("--root", default=".")
    a = ap.parse_args()
    line = CHECKS_ALL[a.check](a.root)
    print(line)
    return 1 if ": FAIL" in line else 0


if __name__ == "__main__":
    sys.exit(main())
