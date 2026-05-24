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
    // v2 (v0.35.0): additive — `api info` gained weapon_anim/school/cooldown_seconds
    // + real ability descriptions; the [BEELZ:event] stream gained forget /
    // forget-transform / cleared and now emits transform-ended on every revert path.
    // Backward-compatible with v1 parsers (extra keys/events are ignored if unknown).
    const int ApiVersion = 2;

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
            var cat = Categorization.ClassifyAbility(abilityName);
            var type = Categorization.ClassifyTransform(unitName);
            ctx.Reply(
                $"[BEELZ:list] i={i} s={(c.Source == CaptureSource.VBlood ? "V" : "R")}" +
                $" u={c.UnitPrefabGuid} un={unitName}" +
                $" a={c.AbilityPrefabGuid} an={abilityName}" +
                $" cat={cat} type={type}");
        }
        ctx.Reply($"[BEELZ:end] cmd=list count={captured.Count}");
    }

    [Command("slots", description: "Stream the caller's current slot assignments — both universal and weapon-specific buckets (BCH-readable).")]
    public static void Slots(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=slots code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();

        int n = 0;
        // Universal bucket (bucket=any).
        var universal = Core.AbilityRegistry.GetSlots(steamId);
        foreach (var (slot, abilityGuid) in universal.OrderBy(kv => kv.Key))
        {
            ctx.Reply(
                $"[BEELZ:slot] bucket=any slot={slot}" +
                $" a={abilityGuid} an={new PrefabGUID(abilityGuid).GetPrefabName()}");
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
                    $" a={abilityGuid} an={new PrefabGUID(abilityGuid).GetPrefabName()}");
                n++;
            }
        }
        // Current weapon footer so BCH knows which bucket is active right now.
        var current = Beelzebub.Services.SlotApply.GetCurrentWeapon(ctx.Event.SenderCharacterEntity);
        ctx.Reply($"[BEELZ:slot-current] weapon={current}");
        ctx.Reply($"[BEELZ:end] cmd=slots count={n}");
    }

    [Command("transforms", description: "Stream the caller's transform unlocks (BCH-readable). TX1 matrix attributes included.")]
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
                $" scaling_mode={scalingMode}");
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
        string unitName = new PrefabGUID(c.UnitPrefabGuid).GetPrefabName();
        string abilityName = new PrefabGUID(c.AbilityPrefabGuid).GetPrefabName();
        string label = abilityName.Humanize();

        // A1-full: pull richer metadata from the AbilityMap matrix if curated.
        // Description prefers the admin-curated Notes field; falls back to a generic
        // "Captured from X." string. Weapons / Forms / scale data also surfaced for
        // BCH so it can render full tooltips without a second round-trip.
        var families = Core.AbilityRules.ClassifyWeaponFamilies(abilityName);
        var forms = Core.AbilityRules.GetFormRestriction(abilityName);
        bool transformOnly = Core.AbilityRules.IsTransformOnly(abilityName, c.AbilityPrefabGuid);
        bool enabled = Core.AbilityRules.IsEnabled(abilityName, c.AbilityPrefabGuid);
        float damageScale = Core.AbilityRules.GetDamageScale(abilityName);
        float cooldownScale = Core.AbilityRules.GetCooldownScale(abilityName);

        // v0.35.0: prefer the REAL ability description from resolved metadata
        // (shipped ability_metadata.json + ECS), with %param% substitution; fall
        // back to the admin-curated Notes, then a generic string.
        var meta = Core.AbilityMetadata?.Resolve(c.AbilityPrefabGuid);
        string realDesc = meta?.Description;
        if (!string.IsNullOrWhiteSpace(realDesc) && meta?.Parameters != null)
            foreach (var kv in meta.Parameters) realDesc = realDesc.Replace("%" + kv.Key + "%", kv.Value);
        string desc = (!string.IsNullOrWhiteSpace(realDesc) ? realDesc : TryGetCuratedNotes(abilityName))
                      ?? $"Captured from {unitName.Humanize()}.";
        string difficulty = Core.AbilityRules.GetAbilityDifficulty(abilityName);

        // v0.35.0: BCH tooltip enrichment — the weapon whose animation the ability
        // is bound to (None for spells), plus school + cooldown for the tooltip.
        var animWeapon = Core.AbilityRules.GetAnimationWeapon(abilityName);
        string school = string.IsNullOrEmpty(meta?.School) ? "none" : meta.School;
        string cdSecs = meta?.CooldownSeconds.HasValue == true ? meta.CooldownSeconds.Value.ToString("F1") : "0";

        ctx.Reply(
            $"[BEELZ:info] i={index} s={(c.Source == CaptureSource.VBlood ? "V" : "R")}" +
            $" u={c.UnitPrefabGuid} un={unitName}" +
            $" a={c.AbilityPrefabGuid} an={abilityName}" +
            $" label={SafeToken(label)}" +
            $" desc={SafeToken(desc)}" +
            $" weapons={string.Join(",", families)}" +
            $" weapon_anim={animWeapon}" +
            $" school={SafeToken(school)}" +
            $" cooldown_seconds={cdSecs}" +
            $" forms={(forms.Count == 0 ? "any" : string.Join(",", forms))}" +
            $" transform_only={(transformOnly ? 1 : 0)}" +
            $" enabled={(enabled ? 1 : 0)}" +
            $" difficulty={difficulty}" +
            $" damage_scale={damageScale:F2}" +
            $" cooldown_scale={cooldownScale:F2}");
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
        ctx.Reply(sb.ToString());
    }

    [Command("progress", description: "Return the caller's collection-completion data (BCH-readable, IN4).")]
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

    [Command("catalog", description: "Summary of what's in the curated catalog (counts only). Drill in via .beelz api catalog units|abilities. (IN3)")]
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
            ctx.Reply(
                $"[BEELZ:catalog-unit] un={name}" +
                $" enabled={(entry.Enabled ? 1 : 0)}" +
                $" difficulty={entry.Difficulty}" +
                $" tier={entry.Tier}" +
                $" type={type}" +
                $" full_replace={(entry.FullReplace ? 1 : 0)}" +
                $" scaling_mode={scalingMode}" +
                $" damage_scale={entry.DamageScale:F2}" +
                $" cooldown_scale={entry.CooldownScale:F2}" +
                $" health_scale={entry.HealthScale:F2}" +
                $" speed_scale={entry.MovementSpeedScale:F2}" +
                $" notes={SafeToken(entry.Notes ?? "")}");
        }
        ctx.Reply($"[BEELZ:end] cmd=catalog-units count={ordered.Count} total={total} page={page} pages={pages}");
    }

    [Command("catalog abilities", description: "Stream every curated ability + its matrix attributes (BCH collection book). Optional page index, default 0. Page size 40.")]
    public static void CatalogAbilities(ChatCommandContext ctx, int page = 0)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=catalog-abilities code=not_ready msg=plugin_not_initialized"); return; }
        var map = Core.AbilityRules?.Current?.AbilityMap;
        if (map == null || map.Count == 0)
        {
            ctx.Reply("[BEELZ:end] cmd=catalog-abilities count=0 total=0 page=0 pages=1");
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
            string weapons = entry.Weapons is { Count: > 0 } ? string.Join(",", entry.Weapons) : "any";
            string forms = entry.Forms is { Count: > 0 } ? string.Join(",", entry.Forms) : "any";
            // IN2 (v0.15.1): ability `cat` badge derived from prefab name.
            var cat = Categorization.ClassifyAbility(name);
            ctx.Reply(
                $"[BEELZ:catalog-ability] an={name}" +
                $" weapons={weapons}" +
                $" forms={forms}" +
                $" transform_only={(entry.TransformOnly ? 1 : 0)}" +
                $" enabled={(entry.Enabled ? 1 : 0)}" +
                $" difficulty={entry.Difficulty}" +
                $" cat={cat}" +
                $" damage_scale={entry.DamageScale:F2}" +
                $" cooldown_scale={entry.CooldownScale:F2}" +
                $" notes={SafeToken(entry.Notes ?? "")}");
        }
        ctx.Reply($"[BEELZ:end] cmd=catalog-abilities count={ordered.Count} total={total} page={page} pages={pages}");
    }

    [Command("hotkeys", description: "Stream the caller's named hotkey bindings (BCH-readable). W4 extra slots beyond V Rising's 6.")]
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
        ctx.Reply("[BEELZ:end] cmd=transform-config count=2");
    }
}
