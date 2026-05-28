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
> `Beelzebub/Beelzebub/Commands/ApiCommands.cs` (`ApiVersion = 6`). If this doc
> and that file ever disagree, the file wins — and this doc should be corrected.
>
> **⚠️ v0.44.0 — PER-ABILITY BASELINE.** Transformation is now **Dracula & Morgana
> only**; every other unit's "jackpot" roll **Devours** the unit (grants its whole
> ability kit at once). New event `type=devour`; `type=transform-unlock` now fires
> **only** for Dracula/Morgana; `.beelz transform`/`transforms` resolve only those
> two; new admin `.beelz admin devour`. Arbitrary-unit transformation is a postponed
> phase-two feature (needs a client-side renderer). A BCH "transform browser" should
> expect at most the two boss entries; the collection UI should center on abilities
> (`api list` / `api bestiary`) + the Devour event.
>
> **⚠️ v0.45.0 — SUMMONS WORK UNTRANSFORMED + loadout discoverability.** Two changes,
> **no wire/event/ApiVersion change (still 6)** — both are additive behavior + chat-only:
> - **Summons are no longer transform-gated.** Casting a captured *summon* ability in
>   normal form (e.g. a Raise-Dead / Reinforcement) now spawns tracked player-allies just
>   like a transform summon did. **Implication for BCH:** `[BEELZ:active] none=1` (no active
>   transform) **no longer implies "no summons"** — summon state is now independent of
>   transformation. `.beelz summons [stash|restore|clear|status]` and `.beelz tp` work
>   untransformed; **new `clear` action** despawns all of a player's summons. Standalone
>   summons are despawned on disconnect (same `Transform_DespawnSummonsOnDisconnect` gate).
> - **Loadout surfacing.** New read-only player command **`.beelz loadouts`** (summary of
>   the universal "basic" set + each per-weapon set + which is active). No new wire surface —
>   BCH already has everything to build a loadout UI from **`api slots`** (`bucket=any` =
>   universal fallback, `bucket=<WeaponFamily>` = per-weapon override, `[BEELZ:slot-current]
>   weapon=` = active bucket). **Semantics to render correctly:** a per-weapon set overrides
>   the universal set *on its bound slots only*; swapping weapons auto-switches the active
>   set; **Unarmed is its own weapon family** (the spellcasting/fists loadout).
>
> **⚠️ v0.46.0 — ABILITY CAST TUNING (admin/server-side; no wire change, ApiVersion still 6).**
> New opt-in server feature: make long casts interruptible (dash/shield-cancel) and free the
> player to move once the cast finishes. **No new `[BEELZ:*]` line or event** — admin surface only:
> - Config key **`AbilityTuning_Enabled`** (default false) — appears automatically in `api config`
>   (reflection-streamed), so a BCH settings panel can toggle it via `.beelz admin set`.
> - New admin commands **`.beelz admin tune <ability> <interrupt|freemove|castspeed> <on|off|0..1>`**
>   and **`.beelz admin tune-list`**; `.beelz admin reload` re-applies tuning live (no restart).
> - Per-ability state lives in `ability_rules.json` (`AbilityMap[...]` fields `Interruptible`,
>   `FreeMoveAfterCast`, `CastMovementSpeed`) — not streamed over the wire; a BCH admin panel would
>   edit it via the chat commands above. The edit is GLOBAL — it also changes the source NPC/boss cast.
>
> **v0.47.0 — EXPERIMENTAL native-form test (admin-only; no wire change, ApiVersion still 6).**
> New admin command **`.beelz admin testform <wolf|bear|off>`** drops the admin into a native
> Wolf/Bear form (persistent ExoForm recipe) carrying their loadout's abilities — Phase-1
> feasibility probe for a future per-form loadout system (does the form hold through casting?).
> Routes through the normal transform lifecycle (`.beelz revert` / logout both clear it). **No
> BCH impact** — no `[BEELZ:*]` line/event, no config a panel needs. If the test holds, a Phase-2
> per-form loadout feature (parallel to the per-weapon buckets) would follow and *then* get a wire
> surface; until then BCH needs nothing here.
>
> **v0.48.0 — EXPERIMENTAL custom abilities on vanilla forms (server-side; no wire change, ApiVersion still 6).**
> Config `Forms_CustomAbilities_Enabled` (default false, auto-streams via `api config`): entering a
> vanilla Wolf/Bear form via the shapeshift wheel injects the player's loadout onto the form bar +
> strips the break-on-cast trigger so the form holds. Feasibility probe for a future per-form
> loadout system — **no BCH impact yet** (no `[BEELZ:*]` line/event). If it holds in-game, a Phase-2
> per-form loadout (parallel to per-weapon buckets, with `api`/grant surface) would follow and get a
> wire surface then.
>
> **v0.50.0 — INCLUSIVITY + transform-only wall down + admin config (no wire-format change, ApiVersion still 6).**
> Server/behavioral changes; the wire contract (lines/events) is unchanged, but two behaviors BCH
> should be aware of:
> - **The "transform only" wall is OFF by default.** New config `Grant_EnforceTransformOnly`
>   (default false): abilities previously rejected with *"reserved for .beelz transform"* now grant
>   to the normal bar. `api info`'s `transform_only=` field still reports the underlying flag, but
>   it is **not enforced** unless an admin turns enforcement on. BCH: an ability with
>   `transform_only=1` is still grantable/castable by default — don't pre-disable its button.
> - **Inclusive capture is ON by default.** New config `Capture_InclusiveMode` (default true): the
>   capturable/devourable pool is much larger (deny lists + difficulty gate bypassed). BCH's
>   collection list will simply show more abilities; no parsing change.
> - Both new keys **auto-stream via `api config`** (reflection), so a BCH settings panel can toggle
>   them through `.beelz admin set` like any other config. Full admin reference:
>   `docs/ABILITY_CONFIG.md`. A new `Defaults` block in `ability_rules.json` sets server-wide
>   damage/cooldown baselines (not wire-exposed).
>
> **v0.49.0 — BCH-test fixes (no wire change, ApiVersion still 6).** Three fixes; one is
> BCH-facing (advisory):
> - **`WeaponFamily` value set narrowed: `DualHammers` removed.** It was unobtainable cut content
>   (no craftable weapon in V Rising). It will no longer appear in any `weapons=` / `bucket=`
>   value, and `.beelz weapon-grant`/`weapon-slot` no longer accept it. **Action for BCH:** drop
>   `DualHammers` from any hardcoded weapon-family picker/list. The current valid set is: Sword,
>   GreatSword, Axe, Mace, Spear, Daggers, Crossbow, Longbow, Pistols, Reaper, Whip, Claws,
>   Pollaxe, Slashers, TwinBlades, Unarmed, FishingPole.
> - **Per-weapon loadouts now register reliably.** An ability explicitly placed in a weapon
>   bucket (`bucket=<WeaponFamily>` via `weapon-grant`) is now honored on equip regardless of its
>   name-derived family — previously some were silently dropped. No wire change; existing
>   `api slots` rendering is unaffected (it already streams per-bucket binds).
> - **Morgana transform server-crash fixed** (internal; a double form-buff teardown + a
>   re-activation guard). No BCH impact.
>
> **Last full audit:** Beelzebub **v0.44.0** (2026-05-27); **v0.45.0 + v0.46.0 + v0.47.0 + v0.48.0** deltas
> (summons untransformed, `.beelz loadouts`, `.beelz summons clear`, ability cast-tuning, the
> experimental native-form test, custom abilities on vanilla Wolf/Bear forms) folded in above —
> re-verified against the source
> (`ApiCommands.cs`, `BeelzCommands.cs`, `TransformCommands.cs`, `HotkeyCommands.cs`,
> `AdminCommands.cs`, `AbilityTuningService.cs`, `DevourService.cs`, `Config/Settings.cs`,
> `Services/AbilityRules.cs`, `Services/TransformService.cs`).

---

## 0.0 — ⚡ SINCE YOUR LAST BUILD (BCH baseline v0.44.0 → now v0.48.0) — READ THIS FIRST

**Context (2026-05-27):** BCH began building its Beelzebub UI against the **v0.44.0** handoff
(commit `306026f`, **ApiVersion 6**). Beelzebub then shipped **v0.45.0 → v0.48.0** the same day.
**The important news: nothing you already built breaks** — the wire API (`[BEELZ:*]` lines, event
types, ApiVersion) is UNCHANGED at **6**. This is the consolidated delta so you can fold the new
bits into the UI without re-reading every callout. (Per-version detail is in the callouts above.)

**A) Needs NO action — your existing build still works as-is:**
- ApiVersion is still **6**. No new/changed/removed `[BEELZ:*]` lines or event types. Your parser,
  state cache, and event-router need zero changes.
- All of v0.46–v0.48 (cast tuning, form work) is server-side/admin/config — no new read stream.

**B) BEHAVIOR CHANGE to account for in the UI (the one thing to actually fix):**
- **Summons are no longer tied to transformation (v0.45.0).** A player can have live summons while
  NOT transformed (casting a captured summon ability in normal form). So **`[BEELZ:active] none=1`
  (no active transform) no longer implies "no summons."** Do NOT gate a summon counter/panel on
  having an active transform — treat summon state as independent of transform state. `.beelz summons
  status` and the management commands all work untransformed now.

**C) New things you CAN surface (optional UI wins, no wire schema change):**
- **Per-weapon loadout UI — build it now.** `api slots` already streams it (unchanged): `bucket=any`
  = the universal "basic" set, `bucket=<WeaponFamily>` = a per-weapon override set,
  `[BEELZ:slot-current] weapon=<fam>` = the active bucket. Weapon-specific overrides universal *on
  its slots only*; the **server auto-switches** the active set on weapon swap (you just re-read
  `api slots` / reflect `slot-current` — no client logic needed). Unarmed is its own family. New
  human-readable helper if useful: `.beelz loadouts`.
- **New player command** for a summons panel button: `.beelz summons clear` (despawn all the
  player's summons) — alongside the existing `stash|restore|status`.
- **New config keys** auto-appear in `api config` (same `[BEELZ:config]` rows, no schema change):
  `AbilityTuning_Enabled`, `Forms_CustomAbilities_Enabled` — a settings panel toggles them via
  `.beelz admin set <key> <value>` like any other.
- **New admin commands** (if you build admin panels): `.beelz admin tune <ability>
  <interrupt|freemove|castspeed> <on|off|0..1>`, `.beelz admin tune-list`, `.beelz admin testform
  <wolf|bear|off>`.

**D) Experimental — NO BCH wire surface yet (a Phase-2 delta will follow):**
- Cast tuning (interrupt / post-cast move-unlock) and custom-abilities-on-forms (Wolf/Bear test) are
  live as server/admin features but expose nothing new over the wire. If the form test holds in-game,
  a Phase-2 **per-form loadout** (parallel to the per-weapon buckets, with an `api`/grant surface +
  an ApiVersion bump) follows — you'll get a new "since your last build" section here then.

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

## 0.5 — BCH build plan (architecture · lifecycle · phased roadmap) — START HERE

This section is the *how to assemble it* layer. §1–§7 are the wire reference; this
is the recommended structure so the BCH-side build stays comprehensive, testable,
and resilient as Beelzebub's API grows. Build it as a self-contained **Beelzebub
subcomponent** inside BCH (its own folder/namespace), so it can evolve independently
of BCH's existing Bloodcraft integration.

### 0.5.1 Module breakdown (BCH side)
Keep four concerns separate — it makes the integration unit-testable and means a new
Beelzebub API field rarely touches more than one layer:

- **`BeelzClient`** — the *only* thing that talks to the server. `Send(string cmd)`
  issues `.beelz api …` reads and `.beelz …` mutations over chat. No parsing here.
- **`BeelzWireParser`** — pure/stateless. Turns an inbound `[BEELZ:*]` line into a
  typed record. Must handle: the `[BEELZ:<tag>] k=v k=v` grammar (split on spaces,
  first token is the tag, rest are `key=value`); comma-lists (`k=a,b,c`); multi-record
  **streams** that terminate on `[BEELZ:end] cmd=<name> count=<n>`; and
  `[BEELZ:err] cmd= code= msg=` (treat `code=not_ready` as "retry later").
- **`BeelzState`** — the cached client model (see 0.5.3). Single source of truth the
  UI binds to; updated by parsed reads and by events. UI never parses raw lines.
- **`BeelzEventRouter`** — routes `[BEELZ:event] type=…` lines to `BeelzState` deltas +
  targeted UI refreshes (prefer applying the event's delta over a full re-hydrate).
- **UI components** bind to `BeelzState`: Collection book · Loadout editor · Action-bar
  buttons + cooldown rings · Transform panel · Settings/Admin panel.

### 0.5.2 Integration lifecycle (state machine)
1. **Detect** — on world-join, `.beelz api version`. No reply or `ready=0` or
   `code=not_ready` → retry with backoff. Gate everything on `ready=1`.
2. **Subscribe** — `.beelz api bch on` (subscribes this player to the live event stream).
3. **Hydrate** — initial snapshot: `api list`, `api slots`, `api transforms`,
   `api active`, `api progress`, `api hotkeys`, `api bestiary`; for panels also
   `api config`, `api rules`, `api cooldowns`.
4. **Live** — react to `[BEELZ:event]` lines (apply delta → refresh affected UI). After
   any **mutation** command (they reply in human text, *not* API), re-fetch the affected
   `api` read OR rely on the event it emits.
5. **Reconnect** — RE-HYDRATE and re-send `api bch on`. Do **not** assume a fresh login is
   untransformed — a transform persists across a brief DC within
   `Transform_ReconnectGraceSeconds`, so re-read `api active`/`api transforms`.

### 0.5.3 Cached client state (`BeelzState` fields)
- `apiVersion`, `pluginVersion`, `ready`
- `captures[]` `{index, source R|V, unitGuid, unitName, abilityGuid, abilityName, …}` ← `api list`
- `slots{1..6}` (universal) + `weaponSlots{family→{1..6}}` + `currentWeapon` ← `api slots`
- `transforms[]` (Dracula/Morgana only now) ← `api transforms`
- `active` `{unitGuid, phase, phases, ttl}` | none ← `api active`
- `hotkeys[]` `{name, abilityGuid, abilityName}` ← `api hotkeys` (these drive the action-bar buttons)
- `progress`, `bestiary[]`, `config{}`, `cooldowns{category→remaining}`
- **Index caveat:** capture indices are session-stable but shift after `forget`/`clear`/new
  captures — re-fetch `api list` before any index-based command (`grant`, `info <index>`, …).

### 0.5.4 Phased build order (ship value early)
- **Phase A — MVP (the headline win):** handshake + parser + a **Collection view**
  (`api list`/`bestiary`/`progress`) + **on-screen action-bar buttons** — render each
  `api hotkeys` binding as a button that sends `.beelz cast <name>`. This is the original
  "abilities beyond the 6 slots" vision and the fastest visible payoff. Wire the `capture`
  and `devour` events so the collection updates live.
- **Phase B — Loadout & transform:** the **Loadout editor** (6 slots, universal + weapon
  buckets; `grant`/`weapon-grant`/`unslot`) and the **Transform panel** for Dracula/Morgana
  (`transform`/`revert`/`phase`/`summon`/`detonate` + the summon stash/restore panel),
  all event-driven.
- **Phase C — Admin & polish:** a **Settings/Admin panel** rendered *generically* from
  `api config` (`section/key/value/type` → controls; mutate via `admin set`) plus `api rules`
  and the grant/devour/recovery admin commands; **cooldown rings** (transform cooldowns from
  `api cooldowns`; a force-cast button's cooldown = `api info` `cooldown_seconds × cooldown_scale`).
- **Phase D — phase two (research):** client-side **model rendering** (§7.1) and **animation
  fidelity** (§7.2) — the hard, BCH-only frontier. Beelzebub already emits the events you'd hook.

### 0.5.5 Minimum viable integration
`api version` (gate `ready=1`) → `api bch on` → `api hotkeys` → render a button per binding →
click sends `.beelz cast <name>`. That alone is an on-screen expanded action bar. Everything
else layers on top of the same client + parser + state.

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
| `.beelz api transforms` | `[BEELZ:tx]` … `[BEELZ:end]` | Transform unlocks + matrix attrs: `i= s= u= un= enabled= difficulty= tier= damage_scale= cooldown_scale= health_scale= speed_scale= type= full_replace= scaling_mode=`. **(v6) Now 0–2 entries — Dracula/Morgana only.** |
| `.beelz api active` | `[BEELZ:active]` | Active transform: `u= un= s= ttl=<sec>\|toggle` + phase info; or `none=1` |
| `.beelz api info <index>` | `[BEELZ:info]` | One ability's full tooltip data: `desc=` (real ability description, %params% substituted), `weapons=`, `weapon_anim=<family\|None>` (animation weapon, v2), `school=` (v2), `cooldown_seconds=` (v2), `forms=`, `transform_only=`, `enabled=`, `difficulty=`, `damage_scale=`, `cooldown_scale=` |
| `.beelz api progress` | `[BEELZ:progress]` | Collection %: `abilities_captured= abilities_total= abilities_pct= transforms_unlocked= transforms_total= transforms_pct=` + V-Blood breakdowns |
| `.beelz api rules` | `[BEELZ:rules]` | Loaded filter rules: `version= deny_patterns= allow_patterns= deny_guids= allow_guids=` |
| `.beelz api transform-config` | `[BEELZ:tx-config]` … `[BEELZ:end]` | One line per category `R`/`V`/`S` (shard boss): `src= mode=Toggle\|Timed\|Disabled duration= cooldown=` (count=3, **`src=S` added v0.43.0**). Live cooldown remaining (incl. shard) is in `api cooldowns`. |
| `.beelz api catalog` | `[BEELZ:catalog-summary]` | `abilities= units= server_mode=Basic\|Brutal` |
| `.beelz api catalog units [page]` | `[BEELZ:catalog-unit]` … `[BEELZ:end]` | Curated boss-KIT reference, 40/page, with matrix attrs. **(v6) Read as Devour/collection targets, NOT transform targets** — only Dracula/Morgana transform. |
| `.beelz api catalog abilities [page]` | `[BEELZ:catalog-ability]` … `[BEELZ:end]` | Full curated ability list, 40/page, with matrix attrs |
| `.beelz api hotkeys` | `[BEELZ:hotkeys-config]`, `[BEELZ:hotkey]`, `[BEELZ:end]` | Config footer (`enabled= max=`) + named hotkey bindings |
| `.beelz api verbosity` | `[BEELZ:verbosity]` | `level=Silent\|Summary\|Verbose default=<server default>` |
| `.beelz api bestiary [page]` | `[BEELZ:bestiary]` … `[BEELZ:end]` | Collection book — one line per collected unit: `u= un= s=R\|V captured=X total=Y transform=0\|1` (v2). Cross-ref `api list` (per-ability) for which abilities. Page size 40. |
| `.beelz api config` | `[BEELZ:config]` … `[BEELZ:end]` | **(v3)** Every setting: `section= key= value= type= editable=1`. Foundation for a BCH settings panel; all keys are settable via `.beelz admin set`. |
| `.beelz api cooldowns` | `[BEELZ:cooldown]` … `[BEELZ:end]` | **(v3)** Per-category transform cooldown remaining: `category=regular\|vblood\|shard remaining=<sec>`. For cooldown timers on transform buttons. |

> **(v3)** `api transforms` and `api catalog units` include `shard=0\|1` so BCH can badge/group the shard bosses. The shard bosses — **Dracula, Adam the Firstborn, Solarus the Immaculate, The Winged Horror (Talzur), Megara the Serpent Queen, Gorecrusher the Behemoth** (matched by display/prefab name against `Transform_ShardBossNames`; default also lists "Morgana" as an alias for Megara, whose prefab is `CHAR_…Blackfang_Morgana`) — have their own transform mode/duration/cooldown and a **separate cooldown bucket** (`category=shard` in `api cooldowns`). Don't hard-code the shard list in BCH; read it live from `api config` (`Transform_ShardBossNames`) so admin edits flow through.

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
  | `devour` | `s=R\|V u= un= count=` | **(v6)** Jackpot DEVOURED the unit — `count` abilities granted at once. Fires for every non-boss unit (replaces the old per-unit transform unlock). Re-fetch `api list`. |
  | `transform-unlock` | `s=R\|V u= un=` | A transformation unlocked. **(v6) Now ONLY fires for Dracula & Morgana** — the only renderable forms. |
  | `slot-granted` / `slot-cleared` | `slot= [a= an=]` | Universal-bucket grant/clear |
  | `weapon-slot-granted` / `weapon-slot-cleared` | `weapon= slot= [a= an=]` | Weapon-bucket grant/clear |
  | `hotkey-set` / `hotkey-cleared` | `name= [a= an=]` | Named hotkey bind/clear |
  | `transform-activated` | `u= un=` | Player transforms |
  | `transform-ended` | `u= un= reason=` | Any revert. `reason` ∈ `manual` · `switching-transformation` · `auto` · `admin-clear` · `admin-revoke` · `admin-revert-all` · `admin-wipe-all` |
  | `transform-phase-shift` | `u= un= phase=` | Form/phase swap (`.beelz phase` or Auto-HP) |
  | `forget` / `forget-transform` | `a= u=` / `u=` | Capture / unlock deleted (v2) |
  | `cleared` | — | Player wiped all captures + slots (v2) |
  | `detonate` | `u=` | Player manually fired a transform's detonation AoE via `.beelz detonate` (v2) |
  | `config-changed` | `key= value=` | An admin changed a setting via `.beelz admin set` (v3). Re-fetch `api config`. (Currently sent to the acting admin; broadcast-to-all-subscribers is a follow-up.) |
  | `cast` | `a= an=` | Player force-cast an ability via `.beelz cast` (v4 — expanded action bar). |
  | `summon` | `u= ability=` | Player force-cast a transform's signature add-summon via `.beelz summon` (v5). Spawns become allies. |

---

## 4. Mutations BCH issues — ✅ shipped

BCH sends these exactly as a player would type them. Replies are
**human-readable text**, not API format — treat them fire-and-forget and
re-fetch the affected read command (or wait for the event).

**Player:** `.beelz grant <slot 1-6> <index>` · `.beelz unslot <slot>` ·
`.beelz resetbar` (v0.43.8 — end any active transform + clear ALL slot bindings
[universal + weapon] → vanilla in-game bar; keeps captures/unlocks; emits a
`slot-cleared` event per previously-bound slot, so no new event type. A natural
"reset my bar" button alongside `refresh`) ·
`.beelz transforms [vblood\|shard\|regular]` / `.beelz list [vblood\|shard\|regular] [page]`
(v3 filter — also splits shard bosses into their own group) ·
`.beelz weapon-grant <weapon\|auto> <slot> <index>` ·
`.beelz weapon-unslot <weapon\|auto> <slot>` ·
`.beelz loadouts` (v0.45 — human-text summary of the universal set + each per-weapon set +
the active weapon; the wire-data equivalent is `api slots`) ·
`.beelz transform <index\|name>` · `.beelz revert` · `.beelz phase [n]` ·
`.beelz preview <index\|name>` (human-text — abilities you'd get per phase if you
transformed into a unit; backs a "preview before committing" affordance in the
transform browser) ·
`.beelz refresh` (re-apply the correct spell bar on demand — fixes a blank/wrong bar
after reverting, leaving a travel/wolf/bat form, dismounting a horse, or a weapon
quirk; a good "fix my bar" button. v0.41+) ·
`.beelz detonate` (fire a transform's signature AoE on demand, if it has one — a
natural candidate for a BCH HUD button) ·
`.beelz summons <stash\|restore\|clear\|status>` (status replies human-text; live/stashed
counts. **v0.45:** works for summons cast UNTRANSFORMED too — not just transform summons;
new `clear` despawns all your summons) ·
`.beelz preset <save\|load\|list\|delete> <name>` ·
`.beelz hotkey set <name> <index>` · `.beelz hotkey clear <name>` · `.beelz hotkey list`
(named extra-ability bindings; `set` rejects with a limit message at `Hotkeys_MaxPerPlayer`) ·
`.beelz cast <hotkey name\|index>` (**v4 — expanded action bar:** force-cast any
captured ability on demand, beyond the 6 slots; respects the ability's cooldown.
**This is the BCH-button mechanism** — render each `api hotkeys` binding as a button
that invokes `.beelz cast <name>`. Gated by `Hotkeys_Enabled`; count capped by
`Hotkeys_MaxPerPlayer`. Emits `[BEELZ:event] type=cast`.) ·
`.beelz summon [n]` (**v5 — signature summon:** force-cast a transformed unit's
add-summon — e.g. the Toad King's frogs, the Werewolf Chieftain's caged wolves — that
the boss normally only triggers at a low-HP soft phase. No-arg lists options (or casts
the only one); `<n>` picks one. Spawns become allies. Only units with a registered
summon respond; cooldown `Transform_SummonCooldownSeconds`. A natural BCH HUD button
per `api`-exposed summon. Emits `[BEELZ:event] type=summon`.) ·
`.beelz bestiary [page]` · `.beelz bestiary unit <name>` (collection book — per-unit X/Y abilities + transform status) ·
`.beelz forget <i>` · `.beelz forget-transform <i>` ·
`.beelz clear CONFIRM` (v0.43.23 — now requires the literal `CONFIRM` token; bare `.beelz clear`
replies with a warning + the count of what would be lost and does **not** wipe. A BCH "clear"
button must send `.beelz clear CONFIRM`. Still emits `[BEELZ:event] type=cleared` on success.) ·
`.beelz verbosity <silent\|summary\|verbose>`.

> **Discoverability (v0.43.23).** Each command group has its own help command —
> `.beelz admin help`, `.beelz api help`, `.beelz hotkey help` — and `.beelz commands` is a full
> sectioned list. These reply in human text (not `[BEELZ:*]`); they're for players typing in chat,
> not a machine surface.

> **Note — human-text player reads.** A few player commands reply in human text, not
> `[BEELZ:*]`: `.beelz list` / `.beelz search <term>` / `.beelz info <index\|name>` /
> `.beelz active` / `.beelz current` / `.beelz catalog [page]` / `.beelz preview`. For
> machine reads BCH should use the `api` equivalents (`api list`, `api info <index>`,
> `api active`, `api catalog …`) — these human-text ones are for players typing in chat.

**Admin** (BCH admin panel; full list in §6): grant/revoke (ability +
transform), force/clear-transform, set/clear slot (universal + weapon),
deny/allow patterns, transform mode/duration/cooldown, difficulty, freeze-
captures, revert-all, snapshot, inspect/progress, wipe-all.

---

## 5. What BCH should build (player UI)

| Feature | Backed by | Status |
|---|---|---|
| **Collection book** — per-unit progress (X/Y abilities + transform), captured-abilities grid, search, tooltips | `api bestiary` (per-unit X/Y + transform), `api list`, `api info`, `api catalog abilities` | ✅ data ready · 🟡 UI |
| **Slot loadout editor** — drag ability → one of 6 slots, universal vs weapon-specific buckets | `api slots` + `grant`/`weapon-grant`/`unslot` | ✅ data ready · 🟡 UI |
| **On-screen ability buttons + cooldown display** (the original BCH vision) | spell-bar state via `api active`/`api slots`; cooldowns from ability metadata | 🟡 needs client render + a cooldown feed |
| **Transform panel (Dracula & Morgana only)** — the two real transforms: activate/revert, phase switch, signature summon/detonate | `api transforms` (now 0–2 entries) + `transform`/`revert`/`phase`/`summon`/`detonate` | ✅ data ready · 🟡 UI |
| **Devour-target / boss-kit reference** — `api catalog units` is now a boss-KIT reference (not transform targets); pair with `api bestiary` to show ABILITY-collection progress | `api catalog units`, `api bestiary` | ✅ data ready · 🟡 UI |
| **Phase switcher** — for multi-form bosses (Dracula warrior↔bloodmage) | `api active` (phase/phases) + `phase <n>` | ✅ · 🟡 UI |
| **Summon panel** — live/stashed counts, stash for waygates | `summons status`/`stash`/`restore` | ✅ · 🟡 UI |
| **Mounted-summon behavior** (v0.42) — on a horse, summons auto-stash or keep following per `Transform_MountedSummonMode` (`Stash`\|`Follow`). Server-driven & automatic; surface the setting in the admin/settings panel via `api config` + `admin set`. No player command needed. | `api config` (`Transform_MountedSummonMode`) | ✅ |
| **Reconnect resilience** (v0.43.7) — a transform + its summons survive a brief disconnect and resume on reconnect within `Transform_ReconnectGraceSeconds` (default 90s; `0` = revert on DC, `-1` = keep until manual revert). On EVERY login Beelzebub reconciles state so a form buff stranded from a previous session is cleared (no stuck bar). Server-driven & automatic — no new command/event. Note: a player's transform state may now **persist across a brief DC**, so re-fetch `api active`/`api transforms` on (re)connect rather than assuming a fresh login is untransformed. | `api config` (`Transform_ReconnectGraceSeconds`) | ✅ |
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
- **Runtime config (v3):** `admin set <key> <value>` — set ANY setting live (persists
  to the `.cfg`); emits `config-changed`. Pair with `api config` (read all keys) for a
  full BCH settings panel. Covers the shard-boss settings (`Transform_*_ShardBoss`,
  `Transform_ShardBossNames`) and everything previously `.cfg`-only (drop-chances, pity, …).
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

**Exact admin signatures** (all `adminOnly`; `<player>` = in-game character name,
matched fuzzily; `<unitGuid>`/`<abilityGuid>` = integer PrefabGUIDs from `api list` /
`api transforms` / `api catalog`):

| Command | Signature |
|---|---|
| Filter rules | `admin rules` · `admin deny <pattern>` · `admin undeny <pattern>` · `admin allow <pattern>` · `admin unallow <pattern>` · `admin reload` |
| Runtime config | `admin set <key> <value>` (any `api config` key; live + persists; emits `config-changed`) |
| Transform settings | `admin transform mode <regular\|vblood> <toggle\|timed\|disabled>` · `admin transform duration <regular\|vblood> <seconds>` · `admin transform cooldown <regular\|vblood> <seconds>` · `admin transform show` |
| Difficulty | `admin difficulty [basic\|brutal]` (no arg = show) |
| Grant / revoke | `admin give <player> <unitGuid> <abilityGuid>` · `admin revoke <player> <unitGuid> <abilityGuid> [reason]` · `admin give-transform <player> <unitGuid>` · `admin revoke-transform <player> <unitGuid> [reason]` |
| **Devour (v6)** | `admin devour <player> <unitGuid>` — grant the player ALL of a unit's eligible abilities at once (the admin alternative to transformation for non-renderable units) |
| Force transform | `admin force-transform <player> <unitGuid>` (bypasses unlock+cooldown) · `admin clear-transform <player>`. **(v6) `give-transform`/`force-transform` accept ONLY Dracula & Morgana** — other units reply pointing to `admin devour`. |
| Remote slots | `admin set-slot <player> <slot 1-6> <abilityGuid>` · `admin clear-slot <player> <slot>` · `admin set-weapon-slot <player> <weapon> <slot> <abilityGuid>` · `admin clear-weapon-slot <player> <weapon> <slot>` (admin binds bypass the TransformOnly/Enabled guards — reply notes a `[WARNING]`) |
| Inspect / recovery | `admin inspect <player>` · `admin progress <player>` · `admin snapshot` · `admin buffs [player]` (v0.43.9 diagnostic — dumps a player's live buffs, entity prefab, equipped-ability slots + override sources to the server log) · `admin respawn [player]` (v0.43.15 — respawn the character in place via the engine's RespawnCharacter; preserves inventory/progress) · `admin clearslotmods [player]` (v0.43.17 — clears orphaned ability-slot modifications) · `admin rebuildslots [player]` (v0.43.19 — safe re-sync of active ability slots to their base values) · `admin copy-collection <player>` / `admin paste-collection <player>` (v0.43.20 — back up a player's captures+transforms to an admin clipboard and paste onto another character; additive, skips dupes) · `admin reset-character <player> CONFIRM-RESET` (v0.43.21 — unbind Steam ID + kick → player creates a fresh character on next login; Beelzebub collection preserved; self-contained, no KindredCommands needed) |
| Bulk / ops | `admin revert-all` · `admin freeze-captures <on\|off\|status>` · `admin scan-abilities` · `admin desummon <player>` · `admin desummon-all` · `admin wipe-all CONFIRM-WIPE` (destructive — literal token required) |

> **Shard-boss transform settings:** there is **no** `admin transform … shard` variant —
> `admin transform mode/duration/cooldown` accept only `regular`/`vblood`. Change the
> shard-boss equivalents through `admin set Transform_Mode_ShardBoss <…>` /
> `Transform_DurationSeconds_ShardBoss` / `Transform_CooldownSeconds_ShardBoss` /
> `Transform_ShardBossNames`. (All settable because `admin set` covers every `api config` key.)
>
> **Reply format:** admin commands reply in **human text** (often multi-line) and
> audit to `LogOutput.log` as `[Beelz AUDIT] admin=… action=… target=…`. Treat them
> fire-and-forget; re-read the relevant `api` endpoint or wait for an event.

A BCH **admin panel** would wrap these as forms/toggles, reading current state
from `api config` / `api rules` / `api transform-config` / `api cooldowns` /
`api catalog units|abilities`. (`admin snapshot` is a chat-only human-text overview —
there is no `api snapshot`; build the panel's summary from the `api` reads instead.)

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
**Transform cooldowns: ✅ (v3)** — `.beelz api cooldowns` gives per-category
(regular/vblood/shard) remaining seconds for transform-button timers.
**Still open:** a *per-ability spell-slot* live cooldown feed (remaining per slot,
per tick) for on-screen ability rings — Beelzebub exposes static cooldown values
(`api info` `cooldown_seconds`) but not live per-slot remaining; design when building the HUD.

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
- **`api transform-config` now includes shard (v0.43.0).** It emits `R`+`V`+`S`
  (`count=3`) — `src=S` is the shard-boss category. Live shard cooldown remaining is in
  `api cooldowns` (`category=shard`). (You can also read the raw keys via `api config`:
  `Transform_*_ShardBoss`.)
- **`config-changed` is per-admin today.** It's sent only to the admin who ran
  `admin set`, not broadcast to all subscribers — so a BCH panel on another client
  won't auto-refresh after someone else's change. Re-fetch `api config` on panel open
  / on a timer until the broadcast-to-all follow-up lands.
- **Mounted-summon behavior (v0.42)** is fully server-side and automatic — there's no
  command or event for it; it's a single config key (`Transform_MountedSummonMode`,
  `Stash`\|`Follow`) read via `api config` and changed via `admin set`. Dismounting also
  re-applies the transform bar, so no client action is needed around mounting.
- **Config is reflection-dumped.** `api config` enumerates every `ConfigEntry` on the
  settings class, so new keys appear automatically (no doc/contract bump). BCH should
  render the settings panel generically from `section/key/value/type` rather than
  hard-coding keys — new settings then "just appear."
- **Force-cast cooldown is scaled (v0.44.0).** `.beelz cast` now multiplies the ability's
  cooldown by its `CooldownScale` before enforcing it (floor 1s). For an accurate cooldown
  ring on a force-cast/hotkey button, use `cooldown_seconds × cooldown_scale` (both from
  `api info`). Native spell-bar slot cooldowns are unchanged (V Rising's own).
- **Transform is Dracula/Morgana only (v0.44.0).** Don't build a generic "transform into any
  unit" browser — `api transforms` returns at most those two. The collection/progression UI
  should center on **abilities** (`api list`/`api bestiary`/the `devour` event), with a small
  dedicated panel for the two boss transforms. Arbitrary-unit transformation is §7.1 (phase two).

---

## 9. Related docs

- `docs/ABILITY_MAP_FORMAT.md` — full `ability_rules.json` schema (the admin
  curation surface BCH wraps).
- `docs/SUMMON_AS_ALLY.md` — summon system internals (the summon panel's backing
  behavior).
- `docs/INTEROP_BLOODCRAFT.md` — coexistence with Bloodcraft (shared patch
  surfaces; relevant if BCH talks to both).
- `docs/SETUP_GUIDE.md` — install / first-run.
- `Commands/ApiCommands.cs` — **canonical** wire API (`ApiVersion = 6`).
- `Services/SummonRegistry.cs` — curated unit→signature-summon map for `.beelz summon` (v5).
- `Services/DevourService.cs` — reads a unit's prefab kit + grants it all at once (the v6 Devour jackpot, migration, and `admin devour`).

---

*Maintenance: when Beelzebub work changes any command, config key, `[BEELZ:*]`
line, or transform capability, update the relevant section here in the same
change. A repo hook reminds on edits to BCH-relevant files (see this project's
`CLAUDE.md` → "BCH integration handoff").*
