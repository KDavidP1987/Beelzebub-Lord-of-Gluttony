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
> `Beelzebub/Beelzebub/Commands/ApiCommands.cs` (`ApiVersion = 22`). If this doc
> and that file ever disagree, the file wins — and this doc should be corrected.
>
> ## 📥 PENDING BCH REQUESTS — implement in Beelzebub
>
> *(Added 2026-05-31 from the BCH session. The BCH client is already coded to CONSUME everything
> below — these need the server-side EMIT/behavior. Implement in this Beelzebub session, then fold
> each into the dated callouts above and tick it off here. Priority order.)*
>
> **✅ 2026-05-31 (BCH v0.20.0 round) — #7 + #8 are DONE (v0.100.0 re-release, ApiVersion 21→22).** Both
> shipped exactly as proposed below: `api tform-kit <unit>` + `api tform-binds <unit>` (#7) and the admin
> `api broadcast-msgs <complete|leaderboard>` (#8). All additive (new read commands + new `[BEELZ:*]`
> lines); the plugin version stays **0.100.0** but **ApiVersion bumped to 22** so BCH can capability-detect
> the new reads (`api>=22` ⇒ structured transform-loadout + broadcast-pool reads available). The emitted
> shapes match the proposals below verbatim except: `an=` carries the **raw prefab name** (matching the
> existing `[BEELZ:slot]` / `[BEELZ:form-slot]` convention BCH already humanizes), and `broadcast-msg`
> `idx=` is **1-based** (so BCH can feed it straight into `admin broadcast-msg edit/remove <n>`). See the
> dated "v0.100.0 — STRUCTURED TRANSFORM-LOADOUT + BROADCAST READS" callout below for the final contract.
>
> 1. **✅ DONE (v0.100.0, ApiVersion 21): `catalog-ability` now emits `a=<guid>`, `unit=<name>`, `unitguid=<int>`.**
>    `a=` is the ability's PrefabGUID; `unit=` is the primary source-NPC name **SafeToken-encoded like
>    `desc=`/`notes=`** (decode with the same reverse), `-` when unknown; `unitguid=` is the source-NPC
>    PrefabGUID, `0` when unknown. From `ability_metadata.json` SourceNpcs. BCH can now fill the Unit column +
>    ID + search-by-GUID for UNCAPTURED abilities from the single existing scan (graceful when `-`/`0`).
>
> 2. **✅ ADDRESSED (v0.100.0).** Two scopes now exist: **`api catalog abilities`** (player/default) streams
>    the COLLECTIBLE set — abilities actually capturable under the active allow/deny/difficulty rules (honest
>    count/% regardless of `Capture_InclusiveMode`); **`api catalog abilities-all`** (admin-only) streams
>    EVERY real ability group regardless of enable/deny/difficulty for config. Both carry `enabled=` per row,
>    so a client can also filter the admin list to the enabled set. (`[BEELZ:end]` cmd is `catalog-abilities`
>    vs `catalog-abilities-all` respectively.)
>
> 3. **Ability name/description metadata coverage.** The "No name / no description" abilities are gaps in
>    `ability_metadata.json` (desc ~26%, school ~9% as of v0.60). Filling more (or via
>    `ability_metadata_overrides.json`) auto-populates BCH tooltips + Bestiary "Missing" rows — no wire change.
>
> 4. **Grant should refresh the action bar without needing a weapon swap (BCH "A2/T1").** v0.63 sets
>    `AbilityGroupSlot.DirtyTag` + runs the slot system server-side, but the CLIENT HUD doesn't re-resolve
>    until a trigger (weapon swap). Investigate a server-side nudge to re-resolve the bar / slot 0 on grant,
>    or confirm it's an engine limitation — BCH can't safely force the client HUD (past crash class).
>
> 5. **✅ DONE (v0.100.0): `unslot` / `weapon-unslot` / `form-unslot` now accept the `primary` / `ultimate`
>    slot tokens** (via `TryParseSlotToken`). BCH can clear a single primary/ultimate bind directly instead
>    of routing through `.beelz clearbar <bucket>`.
>
> 6. **Optional / low priority: machine-readable SUMMON STASH STATE (token or event).** *(Added 2026-05-31.)*
>    BCH v0.19 (player-feedback round 8) moved summon management out of the Transforms tab into the **Hotkeys
>    tab** and added a new on-screen **Summons overlay** — a draggable panel whose primary button toggles
>    **Stash ⟷ Restore** (plus Recall/Clear). Because `summons status` replies as **human text** and nothing
>    is **pushed** on stash/restore, that toggle's stashed/restored state is currently **client-side
>    optimistic** (it flips its own label on each press; it can drift if the server stashes/restores summons
>    for another reason — e.g. mounted auto-stash, or another client/command). This is acceptable and BCH
>    ships it that way. To make it authoritative, either:
>    - add a parseable token to the `summons status` reply (e.g. `summons:live=<n>;stashed=<n>` alongside the
>      human text — BCH already strips/parses that kind of tagged tail), **or**
>    - emit a tiny `[BEELZ:event] type=summons state=<stashed|live> live=<n> stashed=<n>` on every
>      stash/restore/clear/auto-stash so BCH can set the toggle from the authoritative state.
>    BCH would consume whichever is easier; no change is also fine (overlay stays optimistic). No
>    other recent BCH work (admin-tab gating, text-caret fixes, ID-from-captures) needs anything
>    server-side — those are all client-only, and ID-from-captures is already covered by item #1.
>
> 7. **✅ DONE (v0.100.0, ApiVersion 22): STRUCTURED TRANSFORM-LOADOUT READS — kit + current binds.**
>    Shipped as `api tform-kit <unit>` (→ `[BEELZ:tform-ability] unit= idx= a= an=` + `[BEELZ:end]
>    cmd=tform-kit unit= count=`) and `api tform-binds <unit>` (→ `[BEELZ:tform-slot] unit= phase= slot= a=
>    an=` + `[BEELZ:end] cmd=tform-binds unit= count= phases=<n>`). `<unit>` resolves exactly like
>    `.beelz tform`. `an=` is the raw prefab name (humanize client-side like the slot lines); the `phases=`
>    footer is the optional nicety (how many phases the form has, incl. player-defined custom). *(Original
>    request preserved below.)* BCH 0.20.0's new transform-loadout editor (Transforms
>    tab) drives `.beelz tform <unit> set|clear|defaults` fine, but it has to **parse the human-text
>    `.beelz tform <unit> abilities` reply** for the kit, and it has **no way to read the player's CURRENT
>    binds** — so today it's "build-and-apply" (it can't show what's already bound per phase/slot). Two small
>    structured reads fix both. Suggested (mirrors the existing `[BEELZ:slot]` / `[BEELZ:form-slot]` +
>    `api slots` pattern; both `unit` accepts the same index/name/guid `tform` already resolves):
>    - **Kit read** — `.beelz api tform-kit <unit>` → one line per eligible ability:
>      `[BEELZ:tform-ability] unit=<int> idx=<int> a=<int abilityGuid> an=<name SafeToken-encoded>`
>      then `[BEELZ:end] cmd=tform-kit unit=<int>` (chunk with `part=k/n` if a line ever exceeds the reply
>      cap, like `catalog-ability`). This is the same data as `UnitKitService.FullEligibleKit` — just emitted
>      as `[BEELZ:*]` instead of `ctx.Reply` text.
>    - **Current binds read** — `.beelz api tform-binds <unit>` (or `tform-loadout`) → one line per bound
>      slot the player has customized: `[BEELZ:tform-slot] unit=<int> phase=<int> slot=<0-7> a=<int abilityGuid>
>      an=<name SafeToken-encoded>` then `[BEELZ:end] cmd=tform-binds unit=<int>`. Empty (no custom binds) =
>      just the end line. Sourced from the `TransformLoadouts` block (keyed `"unit:phase"`) you persist.
>      *(Optional nicety: include `phases=<n>` on the end line so BCH knows how many phases the form actually
>      has, instead of always offering 1/2.)*
>    With these, BCH renders an 8-slot × per-phase grid showing the bound ability in each slot (and "empty →
>    curated default" otherwise), exactly like the weapon/form loadout panels — no human-text parsing.
>
> 8. **✅ DONE (v0.100.0, ApiVersion 22): STRUCTURED `broadcast-msg list` READ.** Shipped as the admin
>    `api broadcast-msgs <complete|leaderboard>` → one `[BEELZ:broadcast-msg] pool= idx= text=` per message
>    (`idx` 1-based to match `admin broadcast-msg edit/remove <n>`; `text=` SafeToken-encoded, clamped 256)
>    + `[BEELZ:end] cmd=broadcast-msgs pool= count=`. Empty pool = just the end line. *(Original request
>    preserved below.)* BCH 0.20.0's announcements editor manages the pools via
>    `.beelz admin broadcast-msg <pool> add|edit|remove`, but to show the current messages it has to **parse
>    the human-text `... list` reply** (the `  [n] <text>` lines). A structured read makes that robust.
>    Suggested: `.beelz api broadcast-msgs <complete|leaderboard>` → one line per message:
>    `[BEELZ:broadcast-msg] pool=<complete|leaderboard> idx=<int> text=<SafeToken-encoded>` then
>    `[BEELZ:end] cmd=broadcast-msgs pool=<...>`. SafeToken-encode `text=` exactly like `desc=`/`notes=`
>    (messages contain spaces/`%player%`/punctuation). Empty pool = just the end line. BCH would read this
>    instead of the chat-text `list` (the `add|edit|remove` write commands are already fine as-is).
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
> - Config key **`Abilities_ApplyConfig`** (default false) — appears automatically in `api config`
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
> **ℹ️ v0.88.0 — SERVER ANNOUNCEMENTS (config-only; no wire/event/ApiVersion change, still 20).** New
> `Announcements`-section config keys (streamed by `api config` like all others): a 100%-collection
> broadcast (`Broadcast_CollectionComplete_Enabled`/`_Messages`, default ON) and an optional periodic
> leaderboard broadcast (`Broadcast_Leaderboard_Enabled` default OFF, `_IntervalMinutes`, `_TopN`,
> `_Messages`). Admin command `.beelz admin broadcast <status|leaderboard on|off|interval <min>|top <1-5>|
> complete on|off|test>`. Messages go out as normal server chat (NOT `[BEELZ:*]` events), so no parser
> change — but a BCH admin panel can expose these keys as toggles/sliders/text-pools (pipe-separated;
> `%player%` for the complete message, `%top%`/`%count%` for the leaderboard).
>
> **⚠️ v0.87.0 — CAST MODIFIERS: freelymove + interrupt-on-hit (ApiVersion 19 → 20; additive).** Two new
> fields on `api info` + `catalog-ability`:
> - **`free_move_secs=`** (seconds; `-` = none) — free the caster to move N seconds INTO the cast (the cast
>   continues). Set via `.beelz admin ability <name|id> freelymove <sec>` / `tune`.
> - **`interrupt_on_hit=on|off|auto`** — the cast is cancelled when the caster TAKES DAMAGE. Set via
>   `.beelz admin ability <name|id> interruptonhit on|off`. **Distinct from the existing `interruptible=`
>   field** (that's the player's own dash/shield self-cancel; this is "an enemy hit breaks my cast").
> Both server-wide baked edits (Abilities_ApplyConfig), cleared by `defaults`. A BCH ability-config panel
> reads/writes them like the other `_override`/cast-tuning fields. Additive — older parsers ignore them.
>
> **✅ v0.100.0 — STRUCTURED TRANSFORM-LOADOUT + BROADCAST READS (ApiVersion 21 → 22, additive; plugin
> stays 0.100.0). Resolves PENDING #7 + #8.** Three new BCH-readable `api` commands so the transform-loadout
> and announcements editors read state structurally instead of parsing human chat text:
> - **`api tform-kit <unit>`** — a transform unit's FULL eligible ability kit (the pool you bind from).
>   Streams one **`[BEELZ:tform-ability] unit=<int> idx=<int> a=<int abilityGuid> an=<rawPrefabName>`** per
>   ability, then **`[BEELZ:end] cmd=tform-kit unit=<int> count=<n>`**. Same data as
>   `UnitKitService.FullEligibleKit`; `idx` is the index you pass to `.beelz tform <unit> set <phase> <slot>
>   <idx>`.
> - **`api tform-binds <unit>`** — the CALLER's CUSTOM per-phase binds for that unit. Streams one
>   **`[BEELZ:tform-slot] unit=<int> phase=<int> slot=<0-7> a=<int abilityGuid> an=<rawPrefabName>`** per
>   bound slot (none = just the end line), then **`[BEELZ:end] cmd=tform-binds unit=<int> count=<n>
>   phases=<n>`** — `phases=` is how many phases the form has (incl. player-defined custom), so BCH offers
>   the right phase count instead of guessing 1/2. Slots the player hasn't set are "empty → curated default".
> - **`api broadcast-msgs <complete|leaderboard>`** (ADMIN) — a broadcast pool's current custom messages.
>   Streams one **`[BEELZ:broadcast-msg] pool=<complete|leaderboard> idx=<1-based> text=<SafeToken>`** per
>   message, then **`[BEELZ:end] cmd=broadcast-msgs pool=<...> count=<n>`**. `idx` is **1-based** so it feeds
>   straight into `admin broadcast-msg edit/remove <n>`; empty pool = just the end line.
> Both `tform-*` commands resolve `<unit>` exactly like `.beelz tform` (index into your unlocks / unlocked
> GUID / name). `an=` is the raw prefab name — humanize client-side like the existing `[BEELZ:slot]` lines.
> All additive: older parsers ignore the new commands/lines. **BCH should gate the structured reads on
> `api>=22`** (and keep the human-text fallback for older servers if desired).
>
> **⚠️ v0.100.0 — CATALOG: unit fields + admin/player scopes (ApiVersion 20 → 21, additive). ACTION FOR BCH.**
> - **`catalog-ability` now emits `a=<guid>` `unit=<name>` `unitguid=<int>`** (PENDING #1 above) — `unit=` is
>   SafeToken-encoded (decode like `desc=`/`notes=`), `-`/`0` when unknown. Fills Unit + ID + GUID-search for
>   UNCAPTURED abilities, from the existing scan.
> - **Two catalog scopes for the three realms you'll surface:**
>   - **Captured abilities** → `api list` (the player's own captures) — unchanged.
>   - **Total AVAILABLE (player progress)** → **`api catalog abilities`** = the collectible set (capturable
>     under the active rules; honest %/count regardless of `Capture_InclusiveMode`). Filter to `enabled=1` if
>     you want strictly the enabled subset. End marker `cmd=catalog-abilities`.
>   - **ALL abilities (admin config)** → **`api catalog abilities-all`** (admin-only) = every real ability
>     group regardless of enable/deny/difficulty, each with `enabled=`. End marker `cmd=catalog-abilities-all`
>     (distinct so the two streams don't mix). Same `[BEELZ:catalog-ability]` line format + chunking as the
>     player scope. Use this for the Admin-Abilities config table; filter `enabled=1` client-side to mirror
>     the player view.
>
> **⚠️ v0.100.0 — CUSTOM TRANSFORM LOADOUTS + transform admin controls (new commands + config keys;
> ApiVersion 20 → 21, additive).**
> - **New PLAYER command `.beelz tform <unit|index> <abilities|set|clear|defaults> [phase] [slot] [index]`** —
>   a per-player custom loadout for each transform: list the boss's full kit, then bind chosen abilities to
>   phase slots (0 = primary, 7 = ultimate); a player can even define a phase 2. Persisted (new
>   `TransformLoadouts` block in state.json, keyed `"unit:phase"`). `<unit>` accepts the index from
>   `.beelz transforms`, a name, or the unit id. **No `[BEELZ:*]` line** — a BCH transform-loadout UI would
>   drive it by relaying these chat commands (like the weapon/form loadout panels), reading the kit from the
>   v0.99 full-catalog and the player's current binds from `api slots`-style data (a dedicated read may be
>   added later if BCH wants one — ask).
> - **New `api config`-streamed keys** (reflection, no parser change): **`Transform_Enabled`** (master
>   on/off), **`Transform_CooldownScope`** (`Global` | `PerCategory` | `PerTransformation`). A BCH admin
>   panel can toggle/select these via `.beelz admin set`.
> - **`.beelz admin transform-set <unit>` gains `duration` + `cooldown`** (per-transformation overrides of
>   the category defaults; `inherit` to clear). Same command surface BCH already relays for transform-set.
> - Power scaling for transforms is unchanged and already complete (`Transform_PowerScalingMode` global +
>   per-unit `scaling_mode`: PrefabAbsolute / PlayerScaled / PlayerLeveled / CuratedScales).
> - **Also new admin commands (chat-only, no wire surface):** `.beelz admin broadcast-msg
>   <complete|leaderboard> <list|add|remove|edit>` manages the announcement message pools individually (a BCH
>   announcements panel could relay these; note `api config` SafeToken-mangles the two pooled `Broadcast_*_Messages`
>   strings, so `broadcast-msg list` is the reliable read); `.beelz admin reset-loadouts <player>` clears a
>   player's binds + custom loadouts + active transform while keeping their collection.
>
> **ℹ️ v0.99.1 — form phase-switching FIX (behavioral; no wire change, ApiVersion still 20).** `.beelz phase`
> now works on the Werewolf/Golem/Gargoyle forms (phase 2 was silently failing — the native forms re-applied
> async and raced; fixed by swapping the kit in place). `type=transform-phase-shift` now reliably fires for
> phase 2 on those forms. The v0.98 Dracula "Wolf" 3rd phase was removed (Dracula = 2 phases again).
>
> **ℹ️ v0.99.0 — FULL CROSS-PHASE KITS + phase-switchable new forms (behavioral; no wire change, ApiVersion
> still 20).** Capture + Devour now grant a boss's **complete cross-phase** ability set (base bar ∪ the
> `ability_metadata.json` SourceNpcs reverse-map ∪ the curated transform-form sets), not just its base/phase-1
> bar — so a unit's collectable ability pool is **larger** (e.g. devouring the Geomancer now yields the golem
> kit, not just his human abilities). **BCH impact:** more `type=capture` / bigger `type=devour count=` per
> unit, and a player can own more abilities per source — purely "more of the same" events, no parser change.
> Also: the Werewolf/Golem/Gargoyle transforms are now multi-phase (2 kits each), so `type=transform-phase-shift`
> fires for them too (a transform UI should expect >1 phase on those, not just Dracula/Morgana).
>
> **⚠️ v0.98.0 — TRANSFORM/DEVOUR/CAPTURE ROLLS SPLIT + more forms (behavioral + new config keys;
> ApiVersion still 20).** The rare jackpot is now THREE independent rolls — per-ability capture, **Devour**
> (whole kit), and **Transformation** (the form) — each with its own drop chance + pity. **BCH impact:**
> - **`[BEELZ:event] type=devour` and `type=transform-unlock` can now BOTH occur for the same boss** (over
>   different kills) — a transform boss is now also devourable, and the form is a separate rarer unlock. A
>   BCH feed shouldn't treat them as mutually exclusive.
> - **`transform-unlock` fires for more units now:** Dracula, Morgana, Werewolf Chieftain (`2079933370`),
>   **Geomancer/Golem (`-1065970933`)**, **Tailor/Gargoyle (`-1942352521`)**, and a **Basic Werewolf from a
>   regular NPC** (`s=R u=-951976780`). `BossFormRegistry.Count` / `api transforms` is now **up to 6**.
>   (A v0.98 Dracula "Wolf" 3rd phase was reverted in v0.99.1 — Dracula is back to 2 phases.)
> - **New `api config`-streamed keys** (reflection — a BCH config panel picks them up automatically, no
>   parser change): `DropChance_TransformUnlock_VBlood`, `DropChance_TransformUnlock_Regular`,
>   `Capture_PityIncrement_Transform`, `Capture_PityMax_Transform`. These are the transform-only dials,
>   separate from the existing `DropChance_Devour_*` / `Capture_*_Devour` ones.
>
> **⚠️ v0.97.0 — GOLEM + GARGOYLE TRANSFORMS + form natural-fallback (behavioral; ApiVersion still 20).**
> Two more player transforms join Werewolf: **Golem** (defeat **Terah the Geomancer**, `u=-1065970933`) and
> **Gargoyle** (defeat **the Tailor**, `u=-1942352521`). Same deal as Werewolf — `transform-unlock` now also
> fires for these, and `.beelz transforms` / `api transforms` / `BossFormRegistry.Count` (now **5**) include
> them. Both are EXPERIMENTAL test forms (golem/gargoyle bars weren't built for players). **No new wire
> line/field** — a transform browser that enumerates `transform-unlock` / `ListTransforms` picks them up.
> Also (non-wire): per-form *loadout* slots a player doesn't grant now keep the form's NATURAL ability as a
> fallback (was blanked) — no BCH impact, the `form-slot`/`bucket=` data is unchanged.
>
> **⚠️ v0.96.0 — WEREWOLF TRANSFORM (behavioral; no new line/field, ApiVersion still 20 — but a CONTRACT
> CORRECTION for BCH).** Defeating the **Werewolf Chieftain (Willfred)** V-Blood now unlocks a third
> player transform (after Dracula & Morgana): the cursed-forest **Werewolf** form, registered in
> `BossFormRegistry`. **BCH-facing correction:** earlier notes (and the event table below) said
> `[BEELZ:event] type=transform-unlock` fires "ONLY for Dracula & Morgana" — that is now **out of date**.
> It also fires for the Werewolf Chieftain (`u=2079933370`), and `.beelz transforms` / `api transforms` /
> the transform count (`BossFormRegistry.Count` = **3**) now include Werewolf. No new wire line or field —
> a BCH transform browser that already enumerates `transform-unlock` / `ListTransforms` picks it up for
> free; just don't hard-assume "exactly two." (Werewolf is shipped as a **test** — its bar is
> CastOptions-based so some injected abilities may not render/fire yet; see
> `docs/WEREWOLF_FORM_TRANSFORM_DESIGN.md`.)
>
> **ℹ️ v0.95.0 — FORM-SLOT FIX + WEREWOLF SCOPING + LOG CLEANUP (no wire/event/ApiVersion change, still 20).**
> Behavior + internal only — **no BCH parser/contract change.**
> - **Per-form loadouts now place each granted ability on its EXACT slot (0–7) and add slots the vanilla
>   form doesn't natively declare**, so the form bar is purely the player's loadout instead of being capped
>   at the form's few pre-populated slots (Wolf was 2, Bear 3 — even with room for 7 — Spider 1). The
>   `[BEELZ:form-slot]` / `bucket=` data BCH already reads is unchanged — those binds simply now render
>   in-game on the slots BCH shows. A BCH form-loadout UI that grids slots 0–7 will see all of them used.
> - **The "Werewolf" form is a cosmetic wolf reskin, not the real werewolf-curse form** — a proper
>   native-form/werewolf transform is scoped server-side (`docs/WEREWOLF_FORM_TRANSFORM_DESIGN.md`). If/when
>   it ships with a config key or event, this handoff gets the wire note then; nothing for BCH today.
> - Server-log cleanup only (toggling an ability `enabled` no longer re-runs cast tuning; `[Beelz TUNE]`
>   detail is verbose-gated). These are server INFO logs BCH never parses.
>
> **🚨 v0.94.0 — CLEAR-BAR FIX + per-bucket clear (no wire/event/ApiVersion change, still 20). ACTION FOR BCH.**
> **A BCH "clear bar" button that calls `.beelz resetbar` is broken** — v0.76 made `resetbar` require a
> `CONFIRM` token, so bare `resetbar` now no-ops. **Switch the clear-bar button to the new
> `.beelz clearbar`** (no confirmation): `.beelz clearbar` / `clearbar all` (everything), `clearbar universal`,
> `clearbar <weapon>` (sword/spear/unarmed/…), or `clearbar <form>` (wolf/bear/…). Lets a BCH UI offer
> per-loadout clear buttons. Still emits `[BEELZ:event] type=slot-cleared`. ("Fix bar" = `.beelz refresh`,
> unchanged — it re-applies the active bar.) Also: clear/reset now include the primary (0) + ultimate (7)
> slots, and ALL shapeshift-form **skins** are now recognized for custom-ability injection (admins can audit
> with `.beelz admin dump forms`).
>
> **ℹ️ v0.93.0 — freelymove channel fix (no wire/event/ApiVersion change, still 20).** `freelymove` now also
> clears the `MovementImpair` flag on an ability's channel/firing buff (the downstream walker now runs for
> freelymove-only entries), so sustained-fire/channel abilities are move-enabled, not just the cast wind-up.
> No BCH impact (the `free_move_secs` field is unchanged).
>
> **ℹ️ v0.92.0 — diagnostics + label fix (no wire/event/ApiVersion change, still 20).** The `.beelz admin
> dump` diagnostic now also logs key field VALUES (`[Beelz DUMP-VAL]`: ModifyMovementDuringCast / cast-time
> / BuffModFlags) for freelymove diagnosis. **Label fix: the ULTIMATE slot is the `T` key (not R)** — slot
> token `ultimate`/`t`/engine slot 7. No BCH impact beyond the label.
>
> **ℹ️ v0.91.0 — FORM SELECTIVE-EXIT + PRIMARY/ULTIMATE SLOTS (no wire/event/ApiVersion change, still 20).**
> Shapeshift forms now exit when the player casts a genuinely foreign ability (hold for assigned + native
> form abilities). **Grant commands now accept the PRIMARY (left-click) and ULTIMATE (T-key) slots:** the
> slot argument of `.beelz grant` / `weapon-grant` / `form-grant` is now a token — `1`–`6`, or `primary`
> (engine slot 0) / `ultimate` (engine slot 7). Numeric slots still work (backward compatible), and
> `api slots` can now stream bindings on slots 0 and 7. **BCH impact:** if BCH builds the grant call, it
> can offer primary/ultimate as targets; a loadout UI should expect slot indices 0 and 7 in addition to 1-6.
>
> **ℹ️ v0.90.0 — TEST FIXES ROUND 2 (no wire/event/ApiVersion change, still 20).** Forms now HOLD through
> casting an injected ability (the real exit trigger was `DestroyOnGameplayEvent`, now stripped), and
> `freelymove` now frees movement on channeled abilities (clears the `MovementImpair` flag on the spell's
> spawned buff). New admin diagnostic `.beelz admin dump <abilityGuid|form>` logs an ability's component
> chain / the active form buff to the server log. No BCH parser impact.
>
> **ℹ️ v0.89.0 — TEST FIXES (no wire/event/ApiVersion change, still 20).** Two behavior-only fixes:
> shapeshift form abilities now actually render on the form bar (the v0.86 inject was a frame too late;
> now injected during slot resolution), and `freelymove` now works on channeled/locked abilities (it
> also clears `UseCastDuration` so the seconds are respected). No BCH parser impact — the `free_move_secs`
> field and per-form loadout data were already correct; they just take effect in-game now.
>
> **ℹ️ v0.86.0 — BUGFIXES (no wire/event/ApiVersion change, still 19).** Two fixes, both behavior-only:
> - **Shapeshift-form abilities now actually inject on the form bar** (entry was detected via a hook the
>   form-state buff never reached; now driven off the shapeshift enter-event). No BCH impact — form
>   loadout data was already correct; it just renders in-game now.
> - **Collection % denominator fixed.** `.beelz top`/`progress` and the `collection-complete` event used to
>   measure against the curated rules count (~450), which full-devour players exceed (showed >100%). The
>   denominator is now the **full capturable universe** (~1400 — i.e. the `total=` of `api catalog abilities`).
>   **BCH implication:** if BCH renders its own collection % or reads `collection-complete`'s `total=`, use the
>   `api catalog abilities` total as the denominator (not `api catalog`'s curated `abilities=` count). The
>   `collection-complete` event now fires at true 100% (previously far too early).
>
> **⚠️ v0.85.0 — FORCE-TIMEOUT (ApiVersion 18 → 19; additive).** New field on `api info` + `catalog-ability`:
> **`force_timeout_override=`** (seconds; `-` = none) — forces an ability's otherwise-INDEFINITE spawned
> effects/buffs to expire after N seconds (adds a LifeTime where the buff has none — the indefinite case
> `duration_override=` can't reach). Set via `.beelz admin ability <name|id> forcetimeout <v>` / `tune`;
> server-wide baked, cleared by `defaults`. A BCH ability-config panel reads/writes it like the other
> `_override=` fields. Additive — older parsers ignore it.
>
> **⚠️ v0.84.0 — `api info-guid` + `collection-complete` (ApiVersion 17 → 18; additive). FIXES THE "No name"
> TOOLTIP GAP.** (a) NEW read command **`api info-guid <abilityGuid>`** — returns the SAME chunked
> `[BEELZ:info]` tooltip data as `api info`, but keyed by an ability's **PrefabGUID** instead of a
> captured-list index. Use it for the abilities on a player's ACTIVE BAR: take the `a=<guid>` from
> `api slots` / `[BEELZ:slot]` and call `api info-guid <guid>` to get full name/desc/school/cooldown/stats.
> Reassemble exactly like `api info`, but the part id is **`a=<guid>`** (not `i=<index>`); `u=0 un=-` when
> the source unit is unknown. This removes the need to map a slot GUID back to a captured index. (b) NEW
> event **`[BEELZ:event] type=collection-complete count=<n> total=<n>`** fired once when a player captures
> the last ability in the curated catalog — a good hook for a "100%" badge. Both additive.
>
> **ℹ️ v0.81–v0.83 — RELIABILITY + PLAYER QOL (no wire change, ApiVersion 17).**
> - v0.81/v0.82: periodic ticks (summon timeout, cooldown enforcer) now run on a real per-frame
>   heartbeat — no BCH impact, but per-ability cooldown/summon config now applies live/immediately, so a
>   BCH config panel's writes take effect without a reload.
> - v0.83: new PLAYER chat commands (not api): `.beelz top` (leaderboard), `.beelz odds` (drop %/pity),
>   `.beelz silent <on|off>`. New admin config **`Capture_PitySessionBased`** (bool) — reflection-streamed
>   via `api config`, settable with `.beelz admin set`.
> - **🔎 KNOWN BCH GAP (to fix next, will bump ApiVersion): tooltips for ACTIVE ASSIGNED abilities.**
>   `api slots` gives only `a=<guid> an=<rawPrefabName>`; full tooltip data (`api info`) is keyed by the
>   CAPTURED-LIST INDEX, not a GUID — so there's no direct GUID→name/desc/stats lookup for a slotted
>   ability (the "No name"/generic-tooltip symptom). PLANNED FIX: add **`api info-guid <guid>`** returning
>   the same chunked `[BEELZ:info]` data by GUID. Until then, BCH can map a slot's `a=` GUID to its
>   `.beelz api list` index and call `api info <index>`.
>
> **⚠️ v0.80.0 — SUMMON UNITS-PER-CAST + SKINNED-FORM FIX (ApiVersion 16 → 17; additive).** New field on
> **`api info`** AND **`catalog-ability`**: **`summon_units_override=`** (per-ability max UNITS one cast
> produces; `-` = the ability's natural count). This is SEPARATE from `summon_cap_override=` (concurrent
> USES) — whichever limit is hit first rules. Set via `.beelz admin ability <name|id> summonunits <n>` /
> `.beelz admin tune`. Behavioral fixes (no wire impact): (a) `summoncap` is now a true USE cap — it no
> longer trims units within a single multi-unit cast (a BCH "max summons" control should be labelled
> "concurrent casts," and the new `summon_units_override=` is "units per cast"); (b) skinned shapeshift
> forms (wolf/bear cosmetic variants) are now recognized for per-form loadouts. All additive — older
> parsers ignore the new key.
>
> **⚠️ v0.79.0 — SUMMON GOVERNANCE (ApiVersion 15 → 16; additive).** Two new fields on **`api info`** AND
> **`catalog-ability`**: **`summon_cap_override=`** (per-ability max simultaneous summon "uses"; `-` = use
> the global `Transform_MaxStacksPerSummonAbility`, default 3) and **`summon_timeout_override=`**
> (per-ability summon auto-despawn seconds; `-` = use the global `Transform_SummonLifetimeSeconds`,
> default **CHANGED 0 → 30** in v0.79 — summons now fade after 30s by default). Set via `.beelz admin
> ability <name|id> <summoncap|summontimeout> <v>` / `.beelz admin tune`; precedence per-ability >
> global > engine. These are read LIVE (not baked). The two globals are reflection-streamed via
> `api config` (a BCH settings panel can set them with `.beelz admin set`). All additive — older parsers
> ignore the new keys. A BCH summon-config panel reads/writes these like the other `_override=` fields.
>
> **⚠️ v0.77.0 + v0.78.0 — COOLDOWN-BLEED FIX + FORMS-AS-GROUP (behavioral; no wire change, ApiVersion 15).**
> - **v0.77 cooldown bleed:** a configured cooldown now follows the ABILITY, not the slot — a slot only
>   carries its live cooldown forward when the SAME ability re-resolves (weapon swap); a different ability
>   placed on that slot gets its own cooldown. No wire impact; fixes "all my abilities suddenly have a
>   long cooldown."
> - **v0.78 forms-as-group:** shapeshift forms (Wolf/Bear/Rat/Spider/Toad/Werewolf/Gargoyle) now behave
>   like a weapon family — entering a form auto-loads its per-form bucket (`.beelz form-grant`, already in
>   `api slots` as `bucket=<Form>`), and EXITING reverts the bar to the active WEAPON group. A BCH loadout
>   UI can present forms as additional buckets alongside weapons; the `api slots` current-context footer
>   already reports the active form. Requires `Forms_CustomAbilities_Enabled` (default ON since v0.75).
> - `.beelz form-grant` / `.beelz weapon-grant` now also accept an **ability ID** (like `.beelz grant`) —
>   BCH "bind to form/weapon" buttons should send the ability **ID**.
>
> **🚨 v0.76.0 — `api info` + `catalog-ability` ARE NOW CHUNKED (ApiVersion 14 → 15; WIRE-BREAKING for
> those two lines — BCH MUST UPDATE ITS PARSER).** These lines had grown past VCF's **512-byte** chat
> reply cap (≈30 fields + description) and threw `FixedString512Bytes: Truncation while copying` —
> `api info` failed outright and one long `catalog-ability` row aborted the whole catalog stream. They
> are now emitted **in parts**:
> - Each line carries a new **`part=k/n`** field and repeats its id — **`i=<index>`** for `info`,
>   **`an=<name>`** for `catalog-ability`. Format: `[BEELZ:info] i=5 part=1/2 <tokens…>`.
> - **Reassemble:** group lines by id, then concatenate the `key=value` tokens of parts `1..n` in order;
>   parse the merged token set exactly as before. A short ability is a single `part=1/1` line.
> - No field names changed. `desc`/`notes` are clamped to 256 chars. `[BEELZ:end] cmd=… count=N` still
>   reports the number of ABILITIES (not lines). All OTHER lines (list/slots/catalog-unit/rules/config/
>   events) are UNCHANGED and still single-line.
> - A parser that ignores `part=` reads only the first chunk of a long ability (degraded, not crashed) —
>   but please update to merge parts so tooltips/config panels see every field.
> - Also in v0.76 (no wire impact): `.beelz grant <slot> <index OR ability ID>` now accepts the **ability
>   PrefabGUID** (a BCH "bind this ability" button should send the **ID**, not a list index — indexes are
>   now stable across logins but the ID is the robust key); `.beelz resetbar` requires `CONFIRM`; setting
>   `cooldown` on a charge-based ability returns a "use chargetime" note.
>
> **⚠️ v0.74.0 — SLOT AUTO-YIELD ACROSS WEAPON GROUPS + PRECEDENCE (behavioral; no wire change).**
> Confirmed in testing (v0.74): a Beelz **weapon-specific** slot bind OUTRANKS a vanilla spellbook pick;
> a vanilla pick acts as the default only on the **UNIVERSAL** bar. So "make my vanilla spell the
> default on this slot" = it must live in the universal bucket (no weapon-specific Beelz bind on that
> slot). BCH player guidance should state this precedence: granted (esp. weapon-bucket) abilities win;
> assign a vanilla ability to the universal group to have it act as the across-weapons default.
> Reliability fix for "mix and match": when a player uses the in-game spellbook to put a vanilla spell
> back on a slot that held a granted ability, that pick now sticks across ALL weapon groups (previously
> it crept back on a weapon swap). The slot's Beelz bind is cleared from the universal bucket and every
> weapon family. **BCH impact:** the existing **`[BEELZ:event] type=slot-cleared slot=<n>`** event fires
> on each such auto-yield (already in the contract) — a BCH loadout view should treat it as "this slot
> is no longer Beelz-bound in any bucket" and refresh `api slots` accordingly. No new/changed wire line.
>
> **⚠️ v0.73.0 — RANGE/DURATION COVERAGE + CHARGES FEEDBACK (no wire change, ApiVersion still 14).**
> Behavior of two existing shaping fields broadened; no `api info`/`catalog-ability` field change.
> - **`range` (`range_override=`) now also clamps a projectile's travel distance** (Projectile.Range),
>   not just the aim/cast clamp — a BCH range slider on a projectile spell now visibly shortens it.
> - **`duration` (`duration_override=`) now also sets the lifetime of directly-spawned effect buffs**
>   (heal/DoT channels), in addition to applied-buff/debuff length. It still does NOT change cast/channel
>   time. Tooltip wording: "how long the effect lasts," not "channel time."
> - **`charges`/`chargetime` capability:** the server now replies with a clear "no charge system" note
>   when these are set on an ability that doesn't support charges (can't be added). A BCH charges control
>   should ideally be shown only for charge-capable abilities; otherwise surface that server note.
>   Reminder for any batch UI: shaping commands take **one field per command**.
>
> **⚠️ v0.72.0 — HEALING-SHAPING FIX + RESET-TO-DEFAULTS (no wire change, ApiVersion still 14).**
> Two BCH-relevant admin-surface changes; the `api info`/`catalog-ability` fields are unchanged.
> - **`healing` now works.** The healing multiplier (`api info` `heal_mult=`) was writing the wrong
>   memory pre-v0.72 — it corrupted channeled/over-time heals and **crashed the server on `healing 0`**.
>   A BCH healing slider is now safe to expose; values scale from the ability's original heal so repeated
>   sends don't compound. (Same field/`heal_mult=` wire value; no parser change.)
> - **New reset command:** `.beelz admin ability <id> defaults` resets ONE ability's shaping config
>   (cooldown/range/charges/aoe/projspeed/duration/healing/interrupt/freemove/castspeed/damage+cooldown
>   scale) to shipped baseline; `.beelz admin ability all defaults` resets every ability. Capture/
>   availability rules (enabled/weapons/forms/deny) are untouched. **BCH should add a "reset to default"
>   affordance** per-ability and/or global that sends these — the natural pairing for any config panel.
>   After a reset, re-read `api info` to refresh displayed values (the overrides clear to "baseline").
> - Field-coverage caveats to reflect in tooltips: `charges`/`chargetime` apply only to abilities that
>   already have a charge system; `range` on a projectile is the aim/cast clamp (projectile travel is
>   separate); `duration` is applied-buff/debuff length, not channel time. (Broader coverage planned.)
>
> **⚠️ v0.69.0 — ADMIN ABILITY COMMANDS ACCEPT AN ID (no wire change, ApiVersion still 14). IMPORTANT
> FOR BCH.** `.beelz admin ability <name|GUID> <field> <value>` and `.beelz admin tune <name|GUID> …`
> now accept a numeric **PrefabGUID** (the `a=` ID from `api list`/`api info`), not just the prefab
> name — the server resolves the ID to the ability. **Before this, sending the GUID silently created a
> dead entry that never applied** (the tuner matches by name), so a BCH ability-config panel that sent
> `.beelz admin ability <id> cooldown 10` would appear to work but do nothing. BCH can now send the ID
> directly. Entries previously mis-keyed by GUID are auto-migrated on the server's next load/reload.
>
> **⚠️ v0.68.0 — EFFECT-DURATION + HEALING SHAPING (ApiVersion 13 → 14; additive).** Two new fields on
> **`api info`** AND **`catalog-ability`**: **`duration_override=`** (applied buff/debuff duration,
> seconds) and **`heal_mult=`** (healing multiplier) — `-` when unset. Set via `.beelz admin ability
> <name> <duration|healing> <v>` or `.beelz admin tune`; server-wide baked edits (Abilities_ApplyConfig,
> default ON). Additive — older parsers ignore them. (Next Beelzebub sub-phase: multi-ability rules —
> incompatible-locks / chain / stack-limit.)
>
> **⚠️ v0.67.0 — MORE ABILITY-SHAPING FIELDS (ApiVersion 12 → 13; additive).** Four new fields on
> **`api info`** AND **`catalog-ability`**: **`charges_override=`** (max charges, int), 
> **`chargetime_override=`** (recharge seconds), **`aoe_override=`** (area radius), 
> **`projspeed_override=`** (projectile speed) — `-` when unset. Set via `.beelz admin ability <name>
> <charges|chargetime|aoe|projspeed> <v>` or `.beelz admin tune`, applied server-wide when
> `Abilities_ApplyConfig` is on (default). A BCH ability-config panel can add these next to
> cooldown_override/range_override. All additive — older parsers ignore the new keys. (Effect-duration
> + healing + multi-ability rules — incompatible-locks/chain/stack-limit — are the next sub-phases.)
>
> **⚠️ v0.66.0 — ABILITY CONFIG NOW DEFAULT-ON (config key rename; no wire-format change, ApiVersion
> still 12).** The opt-in gate **`AbilityTuning_Enabled` (default off)** is renamed to
> **`Abilities_ApplyConfig` (default ON)** — a master kill-switch, not an opt-in. Per-ability config
> (cooldown/range/interrupt/etc.) now applies by default, server-wide. `api config` reflection-streams
> the new key automatically (old key auto-migrated out of the .cfg). **If a BCH settings panel
> hardcodes the `AbilityTuning_Enabled` key string, rename it to `Abilities_ApplyConfig`** (default ON).
> No other change. (Foundation for an expanding server-wide ability-config surface —
> damage/AoE/durations/charges/etc. — in later versions, which WILL bump ApiVersion.)
>
> **⚠️ v0.65.0 — PER-ABILITY COOLDOWN / RANGE OVERRIDES (ApiVersion 11 → 12; additive).** Two new
> fields on **`api info`** AND **`catalog-ability`**: **`cooldown_override=`** (absolute seconds an
> admin set, or `-` if unset) and **`range_override=`** (max cast distance, or `-`). These are the
> admin-SET values; the live baked values remain `cooldown_seconds=`/`range=` on `api info`. Set via
> `.beelz admin ability <name> cooldown|range <v>` (or `.beelz admin tune <name> cooldown|range <v>`),
> applied only when `Abilities_ApplyConfig` (baked, GLOBAL prefab edit). New **global** config
> `Grant_MinimumCooldownSeconds` floors every ability's cooldown — it's reflection-streamed via
> `api config` automatically. A BCH ability-config panel can now show/edit per-ability cooldown+range
> next to the existing damage_scale/cooldown_scale/tuning fields. All additive — older parsers ignore
> the new keys. (Historical note: this callout originally said per-ability healing-scale + effect-duration
> were "server-impossible" — that proved WRONG and they shipped in v0.68 [`heal_mult=`/`duration_override=`],
> with the healing path fixed in v0.72 and duration broadened in v0.73. The remaining multi-ability rules —
> stack-limit / incompatible-array / chain-predecessor — are the next Beelzebub sub-phase.)
>
> **⚠️ v0.64.0 — CONFIG KEY RENAME (no wire-format change, ApiVersion still 11).** `api config`
> still streams `[BEELZ:config] section= key= value= type= editable=` the same way, but the KEY
> STRINGS for the jackpot drop-chance changed: **`DropChance_Transform_Regular`/`_VBlood` →
> `DropChance_Devour_Regular`/`_VBlood`**, plus two NEW keys **`Capture_PityIncrement_Devour`** and
> **`Capture_PityMax_Devour`** (separate Devour pity). A BCH config panel that enumerates the stream
> dynamically needs no change; **if BCH hardcodes any `DropChance_Transform_*` key string (e.g. a
> labelled slider), update it to `DropChance_Devour_*`.** Old keys are auto-migrated server-side and
> removed from the .cfg, so they will no longer appear in the stream.
>
> **⚠️ v0.61–0.63 — SLOT/BAR RELIABILITY (no wire change, ApiVersion still 11).** A run of
> server-side robustness fixes; **no new fields, lines, events, commands, or config — BCH needs no
> changes.** Notable for BCH context: (a) v0.63.0 **grants now refresh the action bar immediately**
> server-side (the engine `AbilityGroupSlot.DirtyTag` is set on grant/preset/refresh/login), so the
> old "switch weapons to make a grant appear" workaround is gone — **do NOT build a client-side
> action-bar refresh; it's handled server-side.** (b) v0.63.0 the spellbook auto-yield (v0.56) now
> clears the slot's bind across **all** weapon buckets, not just the active one — it still emits the
> existing `[BEELZ:event] type=slot-cleared slot=<n>` per cleared slot, so a BCH loadout view that
> already reacts to that event stays correct (it may now see clears for multiple buckets at once;
> just re-query `api slots`). (c) v0.61 hardened against an out-of-range saved slot; v0.62 re-applies
> grants on login. Forms are untouched throughout.
>
> **⚠️ v0.60.0 — DESCRIPTION / SCHOOL DATA-PASS (no wire change, ApiVersion still 11).** Pure
> shipped-data improvement — no new fields, lines, events, commands, or config. The `desc=` and
> `school=` fields that already exist on **`api info`** and **`catalog-ability`** (since v0.58.0) are
> now **populated for roughly twice as many abilities**: descriptions ~13% → ~26%, magic-school
> ~4% → ~9% of the full catalog. The text is extracted from V Rising's own localization tooltip
> strings, so it carries `{param}` placeholders (e.g. `{damage}`, `{duration}`) — BCH should treat
> `desc=` as a display string and may leave `{param}` tokens as-is (they signal stat-scaled values
> the server doesn't compute). A handful of previously foreign-language / placeholder descriptions
> were repaired to English or cleared (→ BCH's friendly-name fallback). **No BCH change required;**
> the Bestiary "Missing"-row descriptions and any school grouping axis simply light up for more rows.
> Coverage is still partial (long-tail abilities have no curated text); admins can fill gaps via
> `ability_metadata_overrides.json`.
>
> **⚠️ v0.59.0 — PER-FORM ABILITY LOADOUTS (ApiVersion 10 → 11; all additive). The "coming soon"
> Forms placeholder can now be a real per-form picker.** Parallel to the per-weapon buckets, players
> can build a distinct captured-ability set per shapeshift form.
> - **Forms:** `Wolf`, `Bear`, `Rat`, `Spider`, `Toad`, `Werewolf`, `Gargoyle` (the seven native
>   models; same name set as the `ShapeshiftForm` enum). Build a per-form dropdown exactly like the
>   weapon-family dropdown.
> - **New commands** (mirror weapon-grant/unslot): **`.beelz form-grant <form|auto> <slot 1-6>
>   <index>`** and **`.beelz form-unslot <form|auto> <slot>`**. `auto` = the form the player is
>   currently in. Binds save with the collection and survive relogs.
> - **`api slots`** now also streams a **NEW line type** per form bucket:
>   **`[BEELZ:form-slot] form=<Form> slot=<n> a=<guid> an=<rawName>`** (distinct from `[BEELZ:slot]`
>   so older parsers that read `bucket=` as a WeaponFamily ignore it). The
>   **`[BEELZ:slot-current]`** footer gains **`form=<Form|None>`** (the active shapeshift form). The
>   `[BEELZ:end] cmd=slots count=` includes the form-slot lines.
> - **New events:** `type=form-slot-granted form= slot= a= an=` and `type=form-slot-cleared form= slot=`.
> - **Gating to surface in BCH:** the loadout only APPLIES in-form when the server has
>   `Forms_CustomAbilities_Enabled` = true (default **off**, experimental — it also depends on the
>   form holding through a custom-ability cast, still being validated). BCH can always *edit* the sets
>   (the data persists regardless); just show a hint that they apply only when the server enables form
>   abilities. Reuse the weapon-loadout UI patterns; a per-form set overrides nothing else (forms are
>   a separate context entered via the shapeshift wheel). Visuals limited to the 7 native forms (the
>   [model-swap hard limit] still applies — no arbitrary-unit form rendering server-side).
>
> **⚠️ v0.58.0 — FRIENDLY NAMES + CATALOG DESCRIPTIONS (ApiVersion 9 → 10; all additive).**
> Lets BCH drop client-side humanizing and show description/school on Bestiary "Missing" rows.
> - **`api list`** now also emits **`label=`** (friendly ability name) and **`ulabel=`** (friendly
>   unit name) alongside the raw `an=`/`un=`. Both are **SafeToken-encoded** (spaces→`_`, `=`→`-`),
>   so decode by reversing that (`_`→space) for display. Friendly names come from the curated
>   metadata (`ability_metadata.json` / overrides; units also via the SourceNpcs table) with a
>   humanized-prefab fallback — so e.g. `un=CHAR_Blackfang_Morgana_VBlood` now also has
>   `ulabel=Blackfang_Morgana`. Raw `an=`/`un=` are unchanged (keep keying on them). BCH's
>   `BeelzNames` humanizer can now defer to `label`/`ulabel` when present.
> - **`catalog-ability`** now also emits **`desc=`** (curated description, %param%-substituted and
>   SafeToken-encoded, or `-`) and **`school=`** (`Blood`/`Chaos`/`Frost`/`Illusion`/`Shadow`/
>   `Shapeshift`/`Storm`/`Unholy`, or `none`). This gives the Bestiary MISSING rows (no capture
>   index → no `api info`) real description/school text. **Coverage is partial:** only the curated
>   subset has these (~13% desc / ~4% school today); everything else is `-`/`none` until the
>   localization data-pass fills more in — so treat `-`/`none` as "unknown," not "empty."
>   (`api info` already carried `desc=`/`school=` for CAPTURED abilities; this brings the same to the
>   catalog for uncaptured ones, via a dict-only lookup — no extra per-row cost.)
>
> **⚠️ v0.57.0 — FULL-CATALOG `catalog-abilities` + classifier polish (ApiVersion 8 → 9).**
> Makes the BCH Bestiary "Missing" list complete and shrinks the `cat=Other` bucket.
> - **`api catalog abilities` now streams the FULL capturable universe** — the union of the curated
>   `AbilityMap` AND every discovered `AB_*_AbilityGroup`/`_Group` that passes the server capture
>   filter — instead of only the curated map (which ships empty). So the catalog is now the true
>   collectible pool; build the "Missing" list straight from it. **Volume:** under the default
>   `Capture_InclusiveMode=true` this is ~1,400+ rows (≈35+ pages at 40/page). BCH already paginates
>   to the last page (`[BEELZ:end] … pages=`), so no parser change is needed — just be ready for the
>   larger count (and the `total=`/`pages=`/progress denominator grows accordingly). The union is
>   rebuilt server-side when **page 0** is requested, so always start a fresh scan from page 0.
> - **New additive field `curated={0|1}`** on every `[BEELZ:catalog-ability]` line: `1` = a
>   hand-curated `ability_rules.json` entry (all tuning fields meaningful); `0` = discovered-only
>   (its `category_override=-`, `phase=1`, `interruptible=auto`, `free_move=0`, `cast_speed=auto`,
>   `notes` empty are defaults, not curation — `cat`/`weapons`/`enabled`/`difficulty`/`transform_only`
>   are still derived from the name heuristics + rules). Older parsers ignore the new key.
> - **`cat=` classifier broadened again** (still the same enum-NAME set + `Other`): primary/heavy
>   attacks now route by weapon class (ranged-weapon primary → `Projectile`, else `Melee`), bare
>   `Melee`/unarmed strikes → `Melee`, ground fields → `Aoe`, pistols/discharges → `Projectile`.
>   `Other` share on the full AbilityGroup set drops ~39% → ~35%. Still treat any unknown `cat=` as
>   `Other`; the residual `Other` is genuinely ambiguous proper-noun moves + non-combat utility, best
>   pinned by an admin `Category` override.
>
> **⚠️ v0.56.0 — SPELLBOOK "MIX AND MATCH" auto-yield (behavioral; no wire change, ApiVersion still 8).**
> Fixes the testers' "vanilla spells won't attach once you've bound captured abilities" report.
> **No new `[BEELZ:*]` line, event type, command, or config** — but one BCH-visible behavior change:
> - A player's slot bind can now be **released by the server on its own**, without any BCH-initiated
>   `unslot`/`resetbar`. When the player uses the in-game **spellbook** to put a *different* vanilla
>   ability on a slot that held a captured ability, Beelzebub drops that slot's bind and the slot
>   returns to the player's pick. It is **per weapon set** (the active bucket only).
> - **Signal BCH already handles:** each auto-yield emits the existing
>   **`[BEELZ:event] type=slot-cleared slot=<N>`** (the same line `.beelz unslot` sends). So a BCH
>   loadout view that already refreshes on `slot-cleared` is correct with no change. **Action item:**
>   make sure the Loadout/slots view treats `slot-cleared` as authoritative and re-reads `api slots`
>   (a bind can vanish between two `api slots` reads even though the user never touched the BCH UI).
> - `.beelz unslot` / `.beelz resetbar` / a BCH per-slot Clear now restore the slot's vanilla ability
>   **instantly** (no weapon-swap/relog needed) — cosmetic for BCH, but the bar will look correct
>   immediately after the command instead of after the next swap.
>
> **⚠️ v0.54.0 — WIRE-EXTRACTION COMPLETENESS (ApiVersion 7 → 8; all additive).** Closes the
> audit's BCH read-gaps. New fields (older parsers ignore unknown keys):
> - **`api info`** adds: `cat`, `category_override` (`-` = none/auto), `cast_time_seconds`,
>   `range`, `behavior`, `phase`, `allow_denied`, `interruptible` (on|off|auto), `free_move`,
>   `cast_speed` (0..1|auto). So a tooltip from `api info` alone is now complete (it previously
>   lacked the `cat` badge).
> - **`catalog-ability`** adds: `category_override`, `phase`, `allow_denied`, `interruptible`,
>   `free_move`, `cast_speed`.
> - **`api rules`** adds: `default_damage_scale`, `default_cooldown_scale`, `transform_only_patterns`,
>   `transform_only_guids` (the global `Defaults` block + reservation lists).
> - **`catalog-unit`** adds: `slot_template` (`slot:ability;slot:ability`, or `-`).
> - **`type=config-changed` now BROADCASTS** to all subscribed clients (was admin-only) — every
>   open BCH panel refreshes.
> - **`cat=` clarification (corrects earlier notes):** the value is the category **NAME**
>   (`Other`/`Travel`/`Aoe`/`Projectile`/`Summon`/`Buff`/`WeaponSpell`/`Spell`/`Melee`), emitted on
>   `api list` / `api info` / `catalog-ability`. It is **not** a number — treat any unknown name as
>   `Other`. (The v0.51.0 note's "cat=9" was wrong; the wire has always used the enum name.)
>
> **v0.53.0 — FLUID ADMIN CONFIG COMMANDS (no wire-format change, ApiVersion still 7).** New
> chat commands let an admin set ANY per-ability / per-unit / global-default rule live (previously
> hand-edit-JSON-only). **For a BCH admin panel:** you can now *write* ability config by relaying
> these to chat (same pattern as `.beelz admin set` for BepInEx config):
> - `.beelz admin ability <name> <field> <value>` — enabled, weapons, forms, transformonly,
>   difficulty, phase, allowdenied, damagescale, cooldownscale, category, interruptible, freemove,
>   castspeed, notes.
> - `.beelz admin transform-set <CHAR_unit> <field> <value>` — enabled, difficulty, tier,
>   damagescale, cooldownscale, healthscale, speedscale, fullreplace, powerscalingmode, notes.
> - `.beelz admin default <damagescale|cooldownscale> <value>`,
>   `.beelz admin denyguid|allowguid <add|remove> <guid>`,
>   `.beelz admin transformonly <add|remove> <pattern|guid>`.
> Read-back is via the existing `api info` / `api catalog-abilities` / `api catalog-units` lines
> (v0.54.0 will widen those to surface the few still-unexposed fields). No `[BEELZ:*]` line/event
> change. Full reference: `docs/ABILITY_CONFIG.md`.
>
> **v0.52.0 — UNTRANSFORMED chain casting (behavioral; no wire change, ApiVersion still 7).**
> Captured chain abilities (projectiles / AoEs / teleport-detonates) now team-fixup and fire vs.
> enemies when cast in normal form, not just while transformed — the chain-entity fixup gate was
> widened from "is transformed" to "transformed OR just cast a captured ability." Summons already
> worked untransformed (v0.45). **No BCH impact** — no `[BEELZ:*]` line/event change; players just
> get more working abilities off the normal bar.
>
> **⚠️ v0.51.0 — ABILITY CATEGORY badge broadened (ApiVersion 6 → 7).** The `cat=` field on
> `[BEELZ:list]` and `[BEELZ:catalog-ability]` now classifies far more abilities into real buckets
> (much less `Other`) and adds a **new category `Melee`**. `cat=` is the category NAME, one of:
> `Other, Travel, Aoe, Projectile, Summon, Buff, WeaponSpell, Spell, Melee`. **Action for BCH:**
> **treat any unknown `cat=` name as Other** (forward-compatible). An admin per-ability `Category`
> override (ability_rules.json) can also set this. No line/event shape changed — only the `cat=`
> value set. *(Correction: the wire emits the enum NAME, not a number — an earlier draft said
> "cat=9"; that was wrong.)*
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

## 0.0 — ⚡ SINCE YOUR LAST BUILD (BCH baseline v0.44.0 → now v0.100.0) — READ THIS FIRST

**Context (updated 2026-05-31):** BCH began building against **v0.44.0** (ApiVersion 6). Beelzebub
is now at **v0.100.0 / ApiVersion 21**. This is the *consolidated* delta so you can fold everything
into the UI in one pass — every per-version detail is in the dated callouts ABOVE this section.

> **🆕 v0.95 → v0.100 (the 2026-05-31 session) — BCH ACTION SUMMARY (ApiVersion 20 → 21, all additive):**
> - **ONE small wire add (ApiVersion 21):** `catalog-ability` now also carries **`a=<guid>` `unit=<name>`
>   `unitguid=<int>`** (`unit=` SafeToken-encoded; `-`/`0` when unknown). Fill the Unit + ID columns + GUID
>   search for UNCAPTURED abilities from the existing scan. Older parsers ignore them — no regression.
> - **TWO catalog scopes (do this for the 3 realms):** `api list` = the player's captures;
>   **`api catalog abilities`** = the COLLECTIBLE/available set (honest progress % regardless of
>   InclusiveMode; filter `enabled=1` for the strict enabled subset); **`api catalog abilities-all`**
>   (ADMIN) = EVERY ability for the config table (end marker `cmd=catalog-abilities-all`; each row still has
>   `enabled=`). Use the admin scope for an Admin-Abilities panel, the player scope for the Bestiary.
> - **Transforms are no longer "Dracula & Morgana only."** There are now **up to 6** renderable forms:
>   Dracula, Morgana, **Werewolf Chieftain** (`2079933370`), **Geomancer/Golem** (`-1065970933`),
>   **Tailor/Gargoyle** (`-1942352521`), **basic werewolf NPC** (`-951976780`, source `s=R`). `transform-unlock`
>   fires for all of them; `api transforms` returns up to 6. **A transform UI must stop assuming exactly two**
>   and expect **multi-phase** kits (`type=transform-phase-shift`) on the new forms too.
> - **Capture / Devour / Transform are now THREE independent rolls** — `type=devour` AND `type=transform-unlock`
>   can BOTH fire for the same boss (over different kills). Capture + Devour now grant a boss's **full
>   cross-phase kit** (more `type=capture` events / larger `count=` per unit). New `api config` keys (reflection
>   -streamed, settable via `admin set`): `DropChance_TransformUnlock_VBlood`/`_Regular`,
>   `Capture_PityIncrement_Transform`/`_Max_Transform`, `Transform_Enabled` (master), `Transform_CooldownScope`.
> - **PENDING #5 DONE:** `unslot` / `weapon-unslot` / `form-unslot` accept the `primary` / `ultimate` tokens —
>   BCH can clear a single primary/ultimate bind directly (no more clearbar-the-whole-bucket workaround).
> - **New chat-only admin commands a BCH panel can relay** (no `[BEELZ:*]` line): `.beelz tform <unit>
>   abilities|set|clear|defaults` (per-player custom transform loadouts), `.beelz admin transform-set <unit>
>   duration|cooldown <sec>`, `.beelz admin broadcast-msg <complete|leaderboard> <list|add|remove|edit>`
>   (manage announcement pools; `api config` SafeToken-mangles the two pooled `_Messages` strings, so
>   `broadcast-msg list` is the reliable read), `.beelz admin reset-loadouts <player>`.

> **🔧 v0.89–v0.94 (all ApiVersion 20, mostly behavior; the items below are the BCH-relevant ones):**
> **clear-bar button must move to `.beelz clearbar`** (bare `resetbar` no-ops since v0.76 — see §A/§4);
> **grant/weapon-grant/form-grant slot arg is now a token** that also accepts **`primary`** (left-click,
> slot 0) and **`ultimate`** (the **T** key, slot 7) — loadout UIs should expect slots 0 and 7 too;
> shapeshift **forms now fully work in-game** (render on the form bar, hold while you cast your form
> abilities, exit when you cast a non-form ability, every skin recognized); `freelymove` now also frees
> channeled/sustained-fire abilities. None of these change the wire format.

> **⚠️ THE ONE BREAKING CHANGE (v0.76.0, ApiVersion 15): `api info` + `catalog-ability` are now
> CHUNKED.** Those two lines outgrew VCF's 512-byte reply cap, so each is emitted across multiple
> replies that repeat the id field (`i=<index>` for `info`, `a=<guid>` for `info-guid`, `an=<name>`
> for `catalog-ability`) and carry **`part=k/n`**. **Your parser MUST reassemble** by concatenating
> the `key=value` tokens of parts 1..n for the same id before parsing. Single-part lines just carry
> `part=1/1`. ALL other lines (`list`/`slots`/`catalog-unit`/`rules`/`config`/events) are unchanged.
> This is the only wire-break since your baseline — everything else below is additive.

### A) NEW PLAYER commands & data (for the user-facing UI)
- **Leaderboard / odds / silence (v0.83):** `.beelz top` (server leaderboard by collection %),
  `.beelz odds` (the player's live drop/Devour/pity chances), `.beelz silent <on|off>` (mute the
  "you already knew that" devour message). Good buttons/toggles for a player panel.
- **Per-form loadouts (v0.59+, default ON):** `.beelz form-grant <form> <slot> <index|abilityID>` and
  `.beelz form-unslot` — a third loadout bucket parallel to universal + per-weapon, one per vanilla
  wheel form (Wolf/Bear/Rat/Spider/Toad/Werewolf/Gargoyle). `api slots` streams these buckets too.
- **`.beelz grant`/`weapon-grant`/`form-grant` accept the ability GUID** (not just the list index) —
  v0.83. So a BCH "assign this ability" button can pass the GUID it already holds.
- **Clear-bar (v0.94): `.beelz clearbar [all|universal|<weapon>|<form>]`** — the clear-bar button MUST
  call this (bare `.beelz resetbar` no-ops since v0.76's CONFIRM requirement). Lets BCH offer per-loadout
  clear buttons (all / universal / a specific weapon / a specific form). Emits `slot-cleared`.
- **Primary + ultimate slots (v0.91/0.94): the slot arg of grant/weapon-grant/form-grant is a token** —
  `1`–`6`, or `primary` (left-click, engine slot 0) / `ultimate` (T key, engine slot 7). `api slots` can
  now stream binds on slots 0 and 7, so a loadout UI should render those too.
- **Tooltip-by-GUID (v0.84): `.beelz api info-guid <abilityGuid>`** — returns the same rich `info`
  body keyed by `a=<guid>` instead of `i=<index>`. **This is the fix for "No name" on active-bar
  abilities** that aren't in the player's capture list — call it for any ability GUID you need a
  tooltip for. (Chunked, same as `info`.)

### B) NEW EVENTS on the push stream (`api bch on`)
- **`type=collection-complete count=<n> total=<m>` (v0.84)** — fires once when a player collects the
  whole catalog. Good for a celebratory toast / 100% badge. **`total=` now means the FULL capturable
  universe (~1400)**, not the curated count (v0.86 fix) — so it matches the `total=` of
  `api catalog abilities`. If BCH renders its own collection %, use THAT as the denominator (the old
  curated `abilities=` from `api catalog` was smaller and produced >100%).
- (Earlier, still current: `type=devour`, `type=slot-cleared`, `type=config-changed`, etc.)

### C) NEW FIELDS on `api info` / `info-guid` / `catalog-ability` (per-ability shaping — for an ADMIN ability-config panel)
All additive `key=value` tokens (reassemble the chunks first). Each is the server-wide override or
`-`/`auto` when unset. A BCH ability-config panel reads these and writes them via `.beelz admin
ability …` (section D). Full set (current at v0.94 — no new wire fields since v0.87):
`cooldown_override` · `range_override` · `charges_override` · `chargetime_override` · `aoe_override` ·
`projspeed_override` · `duration_override` · `heal_mult` · `force_timeout_override` ·
`summon_cap_override` · `summon_timeout_override` · `summon_units_override` · `free_move_secs` ·
`interrupt_on_hit` (on/off/auto) · `interruptible` (on/off/auto) · `free_move` (0/1) · `cast_speed`.

### D) NEW/CHANGED ADMIN commands (for admin panels)
- **`.beelz admin ability <name|id> <field> <value>` — the master per-ability editor.** Accepts the
  ability NAME *or* GUID (v0.69). Fields: `cooldown · range · charges · chargetime · aoe · projspeed ·
  duration · healing · forcetimeout · freelymove · interruptonhit · interruptible · freemove ·
  castspeed · summoncap · summontimeout · summonunits · damagescale · cooldownscale · enabled ·
  weapons · forms · category · notes · …`. Reset: `.beelz admin ability <id> defaults` (one) /
  `all defaults` (every ability). `.beelz admin tune <ability> <knob> <value>` is the one-field
  shorthand. These are GLOBAL prefab edits (the source NPC/boss cast changes too).
- **`.beelz admin broadcast <status|leaderboard on|off|interval <min>|top <1-5>|complete on|off|test>`
  (v0.88)** — toggles/schedules the server announcements. Config-only; messages go out as normal
  server chat (NOT `[BEELZ:*]`), so no parser change — but a panel can expose the toggles + message
  pools.

### E) CONFIG keys (settings panel — all stream via `api config`, set via `.beelz admin set`)
- **Renamed (migrate any hardcoded slider keys):** `AbilityTuning_Enabled` → **`Abilities_ApplyConfig`
  (now DEFAULT ON)** (v0.66); `DropChance_Transform_Regular/VBlood` → **`DropChance_Devour_Regular/VBlood`**
  (v0.64). `Forms_CustomAbilities_Enabled` is now **DEFAULT ON** (v0.75).
- **New:** `Capture_PitySessionBased` (v0.83 — logout resets pity), and the **`Announcements`** section
  (v0.88): `Broadcast_CollectionComplete_Enabled`/`_Messages`, `Broadcast_Leaderboard_Enabled`/
  `_IntervalMinutes`/`_TopN`/`_Messages` (pipe-separated pools; `%player%` / `%top%` / `%count%`).

### F) BEHAVIOR changes that affect the UI (no wire schema change)
- **Summons are independent of transformation (v0.45).** `[BEELZ:active] none=1` does NOT imply "no
  summons" — don't gate a summon panel on having an active transform.
- **Forms now inject reliably on entry (v0.86).** Per-form loadouts actually render on the form bar
  now — if you built a per-form UI, it's live.
- `cat=` is the category NAME and broadened over time (e.g. `Melee`) — treat unknown names as `Other`.

> **Per-weapon / per-form loadouts** stream via `api slots`: `bucket=any` = universal set,
> `bucket=<WeaponFamily>` = per-weapon override, `bucket=<Form>` = per-form set; `[BEELZ:slot-current]`
> reflects the active bucket. The server auto-switches on weapon swap / form enter — you just re-read
> `api slots`. Unarmed is its own family.

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
| `.beelz api list` | `[BEELZ:list]` … `[BEELZ:end]` | Caller's captured abilities: `i= s=R\|V u=<unitGuid> un=<unitName> a=<abilityGuid> an=<abilityName> label=<friendlyAbilityName> ulabel=<friendlyUnitName> cat=<category-NAME> type=<unitType>`. **(v10 added `label=`/`ulabel=` — SafeToken-encoded friendly names; raw `an=`/`un=` unchanged.)** **`cat=` is the category NAME** (`Other\|Travel\|Aoe\|Projectile\|Summon\|Buff\|WeaponSpell\|Spell\|Melee`), not a number — treat unknown names as `Other`. |
| `.beelz api slots` | `[BEELZ:slot]`, `[BEELZ:slot-current]`, `[BEELZ:end]` | Slot assignments per bucket: `bucket=any\|<WeaponFamily> slot=1-6 a= an=`; footer `weapon=<current>` |
| `.beelz api transforms` | `[BEELZ:tx]` … `[BEELZ:end]` | Transform unlocks + matrix attrs: `i= s= u= un= enabled= difficulty= tier= damage_scale= cooldown_scale= health_scale= speed_scale= type= full_replace= scaling_mode=`. **(v0.98) Up to 6 entries — Dracula, Morgana, Werewolf Chieftain, Geomancer (Golem), Tailor (Gargoyle), Basic Werewolf (NPC).** |
| `.beelz api active` | `[BEELZ:active]` | Active transform: `u= un= s= ttl=<sec>\|toggle` + phase info; or `none=1` |
| `.beelz api info <index>` | `[BEELZ:info]` | One ability's COMPLETE tooltip + rule data (a tooltip can be built from this line alone). Fields: `i= s= u= un= a= an= label= desc=` (real description, %params% substituted) `cat=<NAME> category_override=<NAME\|->` (`-` = auto-classified, else admin override) `weapons= weapon_anim=<family\|None> school= cooldown_seconds= cast_time_seconds= range= behavior= forms= transform_only= enabled= difficulty= phase= allow_denied= interruptible=<on\|off\|auto> free_move=<0\|1> cast_speed=<0..1\|auto> damage_scale= cooldown_scale=`. **(v8 added cat, category_override, cast_time_seconds, range, behavior, phase, allow_denied, interruptible, free_move, cast_speed.)** |
| `.beelz api progress` | `[BEELZ:progress]` | Collection %: `abilities_captured= abilities_total= abilities_pct= transforms_unlocked= transforms_total= transforms_pct=` + V-Blood breakdowns |
| `.beelz api rules` | `[BEELZ:rules]` | Loaded filter rules + global scaling baseline: `version= deny_patterns= allow_patterns= deny_guids=<count> allow_guids=<count> default_damage_scale= default_cooldown_scale= transform_only_patterns= transform_only_guids=<count>` **(v8 added the `default_*` and `transform_only_*` fields)** |
| `.beelz api transform-config` | `[BEELZ:tx-config]` … `[BEELZ:end]` | One line per category `R`/`V`/`S` (shard boss): `src= mode=Toggle\|Timed\|Disabled duration= cooldown=` (count=3, **`src=S` added v0.43.0**). Live cooldown remaining (incl. shard) is in `api cooldowns`. |
| `.beelz api catalog` | `[BEELZ:catalog-summary]` | `abilities= units= server_mode=Basic\|Brutal` |
| `.beelz api catalog units [page]` | `[BEELZ:catalog-unit]` … `[BEELZ:end]` | Curated per-unit TransformMap, 40/page: `un= enabled= difficulty= tier= type= full_replace= shard= scaling_mode=<mode\|inherit> damage_scale= cooldown_scale= health_scale= speed_scale= slot_template=<slot:ability;…\|-> notes=` **(v8 added `slot_template`)**. Read as Devour/collection targets, NOT transform targets — only Dracula/Morgana transform. |
| `.beelz api catalog abilities [page]` | `[BEELZ:catalog-ability]` … `[BEELZ:end]` | **FULL capturable universe** (curated AbilityMap ∪ discovered `AB_*` passing the capture filter), 40/page, start at page 0: `an= curated=<0\|1> weapons= forms= transform_only= enabled= difficulty= cat=<NAME> category_override=<NAME\|-> phase= allow_denied= interruptible=<on\|off\|auto> free_move= cast_speed=<0..1\|auto> damage_scale= cooldown_scale= school=<school\|none> desc=<SafeToken\|-> notes=` **(v10 added `school=`/`desc=`; v9 made the stream the full universe + added `curated=`; v8 added category_override, phase, allow_denied, interruptible, free_move, cast_speed)**. Under default inclusive mode this is ~35+ pages — the `total=`/`pages=` denominator is now the whole pool. `desc=`/`school=` are populated only for the curated subset (~13%/4%); else `-`/`none`. |
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
  | `transform-unlock` | `s=R\|V u= un=` | A transformation unlocked. **Fires for renderable forms only — Dracula, Morgana, Werewolf Chieftain (`2079933370`), Geomancer/Golem (`-1065970933`), Tailor/Gargoyle (`-1942352521`), and Basic Werewolf (`s=R u=-951976780`).** Independent of `type=devour` (v0.98 — both can fire for the same boss). Don't hard-assume a fixed count. |
  | `slot-granted` / `slot-cleared` | `slot= [a= an=]` | Universal-bucket grant/clear |
  | `weapon-slot-granted` / `weapon-slot-cleared` | `weapon= slot= [a= an=]` | Weapon-bucket grant/clear |
  | `hotkey-set` / `hotkey-cleared` | `name= [a= an=]` | Named hotkey bind/clear |
  | `transform-activated` | `u= un=` | Player transforms |
  | `transform-ended` | `u= un= reason=` | Any revert. `reason` ∈ `manual` · `switching-transformation` · `auto` · `admin-clear` · `admin-revoke` · `admin-revert-all` · `admin-wipe-all` |
  | `transform-phase-shift` | `u= un= phase=` | Form/phase swap (`.beelz phase` or Auto-HP) |
  | `forget` / `forget-transform` | `a= u=` / `u=` | Capture / unlock deleted (v2) |
  | `cleared` | — | Player wiped all captures + slots (v2) |
  | `detonate` | `u=` | Player manually fired a transform's detonation AoE via `.beelz detonate` (v2) |
  | `config-changed` | `key= value=` | An admin changed a setting via `.beelz admin set` (v3). Re-fetch `api config`. **(v8) Now BROADCAST to every subscribed client** — any open BCH panel refreshes, not just the acting admin. |
  | `cast` | `a= an=` | Player force-cast an ability via `.beelz cast` (v4 — expanded action bar). |
  | `summon` | `u= ability=` | Player force-cast a transform's signature add-summon via `.beelz summon` (v5). Spawns become allies. |

---

## 4. Mutations BCH issues — ✅ shipped

BCH sends these exactly as a player would type them. Replies are
**human-readable text**, not API format — treat them fire-and-forget and
re-fetch the affected read command (or wait for the event).

**Player:** `.beelz grant <slot|primary|ultimate> <index>` · `.beelz unslot <slot>` ·
**`.beelz clearbar [all|universal|<weapon>|<form>]`** (v0.94.0 — **USE THIS for a "clear bar" button**, NOT
`resetbar`: it needs NO confirmation and lets you clear a specific bucket. No arg / `all` = every bucket;
`universal` = the any-weapon set; a weapon family = that weapon's set; a form = that form's set. Keeps
captures; emits a `slot-cleared` event.) ·
`.beelz resetbar CONFIRM` (v0.43.8 — clear ALL slot bindings [universal + weapon + form] → vanilla in-game
bar; keeps captures/unlocks. **⚠️ v0.76.0 added a required `CONFIRM` token — `.beelz resetbar` alone now
just prints a warning and does NOTHING. This is why a BCH clear-bar button calling bare `resetbar` stopped
working; switch it to `.beelz clearbar`.**) ·
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

**Admin** (BCH admin panel; full list in §6): live per-ability config
(`admin ability …`), per-unit transform config (`admin transform-set …`), global
defaults (`admin default …`), grant/revoke (ability + transform), force/clear-transform,
set/clear slot (universal + weapon), deny/allow patterns + GUIDs, transform-only lists,
transform mode/duration/cooldown, difficulty, freeze-captures, revert-all, snapshot,
inspect/progress, wipe-all.

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
`TransformMap` per-unit matrix, `Defaults`, deny/allow patterns + GUIDs,
transform-only lists) — see **`docs/ABILITY_CONFIG.md`** (the full admin field
reference). Read it via `api rules` / `api catalog units|abilities` / `api info`.
**(v0.53.0) Every per-ability and per-unit field is now settable live in-game** (no
file editing) via the commands below — all audited to `LogOutput.log` as
`[Beelz AUDIT] admin=… action=… target=…`:

- **Filter rules:** `admin rules` · `admin deny/undeny <pattern>` ·
  `admin allow/unallow <pattern>` · `admin denyguid/allowguid <add\|remove> <guid>` ·
  `admin transformonly <add\|remove> <pattern\|guid>` · `admin reload`.
- **Per-ability config (v0.53.0; shaping fields added v0.65–0.68, accepts ID since v0.69):**
  `admin ability <name|ID> <field> <value>` — sets ANY AbilityMap field live: curation
  (`enabled`, `weapons`, `forms`, `transformonly`, `difficulty`, `phase`, `allowdenied`, `category`,
  `notes`), scaling (`damagescale`, `cooldownscale`), and the SERVER-WIDE baked shaping fields
  (`cooldown`, `range`, `charges`, `chargetime`, `aoe`, `projspeed`, `duration`, `healing`,
  `interruptible`, `freemove`, `castspeed`) — applied when `Abilities_ApplyConfig` (default ON).
  **ONE field per command.** **(v0.72) Reset:** `admin ability <id> defaults` reverts ONE ability's
  shaping fields to shipped baseline (live, no restart); `admin ability all defaults` resets every
  ability — curation/availability (enabled/weapons/forms/deny) is left untouched. `admin tune <ability>
  <knob> <value>` is the shaping shortcut; `admin tune-list` lists tuned abilities. Read values back from
  `api info` (`cooldown_override=`/`range_override=`/`charges_override=`/`chargetime_override=`/
  `aoe_override=`/`projspeed_override=`/`duration_override=`/`heal_mult=`) / `api catalog abilities`.
  **Coverage caveats (v0.73):** `range` clamps both aim and projectile travel; `duration` = applied-buff
  + over-time effect length (NOT cast/channel time); `charges`/`chargetime` only apply to abilities that
  already have a charge system (the server replies with a "no charge system" note otherwise).
- **Per-unit transform config (v0.53.0):** `admin transform-set <CHAR_unit> <field> <value>` —
  sets any TransformMap scalar (`enabled`, `difficulty`, `tier`, `damagescale`, `cooldownscale`,
  `healthscale`, `speedscale`, `fullreplace`, `powerscalingmode`, `notes`). Read back via
  `api catalog units`. (`SlotTemplate` is a JSON edit + `admin reload`.)
- **Global defaults (v0.53.0):** `admin default <damagescale\|cooldownscale> <value>` — the
  server-wide baseline for abilities with no per-ability override. Read back via `api rules`.
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
| Filter rules | `admin rules` · `admin deny <pattern>` · `admin undeny <pattern>` · `admin allow <pattern>` · `admin unallow <pattern>` · `admin denyguid <add\|remove> <guid>` · `admin allowguid <add\|remove> <guid>` · `admin transformonly <add\|remove> <pattern\|guid>` · `admin reload` |
| **Per-ability config (v0.53.0; +shaping v0.65–0.68; +ID v0.69; +reset v0.72)** | `admin ability <name\|ID> <field> <value>` — ONE field per command. field ∈ curation: enabled, weapons (csv\|any), forms (csv\|any), transformonly, difficulty, phase, allowdenied, category, notes · scaling: damagescale, cooldownscale · baked shaping (Abilities_ApplyConfig, default ON): cooldown (sec\|clear), range (dist\|clear), charges (int\|clear), chargetime (sec\|clear), aoe (radius\|clear), projspeed (speed\|clear), duration (sec\|clear), healing (mult\|clear), interruptible (on\|off\|clear), freemove (on\|off), castspeed (0..1\|clear). Toggles take on\|off. **Reset:** `admin ability <id> defaults` / `admin ability all defaults` (revert shaping to baseline, live). · `admin tune <ability> <interrupt\|freemove\|castspeed\|cooldown\|range\|charges\|chargetime\|aoe\|projspeed\|duration\|healing> <value\|clear>` (one field per command) · `admin tune-list` |
| **Per-unit transform config (v0.53.0)** | `admin transform-set <CHAR_unit> <field> <value>` — field ∈ enabled, difficulty, tier, damagescale, cooldownscale, healthscale, speedscale, fullreplace, powerscalingmode (or `inherit`), notes |
| **Global defaults (v0.53.0)** | `admin default <damagescale\|cooldownscale> <value>` |
| Runtime config | `admin set <key> <value>` (any `api config` key; live + persists; emits `config-changed`) |
| Transform settings | `admin transform mode <regular\|vblood> <toggle\|timed\|disabled>` · `admin transform duration <regular\|vblood> <seconds>` · `admin transform cooldown <regular\|vblood> <seconds>` · `admin transform show` |
| Difficulty | `admin difficulty [basic\|brutal]` (no arg = show) |
| Grant / revoke | `admin give <player> <unitGuid> <abilityGuid>` · `admin revoke <player> <unitGuid> <abilityGuid> [reason]` · `admin give-transform <player> <unitGuid>` · `admin revoke-transform <player> <unitGuid> [reason]` |
| **Devour (v6)** | `admin devour <player> <unitGuid>` — grant the player ALL of a unit's eligible abilities at once (the admin alternative to transformation for non-renderable units) |
| Force transform | `admin force-transform <player> <unitGuid>` (bypasses unlock+cooldown) · `admin clear-transform <player>`. **`give-transform`/`force-transform` accept Dracula, Morgana & (v0.96) the Werewolf Chieftain (`2079933370`)** — other units reply pointing to `admin devour`. |
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
- **Transform is limited to renderable forms — Dracula, Morgana & Werewolf (v0.96).** Don't build a generic "transform into any
  unit" browser — `api transforms` returns at most those two. The collection/progression UI
  should center on **abilities** (`api list`/`api bestiary`/the `devour` event), with a small
  dedicated panel for the two boss transforms. Arbitrary-unit transformation is §7.1 (phase two).

---

## 9. Related docs

- **`Beelzebub/Beelzebub/docs/ABILITY_CONFIG.md`** — the **current, complete** admin config
  reference (all files, every global/per-ability/per-unit field, and the in-game setter commands).
  Supersedes the older `Beelzebub/docs/ABILITY_MAP_FORMAT.md`. **This is the source for §10's
  config guide.**
- `Beelzebub/Beelzebub/docs/ABILITY_AUDIT.md` — ability metadata audit (counts, category coverage,
  the description-coverage gap + path).
- `Beelzebub/docs/SUMMON_AS_ALLY.md` — summon system internals (the summon panel's backing behavior).
- `Beelzebub/docs/INTEROP_BLOODCRAFT.md` — coexistence with Bloodcraft (relevant if BCH talks to both).
- `Beelzebub/docs/SETUP_GUIDE.md` — install / first-run.
- `Commands/ApiCommands.cs` — **canonical** wire API (`ApiVersion = 8`).
- `Services/SummonRegistry.cs` — curated unit→signature-summon map for `.beelz summon` (v5).
- `Services/DevourService.cs` — reads a unit's prefab kit + grants it all at once (the v6 Devour jackpot, migration, and `admin devour`).

---

## 10. IN-APP GUIDE SOURCE (v0.54.0) — overview · command reference · config

> **Purpose:** a single, self-contained reference BCH can adapt into an **in-app help/guide**
> screen — "how Beelzebub works", the full command structure split **player vs admin**, and the
> configuration surface. Everything here is current as of v0.54.0 / ApiVersion 8. (For wire details
> BCH parses, see §1–§4; this section is the human-readable companion.)

### 10.1 What the mod is (player-facing overview)

**Beelzebub, Lord of Gluttony** is a server-side V Rising mod about **devouring your enemies'
powers**. Defeat a unit — any V-Blood or NPC — and you have a chance to **capture one of its
abilities** into your personal collection. Slot captured abilities onto your six-slot action bar,
bind extras to named hotkeys, and complete the bestiary. Two bosses — **Dracula** and **Morgana** —
can also be fully **transformed into**. Abilities that summon minions make those minions **fight
for you**, transformed or not.

**The loop:** kill → (chance to) capture an ability → slot it / bind it / cast it → collect more.
Rarely, a kill hits the **Devour jackpot** and grants a unit's *entire* ability kit at once.

### 10.2 Core concepts (each is a good guide subsection)

- **Capture** — a per-ability roll on each kill (rates configurable). Captured abilities live in
  your collection (`api list` / `api bestiary`).
- **Devour** — the rare jackpot: a unit's whole eligible kit granted at once (`type=devour` event).
- **Slots & loadouts** — bind a captured ability to one of 6 action-bar slots. Three bucket types:
  the **universal** loadout (fires on any weapon), **per-weapon** loadouts (fire only when that
  weapon is drawn; they override the universal bind on their slots), and **per-form** loadouts (one
  per vanilla wheel form — Wolf/Bear/Rat/Spider/Toad/Werewolf/Gargoyle). Auto-switches on weapon swap
  / form enter; all stream via `api slots` (`bucket=any` / `<WeaponFamily>` / `<Form>`).
- **Hotkeys** — named bindings beyond the 6 slots; cast via `.beelz cast <name>` (the BCH-button
  mechanism). Capped by `Hotkeys_MaxPerPlayer`.
- **Transform** — become **Dracula or Morgana** (the only two renderable forms). Multi-phase kits,
  signature summons, and detonation AoEs. Arbitrary-unit transformation is a future client feature
  (§7.1).
- **Summons** — summon abilities spawn player-allied minions (with caps, leashing, stash-on-waygate,
  clean despawn) — whether you're transformed or just cast a captured summon ability.
- **Untransformed casting** — captured chain abilities (projectiles/AoEs/teleport-detonates) fire
  correctly off the normal bar, not only while transformed.

### 10.3 PLAYER command reference (`.beelz …`)

All player-runnable; no special role required. (Human-text replies unless noted; for machine data
use the `api` equivalents.)

**Discover**
- `.beelz` / `.beelz help` — overview. `.beelz commands` — full sectioned command list.

**Collection**
- `.beelz list [vblood|shard|regular] [page]` — your captured abilities (machine: `api list`).
- `.beelz search <term>` — find a captured ability by name.
- `.beelz info <index|name>` — one ability's details (machine: `api info <index>`).
- `.beelz bestiary [page]` · `.beelz bestiary unit <name>` — collection book, per-unit X/Y.
- `.beelz progress` — completion % (machine: `api progress`). `.beelz top` — server leaderboard by collection %.
- `.beelz odds` — your live drop / Devour / pity chances.
- `.beelz catalog [page]` — curated ability/unit reference. `.beelz current` — your active bar.

**Loadouts (action bar)** — the **slot** argument is a token: `1`–`6`, or **`primary`** (left-click attack, engine slot 0) / **`ultimate`** (the **T** key, engine slot 7). Numeric still works.
- `.beelz grant <slot|primary|ultimate> <index|abilityID>` — bind a captured ability to a universal slot (accepts the list index OR the ability GUID).
- `.beelz unslot <slot>` — clear a universal slot.
- `.beelz weapon-grant <weapon|auto> <slot|primary|ultimate> <index|abilityID>` — bind to a per-weapon loadout (`auto` = your current weapon).
- `.beelz weapon-unslot <weapon|auto> <slot>` — clear a per-weapon bind.
- `.beelz form-grant <form> <slot|primary|ultimate> <index|abilityID>` · `.beelz form-unslot <form> <slot>` — per-form loadouts (Wolf/Bear/Rat/Spider/Toad/Werewolf/Gargoyle + all skins); abilities map onto the slots the form actually renders.
- `.beelz loadouts` — summary of universal + per-weapon + per-form sets + active bucket (machine: `api slots`).
- **`.beelz clearbar [all|universal|<weapon>|<form>]`** — clear a chosen loadout (no confirmation; captures kept). No arg/`all` = everything; `universal` = any-weapon set; a weapon = that weapon's set; a form = that form's set. **This is the clear-bar BCH should call** (see §4). Emits `slot-cleared`.
- `.beelz resetbar CONFIRM` — clear ALL binds → vanilla bar (keeps captures). **⚠️ requires the literal `CONFIRM` token (v0.76)** — bare `resetbar` no-ops; prefer `clearbar`. `.beelz refresh` — re-apply your bar if it looks wrong ("fix bar").
- `.beelz preset save|load|list|delete <name>` — save/restore loadout presets.

**Extra hotkeys & casting**
- `.beelz hotkey set <name> <index>` · `clear <name>` · `list` — named bindings (machine: `api hotkeys`).
- `.beelz cast <hotkey name|index>` — force-cast any captured ability on demand (respects cooldown).

**Transform (Dracula / Morgana only)**
- `.beelz transforms [filter]` — your unlocked transforms (machine: `api transforms`).
- `.beelz transform <index|name>` — transform. `.beelz revert` — end it.
- `.beelz preview <index|name>` — what you'd get per phase. `.beelz phase [n]` — switch phase.
- `.beelz active` — your active transform (machine: `api active`). `.beelz detonate` — fire its AoE.
- `.beelz summon [n]` — cast a transformed unit's signature add-summon.

**Summons**
- `.beelz summons <stash|restore|clear|status>` — manage your minions (works untransformed too).
- `.beelz tp` — recall summons to you.

**Manage / settings**
- `.beelz forget <i>` · `.beelz forget-transform <i>` — delete a capture/unlock.
- `.beelz clear CONFIRM` — wipe all your captures + slots (literal `CONFIRM` required).
- `.beelz verbosity <silent|summary|verbose>` — chat-notification level. `.beelz silent <on|off>` — mute just the "you already knew that" devour message.

**Machine API (player-runnable; what BCH calls)** — `.beelz api <version|list|slots|transforms|active|info|info-guid <abilityGuid>|progress|rules|catalog|catalog units|catalog abilities|hotkeys|transform-config|bestiary|config|cooldowns|verbosity|bch>` (full field specs in §2; `info`/`info-guid`/`catalog-ability` are chunked — reassemble `part=k/n`). `.beelz api bch on` subscribes to the live event stream.

### 10.4 ADMIN command reference (`.beelz admin …`)

All `adminOnly` (VCF gates on V Rising admin status). Reply in human text; audited to the server log.

**Inspect** — `admin help` · `admin rules` · `admin inspect <player>` · `admin progress <player>` · `admin snapshot` · `admin buffs [player]` · `admin tune-list` · `admin transform show` · `admin dump <abilityGuid|form|forms>` (v0.92/0.94 DIAGNOSTIC — logs an ability's full component chain + key field values, your active form buff's components, or `forms` = every shapeshift form-buff prefab + which form it maps to; output goes to `LogOutput.log`).

**Capture filters** — `admin deny/undeny <pattern>` · `admin allow/unallow <pattern>` · `admin denyguid/allowguid <add|remove> <guid>` · `admin transformonly <add|remove> <pattern|guid>` · `admin reload`.

**Per-ability config (live)** — `admin ability <name|ID> <field> <value>` — ONE field per command. Curation: enabled, weapons, forms, transformonly, difficulty, phase, allowdenied, category, notes · scaling: damagescale, cooldownscale · server-wide shaping (Abilities_ApplyConfig, default ON): cooldown, range, charges, chargetime, aoe, projspeed, duration, healing, **forcetimeout** (make an indefinite effect expire after N s), **freelymove** (free to move N s into a cast), **interruptonhit** (cancel the cast when hit), interruptible (player self-cancel), freemove, castspeed, **summoncap** (concurrent uses), **summontimeout** (lifespan s), **summonunits** (units per cast). **Reset to baseline:** `admin ability <id> defaults` / `admin ability all defaults`. Shortcut: `admin tune <ability> <knob> <value>` · `admin tune-list`. Caveats: `range` also clamps projectile travel; `duration` = effect length not channel time; `charges` only works on abilities that already have charges; `interruptonhit` ≠ `interruptible` (hit-cancel vs player self-cancel).

**Per-unit transform config (live)** — `admin transform-set <CHAR_unit> <field> <value>` (enabled, difficulty, tier, damagescale, cooldownscale, healthscale, speedscale, fullreplace, powerscalingmode, notes).

**Global config** — `admin default <damagescale|cooldownscale> <value>` · `admin set <key> <value>` (any `.cfg` key) · `admin difficulty [basic|brutal]` · `admin freeze-captures <on|off|status>` · `admin transform mode|duration|cooldown <regular|vblood> <…>`.

**Server announcements (v0.88)** — `admin broadcast <status | leaderboard on|off | interval <minutes> | top <1-5> | complete on|off | test>` — toggles/schedules the 100%-collection broadcast and the periodic leaderboard broadcast. Message pools + enables live in the `Announcements` config section (`Broadcast_*`).

**Player grants** — `admin give|revoke <player> <unitGuid> <abilityGuid>` · `admin devour <player> <unitGuid>` · `admin give-transform|revoke-transform <player> <unitGuid>` · `admin set-slot|clear-slot <player> <slot> [abilityGuid]` · `admin set-weapon-slot|clear-weapon-slot <player> <weapon> <slot> [abilityGuid]`.

**Transform control** — `admin force-transform <player> <unitGuid>` · `admin clear-transform <player>` · `admin revert-all`.

**Summons / recovery** — `admin desummon <player>` · `admin desummon-all` · `admin respawn|rebuildbar|rebuildslots|clearslotmods [player]` · `admin copy-collection|paste-collection <player>` · `admin reset-character <player> CONFIRM-RESET`.

**Test / destructive** — `admin testform <wolf|bear|off>` · `admin scan-abilities` · `admin wipe-all CONFIRM-WIPE`.

### 10.5 Configuration (what an admin can change, and how)

Three files under the server's `BepInEx/config/` (all auto-created on first run; the mod runs on
defaults with zero setup):

| File | Controls | Live in-game? |
|---|---|---|
| `kdpen.Beelzebub.cfg` | Global switches: capture on/off + drop rates, `Capture_InclusiveMode`, `Grant_EnforceTransformOnly`, `Capture_PitySessionBased`, transform modes/durations/cooldowns, summon behavior, power-scaling modes, `Abilities_ApplyConfig` (per-ability shaping master, **default ON**), `Forms_CustomAbilities_Enabled` (**default ON**), the **`Announcements`** section (`Broadcast_CollectionComplete_*`, `Broadcast_Leaderboard_*`), server difficulty, hotkey limits, logging. NOTE renamed keys: `AbilityTuning_Enabled`→`Abilities_ApplyConfig`, `DropChance_Transform_*`→`DropChance_Devour_*`. | `admin set <key> <value>` (+ `difficulty`, `freeze-captures`, `transform …`, `broadcast …`). Hand-edits need a **server restart**. |
| `kdpen.Beelzebub/ability_rules.json` | Capture deny/allow lists + GUIDs, transform-only lists, `Defaults` scaling, per-ability `AbilityMap`, per-unit `TransformMap`, drop-rate overrides. | `admin ability` / `transform-set` / `default` / `deny*` / `allow*` / `transformonly`. Hand-edits need **`admin reload`** (no restart). |
| `kdpen.Beelzebub/ability_metadata_overrides.json` | Optional: override an ability's display name/description/school/type/category. | Hand-edit + `admin reload`. |

Full field-by-field reference: **`docs/ABILITY_CONFIG.md`**. BCH can build a settings panel
generically from `api config` (reflection-streamed keys) + `api rules` / `api catalog units|abilities`,
and write via the admin commands above.

### 10.6 User vs admin — how BCH should gate the UI

- **Players** see/operate their **own** collection, loadouts, hotkeys, transforms, summons, presets,
  and verbosity. Every player command targets the caller; none take a `<player>` argument.
- **Admins** operate on **other players** (commands take a `<player>` name) and on **server-wide
  rules/config** (capture filters, per-ability/per-unit tuning, drop rates, wipes). Admin commands
  reply in human text + audit to the log.
- **Gating signal:** there is no "am I admin?" wire query — BCH should mirror V Rising's own admin
  status (the same gate VCF uses) to decide whether to render the admin panel. A non-admin who
  sends an admin command just gets a rejection reply.
- **Read vs write:** all `api …` reads are player-safe (BCH uses them freely). All *writes* go
  through normal chat commands — player writes for the caller, `admin …` writes for everything else.

---

*Maintenance: when Beelzebub work changes any command, config key, `[BEELZ:*]`
line, or transform capability, update the relevant section here in the same
change. A repo hook reminds on edits to BCH-relevant files (see this project's
`CLAUDE.md` → "BCH integration handoff").*
