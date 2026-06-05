using System;
using System.Linq;
using System.Text;
using Beelzebub.Services;
using ProjectM;
using ProjectM.Network;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Entities;
using VampireCommandFramework;

namespace Beelzebub.Commands;

[CommandGroup("beelz admin")]
internal static class AdminCommands
{
    /// <summary>
    /// Audit-log line for every admin grant/revoke/force action. Goes to BepInEx\LogOutput.log.
    /// Format is grep-friendly: "[Beelz AUDIT] admin=<sid> action=<name> target=<sid> ..."
    /// </summary>
    static void Audit(ChatCommandContext ctx, string action, ulong targetSteamId, string targetName, string detail)
    {
        ulong adminSteamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Core.Log.LogInfo($"[Beelz AUDIT] admin={adminSteamId} action={action} target={targetSteamId} ({targetName}) {detail}");
    }

    /// <summary>v0.38.0: friendly in-game unit name for admin replies (falls back to the prefab name).</summary>
    static string UnitDisplay(int guid) => Core.AbilityMetadata?.ResolveUnitName(guid) ?? new Stunlock.Core.PrefabGUID(guid).GetPrefabName();

    /// <summary>v0.38.0: friendly ability name for admin replies (falls back to the prefab name).</summary>
    static string AbilityDisplay(int guid)
    {
        var i = Core.AbilityMetadata?.Resolve(guid);
        return (i != null && !string.IsNullOrEmpty(i.Name)) ? i.Name : new Stunlock.Core.PrefabGUID(guid).GetPrefabName();
    }

    [Command("help", description: "List all Beelzebub admin commands, grouped by purpose.", adminOnly: true)]
    public static void Help(ChatCommandContext ctx)
    {
        ctx.Reply("=== Beelzebub ADMIN commands === (player commands: .beelz commands)");
        ctx.Reply("-- RULES / CAPTURE FILTERS --");
        ctx.Reply(".beelz admin rules / reload — show / re-read the ability rules");
        ctx.Reply(".beelz admin ability <name> [<field> <value> ...] — set per-ability rule(s) live, up to 5 pairs; omit field to READ current config. Fields: enabled, weapons, forms, transformonly, difficulty, phase, allowdenied, damagescale, cooldownscale, cooldown, range, charges, chargetime, aoe, projspeed, duration, healing, summoncap, summontimeout, summonunits, forcetimeout, category, reviewstatus, reviewtag, condition, interrupt/interruptonhit/freemove/freelymove/castspeed, notes");
        ctx.Reply(".beelz admin ability-set <id> \"(field=value)(field2=value2)...\" — BULK-set MANY fields in one command (parens or ;-separated; handles spaces/commas)");
        ctx.Reply(".beelz admin ability <id> defaults  /  .beelz admin ability all defaults — reset one/every ability's shaping config to shipped baseline");
        ctx.Reply(".beelz admin transform-set <CHAR_unit> <field> <value> — set a per-unit transform rule (enabled, difficulty, tier, damagescale, cooldownscale, healthscale, speedscale, fullreplace, powerscalingmode, notes)");
        ctx.Reply(".beelz admin default <damagescale|cooldownscale> <value> — server-wide scaling baseline");
        ctx.Reply(".beelz admin tune <ability> <interrupt|freemove|castspeed|cooldown|range|charges|chargetime|aoe|projspeed|leapheight|duration|healing> <value> / tune-list — ability shaping shortcut, ONE field per command (Abilities_ApplyConfig, default ON)");
        ctx.Reply(".beelz admin deny|undeny|allow|unallow <pattern> · denyguid|allowguid <add|remove> <guid> · transformonly <add|remove> <pattern|guid> — capture/reservation filters");
        ctx.Reply(".beelz admin freeze-captures <on|off|status> — master CaptureOnKill toggle");
        ctx.Reply("-- TRANSFORM CONFIG --");
        ctx.Reply(".beelz admin transform mode|duration|cooldown <regular|vblood> <...> — transform tuning");
        ctx.Reply(".beelz admin transform show — current transform settings · difficulty [basic|brutal] — server gating");
        ctx.Reply(".beelz admin testform <wolf|bear|off> / testmount <player> <on|off> — TEST: enter a native form / mounted bar with your loadout abilities");
        ctx.Reply("-- BROADCASTS --");
        ctx.Reply(".beelz admin broadcast <status|leaderboard on|off|interval <min>|top <n>|complete on|off|test> — server announcement controls");
        ctx.Reply(".beelz admin broadcast-msg <complete|leaderboard> <list|add|remove|edit> — manage the announcement message pool (wrap text in \"quotes\")");
        ctx.Reply("-- PLAYER GRANTS --");
        ctx.Reply(".beelz admin give|revoke <player> <unitGuid> <abilityGuid> — grant / remove one captured ability");
        ctx.Reply(".beelz admin devour <player> <unitGuid> — grant ALL of a unit's abilities at once (alternative to transformation)");
        ctx.Reply(".beelz admin give-transform|revoke-transform|force-transform|clear-transform <player> [unitGuid] — renderable forms: Dracula, Morgana, Werewolf, Golem, Gargoyle (+ basic werewolf)");
        ctx.Reply(".beelz admin set-slot|clear-slot <player> <slot> [abilityGuid] — universal slot binds");
        ctx.Reply(".beelz admin set-weapon-slot|clear-weapon-slot <player> <weapon> <slot> [abilityGuid] — per-weapon binds");
        ctx.Reply(".beelz admin reset-loadouts <player> CONFIRM — clear ALL of a player's slot/form/transform loadouts (captures kept)");
        ctx.Reply("-- INSPECT --");
        ctx.Reply(".beelz admin inspect <player> / progress <player> — view a player's state");
        ctx.Reply(".beelz admin snapshot — server-wide summary · scan-abilities — dump ability metadata to disk · dump <abilityGuid|form> — log an ability's ECS component chain (DIAGNOSTIC)");
        ctx.Reply("-- SUMMONS --");
        ctx.Reply(".beelz admin desummon <player> / desummon-all — clean up ally summons · revert-all — end all transforms");
        ctx.Reply("-- RECOVERY (fix a stuck player, no server wipe) --");
        ctx.Reply(".beelz admin respawn <player> — rebuild a stuck bar by respawning in place (keeps progress)");
        ctx.Reply(".beelz admin purge <player> CONFIRM — LAST RESORT: wipe ALL bar integration to vanilla incl. the engine modification LEAK (captures/unlocks kept; player re-slots after)");
        ctx.Reply(".beelz admin rebuildslots / clearslotmods / rebuildbar <player> — slot/bar repair levers");
        ctx.Reply(".beelz admin unmount <player> — force-dismount + clear stuck mount buffs (re-applies grants)");
        ctx.Reply(".beelz admin cleanse <player> [buffNameOrGuid] — strip stuck STATE buffs (invisible/phased/immaterial that survive respawn+relog); omit buff to remove the known ones");
        ctx.Reply(".beelz admin buffs <player> — DIAGNOSTIC: dump buffs + slot overrides to the server log");
        ctx.Reply(".beelz admin copy-collection <player> / paste-collection <player> — backup + restore a collection");
        ctx.Reply(".beelz admin reset-character <player> CONFIRM-RESET — fresh character (collection preserved)");
        ctx.Reply("-- DANGER --");
        ctx.Reply(".beelz admin set <key> <value> — set any config live (keys via .beelz api config)");
        ctx.Reply(".beelz admin wipe-all CONFIRM-WIPE — wipe ALL player data on the server");
    }

    [Command("rules", description: "Show the currently loaded ability filter rules.", adminOnly: true)]
    public static void Rules(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var r = Core.AbilityRules.Current;
        var sb = new StringBuilder();
        sb.Append("Ability rules (v").Append(r.Version).Append("):").AppendLine();
        sb.Append("  Deny patterns: ").AppendLine(r.DenyPatterns.Count == 0 ? "(none)" : string.Join(", ", r.DenyPatterns));
        sb.Append("  Allow patterns: ").AppendLine(r.AllowPatterns.Count == 0 ? "(none — all allowed unless denied)" : string.Join(", ", r.AllowPatterns));
        sb.Append("  Deny GUIDs: ").Append(r.DenyGuids.Count).Append(" entries").AppendLine();
        sb.Append("  Allow GUIDs: ").Append(r.AllowGuids.Count).Append(" entries").AppendLine();
        sb.Append("File: ").Append(Core.AbilityRules.RulesFilePath);
        ctx.Reply(sb.ToString());
    }

    [Command("deny", description: "Add a substring pattern to the deny list. Usage: .beelz admin deny <pattern>", adminOnly: true)]
    public static void Deny(ChatCommandContext ctx, string pattern)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (Core.AbilityRules.AddDenyPattern(pattern))
            ctx.Reply($"Added deny pattern '{pattern}'. Existing captures retain (not retroactive); future captures are filtered.");
        else
            ctx.Reply($"Pattern '{pattern}' already on the deny list (or empty).");
    }

    [Command("undeny", description: "Remove a substring pattern from the deny list. Usage: .beelz admin undeny <pattern>", adminOnly: true)]
    public static void Undeny(ChatCommandContext ctx, string pattern)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ctx.Reply(Core.AbilityRules.RemoveDenyPattern(pattern)
            ? $"Removed '{pattern}' from deny list."
            : $"'{pattern}' not found on deny list.");
    }

    [Command("allow", description: "Add a substring pattern to the allow list (when non-empty, only matching abilities are captured). Usage: .beelz admin allow <pattern>", adminOnly: true)]
    public static void Allow(ChatCommandContext ctx, string pattern)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (Core.AbilityRules.AddAllowPattern(pattern))
            ctx.Reply($"Added allow pattern '{pattern}'. When the allow list is non-empty, ONLY abilities whose names contain one of these patterns are captured.");
        else
            ctx.Reply($"Pattern '{pattern}' already on the allow list (or empty).");
    }

    [Command("unallow", description: "Remove a substring pattern from the allow list. Usage: .beelz admin unallow <pattern>", adminOnly: true)]
    public static void Unallow(ChatCommandContext ctx, string pattern)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ctx.Reply(Core.AbilityRules.RemoveAllowPattern(pattern)
            ? $"Removed '{pattern}' from allow list."
            : $"'{pattern}' not found on allow list.");
    }

    [Command("reload", description: "Re-read ability rules JSON from disk (for hand-edited changes).", adminOnly: true)]
    public static void Reload(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        Core.AbilityRules.Load();
        Beelzebub.Services.BestiaryService.InvalidateTotals();   // v0.86.0 (Bug B): deny/allow changes can shift the capturable total
        // v0.46.0: re-apply ability cast-tuning from the freshly-loaded rules (no-op unless
        // Abilities_ApplyConfig). Lets admins hand-edit Interruptible/FreeMoveAfterCast and
        // reload without a server restart.
        // v0.120.0: restore-then-reapply so a hand-edit that LOWERS or CLEARS a value takes effect on reload
        // (ApplyAll alone left the old baked value until a restart — the "reload only works after restart" bug).
        int tuned = AbilityTuningService.ReapplyAll();
        string tuneNote = Beelzebub.Config.Settings.Abilities_ApplyConfig.Value
            ? $" Ability tuning re-applied to {tuned} cast prefab(s)."
            : " (Ability tuning disabled — set Abilities_ApplyConfig to use it.)";
        ctx.Reply($"Rules reloaded from {Core.AbilityRules.RulesFilePath}.{tuneNote}");
    }

    [Command("tune", description: "Shape an ability server-wide: interrupt on|off, interruptonhit on|off, freemove on|off, freelymove <seconds>, castspeed <0..1>, cooldown <seconds>, range <distance>, charges <n>, chargetime <seconds>, aoe <radius>, projspeed <speed>, leapheight <height> (lower a boss leap's apex so a player caster isn't flung sky-high; vanilla ~250), duration <seconds>, healing <multiplier>, summoncap <n>, summontimeout <seconds>, forcetimeout <seconds>. Applies when Abilities_ApplyConfig is on (default). ONE field per command. Usage: .beelz admin tune <ability name> <knob> <value|clear>", adminOnly: true)]
    public static void Tune(ChatCommandContext ctx, string ability, string knob, string value)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        string name = (ability ?? "").Trim();
        if (name.Length == 0) { ctx.Reply("Usage: .beelz admin tune <ability name or ID> <knob> <value>. Use the ability/group prefab name OR ID from .beelz list / .beelz api list."); return; }
        // v0.69.0: accept a numeric PrefabGUID (the ID .beelz list / BCH show) and resolve to the name key.
        if (int.TryParse(name, out _))
        {
            string resolved = Beelzebub.Services.AbilityRules.ResolveAbilityKey(name);
            if (resolved == null) { ctx.Reply($"No ability prefab found for ID '{name}'. Use the name or a valid ID from .beelz list."); return; }
            name = resolved;
        }

        // v0.129.0: guard the "showed applied but did nothing" trap — if the name isn't a real ability prefab
        // (e.g. a humanized display name like "Bat Vampire Summon Minions"), tuning it would silently create a
        // dead rule entry that never resolves to a prefab. Reject with guidance instead.
        if (Beelzebub.Services.AbilityRules.ResolveAbilityGuid(name) == 0)
        {
            ctx.Reply($"'{name}' doesn't match a known ability prefab, so tuning it would do nothing. Use the ability's ID (from .beelz list / BCH) or its exact prefab name (e.g. AB_BatVampire_SummonMinions_AbilityGroup).");
            return;
        }

        var map = Core.AbilityRules.Current.AbilityMap;
        if (!map.TryGetValue(name, out var e)) { e = new AbilityRules.AbilityEntry(); map[name] = e; }

        string k = (knob ?? "").Trim().ToLowerInvariant();
        string v = (value ?? "").Trim().ToLowerInvariant();
        bool? onOff = v is "on" or "true" or "1" ? true : v is "off" or "false" or "0" ? false : (bool?)null;
        bool clear = v is "clear" or "none" or "null";

        switch (k)
        {
            case "interrupt":
                if (onOff == null) { ctx.Reply("interrupt expects on|off."); return; }
                e.Interruptible = onOff.Value;
                break;
            case "interruptonhit": case "interruptattack": case "breakonhit":   // v0.87.0
                if (onOff == null) { ctx.Reply("interruptonhit expects on|off (cancel the cast when the caster is hit)."); return; }
                e.InterruptOnHit = onOff.Value;
                break;
            case "freemove":
                if (onOff == null) { ctx.Reply("freemove expects on|off."); return; }
                e.FreeMoveAfterCast = onOff.Value;
                break;
            case "freelymove": case "freemovesecs": case "freemoveafter":         // v0.87.0
                if (clear) { e.FreeMoveAfterSeconds = null; break; }
                if (!float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fms) || fms < 0f)
                { ctx.Reply("freelymove expects seconds >= 0 — free to move that many seconds INTO the cast (the cast continues), or 'clear'."); return; }
                e.FreeMoveAfterSeconds = fms;
                break;
            case "castspeed":
                if (!float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) || f < 0f)
                { ctx.Reply("castspeed expects a number >= 0 (0 = rooted during cast, 1 = full speed)."); return; }
                e.CastMovementSpeed = f;
                break;
            case "cooldown": case "cd":
                if (clear) { e.CooldownSeconds = null; break; }
                if (!float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float cd) || cd < 0f)
                { ctx.Reply("cooldown expects an absolute time in seconds >= 0, or 'clear'."); return; }
                e.CooldownSeconds = cd;
                break;
            case "range": case "maxrange":
                if (clear) { e.MaxRangeOverride = null; break; }
                if (!float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float rg) || rg < 0f)
                { ctx.Reply("range expects a max cast distance >= 0, or 'clear'."); return; }
                e.MaxRangeOverride = rg;
                break;
            case "charges": case "maxcharges":
                if (clear) { e.ChargesMax = null; break; }
                if (!int.TryParse(v, out int mc) || mc < 0) { ctx.Reply("charges expects an integer >= 0, or 'clear'."); return; }
                e.ChargesMax = mc;
                break;
            case "chargetime": case "chargeuptime":
                if (clear) { e.ChargeTimeSeconds = null; break; }
                if (!float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ctv) || ctv < 0f)
                { ctx.Reply("chargetime expects seconds >= 0 (recharge per charge), or 'clear'."); return; }
                e.ChargeTimeSeconds = ctv;
                break;
            case "aoe": case "aoeradius": case "radius":
                if (clear) { e.AoeRadius = null; break; }
                if (!float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ar) || ar < 0f)
                { ctx.Reply("aoe expects an area radius >= 0, or 'clear'."); return; }
                e.AoeRadius = ar;
                break;
            case "projspeed": case "projectilespeed":
                if (clear) { e.ProjectileSpeed = null; break; }
                if (!float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ps) || ps < 0f)
                { ctx.Reply("projspeed expects a projectile speed >= 0, or 'clear'."); return; }
                e.ProjectileSpeed = ps;
                break;
            case "leapheight": case "travelheight":
                if (clear) { e.LeapHeight = null; break; }
                if (!float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float lh) || lh < 0f)
                { ctx.Reply("leapheight expects a leap/travel height >= 0 (vanilla boss leaps ~250; try ~20-40), or 'clear'."); return; }
                e.LeapHeight = lh;
                break;
            case "duration": case "effectduration":
                if (clear) { e.EffectDurationSeconds = null; break; }
                if (!float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ed) || ed < 0f)
                { ctx.Reply("duration expects the buff/debuff duration in seconds >= 0, or 'clear'."); return; }
                e.EffectDurationSeconds = ed;
                break;
            case "healing": case "healmult":
                if (clear) { e.HealingMultiplier = null; break; }
                if (!float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hm) || hm < 0f)
                { ctx.Reply("healing expects a multiplier >= 0 (1.0 = no change), or 'clear'."); return; }
                e.HealingMultiplier = hm;
                break;
            case "summoncap": case "summonlimit":
                if (clear) { e.SummonCap = null; break; }
                if (!int.TryParse(v, out int sc) || sc < 0)
                { ctx.Reply("summoncap expects an integer >= 0 (0 = unlimited), or 'clear'. Overrides the global summon cap for this ability."); return; }
                e.SummonCap = sc;
                break;
            case "summontimeout": case "summonlifetime":
                if (clear) { e.SummonTimeoutSeconds = null; break; }
                if (!float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float st) || st < 0f)
                { ctx.Reply("summontimeout expects seconds >= 0 (0 = never expires), or 'clear'. Overrides the global summon timeout for this ability."); return; }
                e.SummonTimeoutSeconds = st;
                break;
            case "summonunits": case "unitspercast":
                if (clear) { e.SummonUnitsPerCast = null; break; }
                if (!int.TryParse(v, out int su) || su < 0)
                { ctx.Reply("summonunits expects an integer >= 0 (max UNITS one cast summons; 0 = natural count), or 'clear'. Separate from summoncap (concurrent USES)."); return; }
                e.SummonUnitsPerCast = su;
                break;
            case "forcetimeout": case "bufftimeout":
                if (clear) { e.ForceTimeoutSeconds = null; break; }
                if (!float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fto) || fto < 0f)
                { ctx.Reply("forcetimeout expects seconds >= 0 — force this ability's otherwise-INDEFINITE effects/buffs to expire after this long (adds a lifetime where there is none), or 'clear'."); return; }
                e.ForceTimeoutSeconds = fto;
                break;
            case "powerwindow": case "dmgwindow":
                if (clear) { e.PowerWindowSeconds = null; break; }
                if (!float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pw) || pw < 0f)
                { ctx.Reply("powerwindow expects seconds >= 0 — how long the granted-cast power buff lasts so a POWER-SCALED DoT/AoE that ticks after the cast is still boosted (0/clear = default 1.5s). Only affects power-scaled damage; flat boss DoTs can't be scaled."); return; }
                e.PowerWindowSeconds = pw;
                break;
            default:
                ctx.Reply("Unknown knob. Use: interrupt | interruptonhit | freemove | freelymove | castspeed | cooldown | range | charges | chargetime | aoe | projspeed | leapheight | duration | healing | summoncap | summontimeout | summonunits | forcetimeout | powerwindow.");
                return;
        }
        Core.AbilityRules.Save();

        if (!Beelzebub.Config.Settings.Abilities_ApplyConfig.Value)
        {
            ctx.Reply($"Saved {k}={v} for '{name}', but Abilities_ApplyConfig is OFF — set it true (it applies on next load / reload).");
            return;
        }
        int applied = AbilityTuningService.ReapplyAll();   // v0.120.0: restore-then-reapply so lowering/clearing this field takes effect now, not next restart
        ctx.Reply($"Tuned '{name}': {k}={v}. Re-applied to {applied} cast prefab(s). NOTE: this is a GLOBAL prefab edit — the original NPC/boss cast of this ability changes too.");
        // v0.73.0: warn when charges can't apply (the ability has no charge system).
        if ((k == "charges" || k == "maxcharges" || k == "chargetime" || k == "chargeuptime")
            && !AbilityTuningService.AbilityChainHasCharges(name))
            ctx.Reply("NOTE: this ability has no charge system, so this won't apply. Charges can only be tuned on abilities that already use charges (e.g. dashes).");
        // v0.76.0: cooldown on a charge-based ability is governed by recharge, not AbilityCooldownData.
        if ((k == "cooldown" || k == "cd") && AbilityTuningService.AbilityChainHasCharges(name))
            ctx.Reply("NOTE: this ability is charge-based — its delay is the charge RECHARGE, not a cooldown. Use 'chargetime' (recharge seconds) and/or 'charges' instead.");
        Audit(ctx, "tune", 0, name, $"{k}={v} applied={applied}");
    }

    [Command("tune-list", description: "List every ability with ANY shaping/tuning set (cooldown, range, charges, aoe, projspeed, duration, healing, forcetimeout, summons, scales, interrupt, freemove/freelymove, castspeed). Usage: .beelz admin tune-list", adminOnly: true)]
    public static void TuneList(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var map = Core.AbilityRules.Current.AbilityMap;
        bool enabled = Beelzebub.Config.Settings.Abilities_ApplyConfig.Value;
        ctx.Reply($"=== Ability shaping/tuning === (Abilities_ApplyConfig={(enabled ? "ON" : "OFF")})");
        int n = 0;
        foreach (var (name, e) in map.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            string parts = "";
            // cast modifiers
            if (e.Interruptible.HasValue) parts += $" interrupt={(e.Interruptible.Value ? "on" : "off")}";
            if (e.InterruptOnHit.HasValue) parts += $" interruptonhit={(e.InterruptOnHit.Value ? "on" : "off")}";
            if (e.FreeMoveAfterCast) parts += " freemove=on";
            if (e.FreeMoveAfterSeconds.HasValue) parts += $" freelymove={e.FreeMoveAfterSeconds.Value:F2}s";
            if (e.CastMovementSpeed.HasValue) parts += $" castspeed={e.CastMovementSpeed.Value:F2}";
            // baked overrides (v0.100.0: previously invisible to tune-list)
            if (e.CooldownSeconds.HasValue) parts += $" cooldown={e.CooldownSeconds.Value:F1}s";
            if (e.MaxRangeOverride.HasValue) parts += $" range={e.MaxRangeOverride.Value:F1}";
            if (e.ChargesMax.HasValue) parts += $" charges={e.ChargesMax.Value}";
            if (e.ChargeTimeSeconds.HasValue) parts += $" chargetime={e.ChargeTimeSeconds.Value:F1}s";
            if (e.AoeRadius.HasValue) parts += $" aoe={e.AoeRadius.Value:F1}";
            if (e.ProjectileSpeed.HasValue) parts += $" projspeed={e.ProjectileSpeed.Value:F1}";
            if (e.LeapHeight.HasValue) parts += $" leapheight={e.LeapHeight.Value:F1}";
            if (e.EffectDurationSeconds.HasValue) parts += $" duration={e.EffectDurationSeconds.Value:F1}s";
            if (e.HealingMultiplier.HasValue) parts += $" healing={e.HealingMultiplier.Value:F2}";
            if (e.ForceTimeoutSeconds.HasValue) parts += $" forcetimeout={e.ForceTimeoutSeconds.Value:F1}s";
            if (e.PowerWindowSeconds.HasValue) parts += $" powerwindow={e.PowerWindowSeconds.Value:F1}s";
            if (e.SummonCap.HasValue) parts += $" summoncap={e.SummonCap.Value}";
            if (e.SummonTimeoutSeconds.HasValue) parts += $" summontimeout={e.SummonTimeoutSeconds.Value:F0}s";
            if (e.SummonUnitsPerCast.HasValue) parts += $" summonunits={e.SummonUnitsPerCast.Value}";
            if (Math.Abs(e.DamageScale - 1f) > 0.0001f) parts += $" damagescale={e.DamageScale:F2}";
            if (Math.Abs(e.CooldownScale - 1f) > 0.0001f) parts += $" cooldownscale={e.CooldownScale:F2}";
            if (parts.Length == 0) continue;
            ctx.Reply($"  {name}:{parts}");
            n++;
        }
        if (n == 0) ctx.Reply("  (none — e.g. .beelz admin tune AB_Vampire_VeilOfChaos_Group interrupt on)");
        else ctx.Reply($"{n} tuned ability(ies).{(enabled ? "" : " Set Abilities_ApplyConfig to apply them.")}");
    }

    [Command("broadcast", description: "Server announcements. Sub: status | leaderboard on|off | interval <minutes> | top <1-5> | complete on|off | test. Usage: .beelz admin broadcast <sub> [value]", adminOnly: true)]
    public static void Broadcast(ChatCommandContext ctx, string sub = "status", string value = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        string s = (sub ?? "status").Trim().ToLowerInvariant();
        string v = (value ?? "").Trim().ToLowerInvariant();
        bool? onOff = v is "on" or "true" or "1" ? true : v is "off" or "false" or "0" ? false : (bool?)null;

        switch (s)
        {
            case "status":
                ctx.Reply($"=== Broadcasts === collection-complete={(Beelzebub.Config.Settings.Broadcast_CollectionComplete_Enabled.Value ? "ON" : "off")} · " +
                    $"leaderboard={(Beelzebub.Config.Settings.Broadcast_Leaderboard_Enabled.Value ? "ON" : "off")} " +
                    $"(every {Math.Max(1, Beelzebub.Config.Settings.Broadcast_Leaderboard_IntervalMinutes.Value)} min, top {Math.Clamp(Beelzebub.Config.Settings.Broadcast_Leaderboard_TopN.Value, 1, 5)})");
                return;
            case "leaderboard": case "board":
                if (onOff == null) { ctx.Reply("Usage: .beelz admin broadcast leaderboard on|off"); return; }
                Beelzebub.Config.Settings.Broadcast_Leaderboard_Enabled.Value = onOff.Value;
                if (onOff.Value) Beelzebub.Services.BroadcastService.ResetLeaderboardClock();
                ctx.Reply($"Leaderboard broadcast {(onOff.Value ? $"ON — next in ~{Math.Max(1, Beelzebub.Config.Settings.Broadcast_Leaderboard_IntervalMinutes.Value)} min" : "off")}.");
                break;
            case "interval": case "minutes":
                if (!int.TryParse(v, out int mins) || mins < 1) { ctx.Reply("interval expects minutes >= 1 (60 = hourly, 1440 = daily)."); return; }
                Beelzebub.Config.Settings.Broadcast_Leaderboard_IntervalMinutes.Value = mins;
                Beelzebub.Services.BroadcastService.ResetLeaderboardClock();
                ctx.Reply($"Leaderboard interval set to {mins} min.");
                break;
            case "top": case "topn":
                if (!int.TryParse(v, out int topn) || topn < 1 || topn > 5) { ctx.Reply("top expects 1..5."); return; }
                Beelzebub.Config.Settings.Broadcast_Leaderboard_TopN.Value = topn;
                ctx.Reply($"Leaderboard will list the top {topn}.");
                break;
            case "complete": case "completion":
                if (onOff == null) { ctx.Reply("Usage: .beelz admin broadcast complete on|off"); return; }
                Beelzebub.Config.Settings.Broadcast_CollectionComplete_Enabled.Value = onOff.Value;
                ctx.Reply($"Collection-complete broadcast {(onOff.Value ? "ON" : "off")}.");
                break;
            case "test":
                Beelzebub.Services.BroadcastService.BroadcastLeaderboard();
                ctx.Reply("Sent a leaderboard broadcast now (if anyone has collected abilities).");
                break;
            default:
                ctx.Reply("Unknown sub. Use: status | leaderboard on|off | interval <minutes> | top <1-5> | complete on|off | test.");
                return;
        }
        Audit(ctx, "broadcast", 0, s, v);
    }

    // v0.100.1: manage the broadcast MESSAGE POOLS individually (add/remove/list/edit) instead of
    // hand-editing the whole pipe-separated config string. Each pool rotates through its messages.
    // v0.100.0: non-destructive per-player reset — clears bindings + custom loadouts + active transform,
    // KEEPS captures + unlocks. Fills the recovery-ladder gap between "respawn" (keeps everything) and
    // "reset-character"/"wipe" (nukes the collection).
    [Command("reset-loadouts", description: "Reset a player's slot loadouts (universal + per-weapon + per-form) AND their custom transform loadouts, and end any active transform — KEEPS their captured abilities + transform unlocks. Usage: .beelz admin reset-loadouts <player> CONFIRM", adminOnly: true)]
    public static void ResetLoadouts(ChatCommandContext ctx, string player, string confirm = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        if (!string.Equals(confirm?.Trim(), "CONFIRM", StringComparison.OrdinalIgnoreCase))
        { ctx.Reply($"This clears ALL of {fullName}'s slot/form/transform loadouts (captures + unlocks kept). Re-run: .beelz admin reset-loadouts {player} CONFIRM"); return; }

        if (Core.AbilityRegistry.GetActiveTransform(steamId) != null)
            Core.Transforms.Revert(steamId, "admin reset-loadouts");
        int removed = Core.AbilityRegistry.ClearAllLoadouts(steamId);
        // Clear the live bar too (online players) so it reverts to vanilla immediately.
        if (character.Exists())
            for (int slot = 0; slot <= 7; slot++) { try { Beelzebub.Services.SlotApply.ClearGrant(character, slot); } catch { } }
        Core.Persistence.RequestSave();
        ctx.Reply($"Reset {fullName}'s loadouts: removed {removed} slot bind(s) + all per-form & custom transform loadouts, ended any active transform. Captures + unlocks KEPT. (They may need to swap weapons or relog for the live bar to fully refresh.)");
        Audit(ctx, "reset-loadouts", steamId, fullName, $"removed={removed}");
    }

    [Command("broadcast-msg", description: "Manage a broadcast message pool. Usage: .beelz admin broadcast-msg <complete|leaderboard> <list|add|remove|edit> [args]. add \"<text>\" · remove <n> · edit <n> \"<text>\" · list. WRAP multi-word messages in \"quotes\". Tokens: %player% (complete), %top%/%count% (leaderboard).", adminOnly: true)]
    public static void BroadcastMsg(ChatCommandContext ctx, string pool, string action = "list", string arg1 = null, string arg2 = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var entry = BroadcastPoolEntry(pool, out string label, out string tokenHint);
        if (entry == null) { ctx.Reply("First arg = the pool: 'complete' (100%-collection, token %player%) or 'leaderboard' (tokens %top% / %count%)."); return; }

        var msgs = SplitPool(entry.Value);
        string act = (action ?? "list").Trim().ToLowerInvariant();
        switch (act)
        {
            case "list":
                if (msgs.Count == 0) { ctx.Reply($"{label}: no custom messages — the built-in default is used. Add one with: .beelz admin broadcast-msg {pool} add \"<text>\""); return; }
                ctx.Reply($"=== {label} messages ({msgs.Count}) — tokens: {tokenHint} ===");
                for (int i = 0; i < msgs.Count; i++) ctx.Reply($"  [{i + 1}] {msgs[i]}");
                return;

            case "add":
                if (string.IsNullOrWhiteSpace(arg1)) { ctx.Reply($"Usage: .beelz admin broadcast-msg {pool} add \"<text>\"  (wrap the message in quotes)."); return; }
                if (arg2 != null) { ctx.Reply("Too many arguments — wrap the WHOLE message in \"quotes\" so it's one argument."); return; }
                if (arg1.Contains('|')) { ctx.Reply("A message can't contain '|' (it's the internal separator)."); return; }
                msgs.Add(arg1.Trim());
                WritePool(entry, msgs);
                ctx.Reply($"Added to {label} (now {msgs.Count}): [{msgs.Count}] {arg1.Trim()}");
                break;

            case "remove": case "rem": case "delete": case "del":
            {
                if (!int.TryParse((arg1 ?? "").Trim(), out int idx) || idx < 1 || idx > msgs.Count) { ctx.Reply($"remove expects an index 1-{msgs.Count} (see 'list')."); return; }
                string removed = msgs[idx - 1];
                msgs.RemoveAt(idx - 1);
                WritePool(entry, msgs);
                ctx.Reply($"Removed [{idx}] from {label} (now {msgs.Count}): {removed}");
                break;
            }

            case "edit": case "set":
            {
                if (!int.TryParse((arg1 ?? "").Trim(), out int idx) || idx < 1 || idx > msgs.Count) { ctx.Reply($"Usage: .beelz admin broadcast-msg {pool} edit <n> \"<text>\"  (n = 1-{msgs.Count}, see 'list')."); return; }
                if (string.IsNullOrWhiteSpace(arg2)) { ctx.Reply("Edit needs new text — wrap it in \"quotes\": edit <n> \"<text>\"."); return; }
                if (arg2.Contains('|')) { ctx.Reply("A message can't contain '|'."); return; }
                msgs[idx - 1] = arg2.Trim();
                WritePool(entry, msgs);
                ctx.Reply($"Edited [{idx}] of {label}: {arg2.Trim()}");
                break;
            }

            default:
                ctx.Reply("Action must be: list | add \"<text>\" | remove <n> | edit <n> \"<text>\".");
                return;
        }
        Audit(ctx, "broadcast-msg", 0, pool, act);
    }

    static BepInEx.Configuration.ConfigEntry<string> BroadcastPoolEntry(string pool, out string label, out string tokenHint)
    {
        switch ((pool ?? "").Trim().ToLowerInvariant())
        {
            case "complete": case "completion": case "collection":
                label = "Collection-complete"; tokenHint = "%player%";
                return Beelzebub.Config.Settings.Broadcast_CollectionComplete_Messages;
            case "leaderboard": case "board": case "top":
                label = "Leaderboard"; tokenHint = "%top%, %count%";
                return Beelzebub.Config.Settings.Broadcast_Leaderboard_Messages;
            default:
                label = null; tokenHint = null; return null;
        }
    }

    static System.Collections.Generic.List<string> SplitPool(string raw)
    {
        var list = new System.Collections.Generic.List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return list;
        foreach (var p in raw.Split('|')) { var t = p.Trim(); if (t.Length > 0) list.Add(t); }
        return list;
    }

    static void WritePool(BepInEx.Configuration.ConfigEntry<string> entry, System.Collections.Generic.List<string> msgs)
        => entry.Value = string.Join(" | ", msgs);   // setting .Value persists to the .cfg (SaveOnConfigSet)

    // v0.89.1 DIAGNOSTIC: dump the ECS component list of an ability's prefab chain
    // (Group → Cast → spawned), or of the caster's active shapeshift form buff, to
    // LogOutput.log. Used to find which component governs a channel's movement-lock
    // (freelymove) and a vanilla form's exit-on-cast (forms). Read-only.
    [Command("dump", description: "DIAGNOSTIC: log to LogOutput.log. <abilityGuid> = an ability's full prefab chain (Group→Cast→spawned) + key field values; 'form' = your active shapeshift form buff's components; 'forms' = every shapeshift form-buff prefab the game has + which form Beelzebub maps it to (verify skin coverage). Usage: .beelz admin dump <abilityGuid|form|forms>", adminOnly: true)]
    public static void Dump(ChatCommandContext ctx, string target)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        string t = (target ?? "").Trim();

        if (t.Equals("forms", StringComparison.OrdinalIgnoreCase))
        {
            // v0.94.0: list EVERY shapeshift form buff the game knows about + which form Beelzebub maps it
            // to — so admins can confirm all player skins are recognized for custom-ability injection.
            int total = 0, recognized = 0;
            var byForm = new System.Collections.Generic.SortedDictionary<string, System.Collections.Generic.List<string>>();
            foreach (var (fguid, fname) in Core.PrefabNames)
            {
                if (string.IsNullOrEmpty(fname)) continue;
                if (fname.IndexOf("Shapeshift", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!fname.EndsWith("_Buff", StringComparison.Ordinal)) continue;
                total++;
                var form = Beelzebub.Services.ShapeshiftAbilityService.FormForBuff(fguid, fname);
                string key = form.ToString();
                if (form != Beelzebub.Services.ShapeshiftForm.None) recognized++;
                if (!byForm.TryGetValue(key, out var list)) { list = new System.Collections.Generic.List<string>(); byForm[key] = list; }
                list.Add($"{fname}({fguid})");
            }
            foreach (var (form, list) in byForm)
            {
                list.Sort(StringComparer.OrdinalIgnoreCase);
                Core.Log.LogInfo($"[Beelz DUMP-FORMS] {form} ({list.Count}): {string.Join(", ", list)}");
            }
            ctx.Reply($"Dumped {total} shapeshift form-buff prefab(s) — {recognized} recognized as injectable forms, {total - recognized} = None (travel/utility/ability buffs). See [Beelz DUMP-FORMS] in LogOutput.log.");
            return;
        }

        if (t.Equals("form", StringComparison.OrdinalIgnoreCase))
        {
            Entity ch = ctx.Event.SenderCharacterEntity;
            if (!Core.EntityManager.HasBuffer<BuffBuffer>(ch)) { ctx.Reply("No buff buffer on your character."); return; }
            var buffs = Core.EntityManager.GetBuffer<BuffBuffer>(ch);
            int dumped = 0;
            for (int i = 0; i < buffs.Length; i++)
            {
                int g = buffs[i].PrefabGuid._Value;
                string nm = buffs[i].PrefabGuid.GetPrefabName() ?? "";
                if (Beelzebub.Services.ShapeshiftAbilityService.IsSupportedForm(g, nm))
                {
                    DumpEntity(buffs[i].Entity, $"FORM-BUFF {nm} ({g})");
                    dumped++;
                }
            }
            ctx.Reply(dumped > 0
                ? $"Dumped {dumped} form buff(s) to LogOutput.log — share the [Beelz DUMP] lines."
                : "You're not in a tracked shapeshift form — enter one first, then run this.");
            return;
        }

        if (!int.TryParse(t, out int guid)) { ctx.Reply("Usage: .beelz admin dump <abilityGuid|form>"); return; }
        if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(new PrefabGUID(guid), out Entity group) || !group.Exists())
        { ctx.Reply($"No prefab found for {guid}."); return; }

        DumpEntity(group, $"GROUP {new PrefabGUID(guid).GetPrefabName()} ({guid})");
        int casts = 0, spawns = 0;
        if (Core.EntityManager.HasBuffer<AbilityGroupStartAbilitiesBuffer>(group))
        {
            var starts = Core.EntityManager.GetBuffer<AbilityGroupStartAbilitiesBuffer>(group);
            for (int i = 0; i < starts.Length; i++)
            {
                if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(starts[i].PrefabGUID, out Entity cast) || !cast.Exists()) continue;
                DumpEntity(cast, $"  CAST {starts[i].PrefabGUID.GetPrefabName()} ({starts[i].PrefabGUID._Value})");
                casts++;
                if (!Core.EntityManager.HasBuffer<AbilitySpawnPrefabOnCast>(cast)) continue;
                var sp = Core.EntityManager.GetBuffer<AbilitySpawnPrefabOnCast>(cast);
                for (int j = 0; j < sp.Length; j++)
                {
                    if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(sp[j].SpawnPrefab, out Entity spe) || !spe.Exists()) continue;
                    DumpEntity(spe, $"    SPAWN {sp[j].SpawnPrefab.GetPrefabName()} ({sp[j].SpawnPrefab._Value})");
                    spawns++;
                }
            }
        }
        ctx.Reply($"Dumped GROUP + {casts} cast(s) + {spawns} spawn(s) to LogOutput.log — share the [Beelz DUMP] lines.");
    }

    static void DumpEntity(Entity e, string label)
    {
        try
        {
            var arr = Core.EntityManager.GetComponentTypes(e).ToArray();
            var names = arr
                .Select(x => { try { return Unity.Entities.TypeManager.GetType(x.TypeIndex).Name; } catch { return null; } })
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Core.Log.LogInfo($"[Beelz DUMP] {label}: {string.Join(", ", names)}");

            // v0.92.0: also dump the VALUES of the lock/movement/cast components so freelymove can be
            // diagnosed (is the cast a channel? did UseCastDuration=false take? is the root MovementImpair?).
            if (e.TryGetComponent<ModifyMovementDuringCastData>(out var mm))
                Core.Log.LogInfo($"[Beelz DUMP-VAL] {label} ModifyMovementDuringCast: speedMult={mm.MovementSpeedMultiplier._Value:F2} in={mm.InDuration._Value:F2} dur={mm.Duration._Value:F2} out={mm.OutDuration._Value:F2} useCastDur={mm.UseCastDuration}");
            if (e.TryGetComponent<AbilityCastTimeData>(out var ct))
                Core.Log.LogInfo($"[Beelz DUMP-VAL] {label} AbilityCastTime: maxCast={ct.MaxCastTime._Value:F2} postCast={ct.PostCastTime._Value:F2} hideBar={ct.HideCastBar}");
            if (e.TryGetComponent<BuffModificationFlagData>(out var bm))
                Core.Log.LogInfo($"[Beelz DUMP-VAL] {label} BuffModFlags: ModificationTypes={bm.ModificationTypes} (MovementImpair={(bm.ModificationTypes & 16L) == 16L})");
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz DUMP] {label} failed: {ex.Message}"); }
    }

    // ---------------------------------------------------------------------
    // v0.53.0 — FLUID ABILITY/TRANSFORM CONFIG. One command per config object;
    // each delegates parse/validate/persist to AbilityRules and reports the result.
    // Closes the gap where per-ability/per-unit/global-default fields were hand-edit-only.
    // ---------------------------------------------------------------------

    [Command("ability", description: "Set ANY per-ability rule live. Usage: .beelz admin ability <name|id> [<field> <value> ...] — set UP TO 5 fields at once; OMIT field/value to READ every current setting. weapons/forms take a WHITELIST (e.g. 'sword,axe' or 'wolf') or '!X' to BLACKLIST (e.g. 'forms !mounted' = usable everywhere except mounted). Fields: enabled, weapons, forms, transformonly, difficulty, phase, allowdenied, damagescale, cooldownscale, cooldown, range, charges, chargetime, aoe, projspeed, duration, healing, summoncap, summontimeout, summonunits, forcetimeout, category, interruptible, interruptonhit, freemove, freelymove, castspeed, notes. (weapons/forms take a comma list or 'any' to clear; cooldown/range/charges/aoe/projspeed/duration/healing/forcetimeout/freelymove/interruptonhit are baked edits applied when Abilities_ApplyConfig is on, default; forcetimeout makes otherwise-indefinite effects expire; freelymove frees movement N seconds into a cast; interruptonhit cancels the cast when the caster is hit; summoncap/summontimeout/summonunits govern summons.) CURATION: reviewstatus <Unreviewed|Reviewed|Approved|Blocked|Hidden>, reviewtag <type>, condition <Aimed|CloseRange|Summon|SelfCast|Movement|clear> (confirms an activation condition in-game -> conditionSource=confirmed, saved to the override file). RESET: .beelz admin ability <id> defaults — clear one ability's shaping config back to shipped baseline; .beelz admin ability all defaults — reset every ability.", adminOnly: true)]
    public static void AbilitySet(ChatCommandContext ctx, string ability, string field = null, string value = null,
        string field2 = null, string value2 = null, string field3 = null, string value3 = null,
        string field4 = null, string value4 = null, string field5 = null, string value5 = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        // v0.101.0: READ-ALL — ".beelz admin ability <id>" with no field dumps every configured setting.
        if (field == null)
        {
            string rkey = AbilityRules.ResolveAbilityKey(ability);
            if (string.IsNullOrEmpty(rkey)) { ctx.Reply($"Couldn't resolve ability '{ability}' — use a name or ID from .beelz list."); return; }
            string cfg = Core.AbilityRules.DescribeAbilityConfig(rkey);
            ctx.Reply($"{rkey} config: {cfg}");
            Core.Log.LogInfo($"[Beelz ABILITY-CFG] {rkey}: {cfg}");
            return;
        }

        // v0.72.0: reset-to-defaults. `.beelz admin ability <id> defaults` / `.beelz admin ability all defaults`.
        string f = (field ?? "").Trim().ToLowerInvariant();
        if (f is "defaults" or "default" or "reset")
        {
            bool apply = Beelzebub.Config.Settings.Abilities_ApplyConfig.Value;
            if (string.Equals((ability ?? "").Trim(), "all", StringComparison.OrdinalIgnoreCase))
            {
                int cleared = Core.AbilityRules.ResetAllAbilityDefaults();
                int restored = apply ? AbilityTuningService.RestoreAll() : 0;
                if (apply) AbilityTuningService.ApplyAll();   // re-assert any global cooldown floor
                ctx.Reply($"Reset shaping config on {cleared} ability(ies) to shipped defaults; live-restored {restored} prefab(s)."
                    + (apply ? "" : " (Abilities_ApplyConfig is OFF — baseline takes effect on next load.)")
                    + " Captures/availability rules (enabled/weapons/forms/deny) are unchanged. A server restart guarantees a full baseline.");
                Audit(ctx, "ability-defaults", 0, "all", $"cleared={cleared} restored={restored}");
                return;
            }
            var (rok, rmsg) = Core.AbilityRules.ResetAbilityDefaults(ability);
            ctx.Reply(rmsg);
            if (!rok) return;
            if (apply) { AbilityTuningService.RestoreAbility(ability); AbilityTuningService.ApplyAll(); }
            else ctx.Reply("(Abilities_ApplyConfig is OFF — baseline takes effect on next load.)");
            Audit(ctx, "ability-defaults", 0, ability ?? "", "reset");
            return;
        }

        // v0.101.0: SET one OR several field/value pairs in a single command (up to 5).
        var pairs = new System.Collections.Generic.List<(string field, string value)>();
        void AddPair(string ff, string vv) { if (!string.IsNullOrWhiteSpace(ff)) pairs.Add((ff, vv)); }
        AddPair(field, value); AddPair(field2, value2); AddPair(field3, value3); AddPair(field4, value4); AddPair(field5, value5);
        if (pairs.Count == 0) { ctx.Reply("Usage: .beelz admin ability <name|id> [<field> <value> ...]  —  omit field/value to READ the current config; '<id> defaults' / 'all defaults' to reset shaping."); return; }

        ApplyAbilityFieldPairs(ctx, ability, pairs);
    }

    /// <summary>
    /// v0.116.0: apply a list of (field,value) config pairs to ONE ability — the shared core of both
    /// <c>.beelz admin ability</c> (up to 5 space-separated pairs) and <c>.beelz admin ability-set</c> (a
    /// quoted (field=value) blob, unlimited). Routes `condition` to the metadata override; everything else to
    /// <see cref="AbilityRules.SetAbilityField"/>; re-applies baked cast tuning ONCE if any baked field changed.
    /// </summary>
    static void ApplyAbilityFieldPairs(ChatCommandContext ctx, string ability,
        System.Collections.Generic.List<(string field, string value)> pairs)
    {
        var results = new System.Collections.Generic.List<string>();
        bool anyBaked = false, chargeTouched = false, cooldownTouched = false;
        foreach (var (pf, pv) in pairs)
        {
            string pfl = pf.Trim().ToLowerInvariant();
            if (pv == null) { results.Add($"{pf}: needs a value"); continue; }
            // v0.114.0 (B4): `condition` is METADATA, not a rule field — route it to the metadata override
            // (sets conditionSource=confirmed). An admin/tester deliberately setting it IS the confirmation.
            if (pfl == "condition")
            {
                int cguid = AbilityRules.ResolveAbilityGuid(ability);
                if (cguid == 0) { results.Add("condition: couldn't resolve the ability to a GUID (use an id from .beelz list)."); continue; }
                var (cok, cmsg) = Core.AbilityMetadata.SetConditionConfirmed(cguid, pv);
                results.Add(cmsg);
                continue;
            }
            var (ok, msg) = Core.AbilityRules.SetAbilityField(ability, pf, pv);
            results.Add(msg);
            if (ok)
            {
                if (IsBakedTuningField(pfl)) anyBaked = true;
                if (pfl.Contains("charge")) chargeTouched = true;
                if (pfl is "cooldown" or "cd") cooldownTouched = true;
            }
        }
        ctx.Reply(string.Join("  |  ", results));

        // Re-apply baked cast tuning ONCE if any baked-tuning field changed (capture/availability rules
        // like enabled/weapons/forms don't need a re-tune).
        if (anyBaked && Beelzebub.Config.Settings.Abilities_ApplyConfig.Value)
            AbilityTuningService.ReapplyAll();   // v0.120.0: restore-then-reapply (lowering/clearing applies live)
        // Advisory notes (charge system vs cooldown), same as the single-set path.
        if (chargeTouched && !AbilityTuningService.AbilityChainHasCharges(ability))
            ctx.Reply("NOTE: this ability has no charge system, so 'charges'/'chargetime' won't apply (only abilities that already use charges, e.g. dashes).");
        if (cooldownTouched && AbilityTuningService.AbilityChainHasCharges(ability))
            ctx.Reply("NOTE: this ability is charge-based — its delay is the charge RECHARGE, not a cooldown. Use 'chargetime' instead.");
        Audit(ctx, "ability-set", 0, ability ?? "", string.Join(",", pairs.ConvertAll(x => $"{x.field}={x.value}")));
    }

    [Command("ability-set", description: "BULK-set many ability config fields in ONE command. Group each setting as (field=value) — or separate with ';' — so values can contain spaces/commas (weapon lists, notes) without breaking. Usage: .beelz admin ability-set <name|id> \"(cooldown=30)(weapons=sword,axe)(reviewstatus=Approved)(notes=big strong nuke)\"  OR  .beelz admin ability-set <id> \"cooldown=30; range=50; reviewstatus=Approved\". WRAP the whole list in \"quotes\" if any value has spaces. Same field names as .beelz admin ability (enabled, weapons, forms, transformonly, difficulty, phase, cooldown, range, charges, chargetime, aoe, projspeed, duration, healing, summoncap, summontimeout, summonunits, forcetimeout, category, reviewstatus, reviewtag, condition, interruptible, interruptonhit, freemove, freelymove, castspeed, notes).", adminOnly: true)]
    public static void AbilitySetBulk(ChatCommandContext ctx, string ability, string configs)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (string.IsNullOrWhiteSpace(ability)) { ctx.Reply("Provide the ability name or id, then the config list."); return; }
        if (string.IsNullOrWhiteSpace(configs))
        {
            ctx.Reply("Usage: .beelz admin ability-set <id> \"(field=value)(field2=value2)...\"  —  or  \"field=value; field2=value2\". Wrap in \"quotes\" if any value has spaces. Fields: same as .beelz admin ability.");
            return;
        }
        var pairs = ParseConfigBlob(configs, out string parseErr);
        if (parseErr != null) { ctx.Reply(parseErr); return; }
        if (pairs.Count == 0) { ctx.Reply("No field=value pairs found. Use (field=value)(field2=value2) or field=value; field2=value2."); return; }
        ApplyAbilityFieldPairs(ctx, ability, pairs);
    }

    /// <summary>
    /// v0.116.0: parse a bulk-config blob into (field,value) pairs. Two forms (mutually compatible):
    ///   - PARENTHESES: <c>(field=value)(field=value)</c> — each group is one field, so commas/spaces inside a
    ///     value are preserved verbatim (weapon lists, multi-word notes). Preferred — unambiguous.
    ///   - SEMICOLON:   <c>field=value; field=value</c> — split on ';' only, so a comma stays inside a value
    ///     (e.g. weapons=sword,axe). The value is everything after the first '='.
    /// Returns the pairs; sets <paramref name="error"/> (and returns what parsed) on a malformed token.
    /// </summary>
    static System.Collections.Generic.List<(string field, string value)> ParseConfigBlob(string blob, out string error)
    {
        error = null;
        var pairs = new System.Collections.Generic.List<(string, string)>();
        blob = (blob ?? "").Trim();
        System.Collections.Generic.List<string> tokens = new();
        if (blob.Contains("("))
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(blob, @"\(([^)]*)\)");
            if (matches.Count == 0) { error = "Couldn't parse any (field=value) groups. Example: (cooldown=30)(range=50)."; return pairs; }
            foreach (System.Text.RegularExpressions.Match m in matches) tokens.Add(m.Groups[1].Value);
        }
        else
        {
            foreach (var t in blob.Split(';')) tokens.Add(t);
        }
        foreach (var raw in tokens)
        {
            var s = raw.Trim();
            if (s.Length == 0) continue;
            int eq = s.IndexOf('=');
            if (eq <= 0) { error = $"Bad token '{s}' — expected field=value (e.g. cooldown=30)."; return pairs; }
            pairs.Add((s.Substring(0, eq).Trim(), s.Substring(eq + 1).Trim()));
        }
        return pairs;
    }

    /// <summary>
    /// v0.95.0: does this ability field produce a BAKED prefab edit that <see cref="AbilityTuningService.ApplyAll"/>
    /// re-applies? Only these warrant a live re-tune; capture/availability rules (enabled/weapons/forms/…) and
    /// scale fields applied at cast time do not — re-tuning on them is wasted work + log noise.
    /// </summary>
    static bool IsBakedTuningField(string f) => f switch
    {
        "cooldown" or "cd" or "range" or "charges" or "chargetime" or "aoe" or "projspeed"
        or "duration" or "effectduration" or "healing" or "healmult" or "healingmultiplier"
        or "forcetimeout" or "freelymove" or "interruptonhit" or "interruptible"
        or "freemove" or "castspeed" => true,
        _ => false,
    };

    [Command("transform-set", description: "Set a per-unit transform rule live. Usage: .beelz admin transform-set <CHAR_unit> <field> <value>. Fields: enabled, difficulty, tier, damagescale, cooldownscale, healthscale, speedscale, fullreplace, powerscalingmode, notes. (SlotTemplate is edited in ability_rules.json.)", adminOnly: true)]
    public static void TransformSet(ChatCommandContext ctx, string unit, string field, string value)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var (ok, msg) = Core.AbilityRules.SetTransformField(unit, field, value);
        ctx.Reply(msg);
        if (ok) Audit(ctx, "transform-set", 0, unit ?? "", $"{field}={value}");
    }

    [Command("default", description: "Set a server-wide scaling default for abilities with no per-ability override. Usage: .beelz admin default <damagescale|cooldownscale> <value>.", adminOnly: true)]
    public static void DefaultSet(ChatCommandContext ctx, string field, string value)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var (ok, msg) = Core.AbilityRules.SetDefault(field, value);
        ctx.Reply(msg);
        if (ok) Audit(ctx, "default-set", 0, field ?? "", value ?? "");
    }

    [Command("denyguid", description: "Add/remove an ability GUID on the capture DENY list. Usage: .beelz admin denyguid <add|remove> <guid>.", adminOnly: true)]
    public static void DenyGuid(ChatCommandContext ctx, string action, int guid)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        string a = (action ?? "").Trim().ToLowerInvariant();
        if (a is "add" or "+") ctx.Reply(Core.AbilityRules.AddDenyGuid(guid) ? $"Added deny-guid {guid}." : $"GUID {guid} already on the deny list.");
        else if (a is "remove" or "rm" or "-") ctx.Reply(Core.AbilityRules.RemoveDenyGuid(guid) ? $"Removed deny-guid {guid}." : $"GUID {guid} not on the deny list.");
        else ctx.Reply("Usage: .beelz admin denyguid <add|remove> <guid>.");
    }

    [Command("allowguid", description: "Add/remove an ability GUID on the capture ALLOW list (non-empty = exclusive whitelist). Usage: .beelz admin allowguid <add|remove> <guid>.", adminOnly: true)]
    public static void AllowGuid(ChatCommandContext ctx, string action, int guid)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        string a = (action ?? "").Trim().ToLowerInvariant();
        if (a is "add" or "+") ctx.Reply(Core.AbilityRules.AddAllowGuid(guid) ? $"Added allow-guid {guid}. (Allow list is now an exclusive whitelist.)" : $"GUID {guid} already on the allow list.");
        else if (a is "remove" or "rm" or "-") ctx.Reply(Core.AbilityRules.RemoveAllowGuid(guid) ? $"Removed allow-guid {guid}." : $"GUID {guid} not on the allow list.");
        else ctx.Reply("Usage: .beelz admin allowguid <add|remove> <guid>.");
    }

    [Command("transformonly", description: "Manage the bulk transform-only reservation lists (enforced only when Grant_EnforceTransformOnly is on). A numeric value targets the GUID list, text targets the name-pattern list. Usage: .beelz admin transformonly <add|remove> <pattern|guid>.", adminOnly: true)]
    public static void TransformOnlyRule(ChatCommandContext ctx, string action, string value)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        string a = (action ?? "").Trim().ToLowerInvariant();
        bool add = a is "add" or "+";
        bool rem = a is "remove" or "rm" or "-";
        if (!add && !rem) { ctx.Reply("Usage: .beelz admin transformonly <add|remove> <pattern|guid>."); return; }
        bool isGuid = int.TryParse((value ?? "").Trim(), out int g);
        bool changed = isGuid
            ? (add ? Core.AbilityRules.AddTransformOnlyGuid(g) : Core.AbilityRules.RemoveTransformOnlyGuid(g))
            : (add ? Core.AbilityRules.AddTransformOnlyPattern(value) : Core.AbilityRules.RemoveTransformOnlyPattern(value));
        string what = isGuid ? $"guid {g}" : $"pattern '{value}'";
        ctx.Reply(changed ? $"{(add ? "Added" : "Removed")} transform-only {what}." : $"No change ({what} {(add ? "already present" : "not found")}).");
    }

    [Command("testform", description: "Phase-1 native-form test: drop YOU into wolf/bear form (persistent ExoForm recipe) carrying your current loadout's abilities, to test whether the form HOLDS through casting custom abilities. Usage: .beelz admin testform <wolf|bear|off>", adminOnly: true)]
    public static void TestForm(ChatCommandContext ctx, string form)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        Entity character = ctx.Event.SenderCharacterEntity;
        ulong steamId = character.GetSteamId();
        if (steamId == 0) { ctx.Reply("Could not resolve your Steam ID."); return; }

        string f = (form ?? "").Trim().ToLowerInvariant();
        if (f is "off" or "revert" or "exit")
        {
            var (reverted, _) = Core.Transforms.Revert(steamId, "testform-off");
            ctx.Reply(reverted ? "Exited the form test — your normal bar is restored." : "You're not currently in a form test.");
            return;
        }

        int formGuid = f switch
        {
            "wolf" => -351718282,   // AB_Shapeshift_Wolf_Buff
            "bear" => -1569370346,  // AB_Shapeshift_Bear_Buff
            _ => 0,
        };
        if (formGuid == 0) { ctx.Reply("Usage: .beelz admin testform <wolf|bear|off>."); return; }

        // Build the test ability set: prefer your universal loadout, else your first few captures.
        var set = new System.Collections.Generic.List<int>();
        foreach (var (slot, ag) in Core.AbilityRegistry.GetSlots(steamId).OrderBy(kv => kv.Key))
            if (ag != 0) set.Add(ag);
        if (set.Count == 0)
            foreach (var c in Core.AbilityRegistry.ListFor(steamId))
            {
                set.Add(c.AbilityPrefabGuid);
                if (set.Count >= 6) break;
            }

        var (ok, message) = Core.Transforms.ApplyNativeFormTest(steamId, character, formGuid, set.ToArray(), f == "wolf" ? "Wolf" : "Bear");
        ctx.Reply(message);
        if (ok) Audit(ctx, "testform", steamId, "self", $"form={f} abilities={set.Count}");
    }

    [Command("transform mode", description: "Set transform mode. Usage: .beelz admin transform mode <regular|vblood> <toggle|timed|disabled>", adminOnly: true)]
    public static void TransformMode(ChatCommandContext ctx, string source, string mode)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var s = source.Trim().ToLowerInvariant();
        var m = mode.Trim().ToLowerInvariant();
        if (m != "toggle" && m != "timed" && m != "disabled")
        {
            ctx.Reply("Mode must be one of: toggle, timed, disabled.");
            return;
        }
        var canonical = m == "toggle" ? "Toggle" : m == "timed" ? "Timed" : "Disabled";
        if (s == "regular") { Beelzebub.Config.Settings.Transform_Mode_Regular.Value = canonical; ctx.Reply($"Regular transform mode set to {canonical}."); }
        else if (s == "vblood") { Beelzebub.Config.Settings.Transform_Mode_VBlood.Value = canonical; ctx.Reply($"V-Blood transform mode set to {canonical}."); }
        else ctx.Reply("Source must be 'regular' or 'vblood'.");
    }

    [Command("transform duration", description: "Set Timed-mode duration. Usage: .beelz admin transform duration <regular|vblood> <seconds>", adminOnly: true)]
    public static void TransformDuration(ChatCommandContext ctx, string source, float seconds)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (seconds < 0) { ctx.Reply("Duration must be non-negative."); return; }
        var s = source.Trim().ToLowerInvariant();
        if (s == "regular") { Beelzebub.Config.Settings.Transform_DurationSeconds_Regular.Value = seconds; ctx.Reply($"Regular transform duration: {seconds}s."); }
        else if (s == "vblood") { Beelzebub.Config.Settings.Transform_DurationSeconds_VBlood.Value = seconds; ctx.Reply($"V-Blood transform duration: {seconds}s."); }
        else ctx.Reply("Source must be 'regular' or 'vblood'.");
    }

    [Command("transform cooldown", description: "Set post-revert cooldown. Usage: .beelz admin transform cooldown <regular|vblood> <seconds>", adminOnly: true)]
    public static void TransformCooldown(ChatCommandContext ctx, string source, float seconds)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (seconds < 0) { ctx.Reply("Cooldown must be non-negative."); return; }
        var s = source.Trim().ToLowerInvariant();
        if (s == "regular") { Beelzebub.Config.Settings.Transform_CooldownSeconds_Regular.Value = seconds; ctx.Reply($"Regular transform cooldown: {seconds}s."); }
        else if (s == "vblood") { Beelzebub.Config.Settings.Transform_CooldownSeconds_VBlood.Value = seconds; ctx.Reply($"V-Blood transform cooldown: {seconds}s."); }
        else ctx.Reply("Source must be 'regular' or 'vblood'.");
    }

    [Command("give", description: "Grant a captured ability to a player. Usage: .beelz admin give <player> <unitGuid> <abilityGuid>", adminOnly: true)]
    public static void Give(ChatCommandContext ctx, string player, int unitGuid, int abilityGuid)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Unity.Entities.Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        // v0.117.0: validate the ability GUID resolves to a real ability prefab — refuse to inject a garbage
        // int into the player's collection (it would sit there uncastable). unitGuid is the source label only.
        string giveAbName = (Core.PrefabNames != null && Core.PrefabNames.TryGetValue(abilityGuid, out var _gn))
            ? _gn : new Stunlock.Core.PrefabGUID(abilityGuid).GetPrefabName();
        if (string.IsNullOrEmpty(giveAbName) || giveAbName.IndexOf("Not Found", StringComparison.OrdinalIgnoreCase) >= 0)
        { ctx.Reply($"abilityGuid {abilityGuid} doesn't resolve to a known ability prefab — refusing. Use an ID from .beelz list or .beelz api catalog abilities-all."); return; }
        if (!giveAbName.StartsWith("AB_", StringComparison.OrdinalIgnoreCase))
            ctx.Reply($"Note: {abilityGuid} ('{giveAbName}') is not an AB_ ability group — granting anyway, but verify it's a real castable ability.");

        var source = new Stunlock.Core.PrefabGUID(unitGuid).IsVBloodUnit()
            ? Beelzebub.Services.CaptureSource.VBlood
            : Beelzebub.Services.CaptureSource.Regular;
        bool added = Core.AbilityRegistry.Add(steamId, unitGuid, abilityGuid, source);
        Core.Persistence.RequestSave();

        string abilityName = AbilityDisplay(abilityGuid);
        string unitName = UnitDisplay(unitGuid);
        ctx.Reply(added
            ? $"Granted {abilityName} (from {unitName}, source={source}) to {fullName}."
            : $"{fullName} already has that capture — no change.");
    }

    [Command("give-transform", description: "Grant a TRANSFORMATION unlock to a player. Only units the game can render as a player form qualify (Dracula, Morgana, Werewolf Chieftain, Geomancer/Golem, Tailor/Gargoyle, basic werewolf) — for any other unit use .beelz admin devour to grant its full ability kit. Usage: .beelz admin give-transform <player> <unitGuid>", adminOnly: true)]
    public static void GiveTransform(ChatCommandContext ctx, string player, int unitGuid)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        // v0.44.0: only Dracula & Morgana support a real transformation. For everything else,
        // steer the admin to Devour (grant the kit as abilities).
        if (!Beelzebub.Services.BossFormRegistry.Has(unitGuid))
        {
            ctx.Reply($"{UnitDisplay(unitGuid)} has no player-renderable form, so it can't be a transformation (only units the game can render — Dracula, Morgana, Werewolf, Golem, Gargoyle, basic werewolf). To grant its full kit, use .beelz admin devour {player} {unitGuid}.");
            return;
        }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Unity.Entities.Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        var source = new Stunlock.Core.PrefabGUID(unitGuid).IsVBloodUnit()
            ? Beelzebub.Services.CaptureSource.VBlood
            : Beelzebub.Services.CaptureSource.Regular;
        bool added = Core.AbilityRegistry.AddTransformUnlock(steamId, unitGuid, source);
        Core.Persistence.RequestSave();

        string unitName = UnitDisplay(unitGuid);
        ctx.Reply(added
            ? $"Granted transformation unlock for {unitName} (source={source}) to {fullName}."
            : $"{fullName} already has that transformation unlock — no change.");
    }

    [Command("devour", description: "Grant a player ALL of a unit's eligible abilities at once — the admin equivalent of the rare Devour jackpot, and the alternative to transformation for non-renderable units. Usage: .beelz admin devour <player> <unitGuid>", adminOnly: true)]
    public static void Devour(ChatCommandContext ctx, string player, int unitGuid)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Unity.Entities.Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        var source = new Stunlock.Core.PrefabGUID(unitGuid).IsVBloodUnit()
            ? Beelzebub.Services.CaptureSource.VBlood
            : Beelzebub.Services.CaptureSource.Regular;
        int learned = Beelzebub.Services.DevourService.Devour(steamId, new Stunlock.Core.PrefabGUID(unitGuid), source);
        Core.Persistence.RequestSave();

        string unitName = UnitDisplay(unitGuid);
        ctx.Reply(learned > 0
            ? $"Devoured {unitName} for {fullName}: granted {learned} new ability(ies) (source={source}). They can slot them with .beelz grant."
            : $"{fullName} already had all of {unitName}'s eligible abilities (or it has none capturable).");
        Audit(ctx, "devour", steamId, fullName, $"unit={unitGuid} ({unitName}) granted={learned}");
    }

    [Command("difficulty", description: "Set or show the server's difficulty mode for TX4 gating. Usage: .beelz admin difficulty [basic|brutal]", adminOnly: true)]
    public static void Difficulty(ChatCommandContext ctx, string mode = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (string.IsNullOrWhiteSpace(mode))
        {
            string current = AbilityRules.GetServerDifficulty();
            ctx.Reply($"Server difficulty mode: {current}. Brutal-tagged abilities + transforms are " +
                      (current == "Brutal" ? "AVAILABLE." : "BLOCKED."));
            return;
        }
        var canonical = mode.Trim();
        if (string.Equals(canonical, "Brutal", StringComparison.OrdinalIgnoreCase)) canonical = "Brutal";
        else if (string.Equals(canonical, "Basic", StringComparison.OrdinalIgnoreCase)) canonical = "Basic";
        else { ctx.Reply("Mode must be Basic or Brutal."); return; }

        Beelzebub.Config.Settings.Server_DifficultyMode.Value = canonical;
        ctx.Reply($"Server difficulty mode set to {canonical}. Future captures + transform unlocks gate accordingly.");
    }

    [Command("transform show", description: "Show current transform settings.", adminOnly: true)]
    public static void TransformShow(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var s = new System.Text.StringBuilder();
        s.AppendLine("Transform settings:");
        s.Append("  Regular: mode=").Append(Beelzebub.Config.Settings.Transform_Mode_Regular.Value)
            .Append(", duration=").Append(Beelzebub.Config.Settings.Transform_DurationSeconds_Regular.Value).Append("s")
            .Append(", cooldown=").Append(Beelzebub.Config.Settings.Transform_CooldownSeconds_Regular.Value).Append("s").AppendLine();
        s.Append("  V-Blood: mode=").Append(Beelzebub.Config.Settings.Transform_Mode_VBlood.Value)
            .Append(", duration=").Append(Beelzebub.Config.Settings.Transform_DurationSeconds_VBlood.Value).Append("s")
            .Append(", cooldown=").Append(Beelzebub.Config.Settings.Transform_CooldownSeconds_VBlood.Value).Append("s");
        ctx.Reply(s.ToString());
    }

    // --- AT1: inspect another player's full state ---

    [Command("inspect", description: "View another player's Beelzebub state. Usage: .beelz admin inspect <player>", adminOnly: true)]
    public static void Inspect(ChatCommandContext ctx, string player)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        var sb = new StringBuilder();
        sb.Append("=== ").Append(fullName).Append(" (SteamID ").Append(steamId).Append(") ===").AppendLine();

        var captured = Core.AbilityRegistry.ListFor(steamId);
        int vbloodCount = captured.Count(c => c.Source == CaptureSource.VBlood);
        sb.Append("Captures: ").Append(captured.Count).Append(" total")
            .Append(" (").Append(vbloodCount).Append(" V-Blood, ").Append(captured.Count - vbloodCount).Append(" regular)").AppendLine();

        var universal = Core.AbilityRegistry.GetSlots(steamId);
        sb.Append("Universal slots bound: ").Append(universal.Count).AppendLine();
        var weaponSlots = Core.AbilityRegistry.AllWeaponSlots(steamId);
        if (weaponSlots.Count > 0)
        {
            foreach (var (weapon, slots) in weaponSlots.OrderBy(kv => kv.Key.ToString()))
            {
                sb.Append("  ").Append(weapon).Append(" slots: ").Append(slots.Count).AppendLine();
            }
        }

        var transforms = Core.AbilityRegistry.ListTransforms(steamId);
        int vbloodTxCount = transforms.Count(t => t.Source == CaptureSource.VBlood);
        sb.Append("Transform unlocks: ").Append(transforms.Count)
            .Append(" (").Append(vbloodTxCount).Append(" V-Blood, ").Append(transforms.Count - vbloodTxCount).Append(" regular)").AppendLine();

        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active != null)
        {
            sb.Append("  ACTIVE: ").AppendLine(UnitDisplay(active.UnitPrefabGuid));
        }

        int hotkeyCount = Core.AbilityRegistry.HotkeyCount(steamId);
        if (hotkeyCount > 0)
        {
            sb.Append("Hotkeys bound: ").Append(hotkeyCount).AppendLine();
        }

        var verbosity = Core.AbilityRegistry.GetVerbosity(steamId, Verbosity.Summary);
        bool bchOn = Core.AbilityRegistry.GetEmitApiEvents(steamId);
        sb.Append("Verbosity: ").Append(verbosity).Append(", BCH events: ").Append(bchOn ? "on" : "off");

        ctx.Reply(sb.ToString());
        Audit(ctx, "inspect", steamId, fullName, "");
    }

    // v0.131.0 — known "stuck state" buffs that survive death/relog (no LifeTime + Persists_Through_Death /
    // Immaterial composites). These are what leave a player permanently invisible/phased after casting a
    // captured NPC ability (Spider Baneling HideCorpse, Gaius Corpse Buff, etc.). `cleanse` strips them.
    static readonly System.Collections.Generic.HashSet<int> _stuckStateBuffGuids = new()
    {
        1160901934,   // Buff_General_HideCorpse (Spider Baneling Explode Poison) — permanent invisibility
        -485230865,   // Undead_AreanaChampion_CorpseBuff (Gaius) — Immaterial/GhostMode/Immortal composite
    };
    static readonly string[] _stuckStateBuffNames = {
        "HideCorpse", "Invisible", "Immaterial", "GhostMode", "Corpse",
        "Camouflage", "Stealth", "Shroud", "Cloak",
    };

    [Command("cleanse", description: "RECOVERY: strip stuck STATE buffs from a player — fixes a character stuck invisible/phased/immaterial after casting an ability (survives respawn + relog). With no buff arg, removes the known stuck-state buffs (HideCorpse/Corpse/Invisible/Immaterial/Camouflage/Stealth). Pass a buff NAME-substring or GUID to strip a specific one. Usage: .beelz admin cleanse <player> [buffNameOrGuid]", adminOnly: true)]
    public static void Cleanse(ChatCommandContext ctx, string player = null, string buff = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        Entity character; ulong steamId; string fullName;
        if (string.IsNullOrWhiteSpace(player))
        {
            character = ctx.Event.SenderCharacterEntity; steamId = character.GetSteamId(); fullName = "you";
        }
        else
        {
            character = EntityExtensions.FindCharacterByName(player, out steamId, out fullName);
            if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        }
        if (!character.Exists() || !Core.EntityManager.HasBuffer<BuffBuffer>(character))
        { ctx.Reply("No buff buffer on that character (offline or gone)."); return; }

        string target = (buff ?? "").Trim();
        int targetGuid = 0; bool byGuid = int.TryParse(target, out targetGuid);

        // Snapshot the buff entities first (destroying mutates the buffer).
        var toRemove = new System.Collections.Generic.List<(Entity e, string name)>();
        var bb = Core.EntityManager.GetBuffer<BuffBuffer>(character);
        for (int i = 0; i < bb.Length; i++)
        {
            int guid = bb[i].PrefabGuid._Value;
            Entity be = bb[i].Entity;
            string name = bb[i].PrefabGuid.GetPrefabName() ?? "(unknown)";
            bool match;
            if (target.Length > 0)
                match = byGuid ? guid == targetGuid
                               : name.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0;
            else
                match = _stuckStateBuffGuids.Contains(guid)
                        || Array.Exists(_stuckStateBuffNames, p => name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
            if (match && be.Exists()) toRemove.Add((be, name));
        }

        int removed = 0;
        foreach (var (e, name) in toRemove)
        {
            try
            {
                if (!e.Exists() || e.Has<DestroyTag>()) continue;
                DestroyUtility.Destroy(Core.EntityManager, e, DestroyDebugReason.TryRemoveBuff);
                removed++;
                Core.Log.LogInfo($"[Beelz CLEANSE] {fullName} ({steamId}): removed buff {name} (#{e.GetPrefabGuid()._Value}).");
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz CLEANSE] failed to remove {name}: {ex.Message}"); }
        }

        if (removed == 0)
            ctx.Reply(target.Length > 0
                ? $"No buff matching '{target}' found on {fullName}. Run .beelz admin buffs {(string.IsNullOrWhiteSpace(player) ? "" : player)} to see their buffs, then cleanse by name/GUID."
                : $"No known stuck-state buffs found on {fullName}. Run .beelz admin buffs {(string.IsNullOrWhiteSpace(player) ? "" : player)} to identify the buff, then: .beelz admin cleanse {(string.IsNullOrWhiteSpace(player) ? "<you>" : player)} <buffNameOrGuid>.");
        else
            ctx.Reply($"Cleansed {removed} buff(s) from {fullName}. If they're still affected, equip/swap a weapon or relog to refresh, or run .beelz admin buffs {(string.IsNullOrWhiteSpace(player) ? "" : player)} to find a remaining one.");
        Audit(ctx, "cleanse", steamId, fullName, $"target='{target}' removed={removed}");
    }

    [Command("buffs", description: "DIAGNOSTIC: dump a player's active buffs and which ones override ability slots, to the server log (with a chat summary). Use to find what's driving a stuck ability bar. Usage: .beelz admin buffs [player] (default: you)", adminOnly: true)]
    public static void Buffs(ChatCommandContext ctx, string player = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        Entity character;
        ulong steamId;
        string fullName;
        if (string.IsNullOrWhiteSpace(player))
        {
            character = ctx.Event.SenderCharacterEntity;
            steamId = character.GetSteamId();
            fullName = "you";
        }
        else
        {
            character = EntityExtensions.FindCharacterByName(player, out steamId, out fullName);
            if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        }

        if (!character.Exists() || !Core.EntityManager.HasBuffer<BuffBuffer>(character))
        {
            ctx.Reply("No buff buffer on that character.");
            return;
        }

        // v0.43.10: dump the CHARACTER ENTITY's own prefab — the decisive check for a
        // server-side shapeshift. A normal vampire reads CHAR_Vampire_*; if this shows a
        // creature/bear prefab, the player is genuinely shapeshifted server-side (and the
        // fix must operate on this entity), not just a stale client HUD.
        var charPg = character.GetPrefabGuid();
        Core.Log.LogInfo($"[Beelz BUFFS] character entity prefab = {charPg._Value} {charPg.GetPrefabName()}");

        // v0.43.11: dump the character's OWN equipped-ability slots (the persistent base
        // the cast resolves from). If a shapeshift baked creature abilities in here and the
        // form buff was removed abnormally (logout w/o revert), these stick — and normal
        // equipment/spellbook changes won't overwrite them. This is what a "frozen bar" looks like.
        if (Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(character))
        {
            var slotBuf = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(character);
            Core.Log.LogInfo($"[Beelz BUFFS] character AbilityGroupSlotBuffer ({slotBuf.Length} slot(s)):");
            for (int s = 0; s < slotBuf.Length; s++)
            {
                int ag = slotBuf[s].BaseAbilityGroupOnSlot._Value;
                Core.Log.LogInfo($"[Beelz BUFFS]   baseSlot[{s}] = {ag} {(ag == 0 ? "(empty)" : new Stunlock.Core.PrefabGUID(ag).GetPrefabName())}");
            }
        }
        else
        {
            Core.Log.LogInfo($"[Beelz BUFFS] character has NO AbilityGroupSlotBuffer.");
        }

        var buffs = Core.EntityManager.GetBuffer<BuffBuffer>(character);
        int total = buffs.Length, overriders = 0;
        bool hasBearShapeshift = false, hasCarrier = false;
        Core.Log.LogInfo($"[Beelz BUFFS] === {fullName} (SteamID {steamId}) has {total} buff(s) ===");
        for (int i = 0; i < buffs.Length; i++)
        {
            int guid = buffs[i].PrefabGuid._Value;
            if (guid == -1569370346) hasBearShapeshift = true; // AB_Shapeshift_Bear_Buff
            if (guid == 1171608023) hasCarrier = true;          // Beelzebub ability carrier
            string name = buffs[i].PrefabGuid.GetPrefabName() ?? "(unknown)";
            Entity be = buffs[i].Entity;
            string overrideInfo = "";
            if (be.Exists() && Core.EntityManager.HasBuffer<ReplaceAbilityOnSlotBuff>(be))
            {
                var ovr = Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(be);
                var sbo = new StringBuilder();
                int entries = 0;
                for (int j = 0; j < ovr.Length; j++)
                {
                    var e = ovr[j];
                    if (e.NewGroupId._Value == 0) continue;
                    entries++;
                    sbo.Append($" [slot{e.Slot}={AbilityDisplay(e.NewGroupId._Value)}#{e.NewGroupId._Value}/p{e.Priority}/{e.Target}]");
                }
                if (entries > 0) { overriders++; overrideInfo = " OVERRIDES:" + sbo; }
            }
            Core.Log.LogInfo($"[Beelz BUFFS]   #{i} guid={guid} {name}{overrideInfo}");
        }
        // v0.43.12: hunt ALL player-owned ability-slot override SOURCES — including ones NOT in
        // the BuffBuffer (the orphan that can freeze the bar). This is what actually drives the
        // resolved bar via ReplaceAbilityOnSlotSystem.
        try
        {
            EntityQuery q = Core.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ReplaceAbilityOnSlotBuff>(),
                ComponentType.ReadOnly<EntityOwner>());
            var arr = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            int ownedSrc = 0;
            Core.Log.LogInfo($"[Beelz BUFFS] --- owned ReplaceAbilityOnSlot sources ---");
            for (int i = 0; i < arr.Length; i++)
            {
                Entity e = arr[i];
                if (!e.Exists() || !e.TryGetComponent<EntityOwner>(out var o) || o.Owner != character) continue;
                ownedSrc++;
                var pg = e.GetPrefabGuid();
                var ob = Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(e);
                var sbo = new StringBuilder();
                for (int j = 0; j < ob.Length; j++)
                {
                    if (ob[j].NewGroupId._Value == 0) continue;
                    sbo.Append($" [slot{ob[j].Slot}={AbilityDisplay(ob[j].NewGroupId._Value)}#{ob[j].NewGroupId._Value}/p{ob[j].Priority}]");
                }
                Core.Log.LogInfo($"[Beelz BUFFS]   src {pg._Value} {pg.GetPrefabName()}{sbo}");
            }
            arr.Dispose();
            Core.Log.LogInfo($"[Beelz BUFFS] --- {ownedSrc} owned source(s) ---");
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz BUFFS] owned-source query failed: {ex.Message}"); }

        Core.Log.LogInfo($"[Beelz BUFFS] === end ({overriders} override slots) | charPrefab={charPg.GetPrefabName()} bearShapeshiftBuff={hasBearShapeshift} carrierBuff={hasCarrier} ===");
        ctx.Reply($"Dumped {total} buff(s) for {fullName} to the server log; {overriders} override ability slots; entity prefab = {charPg.GetPrefabName()}. See BepInEx LogOutput.log lines tagged [Beelz BUFFS].");
        Audit(ctx, "buffs", steamId, fullName, $"total={total} overriders={overriders} charPrefab={charPg._Value} bear={hasBearShapeshift} carrier={hasCarrier}");
    }

    [Command("rebuildbar", description: "Force a player's ability bar to rebuild from scratch via the engine's init-state (fixes a bar frozen at the resolved level, e.g. stuck after a shapeshift). Usage: .beelz admin rebuildbar [player] (default: you)", adminOnly: true)]
    public static void RebuildBar(ChatCommandContext ctx, string player = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        Entity character;
        ulong steamId;
        string fullName;
        if (string.IsNullOrWhiteSpace(player))
        {
            character = ctx.Event.SenderCharacterEntity;
            steamId = character.GetSteamId();
            fullName = "you";
        }
        else
        {
            character = EntityExtensions.FindCharacterByName(player, out steamId, out fullName);
            if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        }

        bool ok = TransformBuffService.ForceAbilityBarReinit(character);
        ctx.Reply(ok
            ? $"Forced an ability-bar rebuild for {fullName}. If the bar still looks wrong, try equipping a weapon to trigger the rebuild, and tell me — the server log has the details."
            : $"Could not toggle the ability-bar init-state for {fullName} (logged details). Tell me what the server log says under [Beelz] rebuildbar.");
        Audit(ctx, "rebuildbar", steamId, fullName, $"ok={ok}");
    }

    [Command("respawn", description: "Respawn a player's character AT THEIR CURRENT SPOT — V Rising rebuilds the character fresh, which fixes a stuck/frozen ability bar (the bear-form bug). Inventory, equipment, blood, and progress are preserved (same as dying + respawning). Usage: .beelz admin respawn [player] (default: you)", adminOnly: true)]
    public static void Respawn(ChatCommandContext ctx, string player = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        Entity character;
        ulong steamId;
        string fullName;
        if (string.IsNullOrWhiteSpace(player))
        {
            character = ctx.Event.SenderCharacterEntity;
            steamId = character.GetSteamId();
            fullName = "you";
        }
        else
        {
            character = EntityExtensions.FindCharacterByName(player, out steamId, out fullName);
            if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        }

        // First clear any Beelzebub state so the rebuilt character starts clean.
        try { Core.Transforms.Revert(steamId, "respawn", restoreBar: false); } catch { /* not transformed */ }
        try { TransformBuffService.RemoveAllFormsAndShapeshifts(character); } catch { /* best effort */ }

        try
        {
            if (!character.TryGetComponent<PlayerCharacter>(out var pc))
            {
                ctx.Reply("That entity isn't a player character."); return;
            }
            Entity userEntity = pc.UserEntity;
            var pos = Core.EntityManager.GetComponentData<Unity.Transforms.LocalToWorld>(character).Position;
            var spawnLoc = new Il2CppSystem.Nullable_Unboxed<Unity.Mathematics.float3> { value = pos };

            var sbs = Core.Server.GetExistingSystemManaged<ServerBootstrapSystem>();
            var bufferSystem = Core.Server.GetExistingSystemManaged<Unity.Entities.EntityCommandBufferSystem>();
            var buffer = bufferSystem.CreateCommandBuffer();
            sbs.RespawnCharacter(buffer, userEntity, customSpawnLocation: spawnLoc, previousCharacter: character);

            Core.Log.LogInfo($"[Beelz] respawn: RespawnCharacter requested for {fullName} ({steamId}) at current position to rebuild a clean character + ability bar.");
            ctx.Reply($"Respawning {fullName} on the spot to rebuild the character — this clears a stuck/frozen ability bar. Equipment + progress are preserved. Give it a moment, then check your bar.");
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] respawn failed for {fullName}: {ex}");
            ctx.Reply($"Respawn failed: {ex.Message}");
        }
        Audit(ctx, "respawn", steamId, fullName, "");
    }

    [Command("unmount", description: "RECOVERY: free a player stuck on a summoned mount (e.g. the Sir Erwin 'Militia Fabian Mountup' crash) — strips the mount/rider buffs, despawns the orphaned steed near them, and re-applies their Beelz grants. Safe to run repeatedly; if the bar still looks stuck afterward, follow with .beelz admin respawn. Usage: .beelz admin unmount [player] (default: you)", adminOnly: true)]
    public static void Unmount(ChatCommandContext ctx, string player = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        Entity character;
        ulong steamId;
        string fullName;
        if (string.IsNullOrWhiteSpace(player))
        {
            character = ctx.Event.SenderCharacterEntity;
            steamId = character.GetSteamId();
            fullName = "you";
        }
        else
        {
            character = EntityExtensions.FindCharacterByName(player, out steamId, out fullName);
            if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        }
        if (!character.Exists()) { ctx.Reply("That character no longer exists."); return; }

        var (buffs, steeds, grants) = MountRecovery.Recover(character);
        ctx.Reply($"Unmount recovery for {fullName}: stripped {buffs} mount/rider buff(s), despawned {steeds} steed(s), re-applied {grants} grant(s)."
            + (buffs == 0 && steeds == 0 ? " (Nothing mount-related found — if the bar is still stuck, try .beelz admin respawn.)" : " If the bar still looks stuck, run .beelz admin respawn."));
        Audit(ctx, "unmount", steamId, fullName, $"buffs={buffs} steeds={steeds} grants={grants}");
    }

    [Command("testmount", description: "STAGE-2 TEST: spawn a rideable horse next to a player to test whether mounting coexists with Beelz granted abilities WITHOUT crashing the server. Have some Beelz abilities slotted, walk onto the horse and press your mount key, then ride + cast a spell and watch whether the server stays up. 'off' removes the horse and frees you. Usage: .beelz admin testmount [on|off] [player]", adminOnly: true)]
    public static void TestMount(ChatCommandContext ctx, string mode = "on", string player = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        // Allow ".beelz admin testmount <player>" (mode omitted) by treating a non-keyword first arg as the player.
        string m = (mode ?? "on").Trim().ToLowerInvariant();
        if (m != "on" && m != "off") { player = mode; m = "on"; }

        Entity character;
        ulong steamId;
        string fullName;
        if (string.IsNullOrWhiteSpace(player))
        {
            character = ctx.Event.SenderCharacterEntity;
            steamId = character.GetSteamId();
            fullName = "you";
        }
        else
        {
            character = EntityExtensions.FindCharacterByName(player, out steamId, out fullName);
            if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        }
        if (!character.Exists()) { ctx.Reply("That character no longer exists."); return; }

        if (m == "off")
        {
            int b = MountRecovery.StripMountBuffs(character);
            int h = MountRecovery.DespawnNearbyRideables(character);
            int s = MountRecovery.DespawnNearbyFabianSteeds(character);
            int g = SlotApply.RestoreResolvedGrants(character);
            ctx.Reply($"testmount off for {fullName}: removed {h} horse(s){(s > 0 ? $" + {s} steed(s)" : "")}, stripped {b} mount buff(s), re-applied {g} grant(s).");
            Audit(ctx, "testmount", steamId, fullName, $"off horses={h} steeds={s} buffs={b} grants={g}");
            return;
        }

        bool ok = MountRecovery.SpawnTestHorse(character);
        ctx.Reply(ok
            ? $"Spawned a rideable horse next to {fullName}. Walk onto it and press your mount key (V Rising's own handshake). With Beelz abilities slotted, ride around + cast a spell, then tell me if the SERVER stayed up — that's the test. Run '.beelz admin testmount off' to remove it."
            : $"Could not spawn a test horse for {fullName} (see [Beelz MOUNT] in the log).");
        Audit(ctx, "testmount", steamId, fullName, $"on ok={ok}");
    }

    [Command("purge", description: "LAST-RESORT RECOVERY: wipe ALL of Beelzebub's action-bar integration back to vanilla — clears the deep engine MODIFICATION LEAK (a creature kit stuck on the bar that survives relog/respawn/resetbar), ends + un-parks any transform, and removes every slot/form/weapon/hotkey/loadout binding. KEEPS the player's captured abilities + transform unlocks. Use when a bar is stuck and nothing else worked. Usage: .beelz admin purge <player> CONFIRM", adminOnly: true)]
    public static void Purge(ChatCommandContext ctx, string player = null, string confirm = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        Entity character;
        ulong steamId;
        string fullName;
        if (string.IsNullOrWhiteSpace(player))
        {
            character = ctx.Event.SenderCharacterEntity;
            steamId = character.GetSteamId();
            fullName = "you";
        }
        else
        {
            character = EntityExtensions.FindCharacterByName(player, out steamId, out fullName);
            if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        }

        if (!string.Equals(confirm?.Trim(), "CONFIRM", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Reply($"This wipes ALL Beelzebub bar integration for {fullName} back to vanilla (transform, every slot/form/weapon/hotkey/loadout binding, AND the deep engine modification leak) — their captured abilities + transform unlocks are KEPT. Re-run: .beelz admin purge {(string.IsNullOrWhiteSpace(player) ? "you" : player)} CONFIRM");
            return;
        }

        if (!character.Exists() || !Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(character))
        {
            ctx.Reply("That character has no ability-slot buffer (offline or gone). The player must be ONLINE for a purge — it operates on their live character.");
            return;
        }

        int keptCaptures = Core.AbilityRegistry.ListFor(steamId).Count;
        int keptTransforms = Core.AbilityRegistry.ListTransforms(steamId).Count;

        int loadouts = 0, hotkeys = 0, forms = 0, orphans = 0;
        (int scanned, int removedById, int sourcesDestroyed, int forced) reg = (0, 0, 0, 0);
        try
        {
            // (1) End + UN-PARK any transform (parked = the disconnect-grace record that would otherwise
            // restore the form on reconnect). Revert with restoreBar:false (purge wants a vanilla bar, not
            // re-applied grants), then ClearActiveTransform to drop a parked/disconnected record too.
            try { Core.Transforms.Revert(steamId, "admin purge", restoreBar: false); } catch { /* not transformed */ }
            Core.AbilityRegistry.ClearActiveTransform(steamId);
            Core.AbilityRegistry.ClearTransformCooldowns(steamId);

            // (2) Clear every Beelzebub slot binding (universal + per-weapon + per-form), custom transform
            // loadouts and the slot baseline. KEEPS captures + unlocks.
            loadouts = Core.AbilityRegistry.ClearAllLoadouts(steamId);

            // (3) Clear all custom hotkeys.
            foreach (var name in Core.AbilityRegistry.ListHotkeys(steamId).Keys.ToList())
                if (Core.AbilityRegistry.ClearHotkey(steamId, name)) hotkeys++;

            // (4) Strip any lingering form/shapeshift buffs, then destroy player-owned override SOURCES
            // (orphaned ReplaceAbilityOnSlotBuff carriers) so their modifications become removable.
            forms = TransformBuffService.RemoveAllFormsAndShapeshifts(character);
            orphans = TransformBuffService.DestroyOwnedAbilitySlotOrphans(character);

            // (5) THE DEEP FIX: clear the leaked AbilityGroupSlot modifications at the engine registry
            // level (pop each by id via RemoveAbilityGroupModificationOnSlot + sweep loose orphans),
            // then authoritatively re-resolve the bar from equipment + spellbook.
            reg = TransformBuffService.PurgeAbilitySlotModifications(character);

            Core.Persistence.RequestSave();
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] purge failed for {fullName}: {ex}");
            ctx.Reply($"purge failed: {ex.Message} (some steps may have applied — check the server log under [Beelz PURGE]).");
            return;
        }

        Core.Log.LogInfo($"[Beelz PURGE] {fullName} ({steamId}): loadouts={loadouts} hotkeys={hotkeys} forms={forms} orphanSources={orphans} | registry scanned={reg.scanned} removedById={reg.removedById} sourcesDestroyed={reg.sourcesDestroyed} forced={reg.forced} | kept captures={keptCaptures} transforms={keptTransforms}.");
        // VCF's ctx.Reply caps at FixedString512Bytes (~512 bytes) and THROWS on overflow — keep each
        // line short + ASCII, and split across two replies.
        ctx.Reply($"Purged {fullName}'s bar to vanilla: ended any transform, cleared {loadouts} bind(s)/{hotkeys} hotkey(s)/{forms} form(s), removed {reg.removedById}+{reg.sourcesDestroyed} leaked slot mod(s) across {reg.scanned} slots. Kept {keptCaptures} abilities + {keptTransforms} transform unlock(s).");
        ctx.Reply("Now equip/swap a weapon (or relog) to finish patching, then re-slot with .beelz slot. (Log [Beelz PURGE] has before/after dumps; any 'Could not remove modification id' lines are harmless.)");
        Audit(ctx, "purge", steamId, fullName, $"loadouts={loadouts} hotkeys={hotkeys} forms={forms} orphans={orphans} regRemoved={reg.removedById} sourcesDestroyed={reg.sourcesDestroyed} forced={reg.forced}");
    }

    [Command("clearslotmods", description: "RECOVERY: clear orphaned ability-slot MODIFICATIONS on a player's character (the deep cause of a bar frozen on a creature kit) and force the slots to rebuild from base. Run after .beelz clear if a stuck bar survives everything. Usage: .beelz admin clearslotmods [player] (default: you)", adminOnly: true)]
    public static void ClearSlotMods(ChatCommandContext ctx, string player = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        Entity character;
        ulong steamId;
        string fullName;
        if (string.IsNullOrWhiteSpace(player))
        {
            character = ctx.Event.SenderCharacterEntity;
            steamId = character.GetSteamId();
            fullName = "you";
        }
        else
        {
            character = EntityExtensions.FindCharacterByName(player, out steamId, out fullName);
            if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        }

        if (!character.Exists() || !Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(character))
        {
            ctx.Reply("No ability-slot buffer on that character.");
            return;
        }

        int slots = 0, withMods = 0, modsCleared = 0, dirtied = 0;
        try
        {
            var dirtyTag = Unity.Entities.ComponentType.ReadWrite(Il2CppInterop.Runtime.Il2CppType.Of<AbilityGroupSlot.DirtyTag>());

            // Snapshot the slot entities first — adding components / clearing buffers below can
            // cause structural changes that invalidate the character's live buffer handle.
            var slotEntities = new System.Collections.Generic.List<Entity>();
            var slotBuffer = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(character);
            for (int i = 0; i < slotBuffer.Length; i++)
            {
                Entity se = slotBuffer[i].GroupSlotEntity._Entity;
                if (se != Entity.Null) slotEntities.Add(se);
            }

            foreach (Entity slotEntity in slotEntities)
            {
                if (!slotEntity.Exists()) continue;
                slots++;

                if (Core.EntityManager.HasBuffer<AbilityGroupSlotModificationBuffer>(slotEntity))
                {
                    var modBuf = Core.EntityManager.GetBuffer<AbilityGroupSlotModificationBuffer>(slotEntity);
                    if (modBuf.Length > 0)
                    {
                        withMods++;
                        modsCleared += modBuf.Length;
                        Core.Log.LogInfo($"[Beelz CLEARMODS] slot entity {slotEntity} had {modBuf.Length} modification(s) — clearing.");
                        modBuf.Clear();
                    }
                }

                if (!Core.EntityManager.HasComponent(slotEntity, dirtyTag))
                {
                    Core.EntityManager.AddComponent(slotEntity, dirtyTag);
                    dirtied++;
                }
            }

            if (Core.ReplaceAbilityOnSlotSystem != null) Core.ReplaceAbilityOnSlotSystem.OnUpdate();
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] clearslotmods failed for {fullName}: {ex}");
            ctx.Reply($"clearslotmods failed: {ex.Message}");
            return;
        }

        Core.Log.LogInfo($"[Beelz CLEARMODS] {fullName} ({steamId}): {slots} slot(s) scanned, {withMods} had modifications, {modsCleared} cleared, {dirtied} marked dirty for rebuild.");
        ctx.Reply($"Cleared {modsCleared} ability-slot modification(s) across {withMods}/{slots} slot(s) for {fullName} and forced a rebuild. Check your bar — equip/swap a weapon to refresh it if needed.");
        Audit(ctx, "clearslotmods", steamId, fullName, $"slots={slots} withMods={withMods} cleared={modsCleared} dirtied={dirtied}");
    }

    [Command("rebuildslots", description: "RECOVERY (safe): re-sync a player's active ability slots back to their stored base abilities via the engine's slot-setter — fixes a bar stuck on creature/shapeshift abilities. Values only; never destroys entities. Usage: .beelz admin rebuildslots [player] (default: you)", adminOnly: true)]
    public static void RebuildSlots(ChatCommandContext ctx, string player = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        Entity character;
        ulong steamId;
        string fullName;
        if (string.IsNullOrWhiteSpace(player))
        {
            character = ctx.Event.SenderCharacterEntity;
            steamId = character.GetSteamId();
            fullName = "you";
        }
        else
        {
            character = EntityExtensions.FindCharacterByName(player, out steamId, out fullName);
            if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        }

        if (!character.Exists() || !Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(character))
        {
            ctx.Reply("No ability-slot buffer on that character.");
            return;
        }

        int active = 0, resynced = 0, mismatched = 0;
        try
        {
            // Find the equipped-weapon buff — ModifyAbilityGroupOnSlot needs a modification source
            // (Bloodcraft uses the equip buff the same way).
            Entity equipBuff = Entity.Null;
            var bb = Core.EntityManager.GetBuffer<BuffBuffer>(character);
            for (int i = 0; i < bb.Length; i++)
            {
                string n = bb[i].PrefabGuid.GetPrefabName();
                if (n != null && n.StartsWith("EquipBuff_Weapon", StringComparison.OrdinalIgnoreCase)) { equipBuff = bb[i].Entity; break; }
            }
            if (!equipBuff.Exists())
            {
                ctx.Reply("Couldn't find your equipped-weapon buff to drive the re-sync.");
                return;
            }

            // Snapshot the ACTIVE-region slots (0-8): their stored base ability + slot entity.
            // (Structural changes below can invalidate the live buffer handle, so snapshot first.)
            var slotBuffer = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(character);
            int count = System.Math.Min(slotBuffer.Length, 9);
            var snap = new System.Collections.Generic.List<(int idx, Stunlock.Core.PrefabGUID baseAbility, Entity slot)>();
            for (int i = 0; i < count; i++)
                snap.Add((i, slotBuffer[i].BaseAbilityGroupOnSlot, slotBuffer[i].GroupSlotEntity._Entity));

            // DIAGNOSTIC: log active (what it casts) vs base (what it should be) per slot.
            Core.Log.LogInfo($"[Beelz REBUILDSLOTS] {fullName} ({steamId}) active vs base (pre-resync):");
            foreach (var (idx, baseAbility, slot) in snap)
            {
                string activeName = "(none)";
                if (slot.Exists() && Core.EntityManager.HasComponent<AbilityGroupSlot>(slot))
                {
                    var ags = Core.EntityManager.GetComponentData<AbilityGroupSlot>(slot);
                    Entity st = ags.StateEntity._Entity;
                    activeName = st.Exists() ? (st.GetPrefabGuid().GetPrefabName() ?? "?") : "(none)";
                }
                string baseName = baseAbility._Value == 0 ? "(empty)" : (baseAbility.GetPrefabName() ?? "?");
                if (!string.Equals(activeName, baseName)) mismatched++;
                Core.Log.LogInfo($"[Beelz REBUILDSLOTS]   slot[{idx}] active={activeName} base={baseName}");
            }

            // v0.120.0 AUTHORITATIVE RECOVERY (replaces the old base-resync). Writing the stored base back
            // was a NO-OP whenever the base was already correct but the engine's CACHED resolved value
            // (AbilityGroupSlot.StateEntity) stayed pinned to a form ability — the "rebuildslots reported
            // success but the bar stayed stuck (even across relog)" case from the transform-chaining report.
            // All steps are engine-path with NO slot-entity destruction (the dangling-ref crash only ever
            // came from destroying slot ENTITIES, which we never do here):
            //   1) destroy any player-owned override SOURCE (orphaned form/carrier buff still injecting),
            //   2) push every slot to Empty via ModifyAbilityGroupOnSlot — clears the cached StateEntity and
            //      forces a clean re-resolve from equipment + spellbook,
            //   3) re-apply the player's saved Beelzebub grants on the cleaned bar.
            active = snap.Count;
            int orphans = Beelzebub.Services.TransformBuffService.DestroyOwnedAbilitySlotOrphans(character);
            resynced = Beelzebub.Services.TransformBuffService.ForceResetAbilitySlots(character);
            Beelzebub.Services.SlotApply.RestoreResolvedGrants(character);
            Core.Log.LogInfo($"[Beelz REBUILDSLOTS] authoritative recovery: destroyed {orphans} override source(s), force-cleared {resynced} slot(s), re-applied saved grants.");
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] rebuildslots failed for {fullName}: {ex}");
            ctx.Reply($"rebuildslots failed: {ex.Message}");
            return;
        }

        Core.Log.LogInfo($"[Beelz REBUILDSLOTS] {fullName} ({steamId}): authoritative recovery done ({mismatched} slot(s) were mismatched pre-reset).");
        ctx.Reply($"Force-rebuilt the action bar for {fullName}: destroyed lingering override sources, cleared the engine's cached slot values, and re-applied saved grants ({mismatched} slot(s) were stuck pre-reset). No entities destroyed. ⚠️ If the bar is STILL on a creature kit, the value is cached deep in the engine's per-character bar state (it can even survive a relog) and the ONLY reliable cure is to rebuild the character: run \".beelz admin respawn {(string.IsNullOrWhiteSpace(player) ? "" : player)}\" — it respawns on the spot and KEEPS gear, blood, captures & unlocks (NOT a character reset). Details in the server log under [Beelz REBUILDSLOTS].");
        Audit(ctx, "rebuildslots", steamId, fullName, $"active={active} resynced={resynced} mismatched={mismatched}");
    }

    // --- v0.43.20: collection copy/paste (admin backup-and-restore redundancy) ---

    // In-memory admin clipboard for a player's collected captures + transform unlocks. Survives
    // for the server session (the copy -> re-roll -> paste flow happens in one session: the
    // re-roll just kicks + recreates the character, it doesn't restart the server).
    static System.Collections.Generic.List<CapturedAbility> _clipboardCaptures;
    static System.Collections.Generic.List<UnlockedTransform> _clipboardTransforms;
    static string _clipboardSource;

    [Command("copy-collection", description: "Copy a player's captured abilities + transform unlocks into the admin clipboard (to paste onto another character — e.g. as a backup before a re-roll). Usage: .beelz admin copy-collection <player>", adminOnly: true)]
    public static void CopyCollection(ChatCommandContext ctx, string player)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        _clipboardCaptures = Core.AbilityRegistry.ListFor(steamId).ToList();
        _clipboardTransforms = Core.AbilityRegistry.ListTransforms(steamId).ToList();
        _clipboardSource = fullName;

        ctx.Reply($"Copied {_clipboardCaptures.Count} captured ability(ies) + {_clipboardTransforms.Count} transform unlock(s) from {fullName} to the clipboard. Use .beelz admin paste-collection <player> to apply (clipboard lasts until the server restarts).");
        Audit(ctx, "copy-collection", steamId, fullName, $"captures={_clipboardCaptures.Count} transforms={_clipboardTransforms.Count}");
    }

    [Command("paste-collection", description: "Paste the admin clipboard's captured abilities + transform unlocks onto a player (additive — keeps anything they already have). Run .beelz admin copy-collection first. Usage: .beelz admin paste-collection <player>", adminOnly: true)]
    public static void PasteCollection(ChatCommandContext ctx, string player)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (_clipboardCaptures == null && _clipboardTransforms == null)
        {
            ctx.Reply("Clipboard is empty — run .beelz admin copy-collection <player> first.");
            return;
        }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        int abilities = 0, transforms = 0;
        if (_clipboardCaptures != null)
            foreach (var c in _clipboardCaptures)
                if (Core.AbilityRegistry.Add(steamId, c.UnitPrefabGuid, c.AbilityPrefabGuid, c.Source)) abilities++;
        if (_clipboardTransforms != null)
            foreach (var t in _clipboardTransforms)
                if (Core.AbilityRegistry.AddTransformUnlock(steamId, t.UnitPrefabGuid, t.Source)) transforms++;

        Core.Persistence.RequestSave();

        ctx.Reply($"Pasted {abilities} new ability(ies) + {transforms} new transform unlock(s) onto {fullName}" +
            (string.IsNullOrEmpty(_clipboardSource) ? "" : $" (copied from {_clipboardSource})") +
            ". Their existing collection was kept; duplicates were skipped.");
        Audit(ctx, "paste-collection", steamId, fullName, $"newAbilities={abilities} newTransforms={transforms} from={_clipboardSource}");
    }

    [Command("reset-character", description: "Reset a player's CHARACTER to a fresh start: unbinds their Steam ID + kicks them, so they create a NEW character on next login. Their Beelzebub collection (captures/transforms) is preserved (it's tied to their Steam account). The old body stays in-world but unplayable. Requires a confirm token. Usage: .beelz admin reset-character <player> CONFIRM-RESET", adminOnly: true)]
    public static void ResetCharacter(ChatCommandContext ctx, string player, string confirm = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (!string.Equals(confirm, "CONFIRM-RESET", StringComparison.Ordinal))
        {
            ctx.Reply("This UNBINDS the character: the player is kicked and creates a fresh character on next login. Their Beelzebub captures/transforms are KEPT (tied to their Steam account). The old body stays in-world but becomes unplayable. To do it, re-run: .beelz admin reset-character <player> CONFIRM-RESET");
            return;
        }

        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        if (!character.TryGetComponent<PlayerCharacter>(out var pc) || !pc.UserEntity.Exists())
        {
            ctx.Reply("Couldn't resolve that player's user entity.");
            return;
        }
        Entity userEntity = pc.UserEntity;

        // Kick first (best-effort) while the Steam ID is still bound, then UNBIND by zeroing
        // PlatformId on the User (mirrors KindredCommands .unbindplayer). On next login V Rising
        // sees no character bound to the Steam ID and prompts a fresh one. No entity destruction.
        try { KickUser(userEntity); }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz] reset-character: kick failed (will still unbind): {ex.Message}"); }

        try
        {
            var user = Core.EntityManager.GetComponentData<User>(userEntity);
            user.PlatformId = 0;
            Core.EntityManager.SetComponentData(userEntity, user);
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] reset-character unbind failed for {fullName}: {ex}");
            ctx.Reply($"Reset failed: {ex.Message}");
            return;
        }

        Core.Log.LogInfo($"[Beelz] reset-character: unbound {fullName} ({steamId}) — fresh character on next login (Beelzebub collection preserved).");
        ctx.Reply($"Reset {fullName}: Steam ID unbound + kicked. On their next login they'll create a brand-new character; their Beelzebub collection is preserved. (The old body remains in-world but is unplayable. Use .beelz admin paste-collection if you also backed up a copy.)");
        Audit(ctx, "reset-character", steamId, fullName, "unbound");
    }

    /// <summary>Kick a connected user (mirrors KindredCommands Helper.KickPlayer): create a
    /// KickEvent network event. Best-effort; used by reset-character before unbinding.</summary>
    static void KickUser(Entity userEntity)
    {
        var em = Core.EntityManager;
        var user = em.GetComponentData<User>(userEntity);
        if (!user.IsConnected || user.PlatformId == 0) return;

        Entity e = em.CreateEntity(
            Unity.Entities.ComponentType.ReadOnly(Il2CppInterop.Runtime.Il2CppType.Of<NetworkEventType>()),
            Unity.Entities.ComponentType.ReadOnly(Il2CppInterop.Runtime.Il2CppType.Of<SendEventToUser>()),
            Unity.Entities.ComponentType.ReadOnly(Il2CppInterop.Runtime.Il2CppType.Of<KickEvent>()));
        em.SetComponentData(e, new KickEvent { PlatformId = user.PlatformId });
        em.SetComponentData(e, new SendEventToUser { UserIndex = user.Index });
        em.SetComponentData(e, new NetworkEventType
        {
            EventId = NetworkEvents.EventId_KickEvent,
            IsAdminEvent = false,
            IsDebugEvent = false
        });
    }

    // --- AT2: revoke an ability ---

    [Command("revoke", description: "Remove a captured ability from a player. Usage: .beelz admin revoke <player> <unitGuid> <abilityGuid> [reason]", adminOnly: true)]
    public static void Revoke(ChatCommandContext ctx, string player, int unitGuid, int abilityGuid, string reason = "admin")
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        bool removed = Core.AbilityRegistry.Forget(steamId, unitGuid, abilityGuid);
        Core.Persistence.RequestSave();

        string abilityName = AbilityDisplay(abilityGuid);
        string unitName = UnitDisplay(unitGuid);
        if (removed)
        {
            ctx.Reply($"Revoked {abilityName} (from {unitName}) from {fullName}. Reason: {reason}.");
            Audit(ctx, "revoke-ability", steamId, fullName, $"ability={abilityGuid} ({abilityName}) unit={unitGuid} ({unitName}) reason={reason}");
        }
        else
        {
            ctx.Reply($"{fullName} doesn't have that capture — no change.");
        }
    }

    // --- AT3: revoke a transform unlock ---

    [Command("revoke-transform", description: "Remove a transform unlock from a player. Usage: .beelz admin revoke-transform <player> <unitGuid> [reason]", adminOnly: true)]
    public static void RevokeTransform(ChatCommandContext ctx, string player, int unitGuid, string reason = "admin")
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        // If they're currently transformed into this unit, revert first.
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active != null && active.UnitPrefabGuid == unitGuid)
        {
            Core.Transforms.Revert(steamId, "admin revoke");
        }

        bool removed = Core.AbilityRegistry.ForgetTransform(steamId, unitGuid);
        Core.Persistence.RequestSave();

        string unitName = UnitDisplay(unitGuid);
        if (removed)
        {
            ctx.Reply($"Revoked transform unlock for {unitName} from {fullName}. Reason: {reason}.");
            Audit(ctx, "revoke-transform", steamId, fullName, $"unit={unitGuid} ({unitName}) reason={reason}");
        }
        else
        {
            ctx.Reply($"{fullName} doesn't have that transform unlock — no change.");
        }
    }

    // --- AT4: force / clear transform on another player ---

    [Command("force-transform", description: "Force a transformation on another player (bypasses unlock + cooldown). Only player-renderable forms qualify (Dracula, Morgana, Werewolf, Golem, Gargoyle, basic werewolf). Usage: .beelz admin force-transform <player> <unitGuid>", adminOnly: true)]
    public static void ForceTransform(ChatCommandContext ctx, string player, int unitGuid)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        // v0.44.0: only Dracula & Morgana render a real form — guard before creating an unlock.
        if (!Beelzebub.Services.BossFormRegistry.Has(unitGuid))
        {
            ctx.Reply($"{UnitDisplay(unitGuid)} has no player-renderable form (only Dracula, Morgana, Werewolf, Golem, Gargoyle, basic werewolf can be transformed into). To give its kit, use .beelz admin devour.");
            return;
        }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        // v0.120.0: transforms no longer switch in place (TryActivate refuses if already transformed,
        // so chaining can't corrupt the bar). Make the admin override explicit rather than racy:
        // require the target be cleared first.
        if (Core.AbilityRegistry.GetActiveTransform(steamId) is not null)
        {
            ctx.Reply($"{fullName} is already transformed. Run .beelz admin clear-transform {player} first, then force-transform.");
            return;
        }

        // Ensure unlock exists and clear cooldown so TryActivate sails through.
        var source = new PrefabGUID(unitGuid).IsVBloodUnit() ? CaptureSource.VBlood : CaptureSource.Regular;
        Core.AbilityRegistry.AddTransformUnlock(steamId, unitGuid, source);
        Core.AbilityRegistry.ClearTransformCooldowns(steamId);   // v0.100.0: clear any scope's cooldown

        var (ok, message) = Core.Transforms.TryActivate(steamId, unitGuid);
        Core.Persistence.RequestSave();

        string unitName = UnitDisplay(unitGuid);
        if (ok)
        {
            ctx.Reply($"Forced {fullName} into {unitName}. ({message})");
            Audit(ctx, "force-transform", steamId, fullName, $"unit={unitGuid} ({unitName})");
        }
        else
        {
            ctx.Reply($"Force-transform failed: {message}");
        }
    }

    [Command("clear-transform", description: "End another player's active transformation. Usage: .beelz admin clear-transform <player>", adminOnly: true)]
    public static void ClearTransform(ChatCommandContext ctx, string player)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active == null) { ctx.Reply($"{fullName} is not currently transformed."); return; }

        var (reverted, _) = Core.Transforms.Revert(steamId, "admin clear");
        Core.Persistence.RequestSave();

        string unitName = UnitDisplay(active.UnitPrefabGuid);
        if (reverted)
        {
            ctx.Reply($"Cleared {fullName}'s transformation ({unitName}).");
            Audit(ctx, "clear-transform", steamId, fullName, $"unit={active.UnitPrefabGuid} ({unitName})");
        }
        else
        {
            ctx.Reply($"Could not clear {fullName}'s transformation.");
        }
    }

    [Command("desummon", description: "Clean up all Beelzebub-tagged ally summons following a specific player. Includes orphans from previous sessions. Usage: .beelz admin desummon <player>", adminOnly: true)]
    public static void Desummon(ChatCommandContext ctx, string player)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        int tracked = 0;
        // v0.45.0: cover both the transform's summons AND any standalone (untransformed) summons.
        var active = Core.AbilityRegistry.GetSummonOwner(steamId, createIfMissing: false);
        if (active != null && active.SummonedMinions is { Count: > 0 })
        {
            foreach (var m in active.SummonedMinions)
            {
                if (m.Exists()) { SummonAllyService.EnqueueAdminDespawn(m); tracked++; }
            }
            active.SummonedMinions.Clear();
            active.SummonStacks?.Clear();
        }
        Core.AbilityRegistry.ClearStandaloneSummons(steamId);

        int orphans = SummonAllyService.SweepOrphans(character);
        // v0.23.5: immediate drain for admin cleanup. Tries DestroyUtility, then
        // DestroyEntity, then <Disabled> component as fallback. No staged budget —
        // admin commands run in safe state (chat input), no combat-frame pressure.
        int processed = SummonAllyService.DrainAdminQueueImmediate();
        ctx.Reply($"Queued {tracked} tracked + {orphans} orphan summon(s) for {fullName}. Processed {processed} via immediate drain (DestroyUtility / fallback).");
        Audit(ctx, "desummon", steamId, fullName, $"tracked={tracked} orphans={orphans} processed={processed}");
    }

    [Command("desummon-all", description: "Global sweep — destroy every Beelzebub-tagged ally summon on the map regardless of owner. Use to recover from stale state. Usage: .beelz admin desummon-all", adminOnly: true)]
    public static void DesummonAll(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        // Also clear all live tracked SummonedMinions across every summon owner
        // (transforms + standalone untransformed summoners).
        int tracked = 0;
        foreach (var (_, active) in Core.AbilityRegistry.AllSummonOwners())
        {
            if (active.SummonedMinions is { Count: > 0 })
            {
                foreach (var m in active.SummonedMinions)
                {
                    if (m.Exists()) { SummonAllyService.EnqueueAdminDespawn(m); tracked++; }
                }
                active.SummonedMinions.Clear();
                active.SummonStacks?.Clear();
            }
        }

        // Then sweep every BlockFeedBuff+Faction_Players+Follower entity in the world.
        int orphans = SummonAllyService.SweepOrphans(Entity.Null);
        // v0.23.5: immediate drain — multiple destroy paths with <Disabled> fallback.
        int processed = SummonAllyService.DrainAdminQueueImmediate();
        ctx.Reply($"Queued {tracked} tracked + {orphans} orphan ally summon(s) globally. Processed {processed} via immediate drain.");
        ulong adminSteamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Core.Log.LogInfo($"[Beelz AUDIT] admin={adminSteamId} action=desummon-all tracked={tracked} orphans={orphans} processed={processed}");
    }

    // --- R1.4: ability metadata discovery tool ---

    [Command("scan-abilities", description: "Dump per-ability metadata (shipped + ECS-derived + admin policy) to discovered_abilities.json for the admin to audit. Surfaces abilities lacking curated descriptions so they can be filled in. Usage: .beelz admin scan-abilities", adminOnly: true)]
    public static void ScanAbilities(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        // Collect every ability GUID we know about: from every player's captures,
        // from ability_rules.json, and from the global PrefabNames map (anything
        // starting with AB_ that's an AbilityGroup).
        var guids = new System.Collections.Generic.HashSet<int>();

        // Captured abilities across all players.
        foreach (var (_, list) in Core.AbilityRegistry.Snapshot())
        {
            foreach (var c in list) guids.Add(c.AbilityPrefabGuid);
        }

        // Every AB_*_AbilityGroup / AB_*_Group in the global prefab map.
        foreach (var (guid, name) in Core.PrefabNames)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (!name.StartsWith("AB_", StringComparison.OrdinalIgnoreCase)) continue;
            // Only group-level prefabs — casts/buffs would be too noisy.
            if (!name.EndsWith("_AbilityGroup", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith("_Group", StringComparison.OrdinalIgnoreCase)) continue;
            guids.Add(guid);
        }

        var report = new System.Collections.Generic.List<object>();
        int curated = 0, fallbackOnly = 0, incompatible = 0;
        foreach (int guid in guids.OrderBy(g => g))
        {
            var info = Core.AbilityMetadata.Resolve(guid);
            bool isCurated = info.HasShippedEntry || info.HasOverrideEntry;
            if (isCurated) curated++; else fallbackOnly++;
            if (info.Incompatible) incompatible++;

            report.Add(new
            {
                guid,
                prefabName = new PrefabGUID(guid).GetPrefabName(),
                info.Name,
                info.School,
                info.Type,
                info.Description,
                cooldown = info.CooldownSeconds,
                castTime = info.CastTimeSeconds,
                range = info.MaxRange,
                info.BehaviorType,
                info.Incompatible,
                info.IncompatibleReason,
                source = info.HasOverrideEntry ? "override" : info.HasShippedEntry ? "shipped" : "fallback",
            });
        }

        // Write to data folder alongside ability_rules.json.
        var dir = System.IO.Path.GetDirectoryName(Core.AbilityRules.RulesFilePath);
        var path = System.IO.Path.Combine(dir, "discovered_abilities.json");
        try
        {
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(report, opts));
            ctx.Reply($"Scanned {guids.Count} abilities: {curated} curated, {fallbackOnly} fallback-only, {incompatible} marked incompatible.");
            ctx.Reply($"Report: {path}");
            ulong adminSteamId = ctx.Event.SenderCharacterEntity.GetSteamId();
            Core.Log.LogInfo($"[Beelz AUDIT] admin={adminSteamId} action=scan-abilities total={guids.Count} curated={curated} fallback={fallbackOnly} incompatible={incompatible} path={path}");
        }
        catch (Exception ex)
        {
            ctx.Reply($"Failed to write discovered_abilities.json: {ex.Message}");
        }
    }

    // --- AT5: progress overview (admin can inspect any player) ---

    [Command("progress", description: "Show another player's collection progress. Usage: .beelz admin progress <player>", adminOnly: true)]
    public static void Progress(ChatCommandContext ctx, string player)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        BeelzCommands.ShowProgress(ctx, steamId, fullName + "'s");
    }

    // --- AUDIT-4 (v0.20.1): admin remote slot binding ---
    //
    // Bridges the gap identified in the v0.20.0 audit: admins could give-ability and
    // give-transform to a player, but couldn't actually put the ability on the
    // player's spell bar. For "fix my broken loadout" support tickets, the admin
    // previously had to walk the player through `.beelz grant` themselves.

    [Command("set-slot", description: "Bind a player's universal-bucket slot to an ability. Usage: .beelz admin set-slot <player> <slot 0-7> <abilityGuid>", adminOnly: true)]
    public static void SetSlot(ChatCommandContext ctx, string player, int slot, int abilityGuid)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (!Beelzebub.Services.AbilityRegistry.IsValidSlot(slot)) { ctx.Reply("Slot must be 0 (primary), 1-6, or 7 (ultimate)."); return; }

        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        var ability = new PrefabGUID(abilityGuid);
        string abilityName = ability.GetPrefabName();
        // Admin can override the TransformOnly + Enabled guards — they're explicitly
        // forcing this, so we don't refuse the bind. Log it clearly for audit trail.
        bool transformOnly = Core.AbilityRules.IsTransformOnly(abilityName, abilityGuid);
        bool enabled = Core.AbilityRules.IsEnabled(abilityName, abilityGuid);

        Core.AbilityRegistry.SetSlot(steamId, slot, abilityGuid);
        Core.Persistence.RequestSave();

        // If the player is currently online + not transformed, try to apply live.
        bool appliedNow = false;
        if (character.Exists())
        {
            appliedNow = SlotApply.ApplyGrant(character, slot, ability);
        }

        string warnings = "";
        if (transformOnly) warnings += " [WARNING: ability is TransformOnly — fires only during transform]";
        if (!enabled) warnings += " [WARNING: ability has Enabled=false — admin override]";
        string applyHint = appliedNow ? "Applied to spell bar live." : "Will activate next time the player wields a compatible weapon.";

        ctx.Reply($"Bound slot {slot} on {fullName} to {abilityName}. {applyHint}{warnings}");
        Audit(ctx, "set-slot", steamId, fullName, $"slot={slot} ability={abilityGuid} ({abilityName}) appliedNow={appliedNow} transformOnly={transformOnly} enabled={enabled}");
    }

    [Command("set-weapon-slot", description: "Bind a player's weapon-specific slot to an ability. Usage: .beelz admin set-weapon-slot <player> <weapon> <slot 0-7> <abilityGuid>", adminOnly: true)]
    public static void SetWeaponSlot(ChatCommandContext ctx, string player, string weaponStr, int slot, int abilityGuid)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (!Beelzebub.Services.AbilityRegistry.IsValidSlot(slot)) { ctx.Reply("Slot must be 0 (primary), 1-6, or 7 (ultimate)."); return; }

        if (!Enum.TryParse<WeaponFamily>(weaponStr, ignoreCase: true, out var weapon)
            || weapon == WeaponFamily.None
            || weapon == WeaponFamily.Magic)
        {
            ctx.Reply($"Unknown weapon family '{weaponStr}'. Valid: Sword, GreatSword, Axe, Mace, Spear, Daggers, Crossbow, Longbow, Pistols, Reaper, Whip, Claws, Pollaxe, Slashers, TwinBlades, Unarmed, FishingPole.");
            return;
        }

        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        var ability = new PrefabGUID(abilityGuid);
        string abilityName = ability.GetPrefabName();
        bool transformOnly = Core.AbilityRules.IsTransformOnly(abilityName, abilityGuid);
        bool enabled = Core.AbilityRules.IsEnabled(abilityName, abilityGuid);

        Core.AbilityRegistry.SetSlot(steamId, weapon, slot, abilityGuid);
        Core.Persistence.RequestSave();

        // Apply live only if player currently wields the matching weapon family.
        bool appliedNow = false;
        if (character.Exists() && SlotApply.GetCurrentWeapon(character) == weapon)
        {
            appliedNow = SlotApply.ApplyGrant(character, slot, ability, explicitWeaponBucket: true);
        }

        string warnings = "";
        if (transformOnly) warnings += " [WARNING: ability is TransformOnly]";
        if (!enabled) warnings += " [WARNING: ability has Enabled=false — admin override]";
        string applyHint = appliedNow ? "Applied live." : $"Activates next time the player wields {weapon}.";

        ctx.Reply($"Bound {weapon} slot {slot} on {fullName} to {abilityName}. {applyHint}{warnings}");
        Audit(ctx, "set-weapon-slot", steamId, fullName, $"weapon={weapon} slot={slot} ability={abilityGuid} ({abilityName}) appliedNow={appliedNow}");
    }

    [Command("clear-slot", shortHand: "unslot", description: "Clear a player's universal-bucket slot binding. Usage: .beelz admin clear-slot <player> <slot 0-7>. Alias: .beelz admin unslot.", adminOnly: true)]
    public static void ClearSlot(ChatCommandContext ctx, string player, int slot)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (!Beelzebub.Services.AbilityRegistry.IsValidSlot(slot)) { ctx.Reply("Slot must be 0 (primary), 1-6, or 7 (ultimate)."); return; }

        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        Core.AbilityRegistry.ClearSlot(steamId, slot);
        Core.Persistence.RequestSave();

        bool clearedNow = false;
        if (character.Exists()) clearedNow = SlotApply.ClearGrant(character, slot);

        ctx.Reply($"Cleared {fullName}'s universal slot {slot}. {(clearedNow ? "Removed from spell bar live." : "Saved bind removed; spell bar refreshes on next weapon swap.")}");
        Audit(ctx, "clear-slot", steamId, fullName, $"slot={slot} clearedNow={clearedNow}");
    }

    // --- AUDIT-6 (v0.20.1): bulk admin operations ---
    //
    // For server-wide events / hot-patches / season resets: ops that need to
    // touch every player at once. Each is audit-logged. The destructive ones
    // (wipe-all) require an explicit confirmation token.

    [Command("revert-all", description: "Force every active transformation on the server to end immediately. Usage: .beelz admin revert-all", adminOnly: true)]
    public static void RevertAll(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        // Snapshot the steamIds before mutating — Core.Transforms.Revert removes
        // entries from _activeTransforms, which would otherwise invalidate the
        // enumeration mid-loop.
        var activeSteamIds = Core.AbilityRegistry.AllActiveTransforms()
            .Select(kv => kv.Key)
            .ToList();
        if (activeSteamIds.Count == 0)
        {
            ctx.Reply("No active transformations on the server.");
            return;
        }

        int reverted = 0;
        foreach (ulong sid in activeSteamIds)
        {
            try
            {
                var (ok, _) = Core.Transforms.Revert(sid, "admin revert-all");
                if (ok) reverted++;
            }
            catch (Exception ex)
            {
                Core.Log.LogWarning($"[Beelz AUDIT] revert-all: failed reverting {sid}: {ex}");
            }
        }
        Core.Persistence.RequestSave();

        ctx.Reply($"Reverted {reverted}/{activeSteamIds.Count} active transformations.");
        ulong adminSteamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Core.Log.LogInfo($"[Beelz AUDIT] admin={adminSteamId} action=revert-all reverted={reverted}/{activeSteamIds.Count}");
    }

    [Command("freeze-captures", description: "Toggle the CaptureOnKill master switch at runtime. Usage: .beelz admin freeze-captures <on|off|status>", adminOnly: true)]
    public static void FreezeCaptures(ChatCommandContext ctx, string mode = "status")
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        string verb = mode?.Trim().ToLowerInvariant() ?? "status";

        switch (verb)
        {
            case "status":
                ctx.Reply($"CaptureOnKill = {(Beelzebub.Config.Settings.CaptureOnKill.Value ? "ON (capturing)" : "OFF (frozen)")}");
                return;
            case "on":
            case "unfreeze":
            case "resume":
                Beelzebub.Config.Settings.CaptureOnKill.Value = true;
                ctx.Reply("Captures RESUMED.");
                break;
            case "off":
            case "freeze":
            case "pause":
                Beelzebub.Config.Settings.CaptureOnKill.Value = false;
                ctx.Reply("Captures FROZEN. Kills no longer roll for ability/transform unlocks.");
                break;
            default:
                ctx.Reply("Usage: .beelz admin freeze-captures <on|off|status>");
                return;
        }
        ulong adminSteamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Core.Log.LogInfo($"[Beelz AUDIT] admin={adminSteamId} action=freeze-captures verb={verb} value={Beelzebub.Config.Settings.CaptureOnKill.Value}");
    }

    [Command("snapshot", description: "Dump high-level Beelzebub state to chat (player counts, active transforms, etc.). Usage: .beelz admin snapshot", adminOnly: true)]
    public static void Snapshot(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        int players = Core.AbilityRegistry.PlayerCount;
        int activeTransforms = Core.AbilityRegistry.AllActiveTransforms().Count();
        int abilityMapEntries = Core.AbilityRules?.Current?.AbilityMap?.Count ?? 0;
        int transformMapEntries = Core.AbilityRules?.Current?.TransformMap?.Count ?? 0;
        int denyPatterns = Core.AbilityRules?.Current?.DenyPatterns?.Count ?? 0;
        bool capturesOn = Beelzebub.Config.Settings.CaptureOnKill.Value;
        string serverMode = AbilityRules.GetServerDifficulty();
        string scalingMode = Beelzebub.Config.Settings.Transform_PowerScalingMode?.Value ?? "CuratedScales";

        ctx.Reply($"[SNAPSHOT] players={players} active_transforms={activeTransforms} captures={(capturesOn ? "ON" : "OFF")}");
        ctx.Reply($"  ability_rules: deny_patterns={denyPatterns} curated_abilities={abilityMapEntries} curated_units={transformMapEntries}");
        ctx.Reply($"  server: difficulty={serverMode} scaling_mode={scalingMode}");
        ctx.Reply($"  build: {MyPluginInfo.PLUGIN_NAME} v{MyPluginInfo.PLUGIN_VERSION}");
        ulong adminSteamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Core.Log.LogInfo($"[Beelz AUDIT] admin={adminSteamId} action=snapshot");
    }

    [Command("wipe-all", description: "DESTRUCTIVE: wipe ALL player data (captures, transforms, slots, hotkeys, presets, cooldowns). Requires literal confirmation token. Usage: .beelz admin wipe-all CONFIRM-WIPE", adminOnly: true)]
    public static void WipeAll(ChatCommandContext ctx, string confirmToken)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (confirmToken != "CONFIRM-WIPE")
        {
            ctx.Reply("Wipe NOT executed. To confirm, run: .beelz admin wipe-all CONFIRM-WIPE");
            return;
        }

        // Revert any active transforms FIRST so the cleanup is graceful (carrier
        // buffs destroyed, summoned minions despawned). Without this, the buff
        // entities would linger past the registry wipe and we'd have orphaned
        // transforms.
        var activeSteamIds = Core.AbilityRegistry.AllActiveTransforms().Select(kv => kv.Key).ToList();
        foreach (ulong sid in activeSteamIds)
        {
            try { Core.Transforms.Revert(sid, "admin wipe-all"); } catch { /* swallow */ }
        }

        var (players, abilities, transforms) = Core.AbilityRegistry.WipeAll();
        Core.Persistence.RequestSave();

        ctx.Reply($"WIPED ALL DATA: {players} player(s), {abilities} captured abilit(ies), {transforms} transform unlock(s). Reverted {activeSteamIds.Count} active transforms first.");
        ulong adminSteamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Core.Log.LogWarning($"[Beelz AUDIT] admin={adminSteamId} action=wipe-all players={players} abilities={abilities} transforms={transforms} reverted_first={activeSteamIds.Count}");
    }

    [Command("clear-weapon-slot", shortHand: "weapon-unslot", description: "Clear a player's weapon-specific slot binding. Usage: .beelz admin clear-weapon-slot <player> <weapon> <slot 0-7>. Alias: .beelz admin weapon-unslot.", adminOnly: true)]
    public static void ClearWeaponSlot(ChatCommandContext ctx, string player, string weaponStr, int slot)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (!Beelzebub.Services.AbilityRegistry.IsValidSlot(slot)) { ctx.Reply("Slot must be 0 (primary), 1-6, or 7 (ultimate)."); return; }

        if (!Enum.TryParse<WeaponFamily>(weaponStr, ignoreCase: true, out var weapon)
            || weapon == WeaponFamily.None
            || weapon == WeaponFamily.Magic)
        {
            ctx.Reply($"Unknown weapon family '{weaponStr}'.");
            return;
        }

        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        Core.AbilityRegistry.ClearSlot(steamId, weapon, slot);
        Core.Persistence.RequestSave();

        bool clearedNow = false;
        if (character.Exists() && SlotApply.GetCurrentWeapon(character) == weapon)
        {
            clearedNow = SlotApply.ClearGrant(character, slot);
        }

        ctx.Reply($"Cleared {fullName}'s {weapon} slot {slot}. {(clearedNow ? "Removed live." : "Saved bind removed.")}");
        Audit(ctx, "clear-weapon-slot", steamId, fullName, $"weapon={weapon} slot={slot} clearedNow={clearedNow}");
    }

    // --- v0.39.0: runtime config editing (BCH settings panel) ---

    [Command("set", description: "Set a config value at runtime by key (applies live, persists to the .cfg). Usage: .beelz admin set <key> <value>. See .beelz api config for keys.", adminOnly: true)]
    public static void SetConfig(ChatCommandContext ctx, string key, string value)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        var entry = FindConfigEntry(key);
        if (entry == null)
        {
            ctx.Reply($"No config key matches '{key}'. Use .beelz api config (or the .cfg) for valid keys.");
            return;
        }

        object parsed;
        try { parsed = ParseConfigValue(entry.SettingType, value); }
        catch (Exception ex)
        {
            ctx.Reply($"Couldn't set {entry.Definition.Key}: '{value}' isn't a valid {entry.SettingType.Name} ({ex.Message}).");
            return;
        }

        object old = entry.BoxedValue;
        try { entry.BoxedValue = parsed; }
        catch (Exception ex) { ctx.Reply($"Rejected: {ex.Message}"); return; }

        ctx.Reply($"Set {entry.Definition.Key} = {parsed} (was {old}). Applies live.");
        Audit(ctx, "config-set", 0, "-", $"key={entry.Definition.Key} old={old} new={parsed}");
        // v0.54.0: BCH refresh hint — broadcast to ALL subscribed clients (was sender-only) so a
        // BCH admin panel on any client picks up the change, not just the admin who made it.
        Core.Chat.BroadcastEvent(
            $"[BEELZ:event] type=config-changed key={entry.Definition.Key} value={parsed}");
    }

    static BepInEx.Configuration.ConfigEntryBase FindConfigEntry(string key)
    {
        foreach (var prop in typeof(Beelzebub.Config.Settings).GetProperties(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (prop.GetValue(null) is BepInEx.Configuration.ConfigEntryBase e
                && string.Equals(e.Definition.Key, key, StringComparison.OrdinalIgnoreCase))
                return e;
        }
        return null;
    }

    static object ParseConfigValue(Type t, string raw)
    {
        if (t == typeof(string)) return raw;
        if (t == typeof(bool)) return bool.Parse(raw);
        if (t == typeof(int)) return int.Parse(raw);
        if (t == typeof(float)) return float.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        if (t == typeof(double)) return double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        if (t.IsEnum) return Enum.Parse(t, raw, ignoreCase: true);
        return BepInEx.Configuration.TomlTypeConverter.ConvertToValue(raw, t);
    }
}
