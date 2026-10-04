#!/usr/bin/env python3
"""check_clearbar.py — evidence checks for the clearbar-fullreset dod plan
(Beelzebub/Beelzebub/docs/dod/clearbar-fullreset.md).

Each subcommand prints exactly one summary line, `<name>: ok ...` or `<name>: FAIL <reason>`, and exits 1 on FAIL.
A check that scans nothing FAILs with "no input" — a check that finds nothing never passes.

  handoff   the BCH handoff carries the v0.137.4 contract and its banner equals ApiCommands.ApiVersion (35)
  secrets   no credential-shaped literal in the plugin's .cs / .json / .toml sources (code, embedded data, manifest)
  rollback  the audit's `Rollback range: <first>..<last>` resolves, first is an ancestor of last, and the audit
            states the revert / rebuild / redeploy commands and the no-migration consequence
  selfonly  `clearbar` and `resetbar` take no player/target parameter and are not adminOnly (caller only)
  wiring    the ClearBar handler calls BarResetService.FullReset(..., BarResetScope.ClearSet, ...) exactly once and
            none of the calls the old handler used to change binds or the bar (business rule 6 of the plan), and
            refuses a sender whose SteamID resolves to 0 before it
  tests     every named ClearSet control (D1-D5) exists as a [Fact]/[Theory] method
  entry     nothing in the plugin dispatches commands besides VCF's chat hook (no CommandRegistry.Handle, no direct
            ClearBar/ResetBar call, no RCON/console command)
  paths     every path changed since the plan's recon commit is declared in the plan's Rollout path list
  backlog   docs/BACKLOG.md's `clearbar-fullreset` row says `DONE v0.137.4`
  selftest  runs every check above on temp fixtures: a good fixture must pass, a defect fixture must FAIL, and an
            empty tree must FAIL with "no input" (paths/rollback use git and are planted against the live repo)

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
SECRET_RE = re.compile(r"(?i)\b(password|passwd|api[_-]?key|secret|bearer|access[_-]?token)\b[\"']?\s*[:=]\s*\"[^\"]{8,}\"")
CONSEQUENCE = "no saved-data migration"
ROLLBACK_STEPS = ["git revert", "dotnet build Beelzebub/Beelzebub.sln -c Release", "taskkill /PID"]
COMMANDS = f"{PROJ}/Commands/BeelzCommands.cs"
SERVICE = f"{PROJ}/Services/BarResetService.cs"
GOOD_SERVICE = "    public int RestoreKept() => SlotApply.RestoreResolvedGrantsOrThrow(RequireEquipBuff());\n"
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


SOURCE_EXT = (".cs", ".json", ".toml")
BACKLOG = f"{PROJ}/docs/BACKLOG.md"


def cs_files(root: str) -> list[str]:
    out = []
    base = os.path.join(root, PROJ)
    for d, dirs, files in os.walk(base):
        dirs[:] = [x for x in dirs if x not in ("bin", "obj")]
        out += [os.path.relpath(os.path.join(d, f), root).replace("\\", "/") for f in files if f.endswith(SOURCE_EXT)]
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
        return "secrets: FAIL no input (no .cs/.json/.toml files)"
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
    # the refusal block (`{ ctx.Reply(""); return; }` once strings are blanked) must return, and come before FullReset
    refusal = re.search(r"if\s*\(\s*steamId\s*==\s*0\s*\)\s*(?:\{[^}]*\breturn\s*;[^}]*\}|return\s*;)", body)
    full = re.search(r"\bFullReset\s*\(", body)
    if not refusal or (full and refusal.start() > full.start()):
        bad.append("no steamId == 0 refusal before the reset")
    # A3: the RestoreKept step must use the throwing restore, never the one that swallows a failure as 0
    svc = read(root, SERVICE)
    if svc is None:
        return "wiring: FAIL no input (BarResetService.cs missing)"
    rk = re.search(r"\bint\s+RestoreKept\s*\(\s*\)\s*(=>[^;]*;|\{.*?\n\s*\})", svc, re.S)
    if not rk or "RestoreResolvedGrantsOrThrow(" not in rk[1] or re.search(r"\bRestoreResolvedGrants\s*\(", rk[1]):
        bad.append("RestoreKept does not call RestoreResolvedGrantsOrThrow")
    if bad:
        return "wiring: FAIL " + "; ".join(bad)
    return "wiring: ok, ClearBar calls FullReset(ClearSet) once, 0 bypass calls, RestoreKept throws on failure"


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


def check_backlog(root: str) -> str:
    t = read(root, BACKLOG)
    if t is None:
        return f"backlog: FAIL no input ({BACKLOG} missing)"
    row = next((l for l in t.splitlines() if l.startswith("|") and "clearbar-fullreset" in l.split("|")[1]), None)
    if row is None:
        return "backlog: FAIL no clearbar-fullreset row"
    if "DONE v0.137.4" not in row:
        return "backlog: FAIL the clearbar-fullreset row is not marked `DONE v0.137.4`"
    return "backlog: ok, clearbar-fullreset DONE v0.137.4"


TESTS_DIR = "Beelzebub/Beelzebub.Tests"
# D-item -> (test file, the named controls that must exist as [Fact]/[Theory] methods). A filtered `dotnet test` passes
# when these are deleted; this check does not.
REQUIRED_TESTS = {
    "D1": ("BarResetTests.cs", ["ClearSet_plan_fails_when_RestoreKept_is_missing_or_not_right_after_Reapply",
                                "Plan_fails_when_RestoreKept_leaks_into_another_scope",
                                "ClearSet_plan_fails_when_it_clears_hotkeys_or_an_offline_plan_has_a_live_step"]),
    "D2": ("BarSetTests.cs", ["Keeps_fails_when_all_keeps_any_origin",
                              "Keeps_fails_when_a_set_keeps_its_own_origin_or_drops_another",
                              "Keeps_fails_when_none_is_kept_or_the_comparison_is_case_sensitive",
                              "Keeps_fails_when_an_unknown_origin_is_kept", "Label_fails_when_a_label_differs"]),
    "D3": ("BarResetTests.cs", ["ClearSet_fails_when_a_kept_universal_bind_makes_clearbar_sword_unclean",
                                "ClearSet_fails_when_a_bind_of_the_cleared_set_reads_as_kept",
                                "ClearSet_fails_when_a_kept_slot_with_an_other_mod_reads_clean",
                                "ClearSet_fails_when_a_run_without_RestoreKept_or_with_a_thrown_RestoreKept_reads_clean",
                                "ClearSet_fails_when_clearbar_all_keeps_a_bind_of_any_set",
                                "Run_fails_when_a_reset_without_a_clear_set_keeps_any_bind"]),
    "D4": ("BarResetReplyTests.cs", ["ForClear_fails_when_a_clean_clear_has_a_second_line_or_another_headline",
                                     "ForClear_fails_when_a_suffix_appears_without_its_step",
                                     "ForClear_fails_when_a_failed_step_or_a_leftover_is_not_named",
                                     "ForClear_fails_when_a_line_exceeds_480_bytes"]),
    "D5": ("BarResetLogTests.cs", ["Format_fails_when_a_ClearSet_line_lacks_set_or_another_scope_gains_it"]),
}


def check_tests(root: str) -> str:
    missing, seen, files = [], 0, 0
    for d, (fname, names) in REQUIRED_TESTS.items():
        src = read(root, f"{TESTS_DIR}/{fname}")
        if src is None:
            continue
        files += 1
        for n in names:
            if re.search(r"\[(Fact|Theory)\][^{;]*?public void " + re.escape(n) + r"\(", src, re.S):
                seen += 1
            else:
                missing.append(f"{d} {n}")
    if files == 0:
        return "tests: FAIL no input (no test files)"
    want = sum(len(v[1]) for v in REQUIRED_TESTS.values())
    if missing or seen != want:
        return f"tests: FAIL {want - seen} required control(s) missing: {missing[:4]}"
    return f"tests: ok, {seen} named ClearSet controls present"


# the only way a player command runs is VCF's ChatMessageSystem prefix (a chat event from a connected User entity);
# any of these in the plugin would open a second entry channel for clearbar / resetbar
ENTRY_BYPASS = [r"\bCommandRegistry\.Handle\s*\(", r"\b(?:BeelzCommands\.)?(?:ClearBar|ResetBar)\s*\(\s*(?!ChatCommandContext)",
                r"(?i)\brcon\w*", r"\bConsoleCommand\w*"]


def check_entry(root: str) -> str:
    files = [f for f in cs_files(root) if f.endswith(".cs")]
    if not files:
        return "entry: FAIL no input (no .cs files)"
    hits = []
    for rel in files:
        st = re.sub(r"//[^\n]*", "", read(root, rel))
        st = re.sub(r'"(?:\\.|[^"\\])*"', '""', st)
        for pat in ENTRY_BYPASS:
            for m in re.finditer(pat, st):
                line = st[:m.start()].rsplit("\n", 1)[-1]
                if "public static void" in line:   # the handler's own declaration
                    continue
                hits.append(f"{rel}:{st.count(chr(10), 0, m.start()) + 1} {m.group(0).strip()}")
    if hits:
        return f"entry: FAIL second entry channel(s): {hits[:5]}"
    return f"entry: ok, {len(files)} files, commands reach clearbar/resetbar only through VCF chat"


def check_actors(root: str) -> str:
    """2.1 / 10.1 in one command: the entry channel (entry), the self-only commands (selfonly) and every FullReset /
    ReadBar caller (check_bar_reset.py auth) — fails when any one of them fails."""
    parts = [check_entry(root), check_selfonly(root)]
    tool = os.path.join(os.path.dirname(os.path.abspath(__file__)), "check_bar_reset.py")
    r = subprocess.run([sys.executable, tool, "auth", "--root", root], capture_output=True, text=True)
    parts.append((r.stdout.strip().splitlines() or ["auth: FAIL no output"])[-1])
    bad = [p for p in parts if ": FAIL" in p]
    if any("no input" in p for p in bad) and len(bad) == len(parts):
        return "actors: FAIL no input (" + "; ".join(bad) + ")"
    if bad:
        return "actors: FAIL " + " | ".join(bad)
    return "actors: ok - " + " | ".join(parts)


# the scratch copies the plan's plants make; each must be deleted after its plant
SCRATCH = ["authplant", "BarSet.bak", "cc_plant.py", "plant_ref.py"]


def check_scratch(_root: str) -> str:
    tmp = os.environ.get("TEMP") or os.environ.get("TMPDIR") or "/tmp"
    if not os.path.isdir(tmp):
        return f"scratch: FAIL no input (temp folder {tmp} missing)"
    left = [n for n in SCRATCH if os.path.exists(os.path.join(tmp, n))]
    if left:
        return f"scratch: FAIL undeleted scratch artifact(s) in the temp folder: {left}"
    return f"scratch: ok, {len(SCRATCH)} scratch names absent from the temp folder"


CHECKS = {"tests": check_tests, "entry": check_entry, "wiring": check_wiring, "selfonly": check_selfonly, "paths": check_paths, "handoff": check_handoff,
          "secrets": check_secrets, "rollback": check_rollback, "backlog": check_backlog}


# ---- selftest: every check against a good, a defect and an empty fixture ----

GOOD_CMDS = '''
[Command("clearbar", description: "Clear [all|universal|<weapon>|<form>]")]
public static void ClearBar(ChatCommandContext ctx, string set = "all")
{
    if (steamId == 0) { ctx.Reply("Could not resolve your Steam ID."); return; }
    // ClearAllSlots( is only mentioned in this comment
    var r = BarResetService.FullReset(ctx.Event.SenderCharacterEntity, BarResetScope.ClearSet, "clearbar", parsed);
    ctx.Reply("ClearGrant( inside a string");
}
[Command("resetbar", description: "Reset your bar")]
public static void ResetBar(ChatCommandContext ctx, bool confirm = false)
{
    BarResetService.FullReset(ctx.Event.SenderCharacterEntity, BarResetScope.PlayerReset, "resetbar");
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
    """check -> (good files, defect files). Each file map is written into its own empty temp tree."""
    handoff_good = "ApiVersion = 35\n" + "\n".join(HANDOFF_TOKENS)
    tests_good: dict[str, str] = {}
    for fname, names in REQUIRED_TESTS.values():
        rel = f"{TESTS_DIR}/{fname}"
        tests_good[rel] = tests_good.get(rel, "") + "".join(f"    [Fact]\n    public void {n}() {{ }}\n" for n in names)
    tests_bad = dict(tests_good)
    first = REQUIRED_TESTS["D3"][1][0]
    tests_bad[f"{TESTS_DIR}/BarResetTests.cs"] = tests_bad[f"{TESTS_DIR}/BarResetTests.cs"].replace(f"public void {first}(", "public void Renamed(")
    return {
        "tests": (tests_good, tests_bad),
        "entry": ({COMMANDS: GOOD_CMDS},
                  {COMMANDS: GOOD_CMDS, f"{PROJ}/Services/Rcon.cs": "static class X { static void Go(ChatCommandContext c) { CommandRegistry.Handle(c, \".beelz clearbar\"); } }"}),
        "handoff": ({HANDOFF: handoff_good, API: "public const int ApiVersion = 35;"},
                    {HANDOFF: handoff_good.replace("ApiVersion = 35", "ApiVersion = 34"), API: "public const int ApiVersion = 35;"}),
        "secrets": ({f"{PROJ}/A.cs": 'var name = "hello world";', f"{PROJ}/t.toml": 'versionNumber = "0.137.4"'},
                    {f"{PROJ}/A.cs": "x", f"{PROJ}/c.json": '{ "api_key": "abcdefgh12345678" }'}),
        "selfonly": ({COMMANDS: GOOD_CMDS},
                     {COMMANDS: GOOD_CMDS.replace("string set = \"all\"", "string player, string set = \"all\"")}),
        "wiring": ({COMMANDS: GOOD_CMDS, SERVICE: GOOD_SERVICE},
                   {COMMANDS: GOOD_CMDS.replace("// ClearAllSlots( is only", "SlotApply.ClearAllSlots(e); // only"), SERVICE: GOOD_SERVICE}),
        "backlog": ({BACKLOG: "| `clearbar-fullreset` | DONE v0.137.4 (clearbar-fullreset): ... | x |\n"},
                    {BACKLOG: "| `clearbar-fullreset` | Fold clearbar into the layered reset | x |\n"}),
    }


def selftest(_root: str) -> str:
    import tempfile
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
    # rollback + paths read git: build a throwaway repo
    with tempfile.TemporaryDirectory() as t:
        expect("rollback", check_rollback(t), "no input")
        _git(t, "init", "-q")
        _write(t, {"f.txt": "1"})
        _git(t, "add", "-A"); _git(t, "commit", "-qm", "a")
        first = _git(t, "rev-parse", "--short", "HEAD")
        _write(t, {"f.txt": "2"})
        _git(t, "add", "-A"); _git(t, "commit", "-qm", "b")
        last = _git(t, "rev-parse", "--short", "HEAD")
        body = " ".join(ROLLBACK_STEPS) + " " + CONSEQUENCE
        _write(t, {AUDIT: f"Rollback range: `{first}..{last}`\n{body}\n"})
        expect("rollback", check_rollback(t), "ok")
        _write(t, {AUDIT: f"Rollback range: `{last}..{first}`\n{body}\n"})
        expect("rollback", check_rollback(t), "FAIL")
        # paths: recon = first; the plan declares f.txt and itself
        saved = RECON
        try:
            RECON = first
            expect("paths", check_paths(t), "no input")
            plan = ("## Rollout\n- Paths this change ships, writes or regenerates: `f.txt`, "
                    f"`{PLAN}`, `{AUDIT}`.\n- Next\n")
            _write(t, {PLAN: plan})
            expect("paths", check_paths(t), "ok")
            _write(t, {"undeclared.txt": "x"})
            expect("paths", check_paths(t), "FAIL")
        finally:
            RECON = saved
    if bad:
        return f"selftest: FAIL {len(bad)}: " + " | ".join(bad)
    return f"selftest: ok, {len(CHECKS)} checks x good/defect/empty"


CHECKS_ALL = dict(CHECKS, selftest=selftest, actors=check_actors, scratch=check_scratch)


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
