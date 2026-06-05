"""
B2 (prep-roadmap) — BROKEN / INCOMPATIBLE re-audit.

Goal was to systematically flag abilities that genuinely don't translate to a player. The audit's
honest finding (documented here, not hidden): **"broken for a player" is almost never statically
evident.** Every ability in the dump carries a castable structure (0 of 1,813 lack cast steps), and
the only reliably-detectable broken class — NPC anim-rig-bound teleports — is already essentially
covered by the shipped `incompatible` set (the ShadowStep / Mist-Walk family). The other sub-types
the roadmap named (team-filtered, corpse-required, AI-only) have no precise prefab fingerprint that
separates them from working conditional abilities (the Erwin lesson: "looked broken = was conditional").

So this tool does the DEFENSIBLE thing:
  1. consolidates the confirmed `incompatible` set (with reasons),
  2. surfaces same-CLASS CANDIDATES (anim-rig teleport family + boss phase-transition triggers) for
     IN-GAME verification — proposing nothing for auto-apply,
  3. states plainly that comprehensive broken-detection runs through the verification pipeline
     (B4 condition-confirm + tester reports → metadata.incompatible), not static analysis.

Outputs:
  - audit_incompatible.json  {confirmed:[...], candidates:[...]}
  - AUDIT_INCOMPATIBLE.md    report
Read-only. Proposes nothing. Run: python audit_incompatible.py
"""
import os, re, sys, json
import audit_common as C

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(__file__)
OUT_JSON = os.path.join(HERE, 'audit_incompatible.json')
OUT_MD = os.path.join(HERE, 'AUDIT_INCOMPATIBLE.md')

# Same-class candidate signals (name-based; NPC, not the player's own AB_Vampire_ kit).
ANIMRIG_TOKENS = re.compile(r'ShadowStep|MistWalk|MistShift|_Teleport_Travel|_Phase_Travel', re.I)
PHASE_TRIGGER = re.compile(r'ChangePhase|PhaseTransition|PhaseDual|_EnterPhase|_PhaseShift', re.I)


def main():
    idx, names = C.build_index()
    meta = C.load_metadata()

    confirmed = [{'guid': g, 'name': m.get('name', ''),
                  'prefab': names.get(int(g), ''), 'reason': m.get('incompatibleReason', '')}
                 for g, m in meta.items() if m.get('incompatible')]
    confirmed_guids = {c['guid'] for c in confirmed}

    animrig, phase = [], []
    for g, m in meta.items():
        if g in confirmed_guids:
            continue
        prefab = names.get(int(g), '')
        is_player = prefab.startswith('AB_Vampire_') or prefab.startswith('AB_Shapeshift')
        if ANIMRIG_TOKENS.search(prefab) and not is_player:
            animrig.append({'guid': g, 'name': m.get('name', ''), 'prefab': prefab})
        elif PHASE_TRIGGER.search(prefab):
            phase.append({'guid': g, 'name': m.get('name', ''), 'prefab': prefab})

    json.dump({'confirmed': confirmed, 'candidates_animrig': animrig, 'candidates_phase': phase},
              open(OUT_JSON, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)

    L = []
    L.append('# B2 — Broken / incompatible re-audit (auto-generated)\n')
    L.append('**Finding:** broken-for-a-player is almost never statically detectable. Every ability in '
             'the dump is structurally castable (0 of 1,813 have no cast steps), and the only reliably-'
             'detectable broken class (NPC anim-rig teleports) is already covered by the shipped '
             '`incompatible` set. The roadmap\'s other sub-types (team-filtered / corpse-required / '
             'AI-only) have no prefab fingerprint that separates them from working *conditional* '
             'abilities — so flagging them statically would reproduce the Erwin false-positive. '
             '**Nothing here is auto-applied; candidates are for in-game verification.**\n')
    L.append(f'## Confirmed incompatible ({len(confirmed)}) — the shipped baseline\n')
    for c in confirmed:
        L.append(f"- `{c['prefab']}` — {c['name']}  <sub>{c['reason']}</sub>")
    L.append(f'\n## Candidates: anim-rig teleport family ({len(animrig)}) — VERIFY in-game before blocking\n')
    L.append('Same shape as the confirmed set (NPC ShadowStep / Mist-Walk / teleport-travel). Likely '
             'rig-bound, but confirm — some NPC teleports DO work for a player.\n')
    for c in animrig:
        L.append(f"- `{c['prefab']}` — {c['name']}")
    L.append(f'\n## Candidates: boss phase-transition triggers ({len(phase)}) — likely internal, VERIFY\n')
    L.append('Phase-change logic gates, not signature abilities — usually do nothing useful when a '
             'player casts them. Confirm, then `Hidden`/`Blocked` as appropriate.\n')
    for c in phase:
        L.append(f"- `{c['prefab']}` — {c['name']}")
    L.append('\n## How broken-detection actually proceeds\n')
    L.append('1. Tester reports + the B4 condition-confirmation pass surface genuinely-broken abilities '
             'in play.\n2. Each is recorded in `ability_metadata.json` as `incompatible` + reason (the '
             'authoritative broken record).\n3. The lint warns if an `incompatible` ability is still '
             '`Enabled`. This audit just keeps the candidate frontier visible.')
    open(OUT_MD, 'w', encoding='utf-8').write('\n'.join(L))

    print(f'Confirmed incompatible: {len(confirmed)} | anim-rig candidates: {len(animrig)} | '
          f'phase-trigger candidates: {len(phase)} (all candidates = verify in-game, none auto-applied)')
    print(f'Wrote {os.path.normpath(OUT_JSON)}')
    print(f'Wrote {os.path.normpath(OUT_MD)}')


if __name__ == '__main__':
    main()
