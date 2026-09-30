#!/usr/bin/env python3
"""fault_harness.py — re-runnable planted faults for a dod plan (bar-reset D21).

    python Beelzebub/tools/fault_harness.py bar-reset [--only D1,D2] [--skip D19] [--make [ID ...]] [--list]

Reads Beelzebub/tools/faults/<slug>/manifest.json and checks it against the plan's Definition of Done
(Beelzebub/Beelzebub/docs/dod/<slug>.md):

  * every `test` / `cmd` item (except D21, this harness) has manifest entries, and every part of its
    `fails when:` text is covered by an entry's `clauses` (exact substrings; what is left over may only be
    connectives such as "or", "and", punctuation);
  * every entry's `command` is that item's evidence command (a `<...>` or `%...%` placeholder token in the plan
    matches any token; a `test:` item's command is `dotnet test <Tests.csproj> -c Release --filter
    FullyQualifiedName~<TestClass>`);
  * every entry has a patch file in patches/ (a real source or fixture change) unless it is `silent` (a
    stays-silent clause: the clean baseline run proves it) or `deferred` behind a gate that is not met yet;
    a patch file no entry names is a FAIL.

Then, per command, it runs the clean baseline (must exit 0), and per patch: the patched files must be clean in
git, `git apply` plants it, the command must exit non-zero for the planted reason (a failing test — not a build
error; a checker `FAIL` line — not a crash; `PREFLIGHT FAILED`), `git apply -R` reverts it, and the command must
exit 0 again. A revert always runs, also on an error or Ctrl+C.

--make rewrites the patch of each named entry (all entries with `edits` when none is named) from its `edits`
(find/replace — `all` for every occurrence —, create, delete, truncate) against the current tree — use it when the
files a patch touches moved (the D19 patches name the current version, so they are re-made after each release).
--skip / --only select items; the summary line then names what was left out, so a partial run is never mistaken
for a full one. Output ends with `harness: ok, <n> faults, <k> clauses` or `harness: FAIL <reason>` (exit 1).
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

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TESTS = "Beelzebub/Beelzebub.Tests/Beelzebub.Tests.csproj"
SELF_ITEM = "D21"
# words that may be left over once every clause substring is removed from a `fails when:` text
CONNECTIVES = {"or", "and", "a", "an", "the", "any", "when", "then", "also", "either", "of"}


class HarnessFail(Exception):
    pass


def run(cmd: list[str], timeout: int = 1800) -> tuple[int, str]:
    # stderr is merged INTO stdout as it is written, so the output's last line is the command's real last line
    r = subprocess.run(cmd, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8",
                       errors="replace", timeout=timeout)
    return r.returncode, r.stdout or ""


def git(*args: str) -> str:
    code, out = run(["git", *args])
    if code != 0:
        raise HarnessFail(f"git {' '.join(args)}: {out.strip()[:200]}")
    return out


# ── the plan ─────────────────────────────────────────────────────────────────────────────────────────────────────

def plan_items(slug: str) -> dict:
    """{Dn: {kind, command, fails}} for every test / cmd item of the plan's Definition of Done."""
    path = os.path.join(ROOT, "Beelzebub/Beelzebub/docs/dod", f"{slug}.md")
    with open(path, encoding="utf-8") as f:
        text = f.read()
    m = re.search(r"^## Definition of Done\n(.*?)^## ", text, re.S | re.M)
    if not m:
        raise HarnessFail(f"no input: no Definition of Done in {path}")
    items = {}
    for line in m.group(1).splitlines():
        im = re.match(r"- \[[ x]\] (D\d+) · ", line)
        if not im:
            continue
        did = im.group(1)
        fw = line.find("(fails when: ")
        tm = re.search(r" · test: (\S+\.cs)", line)
        cm = re.search(r" · cmd: `([^`]+)`", line)
        if fw < 0 or not (tm or cm):
            continue
        fails = line[fw + len("(fails when: "):line.rstrip().rfind(")")]
        if tm:
            cls = os.path.splitext(os.path.basename(tm.group(1)))[0]
            command = f"dotnet test {TESTS} -c Release --filter FullyQualifiedName~{cls}"
            items[did] = {"kind": "test", "command": command, "fails": fails}
        else:
            items[did] = {"kind": "cmd", "command": cm.group(1), "fails": fails}
    if not items:
        raise HarnessFail("no input: no test/cmd items with a `fails when:` clause in the plan")
    items.pop(SELF_ITEM, None)
    return items


def command_matches(plan_cmd: str, entry_cmd: str) -> bool:
    a, b = plan_cmd.split(), entry_cmd.split()
    if len(a) != len(b):
        return False
    return all(x == y or ("<" in x and ">" in x) or re.search(r"%\w+%", x) for x, y in zip(a, b))


def leftover(fails: str, clauses: list[str]) -> str:
    """What remains of a `fails when:` text once every clause substring is cut out, minus connectives."""
    rest = fails
    for c in sorted(set(clauses), key=len, reverse=True):
        rest = rest.replace(c, " ")
    words = [w for w in re.split(r"[\s,;:.—()`]+", rest) if w]
    return " ".join(w for w in words if w.lower() not in CONNECTIVES)


# ── patches ──────────────────────────────────────────────────────────────────────────────────────────────────────

def patch_files(patch_text: str) -> list[tuple[str, bool]]:
    """(path, is_new) for every file the patch touches."""
    out = []
    for block in re.split(r"(?m)^diff --git ", patch_text)[1:]:
        m = re.match(r"a/(\S+) b/(\S+)", block)
        if m:
            out.append((m.group(2), "\nnew file mode" in block.split("\n@@", 1)[0]))
    return out


def _diff(rel: str, old: str | None, new: str | None) -> str:
    def lines(s: str | None) -> list[str]:
        if not s:
            return []
        ls = s.splitlines(keepends=True)
        if not ls[-1].endswith("\n"):
            ls[-1] += "\n\\ No newline at end of file\n"
        return ls
    head = f"diff --git a/{rel} b/{rel}\n"
    if old is None:
        head += "new file mode 100644\n"
    elif new is None:
        head += "deleted file mode 100644\n"
    body = difflib.unified_diff(lines(old), lines(new), "/dev/null" if old is None else f"a/{rel}",
                                "/dev/null" if new is None else f"b/{rel}", n=3)
    text = "".join(body)
    if not text:
        raise HarnessFail(f"--make: edits leave {rel} unchanged")
    return head + text


def make_patch(entry: dict) -> str:
    by_file: dict[str, tuple[str | None, str | None]] = {}
    for e in entry["edits"]:
        rel = e["file"]
        p = os.path.join(ROOT, rel)
        old = by_file[rel][1] if rel in by_file else (open(p, encoding="utf-8", newline="").read() if os.path.isfile(p) else None)
        orig = by_file[rel][0] if rel in by_file else old
        if "create" in e:
            if old is not None:
                raise HarnessFail(f"--make {entry['id']}: {rel} already exists")
            new = e["create"]
        elif e.get("delete"):
            new = None
        elif e.get("truncate"):
            new = ""
        else:
            find, repl = e["find"], e["replace"]
            if old is not None and "\r\n" in old:   # a CRLF working file: the edits are written with \n
                find, repl = find.replace("\n", "\r\n"), repl.replace("\n", "\r\n")
            n = 0 if old is None else old.count(find)
            if n == 0 or (n != 1 and not e.get("all")):
                raise HarnessFail(f"--make {entry['id']}: find text occurs {n}x in {rel}: {e['find'][:60]!r}")
            new = old.replace(find, repl)   # `all`: every occurrence (a doc that repeats a name)
        by_file[rel] = (orig, new)
    return "".join(_diff(rel, o, n) for rel, (o, n) in by_file.items())


# ── running ──────────────────────────────────────────────────────────────────────────────────────────────────────

def argv_of(command: str) -> list[str]:
    a = shlex.split(command)
    if a[0] == "python":
        a[0] = sys.executable
    return a


def planted_reason(command: str, out: str) -> str | None:
    """None when the non-zero exit is the planted failure; else why it is not (a build error, a crash). The marker must
    be the command's own verdict — its last line, or dotnet's test summary — never a stray earlier line."""
    lines = [l.strip() for l in out.splitlines() if l.strip()]
    last = lines[-1] if lines else ""
    if command.startswith("dotnet test"):
        if re.search(r"\berror (CS|MSB|NU|NETSDK)\d+", out):
            return "the planted patch broke the build, not a test"
        if not re.search(r"^\s*Failed!\s+-\s+Failed:\s*[1-9]", out, re.M):
            return "no failing-test summary in the output"
    elif "preflight.ps1" in command:
        if not last.startswith("PREFLIGHT FAILED"):
            return f"last line is not PREFLIGHT FAILED: {last[:80]}"
    else:
        if "Traceback" in out:
            return "the checker crashed"
        if not re.match(r"^[\w-]+: FAIL ", last):
            return f"last line is not a checker FAIL line: {last[:80]}"
    return None


def snapshot(files: list[tuple[str, bool]]) -> dict[str, bytes | None]:
    """The exact bytes of every file a patch touches (None = absent), taken before it is planted."""
    out = {}
    for rel, _ in files:
        p = os.path.join(ROOT, rel)
        out[rel] = open(p, "rb").read() if os.path.isfile(p) else None
    return out


def restore(before: dict[str, bytes | None]) -> list[str]:
    """After `git apply -R`: ALWAYS put back the exact snapshot bytes (a file the patch created is removed). Under
    core.autocrlf `git apply` rewrites line endings, which git then reports as a modified file — that difference is
    repaired silently. Returns the files that differed in more than line endings (the revert failed, or the command
    itself wrote to them); they are restored too, and the caller reports them."""
    wrong = []
    for rel, data in before.items():
        p = os.path.join(ROOT, rel)
        now = open(p, "rb").read() if os.path.isfile(p) else None
        if now == data:
            continue
        if data is None or now is None or now.replace(b"\r\n", b"\n") != data.replace(b"\r\n", b"\n"):
            wrong.append(rel)
        if data is None:
            os.remove(p)
        else:
            with open(p, "wb") as f:
                f.write(data)
    run(["git", "update-index", "-q", "--refresh"])
    return wrong


def gate_open(gate: str) -> bool:
    if gate == "rollback":  # D22 can only run once the release commit and its rollback line exist
        code, out = run([sys.executable, "Beelzebub/tools/check_bar_reset.py", "rollback"])
        return "no input" not in out
    raise HarnessFail(f"unknown gate {gate!r}")


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("slug")
    ap.add_argument("--only", default="")
    ap.add_argument("--skip", default="")
    ap.add_argument("--make", nargs="*")
    ap.add_argument("--list", action="store_true", help="print the clause coverage per item and exit")
    a = ap.parse_args(argv)
    try:
        return harness(a)
    except HarnessFail as e:
        print(f"harness: FAIL {e}")
        return 1


def harness(a) -> int:
    fdir = os.path.join(ROOT, "Beelzebub/tools/faults", a.slug)
    mpath = os.path.join(fdir, "manifest.json")
    if not os.path.isfile(mpath):
        raise HarnessFail(f"no input: {mpath} missing")
    with open(mpath, encoding="utf-8") as f:
        manifest = json.load(f)
    entries = manifest.get("entries", [])
    if not entries:
        raise HarnessFail("no input: the manifest has no entries")
    ids = [e["id"] for e in entries]
    if len(ids) != len(set(ids)):
        raise HarnessFail("duplicate entry ids in the manifest")
    pdir = os.path.join(fdir, "patches")

    if a.make is not None:
        todo = [e for e in entries if e.get("edits") and (not a.make or e["id"] in a.make)]
        if a.make and len(todo) != len(a.make):
            raise HarnessFail("--make: unknown id or entry without edits: " + ", ".join(set(a.make) - {e['id'] for e in todo}))
        os.makedirs(pdir, exist_ok=True)
        for e in todo:
            with open(os.path.join(pdir, e["patch"]), "w", encoding="utf-8", newline="\n") as f:
                f.write(make_patch(e))
            print(f"made {e['patch']}")
        return 0

    items = plan_items(a.slug)
    only = {x for x in a.only.split(",") if x}
    skip = {x for x in a.skip.split(",") if x}

    # 1. coverage: every item and every part of its fails-when text
    problems = []
    by_item: dict[str, list[dict]] = {}
    for e in entries:
        if e["item"] not in items:
            problems.append(f"{e['id']}: {e['item']} is not a test/cmd item of the plan")
            continue
        by_item.setdefault(e["item"], []).append(e)
        it = items[e["item"]]
        if not command_matches(it["command"], e["command"]):
            problems.append(f"{e['id']}: command is not {e['item']}'s evidence command ({it['command']})")
        for c in e.get("clauses", []):
            if c not in it["fails"]:
                problems.append(f"{e['id']}: clause not in {e['item']}'s fails-when text: {c[:60]!r}")
        if not e.get("clauses"):
            problems.append(f"{e['id']}: no clauses")
        kinds = [k for k in ("patch", "silent", "deferred") if e.get(k)]
        if len(kinds) != 1:
            problems.append(f"{e['id']}: needs exactly one of patch / silent / deferred")
    for did, it in sorted(items.items(), key=lambda kv: int(kv[0][1:])):
        es = by_item.get(did, [])
        rest = leftover(it["fails"], [c for e in es for c in e.get("clauses", [])])
        if a.list:
            print(f"{did} {len(es)} entries - leftover: {rest or '-'}")
        if not es:
            problems.append(f"{did}: no manifest entry")
        elif rest:
            problems.append(f"{did}: fails-when text not covered by any clause: {rest[:120]}")
    pnames = [e["patch"] for e in entries if e.get("patch")]
    for p in sorted({p for p in pnames if pnames.count(p) > 1}):
        problems.append(f"patches/{p} is named by more than one entry")
    named = set(pnames)
    fixture_only = {"D20": f"Beelzebub/tools/faults/{a.slug}/logs/", "D30": f"Beelzebub/tools/faults/{a.slug}/logs/"}
    for e in entries:
        pp = os.path.join(pdir, e.get("patch") or "")
        if e["item"] in fixture_only and e.get("patch") and os.path.isfile(pp):
            for f_, _ in patch_files(open(pp, encoding="utf-8").read()):
                if not f_.startswith(fixture_only[e["item"]]):
                    problems.append(f"{e['id']}: plants into {f_}, not the tracked fixture logs")
    on_disk = set(os.listdir(pdir)) if os.path.isdir(pdir) else set()
    for p in sorted(on_disk - named):
        problems.append(f"patches/{p} is named by no entry")
    for p in sorted(named - on_disk):
        problems.append(f"patches/{p} missing (run --make)")
    # a manifest, patch or fixture that exists only locally (e.g. caught by a .gitignore) is not re-runnable
    rel_dir = os.path.relpath(fdir, ROOT).replace(os.sep, "/")
    tracked = set(git("ls-files", "--", rel_dir).splitlines())
    local = [f"{rel_dir}/{os.path.relpath(os.path.join(dp, fn), fdir).replace(os.sep, '/')}"
             for dp, _, fns in os.walk(fdir) for fn in fns]
    for p in sorted(set(local) - tracked):
        problems.append(f"{p} is not tracked by git")
    gates = {}
    for e in entries:
        if e.get("deferred"):
            g = e.get("gate")
            if g not in gates:
                gates[g] = gate_open(g) if g else False
            if gates[g]:
                problems.append(f"{e['id']}: deferred until '{g}', which is now met — write its patch")
    if problems:
        raise HarnessFail(f"{len(problems)} manifest problem(s): " + " | ".join(problems[:8]))
    if a.list:
        return 0

    if git("status", "--porcelain", "--", rel_dir).strip():
        raise HarnessFail(f"uncommitted changes under {rel_dir}: commit the manifest and patches before a recorded run")
    tree = git("rev-parse", f"HEAD:{rel_dir}").strip()

    selected = [e for e in entries if (not only or e["item"] in only) and e["item"] not in skip]
    patches = [e for e in selected if e.get("patch")]
    if not patches:
        raise HarnessFail("no input: zero patches selected")

    # 2. the clean baseline, once per command
    baseline = {}
    for cmd in dict.fromkeys(e["command"] for e in selected if not e.get("deferred")):
        code, out = run(argv_of(cmd))
        baseline[cmd] = code
        print(f"baseline {'ok  ' if code == 0 else 'FAIL'} {cmd}")
        if code != 0:
            raise HarnessFail(f"baseline exits {code}: {cmd}\n{out[-1500:]}")

    # 3. plant, run, revert, run
    failures = []
    for e in patches:
        ppath = os.path.join(pdir, e["patch"])
        with open(ppath, encoding="utf-8") as f:
            ptext = f.read()
        files = patch_files(ptext)
        if not files:
            failures.append(f"{e['id']}: patch touches no file")
            continue
        dirty = [p for p, new in files if (os.path.exists(os.path.join(ROOT, p)) if new
                                           else git("status", "--porcelain", "--", p).strip())]
        if dirty:
            failures.append(f"{e['id']}: working tree dirty for {', '.join(dirty)}")
            continue
        code, out = run(["git", "apply", "--check", ppath])
        if code != 0:
            failures.append(f"{e['id']}: patch does not apply: {out.strip()[:160]}")
            continue
        before = snapshot(files)
        code, out = 1, ""
        try:
            git("apply", ppath)
            code, out = run(argv_of(e["command"]))
        finally:
            rcode, rout = run(["git", "apply", "-R", ppath])
            wrong = restore(before)   # runs even when the apply, the command or the revert failed
        if rcode != 0 or wrong:
            failures.append(f"{e['id']}: revert failed ({rout.strip()[:80] or 'bytes differ: ' + ', '.join(wrong)}); snapshot restored")
            print(f"planted {e['id']}: REVERT FAILED (snapshot restored)")
            continue
        why = "exits 0 while planted" if code == 0 else planted_reason(e["command"], out)
        if why:
            failures.append(f"{e['id']}: {why}")
            print(f"planted {e['id']}: NOT CAUGHT ({why})")
            continue
        rcode, rout = run(argv_of(e["command"]))
        if rcode != 0:
            failures.append(f"{e['id']}: exits {rcode} after the revert")
            print(f"planted {e['id']}: caught, but FAILS after revert")
            continue
        print(f"planted {e['id']}: caught (exit {code}), reverted ok")

    if failures:
        raise HarnessFail(f"{len(failures)} of {len(patches)} fault(s): " + " | ".join(failures[:8]))
    clauses = {(e["item"], c) for e in selected for c in e["clauses"]}
    deferred = [e for e in selected if e.get("deferred")]
    silent = [e for e in selected if e.get("silent")]
    tail = f", {len(silent)} silent" if silent else ""
    if deferred:
        tail += f", {len(deferred)} deferred ({', '.join(sorted({e['item'] for e in deferred}))}: gate not met)"
    left_out = sorted(set(items) - {e["item"] for e in selected}, key=lambda d: int(d[1:]))
    if left_out:
        tail += f" (not run: {' '.join(left_out)})"
    print(f"harness: ok, {len(patches)} faults, {len(clauses)} clauses{tail}, patch tree {tree}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
