using System.Linq;
using System.Text;
using Beelzebub.Services;
using Stunlock.Core;
using VampireCommandFramework;

namespace Beelzebub.Commands;

/// <summary>
/// Structured chat output for client UI integration (BCH / BloodCraftHub).
///
/// Wire format: every line begins with a "[BEELZ:&lt;tag&gt;]" marker, followed by
/// space-separated "key=value" fields. Values are bare tokens (no quoting); prefab
/// names are guaranteed [A-Za-z0-9_] only by V Rising's naming conventions.
///
/// Lists are streamed line-by-line and terminated with "[BEELZ:end] cmd=&lt;name&gt; count=&lt;n&gt;".
/// Errors use "[BEELZ:err] cmd=&lt;name&gt; code=&lt;code&gt; msg=&lt;text&gt;".
///
/// Current API version is 1.
/// </summary>
[CommandGroup("beelz api")]
internal static class ApiCommands
{
    // v2 (v0.35.0): `api info` gained weapon_anim/school/cooldown_seconds + real
    //   descriptions; events gained forget/forget-transform/cleared; transform-ended
    //   now fires on every revert path.
    // v3 (v0.39.0): added `api config` (full settings dump) + `api cooldowns`
    //   (per-category remaining); `shard=0|1` on api transforms + api catalog units;
    //   new event type=config-changed (from `.beelz admin set`).
    // v4 (v0.40.0): expanded action bar — `.beelz cast <hotkey|index>` force-casts any
    //   captured ability (the BCH-button mechanism); new event type=cast.
    // v5 (v0.43.3): `.beelz summon [n]` force-casts a transformed unit's signature
    //   add-summon (Toad King frogs, Werewolf cages, …) that bosses trigger at a low-HP
    //   soft phase; new event type=summon (u=, ability=). Config key
    //   Transform_SummonCooldownSeconds (flows through api config / admin set).
    // v6 (v0.44.0): PER-ABILITY BASELINE. Transformation is now Dracula/Morgana-only;
    //   every other unit's "jackpot" roll DEVOURS the unit (grants its whole ability kit
    //   at once). New event type=devour (s= u= un= count=). type=transform-unlock now ONLY
    //   fires for Dracula/Morgana. `.beelz transform`/`transforms` resolve only those two.
    //   New admin command `.beelz admin devour`. Arbitrary-unit transformation = postponed
    //   phase-two feature. All additive — older parsers ignore the unknown type=devour.
    // v7 (v0.51.0): ability `cat=` badge broadened — new category `Melee` and many abilities that
    //   used to report `cat=Other` now classify into a real bucket. `cat=` is the enum NAME
    //   (Other/Travel/Aoe/Projectile/Summon/Buff/WeaponSpell/Spell/Melee), NOT a number. An admin
    //   `Category` override (ability_rules.json) can set it. Parsers MUST treat any unknown `cat=`
    //   value as Other (forward-compatible).
    // v8 (v0.54.0): wire-extraction completeness (additive fields only). `api info` now also emits
    //   cat, category_override, cast_time_seconds, range, behavior, phase, allow_denied,
    //   interruptible, free_move, cast_speed. `catalog-ability` adds category_override, phase,
    //   allow_denied, interruptible, free_move, cast_speed. `api rules` adds default_damage_scale,
    //   default_cooldown_scale, transform_only_patterns, deny_guids/allow_guids lists. `catalog-unit`
    //   adds slot_template. type=config-changed is now BROADCAST to all subscribed clients.
    // v9 (v0.57.0): `api catalog abilities` now streams the FULL capturable universe — the union of
    //   the curated AbilityMap AND every discovered AB_*_AbilityGroup/_Group that passes the capture
    //   filter (AbilityFilter.ShouldCapture) — instead of only the curated map. New additive field
    //   `curated={0|1}` per row distinguishes a hand-curated entry from a discovered-only one (whose
    //   tuning fields are defaults: category_override=-, phase=1, interruptible=auto, free_move=0,
    //   cast_speed=auto, notes empty; weapons/cat/enabled/difficulty/transform_only are still derived
    //   from the name heuristics + rules). The `total=`/`pages=` count is now the full catalog (can be
    //   35+ pages under the default Capture_InclusiveMode), so a "Missing" list built from it is
    //   complete. Field set is otherwise unchanged; older parsers ignore `curated=`.
    // v10 (v0.58.0): friendly names + catalog descriptions (additive fields only).
    //   `api list` now also emits `label=` (friendly ability name) and `ulabel=` (friendly unit
    //   name) alongside the raw `an=`/`un=` — both SafeToken-encoded (spaces→_), so BCH can show
    //   localized names without client-side humanizing (raw names kept for wire stability).
    //   `catalog-ability` now also emits `desc=` (curated description, %param%-substituted, or `-`)
    //   and `school=` (Blood/Chaos/Frost/… or `none`) so the Bestiary's MISSING rows can carry
    //   description/school text. Note: only the curated subset has these (~13% desc / ~4% school) —
    //   the rest are `-`/`none` until the localization data-pass fills more in.
    // v11 (v0.59.0): PER-FORM ability loadouts (additive). `api slots` now also streams
    //   `[BEELZ:form-slot] form=<Form> slot= a= an=` lines for each per-form bucket (a NEW line type
    //   — older parsers that read `bucket=` as a WeaponFamily ignore it), and the `[BEELZ:slot-current]`
    //   footer gains `form=<Form|None>` (active shapeshift form). New player commands `.beelz
    //   form-grant <form|auto> <slot> <index>` / `form-unslot <form> <slot>` (forms: Wolf/Bear/Rat/
    //   Spider/Toad/Werewolf/Gargoyle); new events type=form-slot-granted / type=form-slot-cleared
    //   (form= slot= a= an=). The loadout applies in-form only when Forms_CustomAbilities_Enabled is on.
    // v12 (v0.65.0): per-ability ABSOLUTE cooldown / max-range overrides (additive). `api info` and
    //   `catalog-ability` now also emit `cooldown_override=` (seconds, or `-` if unset) and
    //   `range_override=` (max cast distance, or `-`). These are admin-curated baked-prefab edits
    //   (`.beelz admin ability <name> cooldown|range <v>` / `.beelz admin tune`), applied only when
    //   Abilities_ApplyConfig; the GLOBAL minimum-cooldown floor is the new config
    //   `Grant_MinimumCooldownSeconds` (streamed via `api config`). The live baked values are still
    //   `cooldown_seconds=`/`range=` on `api info`; the `_override=` fields show what the admin SET.
    // v13 (v0.67.0): more server-wide ability shaping (additive). `api info` and `catalog-ability`
    //   now also emit `charges_override=` (max charges), `chargetime_override=` (recharge seconds),
    //   `aoe_override=` (area radius), `projspeed_override=` (projectile speed) — `-` when unset.
    //   Set via `.beelz admin ability <name> <charges|chargetime|aoe|projspeed> <v>` / `.beelz admin
    //   tune`. Applied (server-wide, baked) when Abilities_ApplyConfig is on (now DEFAULT ON — the old
    //   AbilityTuning_Enabled opt-in, renamed in v0.66.0).
    // v14 (v0.68.0): effect-duration + healing shaping (additive). `api info` and `catalog-ability`
    //   now also emit `duration_override=` (applied buff/debuff duration seconds) and `heal_mult=`
    //   (healing multiplier) — `-` when unset. Set via `.beelz admin ability <name> <duration|healing>
    //   <v>` / `.beelz admin tune`. Server-wide baked edits (Abilities_ApplyConfig).
    // v15 (v0.76.0): **WIRE-BREAKING for `api info` + `catalog-ability` — BCH MUST UPDATE ITS PARSER.**
    //   These two lines had grown past ~30 fields + description and EXCEEDED VCF's 512-byte FixedString
    //   reply cap, throwing "Truncation while copying" and FAILING the command (api info was fully
    //   broken; one long catalog row aborted the whole stream). They are now emitted CHUNKED: each line
    //   carries a NEW `part=k/n` field and repeats its id (`i=<index>` for info, `an=<name>` for
    //   catalog-ability). Reassemble by concatenating the `key=value` tokens of parts 1..n for the same
    //   id; a short ability is simply `part=1/1`. No fields changed names; `desc`/`notes` are clamped to
    //   256 chars. Other lines (list/slots/catalog-unit/rules/config/events) are unchanged.
    // All additive EXCEPT v15's chunking of info/catalog-ability — older parsers that ignore `part=` will
    //   read only the first chunk of a long ability (degraded, not crashed).
    // v16 (v0.79.0): summon governance (additive). `api info` and `catalog-ability` now also emit
    //   `summon_cap_override=` (per-ability max simultaneous summon uses; `-` = use the global
    //   Transform_MaxStacksPerSummonAbility, default 3) and `summon_timeout_override=` (per-ability summon
    //   auto-despawn seconds; `-` = use the global Transform_SummonLifetimeSeconds, default CHANGED 0 → 30
    //   in v0.79). Set via `.beelz admin ability <name> <summoncap|summontimeout> <v>` / `.beelz admin
    //   tune`. Precedence: per-ability override > global default > engine. (These are read LIVE — not
    //   baked prefab edits.) A BCH summon-config panel reads/writes these like the other `_override=` fields.
    // v17 (v0.80.0): summon UNITS-per-cast (additive). `api info` + `catalog-ability` now also emit
    //   `summon_units_override=` (per-ability max UNITS a single cast produces; `-` = the ability's
    //   natural count). SEPARATE from `summon_cap_override=` (concurrent USES) — whichever is hit first
    //   rules. Set via `.beelz admin ability <name|id> summonunits <n>` / `.beelz admin tune`. (v0.80 also
    //   fixed summoncap to be a true USE cap — it no longer trims units within a single cast — and made
    //   skinned shapeshift forms recognized; neither is a wire change.)
    // v18 (v0.84.0): additive. (a) NEW read command **`api info-guid <abilityGuid>`** — returns the same
    //   chunked `[BEELZ:info]` tooltip data as `api info` but keyed by an ability's PrefabGUID, for
    //   ACTIVE/ASSIGNED abilities where BCH has the GUID (from `api slots`) but not a captured-list index
    //   (fixes "No name"/generic tooltips on the bar). The reassembly id is `a=<guid>` instead of
    //   `i=<index>`; everything else identical (u=0/un=- when the unit is unknown). (b) NEW event
    //   **`type=collection-complete count=<n> total=<n>`** fired once when a player captures the last
    //   ability in the curated catalog. All additive — older parsers ignore the new command/event.
    // v19 (v0.85.0): force-timeout (additive). `api info` + `catalog-ability` now also emit
    //   `force_timeout_override=` (seconds; `-` = none) — forces an ability's otherwise-INDEFINITE spawned
    //   effects/buffs to expire after N seconds (adds a LifeTime+Destroy where the buff has none, the case
    //   `duration_override` can't reach). Set via `.beelz admin ability <name|id> forcetimeout <v>` /
    //   `.beelz admin tune`. Server-wide baked edit (Abilities_ApplyConfig); cleared by `defaults`.
    // v20 (v0.87.0): two cast modifiers (additive). `api info` + `catalog-ability` now also emit
    //   `free_move_secs=` (seconds; `-` = none) — free the caster to move N seconds INTO the cast
    //   (ModifyMovementDuringCastData.Duration; the cast continues) — and `interrupt_on_hit=on|off|auto`
    //   — cancel the cast when the caster TAKES DAMAGE (InterruptTypes.OnDamageTaken). Set via
    //   `.beelz admin ability <name|id> freelymove <sec>` / `interruptonhit on|off` (or `tune`). Server-wide
    //   baked edit (Abilities_ApplyConfig); cleared by `defaults`. Note `interrupt_on_hit` is distinct from
    //   the existing `interruptible` (player self-cancel / ManualInterrupt).
    // v21 (v0.100.0): `catalog-ability` now also emits `a=<guid>` (the ability's PrefabGUID), `unit=<name>`
    //   (SafeToken-encoded primary source-NPC name, `-` if unknown) and `unitguid=<int>` (source-NPC GUID,
    //   0 if unknown) — filling the owning-unit + ID for UNCAPTURED abilities (was capture-line only). New
    //   admin command `api catalog abilities-all` streams EVERY ability group regardless of enable/deny/
    //   difficulty (for config); the existing `api catalog abilities` stays the collectible/player set, and
    //   both carry `enabled=` so a client filters to the enabled set for progress tracking. All additive.
    // v22 (v0.100.x): structured transform-loadout + broadcast-pool reads for the BCH editors (additive —
    //   older parsers ignore the new commands/lines). NEW reads:
    //   • `api tform-kit <unit>` → one `[BEELZ:tform-ability] unit= idx= a= an=` per ability in a boss's
    //     FULL eligible kit (UnitKitService.FullEligibleKit — the pool you bind from), then
    //     `[BEELZ:end] cmd=tform-kit unit= count=`.
    //   • `api tform-binds <unit>` → the caller's CUSTOM per-phase binds as
    //     `[BEELZ:tform-slot] unit= phase= slot= a= an=` (one per bound slot; empty = just the end line),
    //     then `[BEELZ:end] cmd=tform-binds unit= count= phases=<n>` (phases = how many phases the form
    //     has, including any player-defined custom ones). Both resolve <unit> exactly like `.beelz tform`
    //     (index into your unlocks / unlocked GUID / name).
    //   • `api broadcast-msgs <complete|leaderboard>` (ADMIN) → one
    //     `[BEELZ:broadcast-msg] pool= idx= text=` per message in that pool (idx is 1-based to match the
    //     `admin broadcast-msg edit/remove <n>` write commands; text= SafeToken-encoded), then
    //     `[BEELZ:end] cmd=broadcast-msgs pool= count=`.
    //   These let BCH's transform-loadout + announcements editors read state structurally instead of
    //   parsing human chat text.
    // v23 (v0.101.0): CATALOG FILTERING (additive — old `catalog abilities [page]` calls still work). Both
    //   `api catalog abilities` and `api catalog abilities-all` now accept an optional FILTER:
    //   `api catalog abilities <page> <filter> <value>` where filter ∈
    //     weapon=<family> (e.g. Sword) | cat=<Summon|Spell|Projectile|Melee|Buff|Aoe|Travel|WeaponSpell|Other>
    //     | unit=<substring of source-NPC name> | form=<Wolf|Bear|…|Mounted> (curated Forms-tagged) |
    //     search=<substring of ability name>.
    //   The stream is filtered to the matching subset BEFORE pagination, so BCH can load just "sword
    //   abilities" / "summons" / "Erwin's abilities" quickly instead of the full ~1700-row catalog. The
    //   `[BEELZ:end]` line now also carries `filter=<key|-> value=<val|->`; `total=`/`pages=` reflect the
    //   FILTERED set. Old parsers that ignore the extra tokens + send no filter get the full list as before.
    // v24 (v0.107.0): ACTIVATION-CONDITION metadata (additive). `api info` + `catalog-ability` now also emit
    //   `condition=<Aimed|CloseRange|Summon|SelfCast|Movement|-> condition_mods=<Combo,Charged,Channel|->
    //   condition_source=<auto|confirmed|admin|->`. Auto-classified from prefab data (tools/classify_conditions.py);
    //   it states HOW an ability is used (aim it / be adjacent / summons / self / mobility) so a working-but-
    //   conditional ability isn't shown as broken. INFORMATIONAL ONLY — never disables (distinct from the
    //   `incompatible` flag). `source=auto` = unconfirmed candidate. BCH (api>=24) can show a "Use:" hint /
    //   filter chip; treat `condition=-` as unknown. All additive — old parsers ignore the new tokens.
    // v25 (v0.112.0): REVIEW/CURATION tracking (additive). `api info` + `catalog-ability` now also emit
    //   `review_status=<Unreviewed|Reviewed|Approved|Blocked|Hidden> review_tag=<emote|feed|idle_flee|
    //   variant_hard|basic_attack|reaction|combo|...|->`. review_status = where the ability sits in our
    //   curation workflow; review_tag = the audit-assigned TYPE for grouped follow-up/testing. NEITHER is
    //   a runtime gate (enabled= stays the kill-switch). BCH (api>=25) can group/filter a "test backlog"
    //   by tag (e.g. show all emotes flagged for usability testing) and surface review_status. Additive —
    //   old parsers ignore the two new tokens. Populated from ability_rules(.default).json.
    // v26 (v0.113.0): SOURCE-TIER metadata (additive). `api info` + `catalog-ability` now also emit
    //   `source_level=<int|-> source_tier=<T1|T2|T3|T4|-> is_vblood=<0|1>`. The primary source unit's
    //   level + a level-derived difficulty tier (T1<30 / T2 30-46 / T3 47-63 / T4 64+) + whether that unit
    //   is a VBlood boss. INFORMATIONAL — for "captured from <unit> (T3 VBlood)" display + tier filtering.
    //   `-`/`0` when no source NPC is mapped (~813 of 1,813). Baked into ability_metadata.json by
    //   tools/merge_tier_into_metadata.py. Additive — old parsers ignore the three new tokens.
    // v27 (v0.116.0): CATALOG FILTERS extended (additive). `api catalog abilities[-all] <page> <filter> <value>`
    //   now also accepts filter keys `tag`/`reviewtag` (review_tag), `status`/`reviewstatus` (review_status),
    //   `tier` (T1|T2|T3|T4), and `vblood` (1|0). So BCH can load just one curation/source group server-side
    //   (e.g. `catalog abilities-all 0 tag emote`, or `tier T4`, or `vblood 1`) instead of streaming all rows.
    //   The existing keys (search|weapon|cat|unit|form) are unchanged. Gate `api>=27`. Fully additive.
    // v28 (v0.119.0): `api slots` now emits `label=<friendly ability name>` on every `[BEELZ:slot]` and
    //   `[BEELZ:form-slot]` line (SafeToken-encoded). Lets BCH render a hover CARD with the real ability name
    //   for Beelz-granted slots — the native action-bar tooltip shows "No Name" for NPC abilities (a V Rising
    //   client-localization gap; the server can't fix the native card). Full card data (desc/cooldown/cast/
    //   condition/…) is still per-ability via `api info-guid <guid>`. Only Beelz-bound slots appear in `api
    //   slots`, so BCH scopes its card to these and leaves VANILLA slots' native tooltips untouched. Additive.
    const int ApiVersion = 28;

    [Command("help", description: "List the Beelzebub API/BCH read commands (machine-readable data streams).")]
    public static void Help(ChatCommandContext ctx)
    {
        ctx.Reply("=== Beelzebub API commands === (BCH-readable; emit [BEELZ:*] data lines for client UIs)");
        ctx.Reply(".beelz api version — API + plugin version · .beelz api bch <on|off|status> — toggle your event stream");
        ctx.Reply(".beelz api list / slots / transforms / hotkeys / active — stream your own data");
        ctx.Reply(".beelz api info <index> / info-guid <guid> — ability tooltip data (by list index or PrefabGUID) · .beelz api progress — completion data");
        ctx.Reply(".beelz api bestiary [page] — collection book · .beelz api verbosity — your verbosity setting");
        ctx.Reply(".beelz api catalog [units|abilities] [page] [filter value] — curated-catalog streams · catalog abilities-all (admin) — every ability for config");
        ctx.Reply("    filters: weapon|cat|unit|form|search|tag|reviewstatus|tier|vblood — load just a subset fast");
        ctx.Reply(".beelz api rules / config / cooldowns / transform-config — server config + state streams");
        ctx.Reply(".beelz api tform-kit <unit> / tform-binds <unit> — transform kit + your custom binds · api broadcast-msgs <pool> (admin)");
        ctx.Reply("These power BloodCraftHub's on-screen UI; most just stream data and don't change anything.");
    }

    [Command("version", description: "Return the Beelzebub API version (BCH-readable).")]
    public static void Version(ChatCommandContext ctx)
    {
        ctx.Reply($"[BEELZ:version] api={ApiVersion} plugin={MyPluginInfo.PLUGIN_VERSION} ready={(Core.IsReady ? 1 : 0)}");
    }

    [Command("list", description: "Stream the caller's captured abilities (BCH-readable).")]
    public static void List(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=list code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var captured = Core.AbilityRegistry.ListFor(steamId);
        for (int i = 0; i < captured.Count; i++)
        {
            var c = captured[i];
            // IN2 (v0.15.1): append derived `cat` (ability category) + `type`
            // (unit type) so BCH can render badges without parsing the prefab name.
            string abilityName = new PrefabGUID(c.AbilityPrefabGuid).GetPrefabName();
            string unitName = new PrefabGUID(c.UnitPrefabGuid).GetPrefabName();
            // v0.51.0: admin Category override (ability_rules.json) wins over the name heuristic.
            var cat = Core.AbilityRules.GetAbilityCategoryOverride(abilityName) ?? Categorization.ClassifyAbility(abilityName);
            var type = Categorization.ClassifyTransform(unitName);
            // v0.58.0: friendly names so BCH needn't humanize raw prefab names client-side. Raw
            // an=/un= kept for wire stability; label=/ulabel= are SafeToken-encoded (spaces→_).
            string abilityLabel = Core.AbilityMetadata?.ResolveAbilityName(c.AbilityPrefabGuid) ?? abilityName.Humanize();
            string unitLabel = Core.AbilityMetadata?.ResolveUnitName(c.UnitPrefabGuid) ?? unitName.Humanize();
            ctx.Reply(
                $"[BEELZ:list] i={i} s={(c.Source == CaptureSource.VBlood ? "V" : "R")}" +
                $" u={c.UnitPrefabGuid} un={unitName}" +
                $" a={c.AbilityPrefabGuid} an={abilityName}" +
                $" label={SafeToken(abilityLabel)} ulabel={SafeToken(unitLabel)}" +
                $" cat={cat} type={type}");
        }
        ctx.Reply($"[BEELZ:end] cmd=list count={captured.Count}");
    }

    [Command("slots", description: "Stream the caller's current slot assignments — universal + weapon + form buckets, each row carrying a=<guid> an=<prefab> label=<friendly name> (BCH-readable; label= for the hover card).")]
    public static void Slots(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=slots code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();

        // v0.119.0 (ApiVersion 28): emit the friendly ability NAME per slot so BCH can render a hover card
        // with the real name (the native action-bar tooltip shows "No Name" for NPC abilities — client
        // localization gap). Lightweight name-only resolver (ResolveAbilityName, no ECS probe), SafeToken-
        // encoded. Full card data (desc/stats) is still fetched per-ability via `api info-guid`. Only
        // Beelz-bound slots appear here, so BCH scopes its card to these and leaves vanilla slots' native
        // tooltips untouched.
        string Label(int g) => SafeToken(Core.AbilityMetadata?.ResolveAbilityName(g)
            ?? new PrefabGUID(g).GetPrefabName().Humanize());

        int n = 0;
        // Universal bucket (bucket=any).
        var universal = Core.AbilityRegistry.GetSlots(steamId);
        foreach (var (slot, abilityGuid) in universal.OrderBy(kv => kv.Key))
        {
            ctx.Reply(
                $"[BEELZ:slot] bucket=any slot={slot}" +
                $" a={abilityGuid} an={new PrefabGUID(abilityGuid).GetPrefabName()} label={Label(abilityGuid)}");
            n++;
        }
        // W3 per-weapon buckets.
        var perWeapon = Core.AbilityRegistry.AllWeaponSlots(steamId);
        foreach (var (weapon, slots) in perWeapon.OrderBy(kv => kv.Key.ToString()))
        {
            foreach (var (slot, abilityGuid) in slots.OrderBy(kv => kv.Key))
            {
                ctx.Reply(
                    $"[BEELZ:slot] bucket={weapon} slot={slot}" +
                    $" a={abilityGuid} an={new PrefabGUID(abilityGuid).GetPrefabName()} label={Label(abilityGuid)}");
                n++;
            }
        }
        // v0.59.0: per-FORM buckets, emitted as a distinct line type so older parsers (which read
        // bucket= as a WeaponFamily) ignore them cleanly while new BCH builds the per-form picker.
        var perForm = Core.AbilityRegistry.AllFormSlots(steamId);
        foreach (var (form, slots) in perForm.OrderBy(kv => kv.Key.ToString()))
        {
            foreach (var (slot, abilityGuid) in slots.OrderBy(kv => kv.Key))
            {
                ctx.Reply(
                    $"[BEELZ:form-slot] form={form} slot={slot}" +
                    $" a={abilityGuid} an={new PrefabGUID(abilityGuid).GetPrefabName()} label={Label(abilityGuid)}");
                n++;
            }
        }
        // Current weapon + form footer so BCH knows which buckets are active right now.
        var current = Beelzebub.Services.SlotApply.GetCurrentWeapon(ctx.Event.SenderCharacterEntity);
        var currentForm = Beelzebub.Services.ShapeshiftAbilityService.GetCurrentForm(ctx.Event.SenderCharacterEntity);
        ctx.Reply($"[BEELZ:slot-current] weapon={current} form={currentForm}");
        ctx.Reply($"[BEELZ:end] cmd=slots count={n}");
    }

    [Command("transforms", description: "Stream the caller's transform unlocks (BCH-readable). Matrix attributes included.")]
    public static void Transforms(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=transforms code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var unlocks = Core.AbilityRegistry.ListTransforms(steamId);
        for (int i = 0; i < unlocks.Count; i++)
        {
            var u = unlocks[i];
            // TX1 attributes from TransformMap if the admin curated them. TX6 stat
            // scales appended so BCH can render a "Power Profile" hover on each
            // unlock without a second round-trip to .beelz api catalog units.
            bool enabled = Core.AbilityRules.IsTransformUnitEnabled(u.UnitPrefabGuid);
            string difficulty = Core.AbilityRules.GetTransformDifficulty(u.UnitPrefabGuid);
            int tier = Core.AbilityRules.GetTransformTier(u.UnitPrefabGuid);
            float dmgScale = Core.AbilityRules.GetTransformDamageScale(u.UnitPrefabGuid);
            float cdScale = Core.AbilityRules.GetTransformCooldownScale(u.UnitPrefabGuid);
            float hpScale = Core.AbilityRules.GetTransformHealthScale(u.UnitPrefabGuid);
            float spdScale = Core.AbilityRules.GetTransformMovementSpeedScale(u.UnitPrefabGuid);
            // IN2 (v0.15.1): append unit `type` badge.
            // TX3 (v0.16.0): `full_replace` flag.
            // TX7 (v0.17.0): `scaling_mode` (effective resolution including per-entry override).
            string unitName = new PrefabGUID(u.UnitPrefabGuid).GetPrefabName();
            var type = Categorization.ClassifyTransform(unitName);
            bool fullReplace = Core.AbilityRules.IsTransformFullReplace(u.UnitPrefabGuid);
            var scalingMode = Core.AbilityRules.GetTransformPowerScalingMode(u.UnitPrefabGuid);
            ctx.Reply(
                $"[BEELZ:tx] i={i} s={(u.Source == CaptureSource.VBlood ? "V" : "R")}" +
                $" u={u.UnitPrefabGuid} un={unitName}" +
                $" enabled={(enabled ? 1 : 0)} difficulty={difficulty} tier={tier}" +
                $" damage_scale={dmgScale:F2} cooldown_scale={cdScale:F2}" +
                $" health_scale={hpScale:F2} speed_scale={spdScale:F2}" +
                $" type={type} full_replace={(fullReplace ? 1 : 0)}" +
                $" scaling_mode={scalingMode}" +
                $" shard={(Core.Transforms.IsShardBoss(u.UnitPrefabGuid) ? 1 : 0)}");
        }
        ctx.Reply($"[BEELZ:end] cmd=transforms count={unlocks.Count}");
    }

    [Command("active", description: "Return the caller's active transform, if any (BCH-readable).")]
    public static void Active(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=active code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active is null) { ctx.Reply("[BEELZ:active] none=1"); return; }

        string ttl;
        if (active.Duration.HasValue)
        {
            var remaining = active.Duration.Value.TotalSeconds - (System.DateTime.UtcNow - active.ActivatedAtUtc).TotalSeconds;
            ttl = remaining > 0 ? remaining.ToString("F0") : "0";
        }
        else
        {
            ttl = "toggle";
        }
        var pg = new PrefabGUID(active.UnitPrefabGuid);
        var phases = Core.Transforms.GetAvailablePhases(pg);
        ctx.Reply(
            $"[BEELZ:active] u={active.UnitPrefabGuid}" +
            $" un={pg.GetPrefabName()}" +
            $" s={(active.Source == CaptureSource.VBlood ? "V" : "R")} ttl={ttl}" +
            $" phase={active.CurrentPhase} phases={string.Join(",", phases)}");
    }

    [Command("info", description: "Return detailed info for one captured ability by index (BCH tooltip data).")]
    public static void Info(ChatCommandContext ctx, int index)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=info code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var captured = Core.AbilityRegistry.ListFor(steamId);
        if (index < 0 || index >= captured.Count)
        {
            ctx.Reply($"[BEELZ:err] cmd=info code=out_of_range msg=index_{index}_of_{captured.Count}");
            return;
        }
        var c = captured[index];
        EmitInfo(ctx, $"i={index}", c.AbilityPrefabGuid, c.UnitPrefabGuid, c.Source);
    }

    [Command("info-guid", description: "Return detailed BCH tooltip data for any ability by its PrefabGUID — for ACTIVE/ASSIGNED abilities where you have the GUID (from api slots) but not a captured-list index. Usage: .beelz api info-guid <abilityGuid>")]
    public static void InfoGuid(ChatCommandContext ctx, int abilityGuid)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=info-guid code=not_ready msg=plugin_not_initialized"); return; }
        if (abilityGuid == 0) { ctx.Reply("[BEELZ:err] cmd=info-guid code=bad_arg msg=guid_0"); return; }
        ulong sid = ctx.Event.SenderCharacterEntity.GetSteamId();
        // If the caller has this ability captured, use its real source unit for accurate un=/s=.
        int unitGuid = 0; CaptureSource src = CaptureSource.Regular;
        foreach (var cap in Core.AbilityRegistry.ListFor(sid))
            if (cap.AbilityPrefabGuid == abilityGuid) { unitGuid = cap.UnitPrefabGuid; src = cap.Source; break; }
        EmitInfo(ctx, $"a={abilityGuid}", abilityGuid, unitGuid, src);
    }

    /// <summary>v0.84.0: shared [BEELZ:info] body emitter (chunked) used by `api info` (by captured index)
    /// and `api info-guid` (by GUID). idField is the reassembly key BCH groups the parts by (i=… or a=…).</summary>
    static void EmitInfo(ChatCommandContext ctx, string idField, int abilityGuid, int unitGuid, CaptureSource source)
    {
        string unitName = unitGuid != 0 ? new PrefabGUID(unitGuid).GetPrefabName() : "-";
        string abilityName = new PrefabGUID(abilityGuid).GetPrefabName();
        string label = abilityName.Humanize();

        // A1-full: pull richer metadata from the AbilityMap matrix if curated.
        // Description prefers the admin-curated Notes field; falls back to a generic
        // "Captured from X." string. Weapons / Forms / scale data also surfaced for
        // BCH so it can render full tooltips without a second round-trip.
        var families = Core.AbilityRules.ClassifyWeaponFamilies(abilityName);
        var forms = Core.AbilityRules.GetFormRestriction(abilityName);
        bool transformOnly = Core.AbilityRules.IsTransformOnly(abilityName, abilityGuid);
        bool enabled = Core.AbilityRules.IsEnabled(abilityName, abilityGuid);
        float damageScale = Core.AbilityRules.GetDamageScale(abilityName);
        float cooldownScale = Core.AbilityRules.GetCooldownScale(abilityName);

        // v0.35.0: prefer the REAL ability description from resolved metadata
        // (shipped ability_metadata.json + ECS), with %param% substitution; fall
        // back to the admin-curated Notes, then a generic string.
        var meta = Core.AbilityMetadata?.Resolve(abilityGuid);
        string realDesc = meta?.Description;
        if (!string.IsNullOrWhiteSpace(realDesc) && meta?.Parameters != null)
            foreach (var kv in meta.Parameters) realDesc = realDesc.Replace("%" + kv.Key + "%", kv.Value);
        string desc = (!string.IsNullOrWhiteSpace(realDesc) ? realDesc : TryGetCuratedNotes(abilityName))
                      ?? (unitGuid != 0 ? $"Captured from {unitName.Humanize()}." : label);
        string difficulty = Core.AbilityRules.GetAbilityDifficulty(abilityName);

        // v0.35.0: BCH tooltip enrichment — the weapon whose animation the ability
        // is bound to (None for spells), plus school + cooldown for the tooltip.
        var animWeapon = Core.AbilityRules.GetAnimationWeapon(abilityName);
        string school = string.IsNullOrEmpty(meta?.School) ? "none" : meta.School;
        string cdSecs = meta?.CooldownSeconds.HasValue == true ? meta.CooldownSeconds.Value.ToString("F1") : "0";

        // v0.54.0: surface the fields the audit found were resolved server-side but never wired —
        // the category badge (so a tooltip from `api info` matches `api list`), the runtime
        // metadata (cast time / range / behavior), the multi-phase grouping + allow-denied flag,
        // the cast-tuning state, and whether the category is an admin override vs auto-classified.
        var catVal = Core.AbilityRules.GetAbilityCategoryOverride(abilityName) ?? Categorization.ClassifyAbility(abilityName);
        string catOverride = Core.AbilityRules.GetCategoryOverrideRaw(abilityName);
        int phase = Core.AbilityRules.GetAbilityPhase(abilityName);
        bool allowDenied = Core.AbilityRules.IsAllowDenied(abilityName, abilityGuid);
        bool? interruptible = Core.AbilityRules.GetInterruptible(abilityName);
        bool freeMove = Core.AbilityRules.GetFreeMoveAfterCast(abilityName);
        float? castSpeed = Core.AbilityRules.GetCastMovementSpeed(abilityName);
        float? freeMoveSecs = Core.AbilityRules.GetFreeMoveAfterSeconds(abilityName);   // v0.87.0
        bool? interruptOnHit = Core.AbilityRules.GetInterruptOnHit(abilityName);        // v0.87.0
        float? cdOverride = Core.AbilityRules.GetCooldownOverride(abilityName);
        float? rangeOverride = Core.AbilityRules.GetMaxRangeOverride(abilityName);
        int? chargesOverride = Core.AbilityRules.GetChargesMax(abilityName);
        float? chargeTimeOverride = Core.AbilityRules.GetChargeTimeSeconds(abilityName);
        float? aoeOverride = Core.AbilityRules.GetAoeRadius(abilityName);
        float? projSpeedOverride = Core.AbilityRules.GetProjectileSpeed(abilityName);
        float? durationOverride = Core.AbilityRules.GetEffectDurationSeconds(abilityName);
        float? healMultOverride = Core.AbilityRules.GetHealingMultiplier(abilityName);
        string castTime = meta?.CastTimeSeconds.HasValue == true ? meta.CastTimeSeconds.Value.ToString("F2") : "0";
        string range = meta?.MaxRange.HasValue == true ? meta.MaxRange.Value.ToString("F1") : "0";
        string behavior = string.IsNullOrEmpty(meta?.BehaviorType) ? "none" : meta.BehaviorType;

        // v0.76.0: this line outgrew VCF's 512-byte FixedString reply cap (~30 fields + description),
        // which threw "Truncation while copying" and FAILED the whole command. Emit it CHUNKED via
        // ReplyChunked — each line repeats i= and carries part=k/n; BCH reassembles. desc is clamped so
        // no single token can exceed the per-line budget. (ApiVersion 15.)
        string body =
            $"s={(source == CaptureSource.VBlood ? "V" : "R")}" +
            $" u={unitGuid} un={unitName}" +
            $" a={abilityGuid} an={abilityName}" +
            $" label={SafeToken(label)}" +
            $" desc={SafeToken(Clamp(desc, 256))}" +
            $" cat={catVal}" +
            $" category_override={(string.IsNullOrEmpty(catOverride) ? "-" : SafeToken(catOverride))}" +
            $" weapons={string.Join(",", families)}" +
            $" weapon_anim={animWeapon}" +
            $" school={SafeToken(school)}" +
            $" cooldown_seconds={cdSecs}" +
            $" cast_time_seconds={castTime}" +
            $" range={range}" +
            $" behavior={SafeToken(behavior)}" +
            $" forms={(forms.Count == 0 ? "any" : string.Join(",", forms))}" +
            $" transform_only={(transformOnly ? 1 : 0)}" +
            $" enabled={(enabled ? 1 : 0)}" +
            $" difficulty={difficulty}" +
            $" phase={phase}" +
            $" allow_denied={(allowDenied ? 1 : 0)}" +
            $" interruptible={(interruptible.HasValue ? (interruptible.Value ? "on" : "off") : "auto")}" +
            $" free_move={(freeMove ? 1 : 0)}" +
            $" cast_speed={(castSpeed.HasValue ? castSpeed.Value.ToString("F2") : "auto")}" +
            $" damage_scale={damageScale:F2}" +
            $" cooldown_scale={cooldownScale:F2}" +
            $" cooldown_override={(cdOverride.HasValue ? cdOverride.Value.ToString("F2") : "-")}" +
            $" range_override={(rangeOverride.HasValue ? rangeOverride.Value.ToString("F1") : "-")}" +
            $" charges_override={(chargesOverride.HasValue ? chargesOverride.Value.ToString() : "-")}" +
            $" chargetime_override={(chargeTimeOverride.HasValue ? chargeTimeOverride.Value.ToString("F2") : "-")}" +
            $" aoe_override={(aoeOverride.HasValue ? aoeOverride.Value.ToString("F1") : "-")}" +
            $" projspeed_override={(projSpeedOverride.HasValue ? projSpeedOverride.Value.ToString("F1") : "-")}" +
            $" duration_override={(durationOverride.HasValue ? durationOverride.Value.ToString("F1") : "-")}" +
            $" heal_mult={(healMultOverride.HasValue ? healMultOverride.Value.ToString("F2") : "-")}" +
            $" summon_cap_override={(Core.AbilityRules.GetSummonCap(abilityName) is int sco ? sco.ToString() : "-")}" +
            $" summon_timeout_override={(Core.AbilityRules.GetSummonTimeout(abilityName) is float sto ? sto.ToString("F1") : "-")}" +
            $" summon_units_override={(Core.AbilityRules.GetSummonUnitsPerCast(abilityName) is int suo ? suo.ToString() : "-")}" +
            $" force_timeout_override={(Core.AbilityRules.GetForceTimeoutSeconds(abilityName) is float fto ? fto.ToString("F1") : "-")}" +
            $" free_move_secs={(freeMoveSecs.HasValue ? freeMoveSecs.Value.ToString("F1") : "-")}" +                          // v0.87.0
            $" interrupt_on_hit={(interruptOnHit.HasValue ? (interruptOnHit.Value ? "on" : "off") : "auto")}" +              // v0.87.0
            $" condition={SafeToken(string.IsNullOrEmpty(meta?.Condition) ? "-" : meta.Condition)}" +                       // v0.107.0 (ApiVersion 24)
            $" condition_mods={(meta?.ConditionModifiers is { Count: > 0 } cm ? string.Join(",", cm) : "-")}" +
            $" condition_source={SafeToken(string.IsNullOrEmpty(meta?.ConditionSource) ? "-" : meta.ConditionSource)}" +
            $" review_status={SafeToken(Core.AbilityRules.GetReviewStatus(abilityName))}" +                               // v0.112.0 (ApiVersion 25)
            $" review_tag={(string.IsNullOrEmpty(Core.AbilityRules.GetReviewTag(abilityName)) ? "-" : SafeToken(Core.AbilityRules.GetReviewTag(abilityName)))}" +
            $" source_level={(meta?.SourceLevel.HasValue == true ? meta.SourceLevel.Value.ToString() : "-")}" +          // v0.113.0 (ApiVersion 26)
            $" source_tier={SafeToken(string.IsNullOrEmpty(meta?.SourceTier) ? "-" : meta.SourceTier)}" +
            $" is_vblood={(meta?.IsVBlood == true ? 1 : 0)}";
        ReplyChunked(ctx, "info", idField, body);
    }

    /// <summary>v0.76.0: clamp a value to a max length (protects a single token from exceeding the chunk budget).</summary>
    static string Clamp(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);

    /// <summary>
    /// v0.76.0: emit a long "[BEELZ:&lt;tag&gt;]" line across multiple replies, each safely under VCF's
    /// 512-byte FixedString cap. Every line repeats the id field (e.g. <c>i=5</c>) and carries
    /// <c>part=k/n</c>; the body's space-separated <c>key=value</c> tokens are packed into chunks without
    /// splitting a token. BCH reassembles all parts for the same id (single-part lines look unchanged
    /// apart from the new <c>part=1/1</c>). The budget leaves headroom for the prefix + id + part marker.
    /// </summary>
    const int ChunkBudget = 420;
    static void ReplyChunked(ChatCommandContext ctx, string tag, string idField, string body)
    {
        var tokens = (body ?? "").Split(' ');
        var chunks = new System.Collections.Generic.List<string>();
        var sb = new StringBuilder();
        foreach (var t in tokens)
        {
            if (sb.Length > 0 && sb.Length + 1 + t.Length > ChunkBudget) { chunks.Add(sb.ToString()); sb.Clear(); }
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(t);
        }
        if (sb.Length > 0) chunks.Add(sb.ToString());
        if (chunks.Count == 0) chunks.Add("");
        int n = chunks.Count;
        for (int k = 0; k < n; k++)
            ctx.Reply($"[BEELZ:{tag}] {idField} part={k + 1}/{n} {chunks[k]}");
    }

    static string TryGetCuratedNotes(string abilityName)
    {
        if (Core.AbilityRules?.Current?.AbilityMap == null) return null;
        if (Core.AbilityRules.Current.AbilityMap.TryGetValue(abilityName, out var entry)
            && !string.IsNullOrWhiteSpace(entry.Notes))
            return entry.Notes;
        return null;
    }

    /// <summary>
    /// BCH wire-format value safety: replace whitespace with `_` and `=` (key/value
    /// delimiter) with `-` so the value stays a single bare token. Punctuation and
    /// other characters pass through unchanged.
    /// </summary>
    static string SafeToken(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Replace(' ', '_').Replace('\t', '_').Replace('\n', '_').Replace('\r', '_').Replace('=', '-');
    }

    [Command("bch", description: "Toggle BCH event-stream emission for the caller (BCH calls this on load). Usage: .beelz api bch <on|off|status>")]
    public static void Bch(ChatCommandContext ctx, string mode)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=bch code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var m = mode?.Trim().ToLowerInvariant() ?? "";
        if (m == "on" || m == "true" || m == "1")
        {
            Core.AbilityRegistry.SetEmitApiEvents(steamId, true);
            Core.Persistence.RequestSave();
            ctx.Reply($"[BEELZ:bch] state=on api={ApiVersion}");
        }
        else if (m == "off" || m == "false" || m == "0")
        {
            Core.AbilityRegistry.SetEmitApiEvents(steamId, false);
            Core.Persistence.RequestSave();
            ctx.Reply("[BEELZ:bch] state=off");
        }
        else if (m == "status" || m == "")
        {
            bool on = Core.AbilityRegistry.GetEmitApiEvents(steamId);
            ctx.Reply($"[BEELZ:bch] state={(on ? "on" : "off")} api={ApiVersion}");
        }
        else
        {
            ctx.Reply($"[BEELZ:err] cmd=bch code=bad_mode msg=expected_on_off_or_status_got_{m}");
        }
    }

    [Command("verbosity", description: "Return the caller's current verbosity setting (BCH-readable).")]
    public static void Verbosity(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=verbosity code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var v = Core.Chat.ResolveVerbosity(steamId);
        ctx.Reply($"[BEELZ:verbosity] level={v} default={Beelzebub.Config.Settings.DefaultVerbosity.Value}");
    }

    [Command("rules", description: "Stream the loaded ability rules (BCH-readable, admin-relevant).")]
    public static void Rules(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=rules code=not_ready msg=plugin_not_initialized"); return; }
        var r = Core.AbilityRules.Current;
        var sb = new StringBuilder();
        sb.Append("[BEELZ:rules] version=").Append(r.Version);
        sb.Append(" deny_patterns=").Append(string.Join(",", r.DenyPatterns));
        sb.Append(" allow_patterns=").Append(string.Join(",", r.AllowPatterns));
        sb.Append(" deny_guids=").Append(r.DenyGuids.Count);
        sb.Append(" allow_guids=").Append(r.AllowGuids.Count);
        // v0.54.0: surface the global Defaults block + transform-only reservation lists so a BCH
        // admin panel can read the server-wide baselines (not just per-ability/per-unit entries).
        sb.Append(" default_damage_scale=").Append((r.Defaults?.DamageScale ?? 1.0f).ToString("F2"));
        sb.Append(" default_cooldown_scale=").Append((r.Defaults?.CooldownScale ?? 1.0f).ToString("F2"));
        sb.Append(" transform_only_patterns=").Append(string.Join(",", r.TransformOnlyPatterns));
        sb.Append(" transform_only_guids=").Append(r.TransformOnlyGuids.Count);
        ctx.Reply(sb.ToString());
    }

    [Command("progress", description: "Return the caller's collection-completion data (BCH-readable).")]
    public static void Progress(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=progress code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        EmitProgress(ctx, steamId);
    }

    static void EmitProgress(ChatCommandContext ctx, ulong steamId)
    {
        var captured = Core.AbilityRegistry.ListFor(steamId);
        var transforms = Core.AbilityRegistry.ListTransforms(steamId);
        int totalAbilities = Core.AbilityRules?.Current?.AbilityMap?.Count ?? 0;
        // IN4: prefer curated TransformMap count over the 61 V-Blood placeholder.
        int totalTransforms = Core.AbilityRules?.Current?.TransformMap?.Count ?? 61;
        if (totalTransforms == 0) totalTransforms = 61;
        int vbloodCaptures = captured.Count(c => c.Source == CaptureSource.VBlood);
        int vbloodTransforms = transforms.Count(t => t.Source == CaptureSource.VBlood);
        float aPct = totalAbilities > 0 ? captured.Count * 100f / totalAbilities : 0f;
        float tPct = transforms.Count * 100f / totalTransforms;
        ctx.Reply(
            $"[BEELZ:progress] abilities_captured={captured.Count} abilities_total={totalAbilities} abilities_pct={aPct:F1}" +
            $" transforms_unlocked={transforms.Count} transforms_total={totalTransforms} transforms_pct={tPct:F1}" +
            $" vblood_abilities={vbloodCaptures} vblood_transforms={vbloodTransforms}");
    }

    // --- IN3: catalog endpoints (BCH "collection book" view) ---

    [Command("catalog", description: "Summary of what's in the curated catalog (counts only). Drill in via .beelz api catalog units|abilities.")]
    public static void Catalog(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=catalog code=not_ready msg=plugin_not_initialized"); return; }
        int abilityCount = Core.AbilityRules?.Current?.AbilityMap?.Count ?? 0;
        int transformCount = Core.AbilityRules?.Current?.TransformMap?.Count ?? 0;
        ctx.Reply($"[BEELZ:catalog-summary] abilities={abilityCount} units={transformCount} server_mode={Beelzebub.Services.AbilityRules.GetServerDifficulty()}");
    }

    [Command("catalog units", description: "Stream every curated transform-target unit + its matrix attributes (BCH collection book). Optional page index, default 0. Page size 40.")]
    public static void CatalogUnits(ChatCommandContext ctx, int page = 0)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=catalog-units code=not_ready msg=plugin_not_initialized"); return; }
        var map = Core.AbilityRules?.Current?.TransformMap;
        if (map == null || map.Count == 0)
        {
            ctx.Reply("[BEELZ:end] cmd=catalog-units count=0 total=0 page=0 pages=1");
            return;
        }
        const int pageSize = 40;
        int total = map.Count;
        int pages = (total + pageSize - 1) / pageSize;
        if (page < 0) page = 0;
        if (page >= pages) page = pages - 1;
        var ordered = map.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Skip(page * pageSize).Take(pageSize).ToList();
        foreach (var (name, entry) in ordered)
        {
            // IN2 (v0.15.1): unit `type` badge derived from prefab name.
            // TX3 (v0.16.0): `full_replace` flag.
            // TX7 (v0.17.0): `scaling_mode` — emits the raw per-entry value when
            // set so admins can see the override, blank when inheriting global.
            var type = Categorization.ClassifyTransform(name);
            string scalingMode = string.IsNullOrEmpty(entry.PowerScalingMode) ? "inherit" : entry.PowerScalingMode;
            // v0.54.0: encode the per-slot ability override map as slot:ability;slot:ability (or "-").
            string slotTemplate = entry.SlotTemplate is { Count: > 0 }
                ? string.Join(";", entry.SlotTemplate.Select(kv => $"{kv.Key}:{kv.Value}"))
                : "-";
            ctx.Reply(
                $"[BEELZ:catalog-unit] un={name}" +
                $" enabled={(entry.Enabled ? 1 : 0)}" +
                $" difficulty={entry.Difficulty}" +
                $" tier={entry.Tier}" +
                $" type={type}" +
                $" full_replace={(entry.FullReplace ? 1 : 0)}" +
                $" shard={(Core.Transforms.MatchesShardBossNames(name) ? 1 : 0)}" +
                $" scaling_mode={scalingMode}" +
                $" damage_scale={entry.DamageScale:F2}" +
                $" cooldown_scale={entry.CooldownScale:F2}" +
                $" health_scale={entry.HealthScale:F2}" +
                $" speed_scale={entry.MovementSpeedScale:F2}" +
                $" slot_template={SafeToken(slotTemplate)}" +
                $" notes={SafeToken(entry.Notes ?? "")}");
        }
        ctx.Reply($"[BEELZ:end] cmd=catalog-units count={ordered.Count} total={total} page={page} pages={pages}");
    }

    // v0.57.0: the catalog now streams the UNION of the curated AbilityMap and the discovered
    // capturable universe (every AB_*_AbilityGroup/_Group that passes AbilityFilter.ShouldCapture),
    // so BCH's Bestiary "Missing" list is complete instead of being limited to the hand-curated map.
    // The union is built once per scan (rebuilt when page 0 is requested — BCH always scans from 0)
    // and stored as an immutable snapshot so concurrent readers see a consistent list.
    sealed class CatalogSnapshot
    {
        public System.Collections.Generic.List<string> Names;
        public System.Collections.Generic.Dictionary<string, int> Guids;
    }
    static CatalogSnapshot _catalog;
    static CatalogSnapshot _catalogAll;   // v0.100.0: admin "all abilities" scope (separate page cache)

    // v0.100.0: two scopes. adminAll=false (player/default) = the COLLECTIBLE set — curated rows + every
    // discovered ability that passes the capture filter (respects enable/deny/difficulty/inclusive), so the
    // count/% is the honest "available to collect" total. adminAll=true (admin config) = EVERY real ability
    // group regardless of enable/deny/difficulty (junk stubs excluded), so an admin can configure anything.
    // Both emit enabled= per row, so a client can also filter the admin list down to the enabled set.
    static CatalogSnapshot BuildCatalogSnapshot(bool adminAll)
    {
        var names = new System.Collections.Generic.SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var guids = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var map = Core.AbilityRules?.Current?.AbilityMap;

        // 1) Every curated entry is always present (a disabled/curated row still shows with its flags).
        if (map != null)
            foreach (var name in map.Keys) names.Add(name);

        // 2) The discovered AB_*_AbilityGroup / _Group universe.
        foreach (var (guid, name) in Core.PrefabNames)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (!name.StartsWith("AB_", StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.EndsWith("_AbilityGroup", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith("_Group", StringComparison.OrdinalIgnoreCase)) continue;

            bool curated = map != null && map.ContainsKey(name);
            bool include = adminAll
                ? !Core.AbilityFilter.IsJunkAbility(name)                          // admin: every real ability group (incl. blocked/hidden — admin sees review_status)
                : ((curated || Core.AbilityFilter.ShouldCapture(name, guid, out _)) // player: collectible only...
                   && !Core.AbilityRules.IsReviewGated(name, guid));               // ...minus ReviewStatus Blocked/Hidden (v0.115.0 curation gate)
            if (!include) continue;

            names.Add(name);
            guids[name] = guid; // record the guid so per-row predicates (transform_only/allow_denied) resolve
        }

        return new CatalogSnapshot { Names = names.ToList(), Guids = guids };
    }

    static void EmitCatalogAbilityLine(ChatCommandContext ctx, string name, System.Collections.Generic.Dictionary<string, int> guids)
    {
        var map = Core.AbilityRules?.Current?.AbilityMap;
        // IN2 (v0.15.1): ability `cat` badge derived from prefab name.
        // v0.51.0: admin Category override (ability_rules.json) wins over the name heuristic.
        var cat = Core.AbilityRules.GetAbilityCategoryOverride(name) ?? Categorization.ClassifyAbility(name);
        int guid = (guids != null && guids.TryGetValue(name, out var g)) ? g : 0;

        // v0.58.0: curated description + school for the Bestiary's MISSING rows. Uses the lightweight
        // dict-only getter (NOT Resolve — no ECS probe per row across the full universe). Only the
        // curated subset has these; the rest emit `-`/`none`.
        string desc = "-", school = "none";
        if (guid != 0 && Core.AbilityMetadata != null
            && Core.AbilityMetadata.TryGetCuratedText(guid, out var curatedDesc, out var curatedSchool))
        {
            if (curatedDesc != null) desc = SafeToken(curatedDesc);
            if (curatedSchool != null) school = SafeToken(curatedSchool);
        }

        // v0.100.0 (ApiVersion 21): owning-unit name + GUID for the FULL list — fills the Unit column / ID
        // search for UNCAPTURED abilities (the capture line already carries the unit; this brings it to the
        // catalog). From ability_metadata SourceNpcs; `-`/`0` when unknown. unit= is SafeToken-encoded.
        string unit = "-"; int unitGuid = 0;
        if (guid != 0 && Core.AbilityMetadata != null
            && Core.AbilityMetadata.TryGetPrimarySourceNpc(guid, out int ug, out string un))
        { unitGuid = ug; unit = string.IsNullOrWhiteSpace(un) ? "-" : SafeToken(un); }

        // v0.109.0 (ApiVersion 24 fix): activation-condition tokens — the contract says catalog-ability
        // carries these (it previously only landed in `api info`). Lightweight dict lookup, no ECS probe.
        string cond = "-", condMods = "-", condSrc = "-";
        if (guid != 0 && Core.AbilityMetadata != null
            && Core.AbilityMetadata.TryGetCondition(guid, out var cC, out var cM, out var cS))
        {
            if (!string.IsNullOrEmpty(cC)) cond = SafeToken(cC);
            if (!string.IsNullOrEmpty(cM)) condMods = SafeToken(cM);
            if (!string.IsNullOrEmpty(cS)) condSrc = SafeToken(cS);
        }
        string condTokens = $" condition={cond} condition_mods={condMods} condition_source={condSrc}";

        // v0.113.0 (ApiVersion 26): source-unit tier (level/tier band/VBlood) — lightweight lookup, no ECS probe.
        string srcLevel = "-", srcTier = "-", isVb = "0";
        if (guid != 0 && Core.AbilityMetadata != null
            && Core.AbilityMetadata.TryGetSourceTier(guid, out var sLvl, out var sTier, out var sVb))
        {
            if (sLvl.HasValue) srcLevel = sLvl.Value.ToString();
            if (!string.IsNullOrEmpty(sTier)) srcTier = SafeToken(sTier);
            isVb = sVb ? "1" : "0";
        }
        string tierTokens = $" source_level={srcLevel} source_tier={srcTier} is_vblood={isVb}";

        if (map != null && map.TryGetValue(name, out var entry))
        {
            string weapons = entry.Weapons is { Count: > 0 } ? string.Join(",", entry.Weapons) : "any";
            string forms = entry.Forms is { Count: > 0 } ? string.Join(",", entry.Forms) : "any";
            // No curated metadata description? Fall back to the admin Notes field (also a description).
            if (desc == "-" && !string.IsNullOrWhiteSpace(entry.Notes)) desc = SafeToken(entry.Notes);
            string bodyC =
                $"curated=1" +
                $" a={guid} unit={unit} unitguid={unitGuid}" +   // v0.100.0 / ApiVersion 21
                $" weapons={weapons}" +
                $" forms={forms}" +
                $" transform_only={(entry.TransformOnly ? 1 : 0)}" +
                $" enabled={(entry.Enabled ? 1 : 0)}" +
                $" difficulty={entry.Difficulty}" +
                $" cat={cat}" +
                $" category_override={(string.IsNullOrEmpty(entry.Category) ? "-" : SafeToken(entry.Category))}" +
                $" phase={entry.Phase}" +
                $" allow_denied={(entry.AllowDenied ? 1 : 0)}" +
                $" interruptible={(entry.Interruptible.HasValue ? (entry.Interruptible.Value ? "on" : "off") : "auto")}" +
                $" free_move={(entry.FreeMoveAfterCast ? 1 : 0)}" +
                $" cast_speed={(entry.CastMovementSpeed.HasValue ? entry.CastMovementSpeed.Value.ToString("F2") : "auto")}" +
                $" damage_scale={entry.DamageScale:F2}" +
                $" cooldown_scale={entry.CooldownScale:F2}" +
                $" cooldown_override={(entry.CooldownSeconds.HasValue ? entry.CooldownSeconds.Value.ToString("F2") : "-")}" +
                $" range_override={(entry.MaxRangeOverride.HasValue ? entry.MaxRangeOverride.Value.ToString("F1") : "-")}" +
                $" charges_override={(entry.ChargesMax.HasValue ? entry.ChargesMax.Value.ToString() : "-")}" +
                $" chargetime_override={(entry.ChargeTimeSeconds.HasValue ? entry.ChargeTimeSeconds.Value.ToString("F2") : "-")}" +
                $" aoe_override={(entry.AoeRadius.HasValue ? entry.AoeRadius.Value.ToString("F1") : "-")}" +
                $" projspeed_override={(entry.ProjectileSpeed.HasValue ? entry.ProjectileSpeed.Value.ToString("F1") : "-")}" +
                $" duration_override={(entry.EffectDurationSeconds.HasValue ? entry.EffectDurationSeconds.Value.ToString("F1") : "-")}" +
                $" heal_mult={(entry.HealingMultiplier.HasValue ? entry.HealingMultiplier.Value.ToString("F2") : "-")}" +
                $" summon_cap_override={(entry.SummonCap.HasValue ? entry.SummonCap.Value.ToString() : "-")}" +
                $" summon_timeout_override={(entry.SummonTimeoutSeconds.HasValue ? entry.SummonTimeoutSeconds.Value.ToString("F1") : "-")}" +
                $" summon_units_override={(entry.SummonUnitsPerCast.HasValue ? entry.SummonUnitsPerCast.Value.ToString() : "-")}" +
                $" force_timeout_override={(entry.ForceTimeoutSeconds.HasValue ? entry.ForceTimeoutSeconds.Value.ToString("F1") : "-")}" +
                $" free_move_secs={(entry.FreeMoveAfterSeconds.HasValue ? entry.FreeMoveAfterSeconds.Value.ToString("F1") : "-")}" +   // v0.87.0
                $" interrupt_on_hit={(entry.InterruptOnHit.HasValue ? (entry.InterruptOnHit.Value ? "on" : "off") : "auto")}" +        // v0.87.0
                $" school={school}" +
                $" desc={Clamp(desc, 256)}" +
                $" notes={SafeToken(Clamp(entry.Notes ?? "", 256))}" +
                $" review_status={SafeToken(string.IsNullOrEmpty(entry.ReviewStatus) ? "Unreviewed" : entry.ReviewStatus)}" +   // v0.112.0 (ApiVersion 25)
                $" review_tag={(string.IsNullOrEmpty(entry.ReviewTag) ? "-" : SafeToken(entry.ReviewTag))}" +                    // v0.112.0 (ApiVersion 25)
                condTokens +   // v0.109.0 (ApiVersion 24): condition/condition_mods/condition_source
                tierTokens;    // v0.113.0 (ApiVersion 26): source_level/source_tier/is_vblood
            ReplyChunked(ctx, "catalog-ability", $"an={name}", bodyC);   // v0.76.0: chunked (was >512-byte crash)
            return;
        }

        // Discovered-but-uncurated row: derive what we can from the name-keyed rule getters, defaults
        // for the per-ability tuning fields that only a curated entry carries.
        var fams = Core.AbilityRules.ClassifyWeaponFamilies(name);
        var famList = fams?
            .Where(f => f != WeaponFamily.Magic && f != WeaponFamily.None)
            .Select(f => f.ToString())
            .ToList();
        string weaponsU = (famList != null && famList.Count > 0) ? string.Join(",", famList) : "any";
        string bodyU =
            $"curated=0" +
            $" a={guid} unit={unit} unitguid={unitGuid}" +   // v0.100.0 / ApiVersion 21
            $" weapons={weaponsU}" +
            $" forms=any" +
            $" transform_only={(Core.AbilityRules.IsTransformOnly(name, guid) ? 1 : 0)}" +
            $" enabled={(Core.AbilityRules.IsEnabled(name, guid) ? 1 : 0)}" +
            $" difficulty={Core.AbilityRules.GetAbilityDifficulty(name)}" +
            $" cat={cat}" +
            $" category_override=-" +
            $" phase=1" +
            $" allow_denied={(Core.AbilityRules.IsAllowDenied(name, guid) ? 1 : 0)}" +
            $" interruptible=auto" +
            $" free_move=0" +
            $" cast_speed=auto" +
            $" damage_scale={Core.AbilityRules.GetDamageScale(name):F2}" +
            $" cooldown_scale={Core.AbilityRules.GetCooldownScale(name):F2}" +
            $" cooldown_override=-" +
            $" range_override=-" +
            $" charges_override=-" +
            $" chargetime_override=-" +
            $" aoe_override=-" +
            $" projspeed_override=-" +
            $" duration_override=-" +
            $" heal_mult=-" +
            $" summon_cap_override=-" +
            $" summon_timeout_override=-" +
            $" summon_units_override=-" +
            $" force_timeout_override=-" +
            $" free_move_secs=-" +           // v0.87.0
            $" interrupt_on_hit=auto" +      // v0.87.0
            $" school={school}" +
            $" desc={Clamp(desc, 256)}" +
            $" notes=" +
            $" review_status=Unreviewed review_tag=-" +   // v0.112.0 (ApiVersion 25): uncurated => defaults
            condTokens +   // v0.109.0 (ApiVersion 24): condition/condition_mods/condition_source
            tierTokens;    // v0.113.0 (ApiVersion 26): source_level/source_tier/is_vblood
        ReplyChunked(ctx, "catalog-ability", $"an={name}", bodyU);   // v0.76.0: chunked (was >512-byte crash)
    }

    [Command("catalog abilities", description: "Stream the capturable ability catalog with matrix attributes (BCH collection book). Optional page (default 0, size 40) + FILTER: catalog abilities <page> <weapon|cat|unit|form|search|tag|reviewstatus|tier|vblood> <value> — load just a subset (e.g. weapon Sword, cat Summon, unit Erwin, search lightning, tag emote, tier T4, vblood 1) instead of the full ~1700 rows.")]
    public static void CatalogAbilities(ChatCommandContext ctx, int page = 0, string filter = null, string value = null)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=catalog-abilities code=not_ready msg=plugin_not_initialized"); return; }
        if (page < 0) page = 0;
        // Rebuild the union when a fresh scan starts (page 0) or nothing is cached; reuse for page>0
        // so one scan sees a consistent snapshot and we don't re-enumerate ~14k prefabs per page.
        if (page == 0 || _catalog == null) _catalog = BuildCatalogSnapshot(adminAll: false);
        StreamCatalog(ctx, _catalog, page, "catalog-abilities", filter, value);
    }

    [Command("catalog abilities-all", description: "ADMIN: stream EVERY ability group for configuration — regardless of enable/deny/difficulty/inclusive (junk stubs excluded). Same line format + FILTER support as `catalog abilities` (each row carries enabled=). Usage: catalog abilities-all <page> <weapon|cat|unit|form|search|tag|reviewstatus|tier|vblood> <value>.", adminOnly: true)]
    public static void CatalogAbilitiesAll(ChatCommandContext ctx, int page = 0, string filter = null, string value = null)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=catalog-abilities-all code=not_ready msg=plugin_not_initialized"); return; }
        if (page < 0) page = 0;
        if (page == 0 || _catalogAll == null) _catalogAll = BuildCatalogSnapshot(adminAll: true);
        StreamCatalog(ctx, _catalogAll, page, "catalog-abilities-all", filter, value);
    }

    /// <summary>v0.100.0: shared paginated emit for the player + admin catalog scopes.
    /// v0.101.0 (ApiVersion 23): optional filter (weapon/cat/unit/form/search) applied to the cached full
    /// snapshot BEFORE pagination, so BCH can load a subset fast instead of all ~1700 rows.</summary>
    static void StreamCatalog(ChatCommandContext ctx, CatalogSnapshot snap, int page, string cmd, string filterKey = null, string filterVal = null)
    {
        if (snap == null || snap.Names.Count == 0)
        {
            ctx.Reply($"[BEELZ:end] cmd={cmd} count=0 total=0 page=0 pages=1 filter=- value=-");
            return;
        }

        string fk = filterKey?.Trim().ToLowerInvariant();
        string fv = filterVal?.Trim();
        var names = snap.Names;
        if (!string.IsNullOrEmpty(fk) && !string.IsNullOrEmpty(fv))
            names = names.Where(n => MatchesCatalogFilter(n, snap.Guids, fk, fv)).ToList();

        const int pageSize = 40;
        int total = names.Count;
        int pages = total == 0 ? 1 : (total + pageSize - 1) / pageSize;
        if (page >= pages) page = pages - 1;
        var slice = total == 0
            ? new System.Collections.Generic.List<string>()
            : names.Skip(page * pageSize).Take(pageSize).ToList();
        foreach (var name in slice) EmitCatalogAbilityLine(ctx, name, snap.Guids);
        ctx.Reply($"[BEELZ:end] cmd={cmd} count={slice.Count} total={total} page={page} pages={pages}"
            + $" filter={(string.IsNullOrEmpty(fk) ? "-" : fk)} value={(string.IsNullOrEmpty(fv) ? "-" : SafeToken(fv))}");
    }

    /// <summary>
    /// v0.101.0 (ApiVersion 23): does a catalog row match a load filter? Lets BCH/testers pull a subset
    /// (e.g. all Sword abilities, all Summons, a unit's kit) instead of the whole ~1700-row catalog.
    /// </summary>
    static bool MatchesCatalogFilter(string name, System.Collections.Generic.Dictionary<string, int> guids, string key, string val)
    {
        int guid = (guids != null && guids.TryGetValue(name, out var g)) ? g : 0;
        switch (key)
        {
            case "search": case "name":
                return name.IndexOf(val, StringComparison.OrdinalIgnoreCase) >= 0;

            case "weapon": case "weapons":
                if (!Enum.TryParse<WeaponFamily>(val, ignoreCase: true, out var wf) || wf == WeaponFamily.None) return false;
                var fams = Core.AbilityRules.ClassifyWeaponFamilies(name);
                return fams != null && fams.Contains(wf);

            case "cat": case "type": case "category":
                var cat = Core.AbilityRules.GetAbilityCategoryOverride(name) ?? Categorization.ClassifyAbility(name);
                return string.Equals(cat.ToString(), val, StringComparison.OrdinalIgnoreCase);

            case "unit":
                if (guid == 0 || Core.AbilityMetadata == null) return false;
                if (!Core.AbilityMetadata.TryGetPrimarySourceNpc(guid, out _, out var un) || string.IsNullOrEmpty(un)) return false;
                return un.IndexOf(val, StringComparison.OrdinalIgnoreCase) >= 0;

            case "form": case "forms":
                var map = Core.AbilityRules?.Current?.AbilityMap;
                if (map != null && map.TryGetValue(name, out var e) && e.Forms is { Count: > 0 })
                    return e.Forms.Any(f => f.TrimStart('!').Equals(val, StringComparison.OrdinalIgnoreCase));
                return false;

            // v0.116.0 (ApiVersion 27): curation/source filters — load only the group you want to work.
            case "tag": case "reviewtag":
                return string.Equals(Core.AbilityRules.GetReviewTag(name), val, StringComparison.OrdinalIgnoreCase);

            case "status": case "review": case "reviewstatus":
                return string.Equals(Core.AbilityRules.GetReviewStatus(name), val, StringComparison.OrdinalIgnoreCase);

            case "tier":
                return guid != 0 && Core.AbilityMetadata != null
                    && Core.AbilityMetadata.TryGetSourceTier(guid, out _, out var tr, out _)
                    && string.Equals(tr, val, StringComparison.OrdinalIgnoreCase);

            case "vblood": case "isvblood":
                if (guid == 0 || Core.AbilityMetadata == null) return false;
                Core.AbilityMetadata.TryGetSourceTier(guid, out _, out _, out var isVb);
                bool want = val is "1" or "true" or "yes" or "on" or "vblood";
                return isVb == want;

            default:
                return true;   // unknown filter key → don't filter (stream everything)
        }
    }

    [Command("hotkeys", description: "Stream the caller's named hotkey bindings (BCH-readable). Extra slots beyond V Rising's 6.")]
    public static void Hotkeys(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=hotkeys code=not_ready msg=plugin_not_initialized"); return; }
        if (!Beelzebub.Config.Settings.Hotkeys_Enabled.Value)
        {
            ctx.Reply("[BEELZ:hotkeys-config] enabled=0 max=0");
            ctx.Reply("[BEELZ:end] cmd=hotkeys count=0");
            return;
        }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        int max = Beelzebub.Config.Settings.Hotkeys_MaxPerPlayer.Value;
        ctx.Reply($"[BEELZ:hotkeys-config] enabled=1 max={max}");
        var bindings = Core.AbilityRegistry.ListHotkeys(steamId);
        foreach (var (name, abilityGuid) in bindings.OrderBy(kv => kv.Key))
        {
            ctx.Reply(
                $"[BEELZ:hotkey] name={name}" +
                $" a={abilityGuid} an={new PrefabGUID(abilityGuid).GetPrefabName()}");
        }
        ctx.Reply($"[BEELZ:end] cmd=hotkeys count={bindings.Count}");
    }

    [Command("transform-config", description: "Return current transform mode/duration/cooldown per source (BCH-readable).")]
    public static void TransformConfig(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=transform-config code=not_ready msg=plugin_not_initialized"); return; }
        ctx.Reply(
            $"[BEELZ:tx-config] src=R mode={Beelzebub.Config.Settings.Transform_Mode_Regular.Value}" +
            $" duration={Beelzebub.Config.Settings.Transform_DurationSeconds_Regular.Value}" +
            $" cooldown={Beelzebub.Config.Settings.Transform_CooldownSeconds_Regular.Value}");
        ctx.Reply(
            $"[BEELZ:tx-config] src=V mode={Beelzebub.Config.Settings.Transform_Mode_VBlood.Value}" +
            $" duration={Beelzebub.Config.Settings.Transform_DurationSeconds_VBlood.Value}" +
            $" cooldown={Beelzebub.Config.Settings.Transform_CooldownSeconds_VBlood.Value}");
        // v0.43.0: shard-boss category (src=S) for symmetry with `api cooldowns`'
        // category=shard bucket. Additive — older parsers ignore the extra line.
        ctx.Reply(
            $"[BEELZ:tx-config] src=S mode={Beelzebub.Config.Settings.Transform_Mode_ShardBoss.Value}" +
            $" duration={Beelzebub.Config.Settings.Transform_DurationSeconds_ShardBoss.Value}" +
            $" cooldown={Beelzebub.Config.Settings.Transform_CooldownSeconds_ShardBoss.Value}");
        ctx.Reply("[BEELZ:end] cmd=transform-config count=3");
    }

    [Command("bestiary", description: "Stream the caller's collection book — per-unit ability progress + transform status (BCH-readable). Optional page, size 40.")]
    public static void Bestiary(ChatCommandContext ctx, int page = 0)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=bestiary code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var entries = BestiaryService.Build(steamId);

        const int pageSize = 40;
        int pages = entries.Count == 0 ? 1 : (entries.Count + pageSize - 1) / pageSize;
        if (page < 0) page = 0;
        if (page >= pages) page = pages - 1;

        int shown = 0;
        foreach (var e in entries.Skip(page * pageSize).Take(pageSize))
        {
            ctx.Reply(
                $"[BEELZ:bestiary] u={e.UnitPrefabGuid} un={SafeToken(e.UnitName)}" +
                $" s={(e.Source == CaptureSource.VBlood ? "V" : "R")}" +
                $" captured={e.CapturedCount} total={e.TotalCount} transform={(e.TransformUnlocked ? 1 : 0)}");
            shown++;
        }
        ctx.Reply($"[BEELZ:end] cmd=bestiary count={shown} total={entries.Count} page={page} pages={pages}");
    }

    [Command("config", description: "Stream every Beelzebub config setting (BCH settings panel). One [BEELZ:config] line per setting.")]
    public static void Config(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=config code=not_ready msg=plugin_not_initialized"); return; }
        // v0.39.0: reflect over Settings' static ConfigEntry properties so new settings
        // are included automatically. All are runtime-settable via `.beelz admin set <key> <value>`.
        int n = 0;
        foreach (var prop in typeof(Beelzebub.Config.Settings).GetProperties(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (prop.GetValue(null) is not BepInEx.Configuration.ConfigEntryBase entry) continue;
            string section = entry.Definition.Section;
            string key = entry.Definition.Key;
            string value = entry.BoxedValue?.ToString() ?? "";
            string type = entry.SettingType.Name;
            ctx.Reply($"[BEELZ:config] section={SafeToken(section)} key={SafeToken(key)} value={SafeToken(value)} type={SafeToken(type)} editable=1");
            n++;
        }
        ctx.Reply($"[BEELZ:end] cmd=config count={n}");
    }

    [Command("cooldowns", description: "Per-category transform cooldown remaining, for cooldown timers (BCH-readable).")]
    public static void Cooldowns(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=cooldowns code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var now = System.DateTime.UtcNow;
        foreach (var cat in new[] { TransformCategory.Regular, TransformCategory.VBlood, TransformCategory.ShardBoss })
        {
            double rem = (Core.AbilityRegistry.CooldownUntil(steamId, Core.Transforms.CooldownScopeKey(cat, 0)) - now).TotalSeconds;
            if (rem < 0) rem = 0;
            ctx.Reply($"[BEELZ:cooldown] category={cat.ToString().ToLowerInvariant()} remaining={rem:F0}");
        }
        ctx.Reply("[BEELZ:end] cmd=cooldowns count=3");
    }

    // --- v22 (v0.100.x): structured transform-loadout + broadcast-pool reads for the BCH editors ---

    [Command("tform-kit", description: "Stream a transform unit's FULL eligible ability kit — the pool you bind from with .beelz tform <unit> set. One [BEELZ:tform-ability] line per ability. Usage: .beelz api tform-kit <unit|index> (same resolution as .beelz tform).")]
    public static void TformKit(ChatCommandContext ctx, string unit)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=tform-kit code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        if (!TransformCommands.TryResolveTransformUnit(steamId, unit, out int unitGuid, out string rerr))
        { ctx.Reply($"[BEELZ:err] cmd=tform-kit code=bad_unit msg={SafeToken(rerr)}"); return; }
        var kit = UnitKitService.FullEligibleKit(unitGuid);
        for (int i = 0; i < kit.Count; i++)
        {
            int a = kit[i];
            ctx.Reply($"[BEELZ:tform-ability] unit={unitGuid} idx={i} a={a} an={new PrefabGUID(a).GetPrefabName()}");
        }
        ctx.Reply($"[BEELZ:end] cmd=tform-kit unit={unitGuid} count={kit.Count}");
    }

    [Command("tform-binds", description: "Stream the caller's CUSTOM transform loadout for one unit — one [BEELZ:tform-slot] line per phase/slot the player has bound (empty = just the end line). The end line carries phases=<n> (how many phases the form has). Usage: .beelz api tform-binds <unit|index> (same resolution as .beelz tform).")]
    public static void TformBinds(ChatCommandContext ctx, string unit)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=tform-binds code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        if (!TransformCommands.TryResolveTransformUnit(steamId, unit, out int unitGuid, out string rerr))
        { ctx.Reply($"[BEELZ:err] cmd=tform-binds code=bad_unit msg={SafeToken(rerr)}"); return; }
        var pg = new PrefabGUID(unitGuid);
        var phases = Core.Transforms.GetAvailablePhases(pg, steamId);
        int n = 0;
        foreach (int phase in phases)
        {
            var binds = Core.AbilityRegistry.GetTransformLoadout(steamId, unitGuid, phase);
            foreach (var kv in binds.OrderBy(kv => kv.Key))
            {
                ctx.Reply($"[BEELZ:tform-slot] unit={unitGuid} phase={phase} slot={kv.Key}" +
                          $" a={kv.Value} an={new PrefabGUID(kv.Value).GetPrefabName()}");
                n++;
            }
        }
        ctx.Reply($"[BEELZ:end] cmd=tform-binds unit={unitGuid} count={n} phases={phases.Count}");
    }

    [Command("broadcast-msgs", description: "ADMIN: stream a broadcast message pool's current custom messages. One [BEELZ:broadcast-msg] line per message (idx is 1-based to match .beelz admin broadcast-msg edit/remove <n>; text= SafeToken-encoded). Usage: .beelz api broadcast-msgs <complete|leaderboard>", adminOnly: true)]
    public static void BroadcastMsgs(ChatCommandContext ctx, string pool)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=broadcast-msgs code=not_ready msg=plugin_not_initialized"); return; }
        string p = (pool ?? "").Trim().ToLowerInvariant();
        string poolName;
        BepInEx.Configuration.ConfigEntry<string> entry;
        switch (p)
        {
            case "complete": case "completion": case "collection":
                poolName = "complete"; entry = Beelzebub.Config.Settings.Broadcast_CollectionComplete_Messages; break;
            case "leaderboard": case "board": case "top":
                poolName = "leaderboard"; entry = Beelzebub.Config.Settings.Broadcast_Leaderboard_Messages; break;
            default:
                ctx.Reply("[BEELZ:err] cmd=broadcast-msgs code=bad_pool msg=expected_complete_or_leaderboard"); return;
        }
        // Pool storage mirrors AdminCommands.SplitPool: `|`-separated, trimmed, empties dropped.
        int n = 0;
        var raw = entry.Value;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            foreach (var part in raw.Split('|'))
            {
                string t = part.Trim();
                if (t.Length == 0) continue;
                n++;
                ctx.Reply($"[BEELZ:broadcast-msg] pool={poolName} idx={n} text={SafeToken(Clamp(t, 256))}");
            }
        }
        ctx.Reply($"[BEELZ:end] cmd=broadcast-msgs pool={poolName} count={n}");
    }
}
