# Audit records

One file per dod plan: `docs/audits/<slug>.md` (slug = the plan in `docs/dod/`). Every build step gets a
pre-audit before it and a post-audit after it (CLAUDE.md › Development procedure). Append; never rewrite an
earlier entry. `Beelzebub/tools/preflight.ps1` fails when a record lacks any of the three markers below.
Adapted from Nyarlathotep's `docs/audits/README.md`.

## Template

```markdown
# Audit — <slug>

## Pre-audit
### Step <n> · YYYY-MM-DD · <commit>
- git status: clean | <what was pending>
- compile: 0 errors
- preflight: PREFLIGHT OK | <failures>
- dod status: <n>/<m> verified

## Post-audit
### Step <n> · YYYY-MM-DD · <commit>
- compile / tests / preflight: …
- planted faults: <test> failed with the fault planted, passed restored
- /code-review: <findings, dispositions>
- Codex verdict: APPROVED | REVISE — <one-line summary; findings and ACCEPTED/REJECTED dispositions below>
- in-game: <numbered steps run, what was observed>; log check: <preflight -LogCheck line>
- dod status: <n>/<m> verified; evidence lines added for <Dn …>
```

The literal markers `## Pre-audit`, `## Post-audit` and `Codex verdict:` are what the preflight check looks for.
