# Beelzebub → BloodCraftHub (BCH) Integration Handoff

> **Purpose.** This is the single reference for everything **BloodCraftHub (the
> client-side companion mod)** needs to build to surface and extend Beelzebub.
> It is authored and maintained in the **Beelzebub** workspace (server-side mod);
> carry it over to the BCH workspace when you switch projects and keep building
> from it there.
>
> **Status legend:** ✅ shipped in Beelzebub · 🟡 partial / contract present, UI
> pending · 🔬 experimental / research-blocked · ⛔ not possible server-side
> (BCH-only path).
>
> **Cross-workspace boundary.** BCH lives at
> `C:\Users\KDPen\OneDrive\Documents\CURSOR PROJECTS\Games\V Rising\BloodCraftUI 2\`.
> Per both projects' `CLAUDE.md`, the two repos never cross-edit. All
> integration flows through the **chat-command API** below — exactly the way BCH
> already talks to Bloodcraft. This doc is the contract; implementation happens
> in the BCH workspace.
>
> **Canonical source of truth for the wire API:**
> `Beelzebub/Beelzebub/Commands/ApiCommands.cs` (`ApiVersion = 2`). If this doc
> and that file ever disagree, the file wins — and this doc should be corrected.

---

## 0. Integration model

Beelzebub is **server-side only** and cannot draw anything on a client. BCH is
the **client-side** half. They communicate purely through V Rising chat:

- **BCH → Beelzebub:** BCH sends chat commands (`.beelz api …` for reads,
  `.beelz …` player commands for mutations) exactly as if the player typed them.
- **Beelzebub → BCH:** Beelzebub replies with machine-readable `[BEELZ:*]` chat
  lines that BCH parses, plus an opt-in live **event stream** for push updates.

This mirrors the BCH ↔ Bloodcraft pattern. BCH owns: parsing, caching,
rendering on-screen UI (ability buttons, cooldowns, collection book, transform
browser, admin panels), and any **client-side rendering** work (the model-swap
and animation-fidelity experiments — see §7).

---

## 1. Wire format (`[BEELZ:*]`)

- Every API reply line begins with a `[BEELZ:<tag>]` marker, followed by
  space-separated `key=value` pairs.
- **Values are bare tokens** — no quoting. Prefab names are guaranteed
  `[A-Za-z0-9_]` by V Rising convention. Free-text fields (`desc`, `notes`) are
  sanitized: spaces→`_`, `=`→`-`, newlines→`_`.
- **Lists** use comma-separated values, no spaces: `key=a,b,c`.
- Multi-record results stream record-by-record and terminate with
  `[BEELZ:end] cmd=<name> count=<n>` (paginated streams add
  `total=<n> page=<i> pages=<n>`).
- Errors: `[BEELZ:err] cmd=<name> code=<snake_case> msg=<text>`. The most common
  code is `not_ready` (plugin still initializing — retry).
- Each line stays under V Rising's ~510-byte chat limit (`FixedString512Bytes`).

**On load, BCH should:** call `.beelz api version` (gate on `ready=1`), then
`.beelz api bch on` to enable the event stream, then hydrate state with
`list` / `slots` / `transforms` / `active` / `progress`.

---

## 2. Read API (BCH-readable) — ✅ shipped

All under the `.beelz api` group. Verified against `ApiCommands.cs`.

| Command | Marker(s) | Returns |
|---|---|---|
| `.beelz api version` | `[BEELZ:version]` | `api=<int> plugin=<ver> ready=0\|1` |
| `.beelz api list` | `[BEELZ:list]` … `[BEELZ:end]` | Caller's captured abilities: `i= s=R\|V u=<unitGuid> un=<unitName> a=<abilityGuid> an=<abilityName>` (+ category/type fields) |
| `.beelz api slots` | `[BEELZ:slot]`, `[BEELZ:slot-current]`, `[BEELZ:end]` | Slot assignments per bucket: `bucket=any\|<WeaponFamily> slot=1-6 a= an=`; footer `weapon=<current>` |
| `.beelz api transforms` | `[BEELZ:tx]` … `[BEELZ:end]` | Transform unlocks + matrix attrs: `i= s= u= un= enabled= difficulty= tier= damage_scale= cooldown_scale= health_scale= speed_scale= type= full_replace= scaling_mode=` |
| `.beelz api active` | `[BEELZ:active]` | Active transform: `u= un= s= ttl=<sec>\|toggle` + phase info; or `none=1` |
| `.beelz api info <index>` | `[BEELZ:info]` | One ability's full tooltip data: `desc=` (real ability description, %params% substituted), `weapons=`, `weapon_anim=<family\|None>` (animation weapon, v2), `school=` (v2), `cooldown_seconds=` (v2), `forms=`, `transform_only=`, `enabled=`, `difficulty=`, `damage_scale=`, `cooldown_scale=` |
| `.beelz api progress` | `[BEELZ:progress]` | Collection %: `abilities_captured= abilities_total= abilities_pct= transforms_unlocked= transforms_total= transforms_pct=` + V-Blood breakdowns |
| `.beelz api rules` | `[BEELZ:rules]` | Loaded filter rules: `version= deny_patterns= allow_patterns= deny_guids= allow_guids=` |
| `.beelz api transform-config` | `[BEELZ:tx-config]` … `[BEELZ:end]` | One line per source `R`/`V`: `src= mode=Toggle\|Timed\|Disabled duration= cooldown=` |
| `.beelz api catalog` | `[BEELZ:catalog-summary]` | `abilities= units= server_mode=Basic\|Brutal` |
| `.beelz api catalog units [page]` | `[BEELZ:catalog-unit]` … `[BEELZ:end]` | Full curated transform-target list (collection book), 40/page, with matrix attrs |
| `.beelz api catalog abilities [page]` | `[BEELZ:catalog-ability]` … `[BEELZ:end]` | Full curated ability list, 40/page, with matrix attrs |
| `.beelz api hotkeys` | `[BEELZ:hotkeys-config]`, `[BEELZ:hotkey]`, `[BEELZ:end]` | Config footer (`enabled= max=`) + named hotkey bindings |
| `.beelz api verbosity` | `[BEELZ:verbosity]` | `level=Silent\|Summary\|Verbose default=<server default>` |

---

## 3. Event stream (push) — ✅ shipped

- `.beelz api bch <on\|off\|status>` → `[BEELZ:bch] state=on\|off api=<ver>`.
  BCH calls `on` at load to subscribe the caller to live updates. Events only go
  to subscribed players (independent of chat verbosity).
- While subscribed, Beelzebub pushes `[BEELZ:event] type=<event> …` lines on state
  change so BCH updates without re-polling. **Complete emitted set:**

  | `type=` | Fields | When |
  |---|---|---|
  | `capture` | `s=R\|V u= un= a= an=` | A new ability is captured from a kill |
  | `transform-unlock` | `s=R\|V u= un=` | A new transform unit is unlocked |
  | `slot-granted` / `slot-cleared` | `slot= [a= an=]` | Universal-bucket grant/clear |
  | `weapon-slot-granted` / `weapon-slot-cleared` | `weapon= slot= [a= an=]` | Weapon-bucket grant/clear |
  | `hotkey-set` / `hotkey-cleared` | `name= [a= an=]` | Named hotkey bind/clear |
  | `transform-activated` | `u= un=` | Player transforms |
  | `transform-ended` | `u= un= reason=` | Any revert. `reason` ∈ `manual` · `switching-transformation` · `auto` · `admin-clear` · `admin-revoke` · `admin-revert-all` · `admin-wipe-all` |
  | `transform-phase-shift` | `u= un= phase=` | Form/phase swap (`.beelz phase` or Auto-HP) |
  | `forget` / `forget-transform` | `a= u=` / `u=` | Capture / unlock deleted (v2) |
  | `cleared` | — | Player wiped all captures + slots (v2) |
  | `detonate` | `u=` | Player manually fired a transform's detonation AoE via `.beelz detonate` (v2) |

---

## 4. Mutations BCH issues — ✅ shipped

BCH sends these exactly as a player would type them. Replies are
**human-readable text**, not API format — treat them fire-and-forget and
re-fetch the affected read command (or wait for the event).

**Player:** `.beelz grant <slot 1-6> <index>` · `.beelz unslot <slot>` ·
`.beelz weapon-grant <weapon\|auto> <slot> <index>` ·
`.beelz weapon-unslot <weapon\|auto> <slot>` ·
`.beelz transform <index\|name>` · `.beelz revert` · `.beelz phase [n]` ·
`.beelz detonate` (fire a transform's signature AoE on demand, if it has one — a
natural candidate for a BCH HUD button) ·
`.beelz summons <stash\|restore\|status>` ·
`.beelz preset <save\|load\|list\|delete> <name>` ·
`.beelz hotkey <set\|clear\|list> …` ·
`.beelz forget <i>` · `.beelz forget-transform <i>` · `.beelz clear` ·
`.beelz verbosity <silent\|summary\|verbose>`.

**Admin** (BCH admin panel; full list in §6): grant/revoke (ability +
transform), force/clear-transform, set/clear slot (universal + weapon),
deny/allow patterns, transform mode/duration/cooldown, difficulty, freeze-
captures, revert-all, snapshot, inspect/progress, wipe-all.

---

## 5. What BCH should build (player UI)

| Feature | Backed by | Status |
|---|---|---|
| **Collection book** — captured abilities grid, search, tooltips | `api list`, `api info`, `api catalog abilities` | ✅ data ready · 🟡 UI |
| **Slot loadout editor** — drag ability → one of 6 slots, universal vs weapon-specific buckets | `api slots` + `grant`/`weapon-grant`/`unslot` | ✅ data ready · 🟡 UI |
| **On-screen ability buttons + cooldown display** (the original BCH vision) | spell-bar state via `api active`/`api slots`; cooldowns from ability metadata | 🟡 needs client render + a cooldown feed |
| **Transform browser + hunt catalog** — unlocked vs to-hunt, tier sort, preview | `api transforms`, `api catalog units` + `transform`/`revert` | ✅ data ready · 🟡 UI |
| **Phase switcher** — for multi-form bosses (Dracula warrior↔bloodmage) | `api active` (phase/phases) + `phase <n>` | ✅ · 🟡 UI |
| **Summon panel** — live/stashed counts, stash for waygates | `summons status`/`stash`/`restore` | ✅ · 🟡 UI |
| **Presets & named hotkeys** | `preset …`, `api hotkeys` + `hotkey …` | ✅ · 🟡 UI |
| **Progress / completion meter** | `api progress` | ✅ · 🟡 UI |

---

## 6. Admin surface BCH should expose

Curation lives in `ability_rules.json` (`AbilityMap` per-ability matrix,
`TransformMap` per-unit matrix, deny/allow patterns) — see
`docs/ABILITY_MAP_FORMAT.md`. Read it via `api rules` / `api catalog`. Mutate
via these admin chat commands (all audited to `LogOutput.log` as
`[Beelz AUDIT] admin=… action=… target=…`):

- **Filter rules:** `admin rules` · `admin deny/undeny <pattern>` ·
  `admin allow/unallow <pattern>` · `admin reload`.
- **Transform control:** `admin transform mode/duration/cooldown <regular|vblood> …`
  · `admin transform show` · `admin difficulty [basic|brutal]`.
- **Grant/revoke:** `admin give` / `revoke` (ability) ·
  `admin give-transform` / `revoke-transform` · `admin force-transform` /
  `clear-transform`.
- **Remote slots:** `admin set-slot` / `clear-slot` /
  `set-weapon-slot` / `clear-weapon-slot`.
- **Bulk / ops:** `admin revert-all` · `admin freeze-captures <on|off|status>`
  · `admin snapshot` · `admin inspect <player>` · `admin progress <player>` ·
  `admin scan-abilities` · `admin desummon[-all]` ·
  `admin wipe-all CONFIRM-WIPE` (destructive).

A BCH **admin panel** would wrap these as forms/toggles, reading current state
from `api rules` / `api transform-config` / `api catalog units|abilities`.

---

## 7. Experimental / future work BCH must own

These are the items the server-side mod **cannot** do alone. They are the
reason BCH integration matters most.

### 7.1 ⛔→BCH  Arbitrary-NPC model swap (true "become any unit")
**Hard limit, confirmed by research (2026-05-23).** A server-side mod cannot
render a player as an arbitrary CHAR_ NPC. V Rising model rendering is
**client-authoritative**: the server holds only a `HybridModelSeed`; the ~50
`HybridModel*` systems that resolve mesh + rig live on the **client**, and the
dedicated server even strips hybrid rendering off prefabs. Only ~10 forms swap
the model (8 native shapeshifts + Dracula `Buff_Vampire_Dracula_SpellPhase` +
Morgana `SnakePhase`) because the **stock client special-cases those exact
buffs**. Polymorph (chicken/toad curses) is the same fixed-buff mechanism — not
parameterizable. Transmog only recolors/hides. Familiars render the model only
because a familiar **is a separate entity that literally is the CHAR_ prefab**.

**The only path to arbitrary-NPC appearance is here, in BCH:** a client-side mod
that registers HybridModel render handling for additional CHAR_ forms, with
Beelzebub just toggling the matching buff/seed and announcing it over chat. This
is unproven (no shipped mod does it) and a substantial client-rendering project
— but it is the *correct* layer for it. Server side, Beelzebub can: pick the
form/seed, drive the ability bar, and emit a `[BEELZ:event]` so BCH knows what
the player "should" look like.

> Implementation sketch for BCH: subscribe to transform events → look up the
> target unit's CHAR_ model GUID (Beelzebub can ship the GUID list) → drive the
> client HybridModel pipeline to skin the local player entity. Needs to handle
> the local player + remote players the client can see.

### 7.2 Animation fidelity — what shipped, and what's still BCH-only
**Hard limit (researched 2026-05-23):** an ability's cast animation is BAKED into
its prefab as a client-side `SequenceGUID`. The server CANNOT remap one ability's
animation while keeping its effect — only choose which ability fires, or wield a
matching weapon. So the literal "map any animation to any ability" is not
engine-possible server-side.

**✅ Shipped server-side (v0.34.0) — weapon-match guidance/enforce:** weapon-based
abilities only read right while wielding their weapon family. Beelzebub now
exposes `AbilityRules.GetAnimationWeapon(name)` and surfaces it as guidance — the
`.beelz grant` reply ("✋ Wield <weapon> for the correct animation") and a ✋
weapon tag on each weapon-bound slot in the transform loadout. Opt-in admin
config `Grant_EnforceWeaponMatch` (default off) hard-blocks a universal grant of a
weapon-bound ability and steers to `.beelz weapon-grant`.
**For BCH:** `api info` now returns `weapon_anim=<family>` (v2) — surface it in the
loadout/collection UI so players know which weapon to wield. True per-ability
animation override would require a **client-side** mod
that drives the animation pipeline — same client-authority story as 7.1, and the
best fidelity path remains real transformation (7.1).

### 7.3 ✅ Ability tooltip text (v2)
`api info`'s `desc=` is now populated from resolved ability metadata (shipped
`ability_metadata.json` + ECS, with `%param%` substitution), falling back to the
admin-curated `Notes` then a generic string. Many NPC/V-Blood abilities ship no
localized text, so `desc=` may still be the generic fallback for those — BCH can
layer its own GUID-keyed text table on top if it wants richer copy.

### 7.4 🟡 Real-time cooldown feed
On-screen cooldown rings (§5) need a cooldown source. Beelzebub exposes static
cooldown values in ability metadata; a *live* remaining-cooldown feed (per slot,
per tick) is not yet in the API — design this when building the HUD.

---

## 8. Contract gotchas / open follow-ups

- **Index stability.** `list`/`info`/`grant` indices are stable only for a
  player session and shift after `forget`/`clear`/new captures. **Re-fetch
  `list` after any mutation** before issuing index-based commands.
- **`desc=` may be generic** on `api info` — populated from real metadata when V
  Rising ships text, else the curated/generic fallback (see §7.3).
- **Source convention `s=R|V`:** `R` = regular mob, `V` = V-Blood. V-Blood wins
  when the same ability/unit is later captured from a boss kill.
- **Mutations reply in human text**, not `[BEELZ:*]` — don't parse them; re-read
  or wait for the event.
- **`not_ready`:** any API call before init returns
  `[BEELZ:err] … code=not_ready` — gate on `api version`'s `ready=1`.

---

## 9. Related docs

- `docs/ABILITY_MAP_FORMAT.md` — full `ability_rules.json` schema (the admin
  curation surface BCH wraps).
- `docs/SUMMON_AS_ALLY.md` — summon system internals (the summon panel's backing
  behavior).
- `docs/INTEROP_BLOODCRAFT.md` — coexistence with Bloodcraft (shared patch
  surfaces; relevant if BCH talks to both).
- `docs/SETUP_GUIDE.md` — install / first-run.
- `Commands/ApiCommands.cs` — **canonical** wire API (`ApiVersion = 2`).

---

*Maintenance: when Beelzebub work changes any command, config key, `[BEELZ:*]`
line, or transform capability, update the relevant section here in the same
change. A repo hook reminds on edits to BCH-relevant files (see this project's
`CLAUDE.md` → "BCH integration handoff").*
