# Boss / V-Blood Chained-Ability Audit (#2)

> **Status: static candidate sweep.** This is the planning + test-target document
> for the chained-ability audit. The actual fixes are **deferred to a group-test
> session** — the hard lesson from the Mist Walk Nova fight (see
> `project_nova_chain_investigation`) is **observation > static theory**: eight
> versions were wasted on a static "team-fixup" theory that one live trace
> disproved. So this doc lists *candidates and how to confirm them with the
> `[Beelz TRACE]` tool*, not asserted fixes.
>
> The full prefab **component dump is not in this workspace** (only the name
> table `Resources/prefab_names.tsv`), so candidate identification here is by
> naming pattern + known runtime findings, not component inspection.

## What "chain breakage" means
A boss ability is a *chain*: Cast → (Channel/Travel/Buff) → spawned payload
(projectile fan / proxy / summon / area) → effect. When a **player** casts it
(granted on the spell bar, or while transformed into the unit), links can break
that never break for the NPC.

## Failure classes (from `project_ability_chain_audit` + the Nova fight)
1. **Offset** — payload spawns at the wrong position (origin/NPC anchor instead of the player).
2. **Animation/rig-bound** — the cast is tied to the unit's skeleton; on the player's
   vampire rig it doesn't play / doesn't progress. **Largely solved for form units**
   (Dracula/Morgana/native shapeshifts) by the ExoForm transform — applying the real
   form buff gives the player the correct rig, so these fire. **Still a problem** for
   granted abilities on the normal bar and for transforms into non-form units
   (humanoids/undead/constructs that have no shapeshift form).
3. **Owner-chain loss** — spawned entities lose their `EntityOwner` → no damage / can't
   attribute to the player.
4. **Team filter** — spawned entities carry the boss's NPC `Team`/`FactionReference`
   so they don't hit enemies (or hit the player).
5. **Gated spawn (ConditionBlob)** — the payload spawn is gated by a condition meant
   for the NPC; suppressed for players. **The Mist Walk Nova case** — fixed (v0.24.9)
   by manually spawning the self-contained ProxySpawner owned by the player.

## Confirmed status so far
- ✅ **Undead Priest Mist Walk Nova** (class 5) — FIXED; manual ProxySpawner spawn on
  teleport arrival (`BuffSpawnServerPatch.TeleportDetonateTargets`) + the on-demand
  `.beelz detonate`.
- ⚠️ **Undead Priest C-key standalone Nova** — separate slot-binding issue, not a chain
  break (the `_ProjectileNova_AbilityGroup` has no `_Hard_`/`_Ultimate_` token, so the
  slot heuristic may not bind it to the key pressed). Verify slot placement first.
- ✅ **Form-unit abilities** (Dracula/Morgana) — mostly fire now via ExoForm rig.
  Remaining per-ability misfires are curated out of the form sets.

## Candidate patterns to TRACE (by naming, pending confirmation)
Run with `VerboseLogging` on; transform/grant, cast, and read `[Beelz TRACE]` /
`[Beelz CHAIN]` lines. Decision tree below.

| Pattern (prefab-name substring) | Likely class | Notes |
|---|---|---|
| `_ProxySpawner` | 5 (gated spawn) | Priest is the only one in the table; self-contained — manual-spawn works. |
| `_Channel` / `_ChannelBuff` | 2 (rig) / 5 | Channeled novas (Dracula CrimsonNova, Cardinal LightNova). Channel may not progress on player rig. |
| `_Travel` / `_Teleport` + downstream | 5 (gated on arrival) | Teleport-and-detonate — add to `TeleportDetonateTargets` (the Mist Walk recipe). |
| `_Summon*` / `RaiseDead` / `RaiseHorde` | 3/4 (owner/team) | Summon chains — already handled by summon-as-ally + `SummonTargets`. |
| `*Nova*Area` / `*Barrage*` / `_Fan_` / `ProjectileFan` | 5 (fan-on-event) | Multi-projectile fans; check whether the fan event fires for players. |
| `_Area` / `_RingArea` payloads | 1/4 (offset/team) | Persistent areas — check spawn position + team. |

**Specific units worth tracing** (signature AoEs seen in the name table):
Dracula `CrimsonNova`, Cardinal `LightNova`, Gloomrot Professor `ImplodingOrb`,
IceRanger / Militia Guard `IceNova`, Valyr `FrostNova`. (These are also the
`.beelz detonate` expansion candidates — see `BuffSpawnServerPatch.ManualDetonateTargets`.)

## TRACE decision tree (per ability)
1. **Cast/Channel buff absent in the trace** → the cast itself fails → class 2
   (rig-bound). Fixable only via a form (already done for form units); otherwise
   mark the ability Incompatible in the AbilityMap.
2. **Channel present, payload (proxy/projectile/area) absent** → the spawn event is
   suppressed for players → class 5 → manually spawn the payload owned by the player
   (the Priest recipe: `InstantiateEntityImmediate` + anchor owner/pos).
3. **Payload present but no damage / hits the player** → class 3/4 → fix owner +
   team on the spawned entities (the `FixupBuffOwnership` / sweep path).
4. **Payload present, correct team, wrong location** → class 1 → set translation on
   the player on spawn.

## Tools
- `[Beelz TRACE]` per-cast chain logger (`Services/ChainTraceService.cs`, v0.25) —
  verbose-gated; records what a cast actually produces.
- `[Beelz CHAIN][watch]` in `BuffSpawnServerPatch` — per-frame buff/entity watch.
- Manual-spawn precedent: `BuffSpawnServerPatch.SpawnDetonationOwnedByPlayer` +
  `AbilityCastStartedSystemPatch` SummonTargets.

## Why this is paused, not done
Fixing without a live trace re-runs the team-fixup mistake. The right next step is a
**group-test session**: testers transform/grant the candidate abilities above, we
read the trace, and fix the *confirmed* broken link per the decision tree — one fix
per confirmed break, never speculative. The ExoForm rollout already removed the
biggest class (rig-bound boss abilities) for form units, so the remaining audit is
mostly granted-ability casts and non-form-unit transforms.

See also: `project_chain_ability_audit_sweep`, `project_nova_chain_investigation`,
`project_ability_chain_audit` (memory).
