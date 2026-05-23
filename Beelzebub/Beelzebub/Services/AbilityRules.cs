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

    public void Save()
    {
        try
        {
            var tmp = RulesFilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, _json));
            if (File.Exists(RulesFilePath)) File.Replace(tmp, RulesFilePath, null);
            else File.Move(tmp, RulesFilePath);
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"Failed to save ability rules to {RulesFilePath}: {ex}");
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

    static RulesDto NormalizeNulls(RulesDto dto) => new()
    {
        Version = dto.Version,
        DenyPatterns = dto.DenyPatterns ?? new List<string>(),
        AllowPatterns = dto.AllowPatterns ?? new List<string>(),
        DenyGuids = dto.DenyGuids ?? new List<int>(),
        AllowGuids = dto.AllowGuids ?? new List<int>(),
        DropRateOverrides = dto.DropRateOverrides ?? new List<RateOverride>(),
        AbilityMap = NormalizeAbilityMap(dto.AbilityMap),
        TransformOnlyPatterns = dto.TransformOnlyPatterns ?? new List<string>(),
        TransformOnlyGuids = dto.TransformOnlyGuids ?? new List<int>(),
        TransformMap = NormalizeTransformMap(dto.TransformMap),
    };

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
                Difficulty = string.IsNullOrWhiteSpace(entry.Difficulty) ? "Basic" : entry.Difficulty.Trim(),
                Tier = entry.Tier <= 0 ? 1 : entry.Tier,
                // TX6: scale defaults to 1.0 when absent or non-positive. Negative or
                // zero scales make no semantic sense, so we clamp to 1.0 (no change)
                // rather than letting an admin accidentally zero out the player.
                DamageScale = entry.DamageScale > 0f ? entry.DamageScale : 1.0f,
                CooldownScale = entry.CooldownScale > 0f ? entry.CooldownScale : 1.0f,
                HealthScale = entry.HealthScale > 0f ? entry.HealthScale : 1.0f,
                MovementSpeedScale = entry.MovementSpeedScale > 0f ? entry.MovementSpeedScale : 1.0f,
                FullReplace = entry.FullReplace,
                PowerScalingMode = string.IsNullOrWhiteSpace(entry.PowerScalingMode) ? null : entry.PowerScalingMode.Trim(),
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
                Difficulty = string.IsNullOrWhiteSpace(entry.Difficulty) ? "Basic" : entry.Difficulty.Trim(),
                Phase = entry.Phase <= 0 ? 1 : entry.Phase,
                AllowDenied = entry.AllowDenied,
                DamageScale = entry.DamageScale > 0f ? entry.DamageScale : 1.0f,
                CooldownScale = entry.CooldownScale > 0f ? entry.CooldownScale : 1.0f,
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
    /// Per-ability damage multiplier (1.0 = no change). Used by the W5 damage hook
    /// once that ships. Until then, this is just stored — no runtime effect.
    /// </summary>
    public float GetDamageScale(string abilityName)
    {
        if (Current.AbilityMap != null
            && Current.AbilityMap.TryGetValue(abilityName ?? "", out var entry))
            return entry.DamageScale;
        return 1.0f;
    }

    /// <summary>
    /// Per-ability cooldown multiplier (1.0 = no change). Used by the W5 cooldown
    /// hook once that ships. Until then, this is just stored — no runtime effect.
    /// </summary>
    public float GetCooldownScale(string abilityName)
    {
        if (Current.AbilityMap != null
            && Current.AbilityMap.TryGetValue(abilityName ?? "", out var entry))
            return entry.CooldownScale;
        return 1.0f;
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
    ///   Beelzebub-granted slot. 1.0 = no change. Requires the W5 runtime to be live
    ///   (until then, this value is inert).
    /// - <c>CooldownScale</c>: multiplier on cooldown when the ability is cast via a
    ///   Beelzebub-granted slot. 1.0 = no change. Same W5-dependent caveat.
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
        public string Notes { get; set; } = "";
    }

    public sealed class RateOverride
    {
        public string Pattern { get; set; } = "";
        public float RateRegular { get; set; } = 0f;
        public float RateVBlood { get; set; } = 0f;
    }
}
