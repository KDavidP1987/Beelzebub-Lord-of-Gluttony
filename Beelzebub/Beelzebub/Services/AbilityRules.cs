using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx;

namespace Beelzebub.Services;

internal sealed class AbilityRules
{
    static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public string RulesFilePath { get; }
    public RulesDto Current { get; private set; } = DefaultRules();

    public AbilityRules()
    {
        var dir = Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_GUID);
        Directory.CreateDirectory(dir);
        RulesFilePath = Path.Combine(dir, "ability_rules.json");
    }

    public void Load()
    {
        if (!File.Exists(RulesFilePath))
        {
            Current = DefaultRules();
            Save();
            Core.Log.LogInfo($"Created default ability rules at {RulesFilePath} ({Current.DenyPatterns.Count} deny patterns).");
            return;
        }

        try
        {
            var json = File.ReadAllText(RulesFilePath);
            var dto = JsonSerializer.Deserialize<RulesDto>(json, _json);
            if (dto is null) throw new InvalidOperationException("rules file deserialized to null");
            Current = NormalizeNulls(dto);
            Core.Log.LogInfo($"Loaded ability rules from {RulesFilePath}: " +
                $"deny patterns={Current.DenyPatterns.Count}, allow patterns={Current.AllowPatterns.Count}, " +
                $"deny guids={Current.DenyGuids.Count}, allow guids={Current.AllowGuids.Count}.");
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"Failed to load ability rules from {RulesFilePath}: {ex}. Using defaults.");
            Current = DefaultRules();
        }
    }

    /// <summary>
    /// Persist the current rules to disk. v0.53.0: returns false on failure (was void/silent) so
    /// admin commands can report a failed write instead of falsely claiming success.
    /// </summary>
    public bool Save()
    {
        try
        {
            var tmp = RulesFilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, _json));
            if (File.Exists(RulesFilePath)) File.Replace(tmp, RulesFilePath, null);
            else File.Move(tmp, RulesFilePath);
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"Failed to save ability rules to {RulesFilePath}: {ex}");
            return false;
        }
    }

    public bool AddDenyPattern(string pattern)
    {
        pattern = (pattern ?? "").Trim();
        if (string.IsNullOrEmpty(pattern)) return false;
        if (Current.DenyPatterns.Any(p => string.Equals(p, pattern, StringComparison.OrdinalIgnoreCase))) return false;
        Current.DenyPatterns.Add(pattern);
        Save();
        return true;
    }

    public bool RemoveDenyPattern(string pattern)
    {
        int removed = Current.DenyPatterns.RemoveAll(p => string.Equals(p, pattern, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) Save();
        return removed > 0;
    }

    public bool AddAllowPattern(string pattern)
    {
        pattern = (pattern ?? "").Trim();
        if (string.IsNullOrEmpty(pattern)) return false;
        if (Current.AllowPatterns.Any(p => string.Equals(p, pattern, StringComparison.OrdinalIgnoreCase))) return false;
        Current.AllowPatterns.Add(pattern);
        Save();
        return true;
    }

    public bool RemoveAllowPattern(string pattern)
    {
        int removed = Current.AllowPatterns.RemoveAll(p => string.Equals(p, pattern, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) Save();
        return removed > 0;
    }

    // =====================================================================
    // v0.53.0 — FLUID ADMIN SETTERS. Centralized parse/validate/clamp so the
    // chat commands stay thin and hand-edited JSON and command edits share the
    // same rules. Each returns (ok, message); on ok they mutate Current + Save().
    // =====================================================================

    static bool? ParseOnOff(string v) =>
        v is "on" or "true" or "1" or "yes" ? true
        : v is "off" or "false" or "0" or "no" ? false
        : (bool?)null;

    static bool TryParseFloat(string v, out float f) =>
        float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f);

    static string Persisted(bool saved, string ok) => saved ? ok : ok + " (WARNING: failed to write the rules file — change is in memory only)";

    /// <summary>
    /// v0.53.0: set ANY per-ability AbilityMap field in-game. field is case-insensitive:
    /// enabled, weapons, forms, transformonly, difficulty, phase, allowdenied, damagescale,
    /// cooldownscale, category, interruptible, freemove, castspeed, notes.
    /// </summary>
    /// <summary>
    /// v0.69.0: normalize an admin-supplied ability identifier to the canonical prefab-NAME key the
    /// AbilityMap + the tuning matcher use. Accepts the prefab name as-is, OR a numeric PrefabGUID
    /// (what `.beelz list` / BloodCraftHub show — admins/BCH naturally use the ID) resolved to its
    /// prefab name. Returns null if a numeric ID can't be resolved to a known prefab. Without this,
    /// `.beelz admin ability &lt;guid&gt; cooldown 10` created a dead GUID-keyed entry the tuner never
    /// matched (it scans prefabs by NAME), so the edit silently did nothing.
    /// </summary>
    public static string ResolveAbilityKey(string nameOrGuid)
    {
        string s = (nameOrGuid ?? "").Trim();
        if (s.Length == 0 || !int.TryParse(s, out int guid)) return s;
        if (Core.PrefabNames != null && Core.PrefabNames.TryGetValue(guid, out var n) && !string.IsNullOrEmpty(n)) return n;
        string gn = new Stunlock.Core.PrefabGUID(guid).GetPrefabName();
        if (!string.IsNullOrEmpty(gn) && gn.IndexOf("Not Found", StringComparison.OrdinalIgnoreCase) < 0) return gn;
        return null;
    }

    /// <summary>
    /// v0.69.0: heal AbilityMap entries that an earlier command keyed by GUID instead of prefab name
    /// (pre-fix `.beelz admin ability &lt;guid&gt; ...`). Re-keys each numeric key to its prefab name so
    /// the tuner can match it. Runs once at apply-time (prefab map is ready then); cheap no-op after.
    /// Returns the number migrated.
    /// </summary>
    public int NormalizeNumericKeys()
    {
        var map = Current?.AbilityMap;
        if (map == null) return 0;
        List<string> numeric = null;
        foreach (var k in map.Keys) if (int.TryParse(k, out _)) (numeric ??= new List<string>()).Add(k);
        if (numeric == null) return 0;
        int migrated = 0;
        foreach (var k in numeric)
        {
            string name = ResolveAbilityKey(k);
            if (string.IsNullOrEmpty(name) || name == k) continue;
            var e = map[k];
            if (map.TryGetValue(name, out var existing))
            {
                // v0.70.0: a curated name-keyed entry already exists — MERGE the admin-set override
                // fields from the GUID entry into it (don't drop them, the v0.69 bug that lost a
                // cooldown set by GUID onto an already-curated ability).
                if (e.CooldownSeconds.HasValue) existing.CooldownSeconds = e.CooldownSeconds;
                if (e.MaxRangeOverride.HasValue) existing.MaxRangeOverride = e.MaxRangeOverride;
                if (e.ChargesMax.HasValue) existing.ChargesMax = e.ChargesMax;
                if (e.ChargeTimeSeconds.HasValue) existing.ChargeTimeSeconds = e.ChargeTimeSeconds;
                if (e.AoeRadius.HasValue) existing.AoeRadius = e.AoeRadius;
                if (e.ProjectileSpeed.HasValue) existing.ProjectileSpeed = e.ProjectileSpeed;
                if (e.EffectDurationSeconds.HasValue) existing.EffectDurationSeconds = e.EffectDurationSeconds;
                if (e.HealingMultiplier.HasValue) existing.HealingMultiplier = e.HealingMultiplier;
                if (e.SummonCap.HasValue) existing.SummonCap = e.SummonCap;
                if (e.SummonTimeoutSeconds.HasValue) existing.SummonTimeoutSeconds = e.SummonTimeoutSeconds;
                if (e.SummonUnitsPerCast.HasValue) existing.SummonUnitsPerCast = e.SummonUnitsPerCast;
                if (e.ForceTimeoutSeconds.HasValue) existing.ForceTimeoutSeconds = e.ForceTimeoutSeconds;
                if (e.Interruptible.HasValue) existing.Interruptible = e.Interruptible;
                if (e.CastMovementSpeed.HasValue) existing.CastMovementSpeed = e.CastMovementSpeed;
                if (e.FreeMoveAfterCast) existing.FreeMoveAfterCast = true;
                if (e.FreeMoveAfterSeconds.HasValue) existing.FreeMoveAfterSeconds = e.FreeMoveAfterSeconds;   // v0.87.0
                if (e.InterruptOnHit.HasValue) existing.InterruptOnHit = e.InterruptOnHit;                      // v0.87.0
            }
            else map[name] = e;
            map.Remove(k);
            migrated++;
            Core.Log.LogInfo($"[Beelz] migrated GUID-keyed ability rule {k} -> {name}.");
        }
        if (migrated > 0) Save();
        return migrated;
    }

    /// <summary>
    /// v0.72.0: clear an entry's SHAPING fields back to "leave baked / baseline" — the ability-function
    /// tuning an admin sets (cooldown/range/charges/aoe/projspeed/duration/healing/interrupt/freemove/
    /// castspeed + damage/cooldown scale). Leaves identity/availability (Enabled, Weapons, Forms,
    /// Difficulty, Phase, AllowDenied, TransformOnly, Category, Notes) untouched.
    /// </summary>
    static void ClearShapingFields(AbilityEntry e)
    {
        e.CooldownSeconds = null;
        e.MaxRangeOverride = null;
        e.ChargesMax = null;
        e.ChargeTimeSeconds = null;
        e.AoeRadius = null;
        e.ProjectileSpeed = null;
        e.EffectDurationSeconds = null;
        e.HealingMultiplier = null;
        e.SummonCap = null;
        e.SummonTimeoutSeconds = null;
        e.SummonUnitsPerCast = null;
        e.ForceTimeoutSeconds = null;
        e.Interruptible = null;
        e.FreeMoveAfterCast = false;
        e.CastMovementSpeed = null;
        e.FreeMoveAfterSeconds = null;   // v0.87.0
        e.InterruptOnHit = null;         // v0.87.0
        e.DamageScale = 1.0f;
        e.CooldownScale = 1.0f;
    }

    /// <summary>v0.72.0: reset ONE ability's shaping config to shipped defaults. Clears the rule fields;
    /// the caller live-restores the baked prefab values via AbilityTuningService.RestoreAbility.</summary>
    public (bool ok, string message) ResetAbilityDefaults(string nameOrGuid)
    {
        var map = Current?.AbilityMap;
        if (map == null) return (false, "Ability rules not loaded.");
        string key = ResolveAbilityKey(nameOrGuid);
        if (string.IsNullOrEmpty(key)) return (false, $"No ability prefab found for '{nameOrGuid}'. Use the name or a valid ID from .beelz list.");
        if (!map.TryGetValue(key, out var e) || e == null)
            return (true, $"'{key}' had no shaping config — already at shipped defaults.");
        ClearShapingFields(e);
        Save();
        return (true, $"Reset '{key}' shaping config to shipped defaults (cooldown/range/charges/aoe/projspeed/duration/healing/interrupt/freemove/castspeed/damagescale/cooldownscale cleared).");
    }

    /// <summary>v0.72.0: reset EVERY ability's shaping config to shipped defaults. Returns the count cleared.</summary>
    public int ResetAllAbilityDefaults()
    {
        var map = Current?.AbilityMap;
        if (map == null) return 0;
        int n = 0;
        foreach (var e in map.Values) { if (e == null) continue; ClearShapingFields(e); n++; }
        if (n > 0) Save();
        return n;
    }

    public (bool ok, string message) SetAbilityField(string abilityName, string field, string rawValue)
    {
        string name = (abilityName ?? "").Trim();
        if (name.Length == 0) return (false, "Provide the ability/group prefab name or ID (from .beelz list / api list).");
        // v0.69.0: accept a numeric PrefabGUID and resolve to the prefab-NAME key.
        if (int.TryParse(name, out _))
        {
            string resolved = ResolveAbilityKey(name);
            if (resolved == null) return (false, $"No ability prefab found for ID {name}. Use the name or a valid ID from .beelz list / api list.");
            name = resolved;
        }
        string f = (field ?? "").Trim().ToLowerInvariant();
        string v = (rawValue ?? "").Trim();
        string vl = v.ToLowerInvariant();

        if (!Current.AbilityMap.TryGetValue(name, out var e)) { e = new AbilityEntry(); Current.AbilityMap[name] = e; }

        switch (f)
        {
            case "enabled":
                { var b = ParseOnOff(vl); if (b == null) return (false, "enabled expects on|off."); e.Enabled = b.Value; break; }
            case "transformonly":
                { var b = ParseOnOff(vl); if (b == null) return (false, "transformonly expects on|off."); e.TransformOnly = b.Value; break; }
            case "allowdenied":
                { var b = ParseOnOff(vl); if (b == null) return (false, "allowdenied expects on|off."); e.AllowDenied = b.Value; break; }
            case "freemove":
                { var b = ParseOnOff(vl); if (b == null) return (false, "freemove expects on|off."); e.FreeMoveAfterCast = b.Value; break; }
            case "interruptible": case "interrupt":
                { if (vl is "clear" or "none" or "null") { e.Interruptible = null; break; }
                  var b = ParseOnOff(vl); if (b == null) return (false, "interruptible expects on|off|clear."); e.Interruptible = b.Value; break; }
            case "interruptonhit": case "interruptattack": case "breakonhit":   // v0.87.0: cancel the cast when the caster is hit
                { if (vl is "clear" or "none" or "null") { e.InterruptOnHit = null; break; }
                  var b = ParseOnOff(vl); if (b == null) return (false, "interruptonhit expects on|off|clear."); e.InterruptOnHit = b.Value; break; }
            case "freelymove": case "freemovesecs": case "freemoveafter":         // v0.87.0: free movement N seconds INTO the cast
                { if (vl is "clear" or "none" or "null") { e.FreeMoveAfterSeconds = null; break; }
                  if (!TryParseFloat(v, out float fm) || fm < 0f) return (false, "freelymove expects seconds >= 0 (free to move that many seconds into the cast), or clear.");
                  e.FreeMoveAfterSeconds = fm; break; }
            case "difficulty":
                { if (!vl.Equals("basic") && !vl.Equals("brutal")) return (false, "difficulty expects Basic|Brutal.");
                  e.Difficulty = vl == "brutal" ? "Brutal" : "Basic"; break; }
            case "phase":
                { if (!int.TryParse(v, out int p) || p < 1) return (false, "phase expects an integer >= 1."); e.Phase = p; break; }
            case "damagescale":
                { if (!TryParseFloat(v, out float d) || d <= 0f) return (false, "damagescale expects a number > 0 (1.0 = no change)."); e.DamageScale = d; break; }
            case "cooldownscale":
                { if (!TryParseFloat(v, out float c) || c <= 0f) return (false, "cooldownscale expects a number > 0 (1.0 = no change)."); e.CooldownScale = c; break; }
            case "castspeed": case "castmovementspeed":
                { if (vl is "clear" or "none" or "null") { e.CastMovementSpeed = null; break; }
                  if (!TryParseFloat(v, out float s)) return (false, "castspeed expects 0..1 (0 = rooted, 1 = full speed) or clear.");
                  e.CastMovementSpeed = Math.Clamp(s, 0f, 1f); break; }
            case "cooldownseconds": case "cooldown": case "cd":
                { if (vl is "clear" or "none" or "null") { e.CooldownSeconds = null; break; }
                  if (!TryParseFloat(v, out float cs) || cs < 0f) return (false, "cooldown expects an absolute time in seconds >= 0, or clear. (Needs Abilities_ApplyConfig; the edit is GLOBAL.)");
                  e.CooldownSeconds = cs; break; }
            case "maxrange": case "range":
                { if (vl is "clear" or "none" or "null") { e.MaxRangeOverride = null; break; }
                  if (!TryParseFloat(v, out float r) || r < 0f) return (false, "range expects a max cast distance >= 0, or clear. (Needs Abilities_ApplyConfig; the edit is GLOBAL.)");
                  e.MaxRangeOverride = r; break; }
            case "charges": case "maxcharges":
                { if (vl is "clear" or "none" or "null") { e.ChargesMax = null; break; }
                  if (!int.TryParse(v, out int mc) || mc < 0) return (false, "charges expects an integer >= 0, or clear. (Needs Abilities_ApplyConfig; the edit is GLOBAL.)");
                  e.ChargesMax = mc; break; }
            case "chargetime": case "chargeuptime":
                { if (vl is "clear" or "none" or "null") { e.ChargeTimeSeconds = null; break; }
                  if (!TryParseFloat(v, out float ctv) || ctv < 0f) return (false, "chargetime expects seconds >= 0 (recharge time per charge), or clear.");
                  e.ChargeTimeSeconds = ctv; break; }
            case "aoe": case "aoeradius": case "radius":
                { if (vl is "clear" or "none" or "null") { e.AoeRadius = null; break; }
                  if (!TryParseFloat(v, out float ar) || ar < 0f) return (false, "aoe expects an area radius >= 0, or clear. (Needs Abilities_ApplyConfig; the edit is GLOBAL.)");
                  e.AoeRadius = ar; break; }
            case "projspeed": case "projectilespeed":
                { if (vl is "clear" or "none" or "null") { e.ProjectileSpeed = null; break; }
                  if (!TryParseFloat(v, out float ps) || ps < 0f) return (false, "projspeed expects a projectile speed >= 0, or clear. (Needs Abilities_ApplyConfig; the edit is GLOBAL.)");
                  e.ProjectileSpeed = ps; break; }
            case "duration": case "effectduration":
                { if (vl is "clear" or "none" or "null") { e.EffectDurationSeconds = null; break; }
                  if (!TryParseFloat(v, out float ed) || ed < 0f) return (false, "duration expects the buff/debuff duration in seconds >= 0, or clear. (Needs Abilities_ApplyConfig; the edit is GLOBAL.)");
                  e.EffectDurationSeconds = ed; break; }
            case "healing": case "healmult": case "healingmultiplier":
                { if (vl is "clear" or "none" or "null") { e.HealingMultiplier = null; break; }
                  if (!TryParseFloat(v, out float hm) || hm < 0f) return (false, "healing expects a multiplier >= 0 (1.0 = no change), or clear. (Needs Abilities_ApplyConfig; the edit is GLOBAL.)");
                  e.HealingMultiplier = hm; break; }
            case "summoncap": case "summonlimit": case "maxsummons":
                { if (vl is "clear" or "none" or "null") { e.SummonCap = null; break; }
                  if (!int.TryParse(v, out int sc) || sc < 0) return (false, "summoncap expects an integer >= 0 (max simultaneous 'uses' of this summon ability; 0 = unlimited), or clear. Overrides the global Transform_MaxStacksPerSummonAbility for this ability.");
                  e.SummonCap = sc; break; }
            case "summontimeout": case "summonlifetime": case "summonduration":
                { if (vl is "clear" or "none" or "null") { e.SummonTimeoutSeconds = null; break; }
                  if (!TryParseFloat(v, out float st) || st < 0f) return (false, "summontimeout expects seconds >= 0 (this ability's summons auto-despawn after this long; 0 = never), or clear. Overrides the global Transform_SummonLifetimeSeconds for this ability.");
                  e.SummonTimeoutSeconds = st; break; }
            case "summonunits": case "summonunitspercast": case "unitspercast":
                { if (vl is "clear" or "none" or "null") { e.SummonUnitsPerCast = null; break; }
                  if (!int.TryParse(v, out int su) || su < 0) return (false, "summonunits expects an integer >= 0 (max UNITS one cast of this ability summons; 0 = the ability's natural count), or clear. Separate from summoncap (which limits concurrent USES).");
                  e.SummonUnitsPerCast = su; break; }
            case "forcetimeout": case "effecttimeout": case "bufftimeout":
                { if (vl is "clear" or "none" or "null") { e.ForceTimeoutSeconds = null; break; }
                  if (!TryParseFloat(v, out float fto) || fto < 0f) return (false, "forcetimeout expects seconds >= 0 (force this ability's otherwise-INDEFINITE spawned effects/buffs to expire after this long — adds a lifetime where there is none), or clear. (Needs Abilities_ApplyConfig; GLOBAL baked edit.)");
                  e.ForceTimeoutSeconds = fto; break; }
            case "category":
                { if (vl is "clear" or "none" or "null" or "auto") { e.Category = null; break; }
                  if (!Enum.TryParse<AbilityCategory>(v, ignoreCase: true, out var cat)) return (false, "category expects one of: Travel, Aoe, Projectile, Melee, Summon, Buff, WeaponSpell, Spell, Other (or clear).");
                  e.Category = cat.ToString(); break; }
            case "weapons":
                { e.Weapons = ParseWeaponList(vl, out string err); if (err != null) return (false, err); break; }
            case "forms":
                { e.Forms = ParseFormList(vl, out string err); if (err != null) return (false, err); break; }
            case "notes":
                { e.Notes = v; break; }
            default:
                return (false, "Unknown field. Valid: enabled, weapons, forms, transformonly, difficulty, phase, allowdenied, damagescale, cooldownscale, cooldown, range, charges, chargetime, aoe, projspeed, duration, healing, category, interruptible, interruptonhit, freemove, freelymove, castspeed, notes.");
        }
        bool saved = Save();
        return (true, Persisted(saved, $"Set {f}={v} for '{name}'. (.beelz admin reload re-applies cast tuning if changed.)"));
    }

    static List<string> ParseWeaponList(string csv, out string error)
    {
        error = null;
        var list = new List<string>();
        if (csv is "" or "none" or "any" or "clear" or "universal") return list; // empty = universal
        foreach (var tok in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<WeaponFamily>(tok, ignoreCase: true, out var fam) || fam == WeaponFamily.None)
            { error = $"Unknown weapon family '{tok}'. Valid: Sword, GreatSword, Axe, Mace, Spear, Daggers, Crossbow, Longbow, Pistols, Reaper, Whip, Claws, Pollaxe, Slashers, TwinBlades, Unarmed, FishingPole, Magic (or 'any' to clear)."; return list; }
            list.Add(fam.ToString());
        }
        return list;
    }

    static List<string> ParseFormList(string csv, out string error)
    {
        error = null;
        var list = new List<string>();
        if (csv is "" or "none" or "any" or "clear") return list;
        foreach (var tok in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<ShapeshiftForm>(tok, ignoreCase: true, out var fm) || fm == ShapeshiftForm.None)
            { error = $"Unknown form '{tok}'. Valid: Wolf, Bear, Rat, Spider, Toad (or 'any' to clear)."; return list; }
            list.Add(fm.ToString());
        }
        return list;
    }

    /// <summary>
    /// v0.53.0: set ANY per-unit TransformMap scalar field in-game. field is case-insensitive:
    /// enabled, difficulty, tier, damagescale, cooldownscale, healthscale, speedscale,
    /// fullreplace, powerscalingmode, notes. (SlotTemplate is edited in the JSON file.)
    /// </summary>
    public (bool ok, string message) SetTransformField(string unitName, string field, string rawValue)
    {
        string name = (unitName ?? "").Trim();
        if (name.Length == 0) return (false, "Provide the CHAR_* unit prefab name.");
        string f = (field ?? "").Trim().ToLowerInvariant();
        string v = (rawValue ?? "").Trim();
        string vl = v.ToLowerInvariant();

        if (!Current.TransformMap.TryGetValue(name, out var e)) { e = new TransformEntry(); Current.TransformMap[name] = e; }

        switch (f)
        {
            case "enabled":
                { var b = ParseOnOff(vl); if (b == null) return (false, "enabled expects on|off."); e.Enabled = b.Value; break; }
            case "fullreplace":
                { var b = ParseOnOff(vl); if (b == null) return (false, "fullreplace expects on|off."); e.FullReplace = b.Value; break; }
            case "difficulty":
                { if (!vl.Equals("basic") && !vl.Equals("brutal")) return (false, "difficulty expects Basic|Brutal.");
                  e.Difficulty = vl == "brutal" ? "Brutal" : "Basic"; break; }
            case "tier":
                { if (!int.TryParse(v, out int t) || t < 1) return (false, "tier expects an integer >= 1."); e.Tier = t; break; }
            case "damagescale":
                { if (!TryParseFloat(v, out float d) || d <= 0f) return (false, "damagescale expects a number > 0."); e.DamageScale = d; break; }
            case "cooldownscale":
                { if (!TryParseFloat(v, out float c) || c <= 0f) return (false, "cooldownscale expects a number > 0."); e.CooldownScale = c; break; }
            case "healthscale":
                { if (!TryParseFloat(v, out float h) || h <= 0f) return (false, "healthscale expects a number > 0."); e.HealthScale = h; break; }
            case "speedscale": case "movementspeedscale":
                { if (!TryParseFloat(v, out float s) || s <= 0f) return (false, "speedscale expects a number > 0."); e.MovementSpeedScale = s; break; }
            case "powerscalingmode": case "scalingmode":
                { if (vl is "inherit" or "clear" or "none" or "null") { e.PowerScalingMode = null; break; }
                  if (!Enum.TryParse<PowerScalingMode>(v, ignoreCase: true, out var m)) return (false, "powerscalingmode expects CuratedScales|PrefabAbsolute|PlayerScaled|PlayerLeveled (or inherit).");
                  e.PowerScalingMode = m.ToString(); break; }
            case "duration": case "durationseconds":
                { if (vl is "inherit" or "clear" or "none" or "null") { e.DurationSeconds = null; break; }
                  if (!TryParseFloat(v, out float du) || du < 0f) return (false, "duration expects seconds >= 0 (or 'inherit' to clear)."); e.DurationSeconds = du; break; }
            case "cooldown": case "cooldownseconds":
                { if (vl is "inherit" or "clear" or "none" or "null") { e.CooldownSeconds = null; break; }
                  if (!TryParseFloat(v, out float cd) || cd < 0f) return (false, "cooldown expects seconds >= 0 (or 'inherit' to clear)."); e.CooldownSeconds = cd; break; }
            case "notes":
                { e.Notes = v; break; }
            default:
                return (false, "Unknown field. Valid: enabled, difficulty, tier, damagescale, cooldownscale, healthscale, speedscale, duration, cooldown, fullreplace, powerscalingmode, notes.");
        }
        bool saved = Save();
        return (true, Persisted(saved, $"Set transform {f}={v} for '{name}'."));
    }

    /// <summary>v0.53.0: set a global Defaults scaling baseline (damagescale | cooldownscale).</summary>
    public (bool ok, string message) SetDefault(string field, string rawValue)
    {
        string f = (field ?? "").Trim().ToLowerInvariant();
        string v = (rawValue ?? "").Trim();
        Current.Defaults ??= new DefaultsDto();
        switch (f)
        {
            case "damagescale":
                { if (!TryParseFloat(v, out float d) || d <= 0f) return (false, "damagescale expects a number > 0 (1.0 = no change)."); Current.Defaults.DamageScale = d; break; }
            case "cooldownscale":
                { if (!TryParseFloat(v, out float c) || c <= 0f) return (false, "cooldownscale expects a number > 0 (1.0 = no change)."); Current.Defaults.CooldownScale = c; break; }
            default:
                return (false, "Unknown default. Valid: damagescale, cooldownscale.");
        }
        bool saved = Save();
        return (true, Persisted(saved, $"Set global default {f}={v} (applies to abilities with no per-ability override)."));
    }

    // GUID-based filter lists (the pattern lists already have deny/allow/undeny/unallow commands).
    public bool AddDenyGuid(int guid) { if (Current.DenyGuids.Contains(guid)) return false; Current.DenyGuids.Add(guid); Save(); return true; }
    public bool RemoveDenyGuid(int guid) { bool r = Current.DenyGuids.Remove(guid); if (r) Save(); return r; }
    public bool AddAllowGuid(int guid) { if (Current.AllowGuids.Contains(guid)) return false; Current.AllowGuids.Add(guid); Save(); return true; }
    public bool RemoveAllowGuid(int guid) { bool r = Current.AllowGuids.Remove(guid); if (r) Save(); return r; }

    // Transform-only reservation lists (bulk; per-ability TransformOnly is via SetAbilityField).
    public bool AddTransformOnlyPattern(string p)
    {
        p = (p ?? "").Trim(); if (p.Length == 0) return false;
        if (Current.TransformOnlyPatterns.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase))) return false;
        Current.TransformOnlyPatterns.Add(p); Save(); return true;
    }
    public bool RemoveTransformOnlyPattern(string p)
    { int r = Current.TransformOnlyPatterns.RemoveAll(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)); if (r > 0) Save(); return r > 0; }
    public bool AddTransformOnlyGuid(int guid) { if (Current.TransformOnlyGuids.Contains(guid)) return false; Current.TransformOnlyGuids.Add(guid); Save(); return true; }
    public bool RemoveTransformOnlyGuid(int guid) { bool r = Current.TransformOnlyGuids.Remove(guid); if (r) Save(); return r; }

    static RulesDto NormalizeNulls(RulesDto dto) => new()
    {
        Version = dto.Version,
        DenyPatterns = dto.DenyPatterns ?? new List<string>(),
        AllowPatterns = dto.AllowPatterns ?? new List<string>(),
        DenyGuids = dto.DenyGuids ?? new List<int>(),
        AllowGuids = dto.AllowGuids ?? new List<int>(),
        DropRateOverrides = NormalizeDropRateOverrides(dto.DropRateOverrides),
        Defaults = NormalizeDefaults(dto.Defaults),
        AbilityMap = NormalizeAbilityMap(dto.AbilityMap),
        TransformOnlyPatterns = dto.TransformOnlyPatterns ?? new List<string>(),
        TransformOnlyGuids = dto.TransformOnlyGuids ?? new List<int>(),
        TransformMap = NormalizeTransformMap(dto.TransformMap),
    };

    // v0.53.0: drop invalid/empty-pattern overrides and clamp rates to [0,1] (they're probabilities).
    static List<RateOverride> NormalizeDropRateOverrides(List<RateOverride> raw)
    {
        var result = new List<RateOverride>();
        if (raw == null) return result;
        foreach (var o in raw)
        {
            if (o == null || string.IsNullOrWhiteSpace(o.Pattern)) continue;
            result.Add(new RateOverride
            {
                Pattern = o.Pattern.Trim(),
                RateRegular = Math.Clamp(o.RateRegular, 0f, 1f),
                RateVBlood = Math.Clamp(o.RateVBlood, 0f, 1f),
            });
        }
        return result;
    }

    // v0.50.0: clamp the global default scales (≤0 makes no sense → 1.0 = no change).
    static DefaultsDto NormalizeDefaults(DefaultsDto raw)
    {
        if (raw == null) return new DefaultsDto();
        return new DefaultsDto
        {
            DamageScale = raw.DamageScale > 0f ? raw.DamageScale : 1.0f,
            CooldownScale = raw.CooldownScale > 0f ? raw.CooldownScale : 1.0f,
        };
    }

    // v0.53.0: validate the constrained string/range fields so hand-edited JSON can't inject
    // invalid values that silently degrade (a typo'd "Bruttal" → treated as Basic with a warning,
    // an unknown Category → ignored, an out-of-range CastMovementSpeed → clamped).
    static string NormalizeDifficulty(string raw)
    {
        string d = (raw ?? "").Trim();
        if (d.Equals("Brutal", StringComparison.OrdinalIgnoreCase)) return "Brutal";
        if (!string.IsNullOrEmpty(d) && !d.Equals("Basic", StringComparison.OrdinalIgnoreCase))
            Core.Log?.LogWarning($"[Beelz] ability_rules.json: unknown Difficulty '{d}' → treating as Basic.");
        return "Basic";
    }

    static string NormalizeCategoryName(string raw)
    {
        string c = (raw ?? "").Trim();
        if (c.Length == 0) return null;
        if (Enum.TryParse<AbilityCategory>(c, ignoreCase: true, out var cat)) return cat.ToString();
        Core.Log?.LogWarning($"[Beelz] ability_rules.json: unknown Category '{c}' → ignoring (will auto-classify).");
        return null;
    }

    static string NormalizePowerScalingMode(string raw)
    {
        string m = (raw ?? "").Trim();
        if (m.Length == 0) return null;
        if (Enum.TryParse<PowerScalingMode>(m, ignoreCase: true, out var mode)) return mode.ToString();
        Core.Log?.LogWarning($"[Beelz] ability_rules.json: unknown PowerScalingMode '{m}' → inherit (global).");
        return null;
    }

    static float? ClampCastSpeed(float? v) => v.HasValue ? Math.Clamp(v.Value, 0f, 1f) : (float?)null;

    static Dictionary<string, TransformEntry> NormalizeTransformMap(Dictionary<string, TransformEntry> raw)
    {
        var result = new Dictionary<string, TransformEntry>(StringComparer.OrdinalIgnoreCase);
        if (raw == null) return result;
        foreach (var (name, entry) in raw)
        {
            if (string.IsNullOrWhiteSpace(name) || entry == null) continue;
            result[name.Trim()] = new TransformEntry
            {
                Enabled = entry.Enabled,
                Difficulty = NormalizeDifficulty(entry.Difficulty),
                Tier = entry.Tier <= 0 ? 1 : entry.Tier,
                // TX6: scale defaults to 1.0 when absent or non-positive. Negative or
                // zero scales make no semantic sense, so we clamp to 1.0 (no change)
                // rather than letting an admin accidentally zero out the player.
                DamageScale = entry.DamageScale > 0f ? entry.DamageScale : 1.0f,
                CooldownScale = entry.CooldownScale > 0f ? entry.CooldownScale : 1.0f,
                HealthScale = entry.HealthScale > 0f ? entry.HealthScale : 1.0f,
                MovementSpeedScale = entry.MovementSpeedScale > 0f ? entry.MovementSpeedScale : 1.0f,
                FullReplace = entry.FullReplace,
                PowerScalingMode = NormalizePowerScalingMode(entry.PowerScalingMode),
                Notes = entry.Notes ?? "",
            };
        }
        return result;
    }

    static Dictionary<string, AbilityEntry> NormalizeAbilityMap(Dictionary<string, AbilityEntry> raw)
    {
        var result = new Dictionary<string, AbilityEntry>(StringComparer.OrdinalIgnoreCase);
        if (raw == null) return result;
        foreach (var (name, entry) in raw)
        {
            if (string.IsNullOrWhiteSpace(name) || entry == null) continue;
            result[name.Trim()] = new AbilityEntry
            {
                Weapons = entry.Weapons ?? new List<string>(),
                Forms = entry.Forms ?? new List<string>(),
                TransformOnly = entry.TransformOnly,
                Enabled = entry.Enabled,            // absent JSON field keeps the C# auto-property default (true)
                Difficulty = NormalizeDifficulty(entry.Difficulty),
                Phase = entry.Phase <= 0 ? 1 : entry.Phase,
                AllowDenied = entry.AllowDenied,
                DamageScale = entry.DamageScale > 0f ? entry.DamageScale : 1.0f,
                CooldownScale = entry.CooldownScale > 0f ? entry.CooldownScale : 1.0f,
                // v0.46.0 ability tuning (null/false/unset = leave the prefab's baked value).
                Interruptible = entry.Interruptible,
                FreeMoveAfterCast = entry.FreeMoveAfterCast,
                CastMovementSpeed = ClampCastSpeed(entry.CastMovementSpeed),
                FreeMoveAfterSeconds = entry.FreeMoveAfterSeconds,   // v0.87.0 — preserve across reload
                InterruptOnHit = entry.InterruptOnHit,               // v0.87.0 — preserve across reload
                // v0.65.0–0.68.0 ability shaping — MUST be copied here or a reload/restart silently
                // drops them (this was the cooldown-never-applies bug: the value lived in the file but
                // Load → NormalizeAbilityMap rebuilt the entry without these, wiping them from memory).
                CooldownSeconds = entry.CooldownSeconds,
                MaxRangeOverride = entry.MaxRangeOverride,
                ChargesMax = entry.ChargesMax,
                ChargeTimeSeconds = entry.ChargeTimeSeconds,
                AoeRadius = entry.AoeRadius,
                ProjectileSpeed = entry.ProjectileSpeed,
                EffectDurationSeconds = entry.EffectDurationSeconds,
                HealingMultiplier = entry.HealingMultiplier,
                SummonCap = entry.SummonCap,                          // v0.79.0 — preserve across reload
                SummonTimeoutSeconds = entry.SummonTimeoutSeconds,    // v0.79.0
                SummonUnitsPerCast = entry.SummonUnitsPerCast,        // v0.80.0
                ForceTimeoutSeconds = entry.ForceTimeoutSeconds,      // v0.85.0
                Category = NormalizeCategoryName(entry.Category),
                Notes = entry.Notes ?? "",
            };
        }
        return result;
    }

    static RulesDto DefaultRules() => new()
    {
        Version = 1,
        DenyPatterns = new List<string>
        {
            // Filler / animation
            "_Idle_", "_Flee_", "_Sequence_",
            // Weapon-tied (not castable as spells)
            "_MeleeAttack_", "_Block_", "_Parry_", "_Counter_",
            // Lifecycle events
            "_Spawn_", "_Despawn_", "_Disappear_", "_Death_", "_Wounded_",
            // Brutal-only duplicates
            "_Hard_",
            // v0.20.0: summon abilities are NO LONGER filtered by default. Task #74
            // landed the LinkMinionToOwnerOnSpawnSystem patch + Transform_SummonsAreAllies
            // config — when a Beelzebub-transformed player casts a summon, the spawned
            // minions are now rebound as player-allies and despawn cleanly on revert.
            // Admins who still want to filter (e.g. for game-balance reasons) can add
            //     "_Summon_", "_Summoning_", "_CallReinforcements_", "_Reinforcement_"
            // to the DenyPatterns array in their `ability_rules.json`.
            // Feed-prep abilities — cinematic only, no offensive use
            "_FeedBoss_", "_Feed_Initiate_",
            // Internal / debug
            "_Test_", "_Internal_", "_DEBUG_",
        },
        AllowPatterns = new List<string>(),
        DenyGuids = new List<int>(),
        AllowGuids = new List<int>(),
    };

    /// <summary>
    /// W1: resolve the set of weapon families an ability is compatible with.
    /// Order:
    ///  - If <c>AbilityMap[name].Weapons</c> is non-empty, use that list (admin curation wins).
    ///  - Else fall back to the name-substring classifier, which always returns one family.
    /// Returned list is non-empty; the caller can treat <see cref="WeaponFamily.Magic"/>
    /// (or empty Weapons in the original entry) as "universal — usable with any weapon".
    /// </summary>
    public IReadOnlyList<WeaponFamily> ClassifyWeaponFamilies(string abilityName)
    {
        if (Current.AbilityMap != null
            && Current.AbilityMap.TryGetValue(abilityName ?? "", out var entry)
            && entry.Weapons is { Count: > 0 })
        {
            var parsed = new List<WeaponFamily>(entry.Weapons.Count);
            foreach (var w in entry.Weapons)
            {
                if (Enum.TryParse(w, ignoreCase: true, out WeaponFamily fam))
                    parsed.Add(fam);
            }
            if (parsed.Count > 0) return parsed;
        }
        return new[] { WeaponFamilyClassifier.Classify(abilityName, adminOverrides: null) };
    }

    /// <summary>
    /// W1 convenience: returns the *primary* WeaponFamily for legacy callers /
    /// UI display. First entry of <see cref="ClassifyWeaponFamilies"/>.
    /// </summary>
    public WeaponFamily ClassifyWeaponFamily(string abilityName)
    {
        var list = ClassifyWeaponFamilies(abilityName);
        return list.Count > 0 ? list[0] : WeaponFamily.Magic;
    }

    /// <summary>
    /// #4 (v0.34.0) animation fidelity: the weapon family an ability's CAST
    /// ANIMATION is bound to, or <see cref="WeaponFamily.None"/> for spells /
    /// universal abilities that don't need a weapon to read right. V Rising bakes
    /// the cast animation into the ability prefab (a client-side SequenceGUID) — the
    /// server can't remap it, so the only fidelity lever is wielding the matching
    /// weapon. Returns the first concrete (non-Magic/non-None) family from the
    /// curated weapon list; Magic-only/empty abilities return None.
    /// </summary>
    public WeaponFamily GetAnimationWeapon(string abilityName)
    {
        foreach (var fam in ClassifyWeaponFamilies(abilityName))
            if (fam != WeaponFamily.Magic && fam != WeaponFamily.None)
                return fam;
        return WeaponFamily.None;
    }

    /// <summary>#4: true if a specific weapon must be wielded for this ability's animation to read right.</summary>
    public bool IsWeaponAnimationBound(string abilityName) => GetAnimationWeapon(abilityName) != WeaponFamily.None;

    /// <summary>
    /// W1: resolve the set of shapeshift forms an ability is restricted to.
    /// Empty list = no form restriction (works in any form, or while not transformed).
    /// </summary>
    public IReadOnlyList<ShapeshiftForm> GetFormRestriction(string abilityName)
    {
        if (Current.AbilityMap == null
            || !Current.AbilityMap.TryGetValue(abilityName ?? "", out var entry)
            || entry.Forms is null || entry.Forms.Count == 0)
        {
            return System.Array.Empty<ShapeshiftForm>();
        }
        var parsed = new List<ShapeshiftForm>(entry.Forms.Count);
        foreach (var f in entry.Forms)
        {
            if (Enum.TryParse(f, ignoreCase: true, out ShapeshiftForm sf)) parsed.Add(sf);
        }
        return parsed;
    }

    /// <summary>
    /// Admin kill-switch: is this ability currently allowed (capture + use)?
    /// If `AbilityMap[name].Enabled` is false, the ability is blocked from being
    /// captured on kill, from `.beelz grant`, and from transform spell-bar pickup.
    /// Default true when no entry exists. GUID-based bulk disable lives in
    /// <see cref="RulesDto.DenyGuids"/>; this is the per-ability switch.
    /// </summary>
    public bool IsEnabled(string abilityName, int abilityGuid)
    {
        if (Current.AbilityMap != null
            && Current.AbilityMap.TryGetValue(abilityName ?? "", out var entry)
            && !entry.Enabled) return false;
        return true;
    }

    /// <summary>
    /// v0.27.2: does this ability's AbilityMap entry force-allow it past DenyPatterns
    /// (and DenyGuids)? Lets curated phase abilities with deny-patterned names — e.g.
    /// Solarus's "_Hard_" fallen-angel logic-gate abilities — still be captured/used.
    /// Default false. The per-ability Enabled=false kill-switch still applies.
    /// </summary>
    public bool IsAllowDenied(string abilityName, int abilityGuid)
    {
        if (Current.AbilityMap != null
            && Current.AbilityMap.TryGetValue(abilityName ?? "", out var entry))
            return entry.AllowDenied;
        return false;
    }

    /// <summary>
    /// Per-ability damage multiplier (1.0 = no change). v0.43.5: now LIVE for GRANTED
    /// abilities cast in normal form — <see cref="Services.GrantPowerScalingService"/>
    /// applies it (× the global Grant_PowerScalingMode factor) as a brief power buff
    /// around the cast. (Not applied to transform casts — those use Transform scaling.)
    /// </summary>
    public float GetDamageScale(string abilityName)
    {
        if (Current.AbilityMap != null
            && Current.AbilityMap.TryGetValue(abilityName ?? "", out var entry))
            return entry.DamageScale;
        return Current.Defaults?.DamageScale ?? 1.0f; // v0.50.0 global fallback
    }

    /// <summary>
    /// v0.51.0: admin override for the wire `cat=` ability-category badge. Returns the parsed
    /// <see cref="AbilityCategory"/> from this ability's AbilityMap <c>Category</c> field, or null
    /// to fall back to the <see cref="Categorization.ClassifyAbility"/> name heuristic.
    /// </summary>
    public AbilityCategory? GetAbilityCategoryOverride(string abilityName)
    {
        if (Current.AbilityMap != null
            && Current.AbilityMap.TryGetValue(abilityName ?? "", out var entry)
            && !string.IsNullOrWhiteSpace(entry.Category)
            && Enum.TryParse<AbilityCategory>(entry.Category.Trim(), ignoreCase: true, out var cat))
            return cat;
        return null;
    }

    // v0.54.0: read-back accessors so the wire API (api info / catalog-abilities) can surface the
    // cast-tuning state and the raw category-override string for BCH admin panels.
    public bool? GetInterruptible(string abilityName)
        => Current.AbilityMap != null && Current.AbilityMap.TryGetValue(abilityName ?? "", out var e) ? e.Interruptible : null;
    public bool GetFreeMoveAfterCast(string abilityName)
        => Current.AbilityMap != null && Current.AbilityMap.TryGetValue(abilityName ?? "", out var e) && e.FreeMoveAfterCast;
    public float? GetCastMovementSpeed(string abilityName)
        => Current.AbilityMap != null && Current.AbilityMap.TryGetValue(abilityName ?? "", out var e) ? e.CastMovementSpeed : null;
    public float? GetFreeMoveAfterSeconds(string abilityName)   // v0.87.0
        => Current.AbilityMap != null && Current.AbilityMap.TryGetValue(abilityName ?? "", out var e) ? e.FreeMoveAfterSeconds : null;
    public bool? GetInterruptOnHit(string abilityName)          // v0.87.0
        => Current.AbilityMap != null && Current.AbilityMap.TryGetValue(abilityName ?? "", out var e) ? e.InterruptOnHit : null;
    // v0.65.0 (J1): per-ability absolute cooldown / max-range overrides (null = leave baked).
    public float? GetCooldownOverride(string abilityName)
        => Current.AbilityMap != null && Current.AbilityMap.TryGetValue(abilityName ?? "", out var e) ? e.CooldownSeconds : null;
    public float? GetMaxRangeOverride(string abilityName)
        => Current.AbilityMap != null && Current.AbilityMap.TryGetValue(abilityName ?? "", out var e) ? e.MaxRangeOverride : null;
    // v0.67.0 (Stage 2): charges / AoE-radius / projectile-speed overrides (null = leave baked).
    public int? GetChargesMax(string abilityName)
        => Current.AbilityMap != null && Current.AbilityMap.TryGetValue(abilityName ?? "", out var e) ? e.ChargesMax : null;
    public float? GetChargeTimeSeconds(string abilityName)
        => Current.AbilityMap != null && Current.AbilityMap.TryGetValue(abilityName ?? "", out var e) ? e.ChargeTimeSeconds : null;
    public float? GetAoeRadius(string abilityName)
        => Current.AbilityMap != null && Current.AbilityMap.TryGetValue(abilityName ?? "", out var e) ? e.AoeRadius : null;
    public float? GetProjectileSpeed(string abilityName)
        => Current.AbilityMap != null && Current.AbilityMap.TryGetValue(abilityName ?? "", out var e) ? e.ProjectileSpeed : null;
    // v0.68.0 (Stage 2b): effect-duration / healing-multiplier overrides (null = leave baked).
    public float? GetEffectDurationSeconds(string abilityName)
        => Current.AbilityMap != null && Current.AbilityMap.TryGetValue(abilityName ?? "", out var e) ? e.EffectDurationSeconds : null;
    public float? GetHealingMultiplier(string abilityName)
        => Current.AbilityMap != null && Current.AbilityMap.TryGetValue(abilityName ?? "", out var e) ? e.HealingMultiplier : null;
    // v0.79.0: per-ability summon governance overrides.
    public int? GetSummonCap(string abilityName)
        => Current.AbilityMap != null && Current.AbilityMap.TryGetValue(abilityName ?? "", out var e) ? e.SummonCap : null;
    public float? GetSummonTimeout(string abilityName)
        => Current.AbilityMap != null && Current.AbilityMap.TryGetValue(abilityName ?? "", out var e) ? e.SummonTimeoutSeconds : null;
    public int? GetSummonUnitsPerCast(string abilityName)
        => Current.AbilityMap != null && Current.AbilityMap.TryGetValue(abilityName ?? "", out var e) ? e.SummonUnitsPerCast : null;
    public float? GetForceTimeoutSeconds(string abilityName)
        => Current.AbilityMap != null && Current.AbilityMap.TryGetValue(abilityName ?? "", out var e) ? e.ForceTimeoutSeconds : null;
    /// <summary>v0.80.0: per-ability max units per cast (0/none = the ability's natural count).</summary>
    public int ResolveSummonUnitsPerCast(string abilityName, int fallback)
        => GetSummonUnitsPerCast(abilityName) ?? fallback;
    /// <summary>v0.79.0: effective summon cap for an ability — the per-ability override if set, else the
    /// server-wide default. Precedence: per-ability &gt; global. (0 = unlimited at either layer.)</summary>
    public int ResolveSummonCap(string abilityName, int globalCap)
        => GetSummonCap(abilityName) ?? globalCap;
    /// <summary>v0.79.0: effective summon timeout (seconds) for an ability — per-ability override if set,
    /// else the server-wide default. (0 = never expires.)</summary>
    public float ResolveSummonTimeout(string abilityName, float globalTimeout)
        => GetSummonTimeout(abilityName) ?? globalTimeout;
    /// <summary>v0.79.0: true if ANY ability carries a per-ability summon-timeout override (so the lifespan
    /// sweep must run even when the global timeout is 0).</summary>
    public bool HasAnySummonTimeoutOverride()
    {
        if (Current?.AbilityMap == null) return false;
        foreach (var e in Current.AbilityMap.Values) if (e?.SummonTimeoutSeconds is > 0f) return true;
        return false;
    }
    public string GetCategoryOverrideRaw(string abilityName)
        => Current.AbilityMap != null && Current.AbilityMap.TryGetValue(abilityName ?? "", out var e) ? e.Category : null;

    /// <summary>
    /// Per-ability cooldown multiplier (1.0 = no change). v0.44.0: LIVE for FORCE-CASTS —
    /// <c>.beelz cast &lt;hotkey|index&gt;</c> multiplies the ability's own cooldown by this
    /// before enforcing it (floor 1s). Native spell-BAR slot cooldowns are still V Rising's
    /// own (scaling those needs a cooldown-component hook — a follow-up).
    /// </summary>
    public float GetCooldownScale(string abilityName)
    {
        if (Current.AbilityMap != null
            && Current.AbilityMap.TryGetValue(abilityName ?? "", out var entry))
            return entry.CooldownScale;
        return Current.Defaults?.CooldownScale ?? 1.0f; // v0.50.0 global fallback
    }

    // --- TX4: difficulty gating helpers ---

    /// <summary>
    /// Returns the configured server difficulty mode ("Basic" or "Brutal"),
    /// normalized. Falls back to "Basic" on unrecognized values.
    /// </summary>
    public static string GetServerDifficulty()
    {
        var raw = (Beelzebub.Config.Settings.Server_DifficultyMode?.Value ?? "Basic").Trim();
        return string.Equals(raw, "Brutal", StringComparison.OrdinalIgnoreCase) ? "Brutal" : "Basic";
    }

    /// <summary>
    /// Difficulty classification for a captured ability. Resolution order:
    ///  1. AbilityMap[name].Difficulty (admin override).
    ///  2. Name-substring heuristic: ability names containing "_Hard_" default to "Brutal"
    ///     (V Rising naming convention for brutal-mode-only variants).
    ///  3. Default "Basic".
    /// </summary>
    public string GetAbilityDifficulty(string abilityName)
    {
        if (Current.AbilityMap != null
            && Current.AbilityMap.TryGetValue(abilityName ?? "", out var entry)
            && !string.IsNullOrWhiteSpace(entry.Difficulty))
        {
            return string.Equals(entry.Difficulty, "Brutal", StringComparison.OrdinalIgnoreCase) ? "Brutal" : "Basic";
        }
        if (!string.IsNullOrEmpty(abilityName) && abilityName.Contains("_Hard_", StringComparison.OrdinalIgnoreCase))
        {
            return "Brutal";
        }
        return "Basic";
    }

    /// <summary>
    /// Is <paramref name="entryDifficulty"/> allowed under <paramref name="serverMode"/>?
    /// Rules: Basic entries always pass. Brutal entries pass only on Brutal servers.
    /// </summary>
    public static bool IsDifficultyAllowed(string entryDifficulty, string serverMode)
    {
        if (string.Equals(entryDifficulty, "Brutal", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(serverMode, "Brutal", StringComparison.OrdinalIgnoreCase);
        }
        return true; // Basic / anything else
    }

    // --- TX5: ability phase lookup ---

    /// <summary>
    /// Which phase (1, 2, 3...) does this ability belong to within its source unit's
    /// boss fight? Defaults to 1 — bosses without multi-phase mechanics leave every
    /// ability at Phase=1, so this is a no-op for them.
    /// </summary>
    public int GetAbilityPhase(string abilityName)
    {
        if (Current.AbilityMap != null
            && Current.AbilityMap.TryGetValue(abilityName ?? "", out var entry))
            return entry.Phase;
        return 1;
    }

    // --- TX1: TransformMap helpers ---

    /// <summary>
    /// Admin kill-switch for a transform target. False blocks both unlock rolls
    /// (DeathEventListenerSystemPatch) and activation (.beelz transform).
    /// Default true when no entry exists, so unknown units pass through.
    /// </summary>
    public bool IsTransformUnitEnabled(int unitPrefabGuid)
    {
        if (Current.TransformMap == null || Current.TransformMap.Count == 0) return true;
        string name = new Stunlock.Core.PrefabGUID(unitPrefabGuid).GetPrefabName();
        if (string.IsNullOrEmpty(name)) return true;
        return !Current.TransformMap.TryGetValue(name, out var entry) || entry.Enabled;
    }

    /// <summary>
    /// Difficulty classification for a transform target ("Basic" / "Brutal").
    /// Placeholder for the TX4 difficulty gate. Returns "Basic" if no entry exists.
    /// </summary>
    public string GetTransformDifficulty(int unitPrefabGuid)
    {
        if (Current.TransformMap == null) return "Basic";
        string name = new Stunlock.Core.PrefabGUID(unitPrefabGuid).GetPrefabName();
        if (string.IsNullOrEmpty(name)) return "Basic";
        return Current.TransformMap.TryGetValue(name, out var entry) ? entry.Difficulty : "Basic";
    }

    /// <summary>Tier (1-5) for a transform target. Drives TX6 global scaling. Default 1.</summary>
    public int GetTransformTier(int unitPrefabGuid)
    {
        if (Current.TransformMap == null) return 1;
        string name = new Stunlock.Core.PrefabGUID(unitPrefabGuid).GetPrefabName();
        if (string.IsNullOrEmpty(name)) return 1;
        return Current.TransformMap.TryGetValue(name, out var entry) ? entry.Tier : 1;
    }

    /// <summary>Admin notes for a transform target. Surfaced via .beelz api info. Null if no entry.</summary>
    public string GetTransformNotes(int unitPrefabGuid)
    {
        if (Current.TransformMap == null) return null;
        string name = new Stunlock.Core.PrefabGUID(unitPrefabGuid).GetPrefabName();
        if (string.IsNullOrEmpty(name)) return null;
        return Current.TransformMap.TryGetValue(name, out var entry) && !string.IsNullOrWhiteSpace(entry.Notes)
            ? entry.Notes : null;
    }

    /// <summary>v0.100.0: per-transformation duration override (seconds), or null to inherit the category default.</summary>
    public float? GetTransformDurationOverride(int unitPrefabGuid) =>
        TryGetTransformEntry(unitPrefabGuid, out var entry) ? entry.DurationSeconds : null;

    /// <summary>v0.100.0: per-transformation cooldown override (seconds), or null to inherit the category default.</summary>
    public float? GetTransformCooldownOverride(int unitPrefabGuid) =>
        TryGetTransformEntry(unitPrefabGuid, out var entry) ? entry.CooldownSeconds : null;

    // ---- TX6 (v0.15.0): per-transformation stat scale getters ----
    // All return 1.0 when no TransformMap entry exists — preserves vanilla stats.

    /// <summary>
    /// TX6: damage scale for a transform target (applies to PhysicalPower AND SpellPower
    /// while transformed). 1.0 = no change.
    /// </summary>
    public float GetTransformDamageScale(int unitPrefabGuid) =>
        TryGetTransformEntry(unitPrefabGuid, out var entry) ? entry.DamageScale : 1.0f;

    /// <summary>
    /// TX6: cooldown scale for a transform target. 1.0 = no change. >1.0 = slower
    /// recovery (longer cooldowns). &lt;1.0 = faster recovery (shorter cooldowns).
    /// </summary>
    public float GetTransformCooldownScale(int unitPrefabGuid) =>
        TryGetTransformEntry(unitPrefabGuid, out var entry) ? entry.CooldownScale : 1.0f;

    /// <summary>TX6: health scale for a transform target (MaxHealth multiplier). 1.0 = no change.</summary>
    public float GetTransformHealthScale(int unitPrefabGuid) =>
        TryGetTransformEntry(unitPrefabGuid, out var entry) ? entry.HealthScale : 1.0f;

    /// <summary>TX6: movement-speed scale for a transform target. 1.0 = no change.</summary>
    public float GetTransformMovementSpeedScale(int unitPrefabGuid) =>
        TryGetTransformEntry(unitPrefabGuid, out var entry) ? entry.MovementSpeedScale : 1.0f;

    /// <summary>
    /// TX3 (v0.16.0): is this transform marked as FullReplace mode? When true the
    /// runtime force-enables the native shapeshift visual even if the global
    /// `Transform_NativeShapeshift_Enabled` config is off. Default false.
    /// </summary>
    public bool IsTransformFullReplace(int unitPrefabGuid) =>
        TryGetTransformEntry(unitPrefabGuid, out var entry) && entry.FullReplace;

    /// <summary>
    /// v0.24.5: retrieve the admin's curated per-slot ability template for
    /// a transformation unit, if any. Returns null if no entry or no
    /// SlotTemplate is set. Keys are slot numbers ("1"-"6"); values are
    /// ability-group prefab names. See <see cref="TransformEntry.SlotTemplate"/>
    /// for slot semantics.
    /// </summary>
    public Dictionary<string, string> GetTransformSlotTemplate(int unitPrefabGuid)
    {
        if (!TryGetTransformEntry(unitPrefabGuid, out var entry)) return null;
        if (entry.SlotTemplate is null || entry.SlotTemplate.Count == 0) return null;
        return entry.SlotTemplate;
    }

    /// <summary>
    /// TX7 (v0.17.0): effective power-scaling mode for a transform unit. Resolves
    /// the per-TransformMap-entry override against the global config default.
    /// Returns one of: CuratedScales | PrefabAbsolute | PlayerScaled.
    /// Unknown / typo'd values fall back to CuratedScales (the safe default —
    /// admin-curated values are an explicit opt-in and the data is right there
    /// in the matrix).
    /// </summary>
    public PowerScalingMode GetTransformPowerScalingMode(int unitPrefabGuid)
    {
        // Per-entry override wins if set + parseable.
        if (TryGetTransformEntry(unitPrefabGuid, out var entry)
            && !string.IsNullOrWhiteSpace(entry.PowerScalingMode)
            && TryParsePowerScalingMode(entry.PowerScalingMode, out var perEntry))
        {
            return perEntry;
        }
        // Else use the global default config.
        string raw = Beelzebub.Config.Settings.Transform_PowerScalingMode?.Value;
        return TryParsePowerScalingMode(raw, out var global) ? global : Services.PowerScalingMode.CuratedScales;
    }

    static bool TryParsePowerScalingMode(string raw, out PowerScalingMode mode)
    {
        if (!string.IsNullOrWhiteSpace(raw))
        {
            switch (raw.Trim().ToLowerInvariant())
            {
                case "prefababsolute": mode = Services.PowerScalingMode.PrefabAbsolute; return true;
                case "playerscaled":   mode = Services.PowerScalingMode.PlayerScaled;   return true;
                case "playerleveled":  mode = Services.PowerScalingMode.PlayerLeveled;  return true;
                case "curatedscales":  mode = Services.PowerScalingMode.CuratedScales;  return true;
            }
        }
        mode = Services.PowerScalingMode.CuratedScales;
        return false;
    }

    /// <summary>
    /// TX6: true if any stat scale is != 1.0 for this transform (used to skip the
    /// stat-buffer attach step when the transform has no scaling configured).
    /// </summary>
    public bool HasTransformScales(int unitPrefabGuid)
    {
        if (!TryGetTransformEntry(unitPrefabGuid, out var entry)) return false;
        const float eps = 0.0001f;
        return System.Math.Abs(entry.DamageScale - 1f) > eps
            || System.Math.Abs(entry.CooldownScale - 1f) > eps
            || System.Math.Abs(entry.HealthScale - 1f) > eps
            || System.Math.Abs(entry.MovementSpeedScale - 1f) > eps;
    }

    bool TryGetTransformEntry(int unitPrefabGuid, out TransformEntry entry)
    {
        entry = null;
        if (Current.TransformMap == null) return false;
        string name = new Stunlock.Core.PrefabGUID(unitPrefabGuid).GetPrefabName();
        if (string.IsNullOrEmpty(name)) return false;
        return Current.TransformMap.TryGetValue(name, out entry);
    }

    /// <summary>
    /// Task #41: should this ability be reserved for `.beelz transform` only and
    /// blocked from `.beelz grant`? Resolved in priority order:
    ///  1. AbilityMap[name].TransformOnly = true.
    ///  2. TransformOnlyGuids contains the GUID.
    ///  3. TransformOnlyPatterns substring match.
    /// </summary>
    public bool IsTransformOnly(string abilityName, int abilityGuid)
    {
        if (Current.AbilityMap != null
            && Current.AbilityMap.TryGetValue(abilityName ?? "", out var entry)
            && entry.TransformOnly) return true;
        if (Current.TransformOnlyGuids != null
            && Current.TransformOnlyGuids.Contains(abilityGuid)) return true;
        if (string.IsNullOrEmpty(abilityName) || Current.TransformOnlyPatterns == null) return false;
        foreach (var pattern in Current.TransformOnlyPatterns)
        {
            if (string.IsNullOrEmpty(pattern)) continue;
            if (abilityName.Contains(pattern, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// v0.50.0: is the transform-only reservation ENFORCED for this ability right now? Equals
    /// <see cref="IsTransformOnly"/> AND the global <c>Grant_EnforceTransformOnly</c> switch
    /// (default OFF for this alpha, so every ability is grantable to the normal bar for testing).
    /// Grant / slot / hotkey / cast / devour gates call THIS; read-only surfaces (`api info`,
    /// admin inspection) keep calling the raw <see cref="IsTransformOnly"/> so they still report
    /// the underlying flag regardless of enforcement.
    /// </summary>
    public bool IsTransformOnlyEnforced(string abilityName, int abilityGuid)
        => Beelzebub.Config.Settings.Grant_EnforceTransformOnly.Value
           && IsTransformOnly(abilityName, abilityGuid);

    /// <summary>
    /// C2: find a per-ability rate override matching this ability name. Returns
    /// (true, regular, vblood) for the first matching pattern, else (false, 0, 0).
    /// Caller decides which rate to use based on the kill's source.
    /// </summary>
    public bool TryGetRateOverride(string abilityName, out float rateRegular, out float rateVBlood)
    {
        rateRegular = 0f;
        rateVBlood = 0f;
        if (string.IsNullOrEmpty(abilityName) || Current.DropRateOverrides == null) return false;
        foreach (var entry in Current.DropRateOverrides)
        {
            if (string.IsNullOrEmpty(entry.Pattern)) continue;
            if (abilityName.Contains(entry.Pattern, StringComparison.OrdinalIgnoreCase))
            {
                rateRegular = entry.RateRegular;
                rateVBlood = entry.RateVBlood;
                return true;
            }
        }
        return false;
    }

    public sealed class RulesDto
    {
        public int Version { get; set; } = 1;
        public List<string> DenyPatterns { get; set; } = new();
        public List<string> AllowPatterns { get; set; } = new();
        public List<int> DenyGuids { get; set; } = new();
        public List<int> AllowGuids { get; set; } = new();
        public List<RateOverride> DropRateOverrides { get; set; } = new();

        // v0.50.0: server-wide DEFAULTS for granted-ability scaling, applied to any ability
        // that has NO per-ability AbilityMap entry. A per-ability entry (even with the 1.0
        // default) overrides these. Lets an admin set one global baseline instead of an entry
        // per ability. See GetDamageScale / GetCooldownScale.
        public DefaultsDto Defaults { get; set; } = new();

        // W1 + #41: per-ability admin classification matrix.
        // Key = ability prefab name (exact). Value = matrix of (weapon families,
        // forms, transform-only flag, notes). All fields optional; missing or
        // empty arrays mean "no restriction on that axis". Hot-reloadable.
        // Takes precedence over WeaponFamilyClassifier's name-substring heuristic.
        // See `docs/ABILITY_MAP_FORMAT.md` for usage.
        public Dictionary<string, AbilityEntry> AbilityMap { get; set; } = new();

        // Task #41: bulk substring-based transform-only rules. Useful for
        // "anything matching _Ultimate_VBlood_ is transform-only". Per-ability
        // AbilityMap entries also support TransformOnly individually.
        public List<string> TransformOnlyPatterns { get; set; } = new();
        public List<int> TransformOnlyGuids { get; set; } = new();

        // TX1: per-CHAR_-unit matrix for transformation attributes. Key = CHAR_* prefab
        // name (e.g. "CHAR_Villager_Tailor_VBlood"), value = matrix entry with Enabled
        // kill-switch + difficulty + tier + notes. Hot-reloadable via .beelz admin reload.
        // See `docs/ABILITY_MAP_FORMAT.md` for usage.
        public Dictionary<string, TransformEntry> TransformMap { get; set; } = new();
    }

    /// <summary>
    /// v0.50.0: global default scaling for granted abilities with no per-ability AbilityMap
    /// entry. Both default 1.0 (no change). DamageScale flows through
    /// <see cref="Services.GrantPowerScalingService"/> (the brief power window around a granted
    /// cast); CooldownScale flows through <c>.beelz cast</c> force-casts. A per-ability entry
    /// overrides these.
    /// </summary>
    public sealed class DefaultsDto
    {
        public float DamageScale { get; set; } = 1.0f;
        public float CooldownScale { get; set; } = 1.0f;
    }

    /// <summary>
    /// Per-transformation matrix entry. Used in <see cref="RulesDto.TransformMap"/>.
    /// JSON shape:
    /// <code>
    /// {
    ///   "Enabled": true,
    ///   "Difficulty": "Basic",
    ///   "Tier": 3,
    ///   "DamageScale": 1.0,
    ///   "CooldownScale": 1.0,
    ///   "HealthScale": 1.0,
    ///   "MovementSpeedScale": 1.0,
    ///   "Notes": "Boss-tier transformation; admin notes."
    /// }
    /// </code>
    /// Semantics:
    /// - <c>Enabled</c>: admin kill-switch. False blocks transform unlock rolls
    ///   for this unit AND blocks <c>.beelz transform</c> activation for any
    ///   player who already has the unlock. Default true.
    /// - <c>Difficulty</c>: "Basic" or "Brutal" gate (TX4). Default "Basic".
    /// - <c>Tier</c>: 1-5 power tier the admin assigns. Drives display and
    ///   curation — not directly applied (use the scale fields below for that).
    /// - <c>DamageScale</c>: while transformed, the player's PhysicalPower AND
    ///   SpellPower are scaled by this factor (TX6, v0.15.0). 1.0 = no change,
    ///   1.5 = +50%, 0.5 = -50%. Use to nerf overpowered transforms or buff
    ///   weaker ones. Implemented via ModifyUnitStatBuff_DOTS on the carrier
    ///   buff (TransformBuffService), so the bonus auto-clears on revert.
    /// - <c>CooldownScale</c>: while transformed, scales the player's spell +
    ///   weapon cooldown recovery rate (TX6, v0.15.0). 1.0 = no change. >1.0 =
    ///   slower recovery (longer cooldowns — admin nerf). &lt;1.0 = faster
    ///   recovery (shorter cooldowns — admin buff). Internally maps to
    ///   recovery-rate delta = (1 / CooldownScale) - 1.
    /// - <c>HealthScale</c>: while transformed, scales MaxHealth (TX6, v0.15.0).
    ///   1.0 = no change. 1.5 = +50% HP.
    /// - <c>MovementSpeedScale</c>: while transformed, scales MovementSpeed
    ///   (TX6, v0.15.0). 1.0 = no change.
    /// - <c>Notes</c>: free-text annotation; surfaced via .beelz api info / inspect.
    /// </summary>
    public sealed class TransformEntry
    {
        public bool Enabled { get; set; } = true;
        public string Difficulty { get; set; } = "Basic";
        public int Tier { get; set; } = 1;
        // TX6 (v0.15.0): per-transformation stat scaling. Applied via the carrier
        // buff (TransformBuffService). 1.0 = no change. See class docstring.
        public float DamageScale { get; set; } = 1.0f;
        public float CooldownScale { get; set; } = 1.0f;
        public float HealthScale { get; set; } = 1.0f;
        public float MovementSpeedScale { get; set; } = 1.0f;
        // v0.100.0: per-transformation duration + cooldown OVERRIDES (seconds). null = inherit the
        // category default (Transform_DurationSeconds_* / Transform_CooldownSeconds_*). The cooldown only
        // applies when Transform_CooldownScope is PerTransformation (or as that unit's contribution under
        // PerCategory/Global). Duration applies whenever the active mode is Timed.
        public float? DurationSeconds { get; set; }
        public float? CooldownSeconds { get; set; }
        // TX3 (v0.16.0): FullReplace mode = "be the NPC". When true:
        //   - Force the native shapeshift visual on (Wolf/Bear/Rat/Spider/Toad
        //     when the unit matches), bypassing the global
        //     `Transform_NativeShapeshift_Enabled` config.
        //   - TX8 (v0.17.0): apply BlockEquipmentSwapping so the player can't
        //     swap weapons mid-transform.
        //   - Stat scales (TX6) still apply — combine FullReplace with a curated
        //     DamageScale/HealthScale to build the full "boss form" profile.
        //   - Spell-bar replacement happens via the same carrier buff as
        //     non-FullReplace transforms; no separate code path.
        // Known limitation: native shapeshift visuals drop on first off-form
        // cast (V Rising vanilla behavior, not fixable from the server side).
        // FullReplace is therefore best understood as a 1-3 second cinematic
        // mode plus the stat profile — not a permanent model swap.
        public bool FullReplace { get; set; } = false;
        // TX7 (v0.17.0): per-transformation power-scaling override. Empty/null =
        // inherit the global `Transform_PowerScalingMode` config. One of:
        // "CuratedScales", "PrefabAbsolute", "PlayerScaled". See the
        // Settings.Transform_PowerScalingMode docstring for semantics.
        // Case-insensitive on load; unknown values fall back to global.
        public string PowerScalingMode { get; set; } = null;

        /// <summary>
        /// v0.24.5: admin-curated per-slot ability override. Keys are slot
        /// numbers ("1" through "6") matching V Rising's player UI slots:
        /// <list type="bullet">
        ///   <item>1 = Q (primary weapon attack)</item>
        ///   <item>2 = Space (travel/teleport)</item>
        ///   <item>3 = Shift (typically empty)</item>
        ///   <item>4 = E (secondary weapon attack)</item>
        ///   <item>5 = R (first spell slot)</item>
        ///   <item>6 = C (second spell slot)</item>
        /// </list>
        /// Values are ability-group prefab names (e.g., "AB_Undead_Priest_Elite_Projectile_Group").
        /// Empty/null = use the heuristic classification in
        /// <see cref="TransformService.ReorderForPlayerSlots"/>. Partial maps
        /// are honored — unspecified slots get filled by the heuristic from
        /// the remaining abilities. Admin's curation beats the heuristic.
        /// </summary>
        public Dictionary<string, string> SlotTemplate { get; set; } = null;

        public string Notes { get; set; } = "";
    }

    /// <summary>
    /// Per-ability classification matrix. Used in <see cref="RulesDto.AbilityMap"/>.
    /// JSON shape:
    /// <code>
    /// {
    ///   "Weapons": ["Sword", "GreatSword"],
    ///   "Forms": ["Wolf"],
    ///   "TransformOnly": false,
    ///   "Enabled": true,
    ///   "DamageScale": 1.0,
    ///   "CooldownScale": 1.0,
    ///   "Notes": "optional admin annotation"
    /// }
    /// </code>
    /// Semantics:
    /// - <c>Weapons</c>: ability becomes available when wielding any of these.
    ///   Empty / missing = universal (works regardless of weapon, like Magic).
    /// - <c>Forms</c>: ability is gated to these shapeshift forms. Empty / missing =
    ///   not form-restricted.
    /// - <c>TransformOnly</c>: if true, <c>.beelz grant</c> refuses to bind this
    ///   ability — it can only fire while transformed.
    /// - <c>Enabled</c>: admin kill-switch. False blocks both capture AND use (grant
    ///   and transform pickup). Default true.
    /// - <c>DamageScale</c>: multiplier on damage when the ability is cast via a
    ///   Beelzebub-granted slot/hotkey in normal form. 1.0 = no change. v0.43.5: LIVE —
    ///   applied as a brief Physical+Spell power buff around the cast by
    ///   GrantPowerScalingService (combines with the global Grant_PowerScalingMode).
    /// - <c>CooldownScale</c>: multiplier on cooldown. 1.0 = no change. v0.44.0: LIVE for
    ///   force-casts (<c>.beelz cast</c>); native spell-bar slot cooldowns still pending a hook.
    /// - <c>Notes</c>: free-text annotation, ignored by the runtime.
    /// </summary>
    public sealed class AbilityEntry
    {
        public List<string> Weapons { get; set; } = new();
        public List<string> Forms { get; set; } = new();
        public bool TransformOnly { get; set; }
        public bool Enabled { get; set; } = true;
        // TX4: "Basic" or "Brutal". Default "Basic". Brutal-tagged abilities are
        // blocked from capture on a Basic server (per Server_DifficultyMode config).
        public string Difficulty { get; set; } = "Basic";
        // TX5: which phase of the source unit's boss-fight this ability belongs to (1, 2, 3...).
        // Default 1. Used by `.beelz phase <n>` to swap the transformed spell bar between
        // phase loadouts. Bosses without multi-phase mechanics leave every ability at Phase=1.
        public int Phase { get; set; } = 1;
        // v0.27.2: force-allow this ability even if its name matches a DenyPattern.
        // For curated phase abilities that legitimately carry a deny-patterned name —
        // e.g. Solarus's fallen-angel logic-gate "_Hard_" abilities, which are NOT
        // brutal-only difficulty variants but real phase moves present in both modes.
        // Default false (deny-patterns apply normally).
        public bool AllowDenied { get; set; } = false;
        public float DamageScale { get; set; } = 1.0f;
        public float CooldownScale { get; set; } = 1.0f;
        // v0.46.0 ability tuning (applied at init when Abilities_ApplyConfig; GLOBAL prefab edit):
        // - Interruptible: null = leave baked; true = cast can be cancelled by player action
        //   (dash / raise shield → AbilityInterruptData.ManualInterrupt); false = uninterruptible.
        // - FreeMoveAfterCast: true = the player is freed to move when the CAST finishes
        //   (clamps ModifyMovementDuringCastData to the cast duration instead of the effect's).
        // - CastMovementSpeed: override the move-speed multiplier DURING the cast
        //   (0 = rooted … 1 = full speed). null = leave baked.
        public bool? Interruptible { get; set; }
        public bool FreeMoveAfterCast { get; set; }
        public float? CastMovementSpeed { get; set; }
        // v0.87.0 (test feedback): two more cast modifiers, baked like the fields above.
        // - FreeMoveAfterSeconds: free the player to move N seconds INTO the cast (the cast keeps going).
        //   Sets ModifyMovementDuringCastData.Duration = N directly — so a spell that roots you for its
        //   whole 5s cast can be made to release you after, say, 2s. null = leave baked. (Distinct from
        //   FreeMoveAfterCast, which clamps the lock to the full cast window.)
        // - InterruptOnHit: true = the ability is cancelled when the caster TAKES DAMAGE
        //   (adds InterruptTypes.OnDamageTaken); false = remove that flag. null = leave baked.
        //   (Distinct from Interruptible, which is the player self-cancel / ManualInterrupt flag.)
        public float? FreeMoveAfterSeconds { get; set; }
        public bool? InterruptOnHit { get; set; }
        // v0.65.0 (J1): per-ability ABSOLUTE overrides, baked onto the prefab when
        // Abilities_ApplyConfig (GLOBAL edit, like the cast-tuning fields above):
        // - CooldownSeconds: set the ability's cooldown to this many seconds (null = leave baked).
        //   Also floored by the global Grant_MinimumCooldownSeconds.
        // - MaxRangeOverride: set the ability group's max cast range (null = leave baked).
        // (Effect/buff duration and healing scale are NOT here — they're baked into spawned
        // entities with no single override point; see docs/ABILITY_CONFIG.md.)
        public float? CooldownSeconds { get; set; }
        public float? MaxRangeOverride { get; set; }
        // v0.67.0 (Stage 2): more server-wide shaping, baked onto the prefab chain when
        // Abilities_ApplyConfig (GLOBAL, like the fields above).
        // - ChargesMax / ChargeTimeSeconds: charge-based abilities (AbilityChargesData on the GROUP).
        // - AoeRadius: the ability's area-of-effect max radius (TargetAoE.MaxRange on its spawned
        //   AoE/throw prefab — reached by walking Group→Cast→SpawnPrefab).
        // - ProjectileSpeed: travel speed of the ability's projectile (Projectile.Speed, same walk).
        public int? ChargesMax { get; set; }
        public float? ChargeTimeSeconds { get; set; }
        public float? AoeRadius { get; set; }
        public float? ProjectileSpeed { get; set; }
        // v0.68.0 (Stage 2b): downstream effect shaping (walked from Group→Cast→SpawnPrefab).
        // - EffectDurationSeconds: ABSOLUTE override of the duration of buffs/debuffs this ability
        //   applies (ApplyBuffOnGameplayEvent.OverrideDuration). Idempotent (set, not multiplied).
        // - HealingMultiplier: scales the ability's healing (HealOnGameplayEvent). Applied from the
        //   ORIGINAL baked values (cached) so repeated reloads don't compound. 1.0 = no change.
        public float? EffectDurationSeconds { get; set; }
        public float? HealingMultiplier { get; set; }
        // v0.79.0 (summon governance): per-ability overrides of the server-wide summon CAP
        // (Transform_MaxStacksPerSummonAbility, default 3) and summon TIMEOUT
        // (Transform_SummonLifetimeSeconds, default 30s). Precedence: per-ability override > global
        // default > engine (no limit). null = use the global default. SummonCap 0 = unlimited;
        // SummonTimeoutSeconds 0 = never expires.
        public int? SummonCap { get; set; }
        public float? SummonTimeoutSeconds { get; set; }
        // v0.80.0: per-ability max UNITS produced by a single cast of this summon ability (separate from
        // SummonCap, which limits concurrent USES). e.g. a horde that spawns 10 can be clamped to 3 per
        // cast. null/0 = the ability's natural count. Whichever of summoncap (uses) / summonunits
        // (units-per-cast) is hit first rules.
        public int? SummonUnitsPerCast { get; set; }
        // v0.85.0: FORCE-TIMEOUT — make this ability's otherwise-INDEFINITE spawned buffs expire after this
        // many seconds (adds a LifeTime+Destroy where the buff has none; the case EffectDurationSeconds
        // can't reach). null/0 = leave indefinite. Baked, applied when Abilities_ApplyConfig.
        public float? ForceTimeoutSeconds { get; set; }
        // v0.51.0: admin override for the wire `cat=` badge. One of the AbilityCategory names
        // (Travel/Aoe/Projectile/Melee/Summon/Buff/WeaponSpell/Spell/Other). Empty/null = use
        // the name heuristic (Categorization.ClassifyAbility). Highest precedence.
        public string Category { get; set; }
        public string Notes { get; set; } = "";
    }

    public sealed class RateOverride
    {
        public string Pattern { get; set; } = "";
        public float RateRegular { get; set; } = 0f;
        public float RateVBlood { get; set; } = 0f;
    }
}
