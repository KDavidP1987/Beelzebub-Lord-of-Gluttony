# Crash Reproduction & Diagnostics Test Plan (Tier 1 + Tier 2)

Goal: **locally reproduce** each critical (server-crash) and game-crashing
(client-crash) ability from the tester reports and **capture in-game log
evidence** that confirms or refutes the root-cause hypotheses in
`TESTER_FEEDBACK_TRIAGE.md` — BEFORE we build any fix. The static prefab dumps
already give us the component data; what we can't get statically is *which
component actually faults at runtime*. That's what these tests capture.

Scope here = the 5 crash items: Dracula Bolt Spray, Morgana combo, Sir Erwin
Mountup (all server), Gaius Twinblade Throw, Gloomrot Fiddle (client). Two
character-corrupting items (Leandra forced-death, Spider Baneling invisibility)
are appended as "while-you're-in-there" captures since their evidence is cheap.

---

## 0. Setup (do once)

1. **Use a LOCAL server, solo.** These crash the server repeatedly — don't do it
   on the shared dev server. Build/deploy is the local DS at
   `C:\Program Files (x86)\Steam\steamapps\common\VRisingDedicatedServer\`.
2. **Beelz on the deployed 0.100.0 build.** (Feedback was on 0.55.0; confirm the
   crash still repros on 0.100.0 — some may already be gone.)
3. **Turn on diagnostics:**
   - Beelz config `Diagnostics → VerboseLogging = true` (or in-game if exposed),
     so `[Beelz …]` traces are written.
   - BCH → Beelzebub → Settings → "Enable diagnostic details" (shows ability IDs).
4. **Know your log files** (copy the tail after each crash, before restart):
   - **Server:** `…\VRisingDedicatedServer\BepInEx\LogOutput.log` + the server
     console window text. Unity native crash → look for a `crash`/`*.dmp` folder
     beside the server exe or in `%USERPROFILE%\AppData\LocalLow\Stunlock Studios\`.
   - **Client:** `%USERPROFILE%\AppData\LocalLow\Stunlock Studios\VRising\Player.log`
     and the client install's `BepInEx\LogOutput.log`.
5. **Grant tooling (admin, no farming needed):**
   - `.beelz admin give <you> <unitGuid> <abilityGuid>` — grant ONE ability (best
     for isolation), or `.beelz admin devour <you> <unitGuid>` — grant the unit's
     whole kit, then slot the suspect ID in BCH.
   - After slotting, **cycle your weapon** so the bar registers it, then cast.
   - `.beelz admin dump <abilityGuid>` — log the Group→Cast→spawn component lists
     + movement/cast/buff-flag values to `LogOutput.log` BEFORE you cast (baseline).
6. **Recovery between tests:**
   - Server crash → restart the DS; re-deploy only if you rebuilt.
   - Stuck/invis character → `.beelz admin buffs <you>` (capture first!), then
     `.beelz admin rebuildbar <you>`; if still stuck, relog.
   - Emergency block if an ID makes the server un-keepable:
     `.beelz admin denyguid add <abilityGuid>` (persists; blocks capture, not an
     already-slotted copy — unslot it in BCH too).

**Per-test capture template** (paste into a results doc / new Discord thread):
```
ID(s):                     <abilityGuid>
Beelz/BCH version:         0.100.0 / 0.20.0
.beelz admin dump output:  [Beelz DUMP] lines …
Repro steps taken:         …
Crash? (server/client/no): …
Crash TIMING:              instant-on-cast / ~Ns later / on-hit / on follow-up cast
Last 30 log lines / stack: …
Hypothesis confirmed?:     yes / no / partial — note
```

---

## TIER 1 — Server crashes

### Test 1 — Dracula Spell Stone Bolt Spray
- **ID** `1957691133`. Unit: Dracula Spell Stone Large Blood `32692466`.
- **Hypothesis:** the spawned Buff `-877173379` runs
  `AbilityProjectileFanOnGameplayEvent_DataServer` whose random-enemy target
  lookup NREs on a player caster (server-side script, no try/catch).
- **Steps:** `dump 1957691133` → grant → slot → cast in open world with **no
  enemies near**, then repeat **with an enemy in range** (the random-target lookup
  has something to find).
- **Disambiguator:** if it crashes **only when an enemy is in range** (or only
  with none) → confirms the random-target path. If it crashes the instant you
  cast regardless → the script itself faults on spawn (escalates the fix to
  stripping `RunScriptOnGameplayEvent`).
- **Capture:** server `LogOutput.log` tail. Look for a managed exception naming
  `…ProjectileFan…`, `Script…`, or a null-ref in a `…Server` system just before
  the log ends.

### Test 2 — Blackfang Morgana combo
- **IDs** `-1980019894` (Swarm) THEN `1242557903` (Orb Barrage). Unit: Blackfang
  Morgana `591725925`.
- **Hypothesis:** each spawns `Script_SpawnThrowTowardsNearbyVampires_DataServer`
  ScriptSpawn entities; the combo runs two concurrently on the player → crash.
- **Steps:** `dump` both → grant both. **Critical differential:**
  1. Cast Swarm `-1980019894` **alone** several times → crash? (note timing)
  2. Restart. Cast Orb Barrage `1242557903` **alone** several times → crash?
  3. Restart. Cast Swarm, then Orb Barrage within ~5s (Swarm's Thrower lives 5s)
     → crash?
- **Disambiguator:** if neither alone crashes but the sequence does → confirms the
  concurrent-ScriptSpawn root (fix = strip the throw-script from both
  TriggerToPlayer prefabs `1912709201` + `320348317`). If one crashes alone → that
  one's script is the primary; note which.
- **Capture:** server log tail; look for `…ThrowTowardsNearbyVampires…` / a
  condition-blob or null-ref.

### Test 3 — Sir Erwin "Militia Fabian Mountup"
- **ID** `-1623080868`. Unit: Sir Erwin (find unit GUID via BCH / `.beelz list`).
- **Hypothesis:** spawns the Fabian steed NPC (`SpawnMinionOnGameplayEvent` blob)
  + rider/shared-health server scripts that require the NPC pair → crash ~0.5s
  after cast (the steed-spawn delay). **Expected un-fixable** (no usable remainder).
- **Steps:** `dump -1623080868` → grant → cast on flat open ground. Watch whether
  a steed/horse briefly appears before the crash.
- **Disambiguator:** crash timing ≈0.5s after cast + a momentary mount entity →
  confirms the SpawnMinion vector. If it crashes instantly with no mount → the
  Travel_Phase or an earlier script.
- **Action after confirming:** `.beelz admin denyguid add -1623080868` (this is the
  one we expect to block). Capture the log either way to confirm the vector.

---

## TIER 2 — Client / game crashes (server stays up)

> For these, capture the **client** `Player.log` + client BepInEx log. The server
> survives, so its log shows the cast started but no fault.

### Test 4 — Gaius "Twinblade Throw"
- **ID** `1322698651`. Unit: Gaius (find unit GUID).
- **Hypotheses (two candidates — timing tells them apart):**
  - SpellObject `-1244341833` removes the player's own `…Javelin_Recast_Dash`
    buff → client mishandles the remove. (**fires on hit**)
  - Projectile `660830020` `Script_HomingSpell_DataShared` on the player rig.
    (**fires ~2s after cast / when the projectile spawns**)
  - Throw `599983966` `TargetAOESequence` = Gaius throw anim on player skeleton.
    (**fires instantly on cast**)
- **Steps:** `dump 1322698651` → grant → bind to a **non-twinblade** weapon, cast
  at an NPC. Then repeat bound to a **twinblade** (tester says it still crashed).
- **Disambiguator — note exact crash moment:** instant-on-cast = throw anim;
  ~2s/at projectile = homing script; only on projectile **hit** = the RemoveBuff.
  Non-twinblade vs twinblade tells us if the Javelin-dash remove matters.
- **Capture:** `Player.log` last lines (Unity crash usually logs the faulting
  system / a managed stack or `Crash!!!`).

### Test 5 — Gloomrot Technician Fiddle (+ Bell Ringer class)
- **ID** `1485838951`. Unit: Technician `820492683`.
- **Hypotheses:** combo-state desync (`AbilityGroupComboState` ComboLength=2) and/or
  null `MoveDuringCastData` movement curves. Symptom is the **vanilla sword E**
  crashing *after* Fiddle is bound.
- **Steps / critical differential:**
  1. `dump 1485838951`. Bind Fiddle to the **sword** loadout, cycle weapon.
  2. Cast **Fiddle itself** a few times → does it crash on its own?  (null-curve test)
  3. Then press the **vanilla sword E** on an NPC → crash?  (combo/slot-desync test)
  4. Repeat with **Bell Ringer** bound instead (a different ability that also
     reportedly crashes the sword E) — if Bell Ringer (no combo curves) also
     crashes E, the **slot-desync** class dominates over the curve issue.
- **Disambiguator:** Fiddle-alone crash → null curves. Only-the-next-E crash →
  combo/slot-state desync (and Bell Ringer reproducing it = systemic class, argues
  for a general guard, not just per-ability strips).
- **Capture:** client `Player.log`; note whether the crash is on the E press.

---

## Appendix — character-corruption captures (cheap, do while testing)

### Leandra forced-death — `1325722355` (ShadowStep) / `-1795148379` (TrippleBolt)
- **Hypothesis:** clone minions spawn with `MasterDeathAction:Kill` → your death
  when a clone dies.
- **Steps:** grant → cast in combat near enemies so clones spawn and die. When you
  die at full HP, capture the **server** log around your death — look for a minion
  death / master-death-action line, and note you died with HP remaining.

### Spider Baneling invisibility — `-891106318`
- **Hypothesis:** `Buff_General_HideCorpse` `1160901934` leaks (no LifeTime,
  persists through death).
- **Steps:** grant → cast (you die) → respawn invisible → **`.beelz admin buffs
  <you>`** and capture the list. Confirm `Buff_General_HideCorpse (1160901934)` is
  present on you. (`.beelz admin rebuildbar` won't strip a buff — relog or we'll
  need the fix; for now this just confirms the leak.)

---

## Optional diagnostic enhancement (only if logs come back thin)

`.beelz admin dump` currently only walks Group→Cast→first-level spawn and prints
values for movement/cast/buff-flag components. Several suspect components live one
hop deeper (ApplyBuff/SpawnPrefabOnGameplayEvent chains) — e.g. Dracula's
`-877173379`, the Leandra clone buffers, the Corpse/launch buffs. If the crash
logs don't pinpoint the fault, a small diagnostic-only extension to `dump` (full
chain traversal + print TravelBuff/SpawnMinion/ComboState/LifeTime/Script field
values) would let the logs confirm hypotheses directly. This is logging-only (no
gameplay change) — flag if you want it before the fix build.
