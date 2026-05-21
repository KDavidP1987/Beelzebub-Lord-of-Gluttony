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
    };

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
            // Internal / debug
            "_Test_", "_Internal_", "_DEBUG_",
        },
        AllowPatterns = new List<string>(),
        DenyGuids = new List<int>(),
        AllowGuids = new List<int>(),
    };

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
    }

    public sealed class RateOverride
    {
        public string Pattern { get; set; } = "";
        public float RateRegular { get; set; } = 0f;
        public float RateVBlood { get; set; } = 0f;
    }
}
