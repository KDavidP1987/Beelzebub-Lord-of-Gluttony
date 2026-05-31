using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BepInEx;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.24.0 — R1 ability metadata lookup.
///
/// Provides human-readable names, descriptions, school/type tagging, and
/// source-NPC info for V Rising abilities. Data sourced from a curated
/// JSON shipped with the mod (scraped from <c>vrising.gaming.tools</c> at
/// build time) and merged at lookup time with runtime ECS data (cooldown,
/// cast time, range) read from the live prefab entities via
/// <see cref="PrefabCollectionSystem"/>.
///
/// Lookup priority for each field:
/// 1. Admin override (file: <c>BepInEx/config/kdpen.Beelzebub/ability_metadata_overrides.json</c>)
/// 2. Shipped curated data (embedded resource <c>ability_metadata.json</c>)
/// 3. Runtime ECS prefab entity (cooldown, cast time, range)
/// 4. Humanized prefab name fallback
///
/// This is the "reference" data — what the ability IS. The companion
/// <see cref="AbilityRules"/> service holds "policy" data — what admins
/// CHOOSE to do with each ability (enabled/disabled, transform-only,
/// damage-scale overrides, etc.). The two are kept separate on disk so
/// re-shipping metadata never clobbers admin policy.
/// </summary>
internal sealed class AbilityMetadataService
{
    static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Shipped (embedded) entries keyed by AbilityGroup PrefabGuid integer.</summary>
    readonly Dictionary<int, AbilityMetadataEntry> _shipped = new();

    /// <summary>Admin overrides keyed by AbilityGroup PrefabGuid. Wins over shipped.</summary>
    readonly Dictionary<int, AbilityMetadataEntry> _overrides = new();

    /// <summary>
    /// v0.28.0 (#9): unit/NPC GUID → human display name, aggregated from the
    /// shipped metadata's per-ability SourceNpcs. Lets transforms/messages show
    /// "Dracula the Immortal King" instead of "CHAR_Vampire_Dracula_VBlood".
    /// </summary>
    readonly Dictionary<int, string> _unitNames = new();

    // v0.99.0: reverse index — unit/NPC GUID → every ability GUID whose SourceNpcs include that unit.
    // Built from the SAME metadata scan as _unitNames. This is the full CROSS-PHASE kit (the scraper maps
    // each AB_<Boss>_* ability to its source boss regardless of which combat phase uses it), so Devour /
    // capture can grant a boss's complete kit instead of only the base/phase-1 ability bar.
    readonly Dictionary<int, List<int>> _abilitiesByUnit = new();

    /// <summary>v0.99.0: every ability GUID the metadata attributes to this unit (all phases). Empty if none.</summary>
    public IReadOnlyList<int> GetAbilitiesForUnit(int unitGuid)
        => _abilitiesByUnit.TryGetValue(unitGuid, out var list) ? list : System.Array.Empty<int>();

    public string OverridesFilePath { get; }
    public int ShippedCount => _shipped.Count;
    public int OverrideCount => _overrides.Count;
    public int UnitNameCount => _unitNames.Count;

    public AbilityMetadataService()
    {
        var dir = Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_GUID);
        Directory.CreateDirectory(dir);
        OverridesFilePath = Path.Combine(dir, "ability_metadata_overrides.json");
    }

    public void Load()
    {
        LoadShippedFromEmbeddedResource();
        LoadOverridesFromDisk();
        BuildUnitNameIndex();
    }

    /// <summary>
    /// v0.28.0 (#9): aggregate unit display names from every entry's SourceNpcs.
    /// SourceNpcs carry MULTIPLE localized names per GUID (EN/IT/PT/HU/…); pick the
    /// English one via an ASCII + "the"-title heuristic (V Rising V-Bloods are
    /// overwhelmingly named "X the Y" in English, e.g. "Dracula the Immortal King").
    /// Admin overrides win. Units absent here fall back to a humanized prefab name.
    /// </summary>
    void BuildUnitNameIndex()
    {
        _unitNames.Clear();
        _abilitiesByUnit.Clear();
        var candidates = new Dictionary<int, List<string>>();

        // v0.99.0: iterate KVPs so we have each ability GUID (the dict KEY) for the reverse index.
        void Collect(IEnumerable<KeyValuePair<int, AbilityMetadataEntry>> entries)
        {
            foreach (var (abilityGuid, e) in entries)
            {
                if (e?.SourceNpcs == null) continue;
                foreach (var npc in e.SourceNpcs)
                {
                    if (npc == null) continue;
                    if (!string.IsNullOrWhiteSpace(npc.Name))
                    {
                        if (!candidates.TryGetValue(npc.Guid, out var list))
                            candidates[npc.Guid] = list = new List<string>();
                        if (!list.Contains(npc.Name)) list.Add(npc.Name);
                    }
                    // v0.99.0: unit → abilities reverse index.
                    if (abilityGuid != 0)
                    {
                        if (!_abilitiesByUnit.TryGetValue(npc.Guid, out var abils))
                            _abilitiesByUnit[npc.Guid] = abils = new List<int>();
                        if (!abils.Contains(abilityGuid)) abils.Add(abilityGuid);
                    }
                }
            }
        }
        Collect(_shipped);
        Collect(_overrides);

        foreach (var (guid, names) in candidates)
        {
            var best = PickEnglishName(names);
            if (!string.IsNullOrWhiteSpace(best)) _unitNames[guid] = best;
        }

        Core.Log.LogInfo($"[AbilityMetadata] Built unit-name index: {_unitNames.Count} units from SourceNpcs; unit→abilities index: {_abilitiesByUnit.Count} units.");
    }

    static bool IsAscii(string s)
    {
        foreach (char c in s) if (c > 127) return false;
        return true;
    }

    static string PickEnglishName(List<string> names)
    {
        string best = null;
        int bestScore = int.MinValue;
        foreach (var n in names)
        {
            // Prefer ASCII-only (filters Cyrillic/accented localizations) and the
            // English V-Blood "X the Y" title pattern.
            int score = (IsAscii(n) ? 2 : 0)
                      + (n.IndexOf(" the ", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0);
            if (score > bestScore) { bestScore = score; best = n; }
        }
        return best;
    }

    /// <summary>
    /// v0.28.0 (#9): resolve a unit/NPC GUID to a human display name. Priority:
    /// admin override (a SourceNpcs entry in the overrides file) → shipped
    /// SourceNpcs English name → humanized prefab name (CHAR_/faction/_VBlood
    /// stripped). Never returns null/empty.
    /// </summary>
    public string ResolveUnitName(int unitGuid)
    {
        if (_unitNames.TryGetValue(unitGuid, out var name) && !string.IsNullOrWhiteSpace(name))
            return name;
        return HumanizeUnitPrefab(new PrefabGUID(unitGuid).GetPrefabName());
    }

    /// <summary>
    /// v0.58.0: friendly ability-group display name — the curated override/shipped <c>Name</c> when
    /// present, else the humanized prefab name. Dict-only (no ECS probe), so it's cheap to call per
    /// row of a list/catalog stream. Never returns null/empty.
    /// </summary>
    public string ResolveAbilityName(int abilityGuid)
    {
        if (_overrides.TryGetValue(abilityGuid, out var over) && !string.IsNullOrWhiteSpace(over.Name))
            return over.Name;
        if (_shipped.TryGetValue(abilityGuid, out var ship) && !string.IsNullOrWhiteSpace(ship.Name))
            return ship.Name;
        return new PrefabGUID(abilityGuid).GetPrefabName().Humanize();
    }

    /// <summary>
    /// Fallback prettifier for a CHAR_ prefab name with no curated/source name.
    /// "CHAR_Blackfang_Morgana_VBlood" → "Blackfang Morgana".
    /// </summary>
    public static string HumanizeUnitPrefab(string prefabName)
    {
        if (string.IsNullOrWhiteSpace(prefabName)) return "Unknown";
        string s = prefabName;
        // Strip only the CHAR_ prefix and pure role/tier suffixes — keep clan/faction
        // words since they're often part of the name ("Blackfang Morgana"). Named
        // bosses resolve via SourceNpcs anyway; this is just the generic fallback.
        if (s.StartsWith("CHAR_", StringComparison.OrdinalIgnoreCase)) s = s.Substring(5);
        foreach (var suf in new[] { "_VBlood", "_Standard", "_Servant", "_Minion", "_Base" })
            if (s.EndsWith(suf, StringComparison.OrdinalIgnoreCase)) { s = s.Substring(0, s.Length - suf.Length); break; }
        return s.Replace('_', ' ').Trim().Humanize();
    }

    void LoadShippedFromEmbeddedResource()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            // Resource name follows the project's default naming: <RootNamespace>.<RelativePath>
            var name = "Beelzebub.Resources.ability_metadata.json";
            using var stream = asm.GetManifestResourceStream(name);
            if (stream == null)
            {
                // Resource not embedded — fine for early dev; we just have no shipped data.
                Core.Log.LogInfo($"[AbilityMetadata] No embedded ability_metadata.json found. " +
                    $"Lookups will fall back to humanized prefab names + ECS data only.");
                return;
            }
            using var reader = new StreamReader(stream);
            string raw = reader.ReadToEnd();
            var dto = JsonSerializer.Deserialize<AbilityMetadataFile>(raw, _json);
            if (dto?.Abilities is null) return;
            foreach (var (key, entry) in dto.Abilities)
            {
                if (int.TryParse(key, out int guid))
                {
                    _shipped[guid] = entry;
                }
            }
            Core.Log.LogInfo($"[AbilityMetadata] Loaded {_shipped.Count} shipped ability entries from embedded resource.");
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[AbilityMetadata] Failed to load shipped data: {ex}");
        }
    }

    void LoadOverridesFromDisk()
    {
        if (!File.Exists(OverridesFilePath)) return;
        try
        {
            string raw = File.ReadAllText(OverridesFilePath);
            var dto = JsonSerializer.Deserialize<AbilityMetadataFile>(raw, _json);
            if (dto?.Abilities is null) return;
            foreach (var (key, entry) in dto.Abilities)
            {
                if (int.TryParse(key, out int guid))
                {
                    _overrides[guid] = entry;
                }
            }
            Core.Log.LogInfo($"[AbilityMetadata] Loaded {_overrides.Count} admin override entries from {OverridesFilePath}.");
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[AbilityMetadata] Failed to load overrides from {OverridesFilePath}: {ex}");
        }
    }

    /// <summary>
    /// Resolve full ability info — merges shipped + override + ECS prefab data.
    /// Returns a record with whatever fields are available. Never returns null.
    /// </summary>
    public AbilityInfo Resolve(int abilityGroupGuid)
    {
        _shipped.TryGetValue(abilityGroupGuid, out var ship);
        _overrides.TryGetValue(abilityGroupGuid, out var over);

        // Prefer override > shipped.
        var name = over?.Name ?? ship?.Name;
        var description = over?.Description ?? ship?.Description;
        var school = over?.School ?? ship?.School;
        var type = over?.Type ?? ship?.Type;
        var categories = over?.Categories ?? ship?.Categories;
        var icon = over?.Icon ?? ship?.Icon;
        var sourceNpcs = over?.SourceNpcs ?? ship?.SourceNpcs;
        var parameters = over?.Parameters ?? ship?.Parameters;
        // Incompatible follows the same precedence — override wins if set;
        // shipped is the curated default list.
        var incompatible = over?.Incompatible ?? ship?.Incompatible ?? false;
        var incompatibleReason = over?.IncompatibleReason ?? ship?.IncompatibleReason;

        // Humanized fallback for missing name.
        if (string.IsNullOrEmpty(name))
        {
            name = new PrefabGUID(abilityGroupGuid).GetPrefabName().Humanize();
        }

        // ECS-derived runtime fields.
        var runtime = ProbeRuntimePrefabFields(abilityGroupGuid);

        return new AbilityInfo
        {
            AbilityGroupGuid = abilityGroupGuid,
            Name = name,
            Description = description,
            School = school,
            Type = type,
            Categories = categories,
            Icon = icon,
            SourceNpcs = sourceNpcs,
            Parameters = parameters,
            CooldownSeconds = runtime.cooldown,
            CastTimeSeconds = runtime.castTime,
            BehaviorType = runtime.behaviorType,
            MinRange = runtime.minRange,
            MaxRange = runtime.maxRange,
            HasShippedEntry = ship is not null,
            HasOverrideEntry = over is not null,
            Incompatible = incompatible,
            IncompatibleReason = incompatibleReason,
        };
    }

    /// <summary>
    /// v0.58.0: lightweight curated-text lookup for the catalog stream — returns just the
    /// shipped/override <c>Description</c> (with %param% substitution) + <c>School</c>, WITHOUT the
    /// per-call ECS prefab probe that <see cref="Resolve"/> runs. The catalog emits the full
    /// capturable universe (~1,400 rows/scan), so calling <see cref="Resolve"/> per row would probe
    /// the ECS prefab thousands of times for fields the catalog doesn't even use. Returns false when
    /// neither a description nor a school is curated for this guid (caller emits the "-"/none defaults).
    /// </summary>
    /// <summary>
    /// v0.100.0 (ApiVersion 21): the primary (first valid) SOURCE NPC for an ability group — its GUID and a
    /// resolved English display name — from the curated SourceNpcs (override beats shipped). Lets
    /// `catalog-ability` carry unit=/unitguid= for UNCAPTURED abilities. Returns false if unknown.
    /// </summary>
    public bool TryGetPrimarySourceNpc(int abilityGroupGuid, out int npcGuid, out string npcName)
    {
        npcGuid = 0; npcName = null;
        _overrides.TryGetValue(abilityGroupGuid, out var over);
        _shipped.TryGetValue(abilityGroupGuid, out var ship);
        var npcs = (over?.SourceNpcs is { Count: > 0 }) ? over.SourceNpcs : ship?.SourceNpcs;
        if (npcs == null) return false;
        foreach (var n in npcs)
        {
            if (n == null || n.Guid == 0) continue;
            npcGuid = n.Guid;
            npcName = ResolveUnitName(n.Guid);   // best English name (never null/empty)
            return true;
        }
        return false;
    }

    public bool TryGetCuratedText(int abilityGroupGuid, out string description, out string school)
    {
        description = null;
        school = null;
        _shipped.TryGetValue(abilityGroupGuid, out var ship);
        _overrides.TryGetValue(abilityGroupGuid, out var over);
        if (ship == null && over == null) return false;

        var desc = over?.Description ?? ship?.Description;
        var parameters = over?.Parameters ?? ship?.Parameters;
        if (!string.IsNullOrWhiteSpace(desc) && parameters != null)
            foreach (var kv in parameters) desc = desc.Replace("%" + kv.Key + "%", kv.Value);
        description = string.IsNullOrWhiteSpace(desc) ? null : desc;

        var sch = over?.School ?? ship?.School;
        school = string.IsNullOrWhiteSpace(sch) ? null : sch;

        return description != null || school != null;
    }

    /// <summary>
    /// Probe the live prefab entity for cooldown / cast time / range.
    /// AbilityGroup prefab itself doesn't carry these — they're on the
    /// AbilityCast referenced by AbilityGroupStartAbilitiesBuffer[0].
    /// All fields nullable to signal "not found / not applicable".
    /// </summary>
    static (float? cooldown, float? castTime, string behaviorType, float? minRange, float? maxRange)
        ProbeRuntimePrefabFields(int abilityGroupGuid)
    {
        try
        {
            if (Core.PrefabCollectionSystem is null) return (null, null, null, null, null);

            var groupGuid = new PrefabGUID(abilityGroupGuid);
            if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(groupGuid, out Entity groupEntity)) return (null, null, null, null, null);
            if (!groupEntity.Exists()) return (null, null, null, null, null);

            // AbilityGroupInfo gives min/max range + behavior type.
            string behaviorType = null;
            float? minRange = null, maxRange = null;
            if (groupEntity.TryGetComponent<AbilityGroupInfo>(out var info))
            {
                behaviorType = info.BehaviorType.ToString();
                minRange = info.MinRange;
                maxRange = info.MaxRange;
            }

            // AbilityCast prefab via the StartAbilitiesBuffer.
            float? cooldown = null, castTime = null;
            if (Core.EntityManager.HasBuffer<AbilityGroupStartAbilitiesBuffer>(groupEntity))
            {
                var buf = Core.EntityManager.GetBuffer<AbilityGroupStartAbilitiesBuffer>(groupEntity);
                if (buf.Length > 0)
                {
                    var castGuid = buf[0].PrefabGUID;
                    if (Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(castGuid, out Entity castEntity)
                        && castEntity.Exists())
                    {
                        if (castEntity.TryGetComponent<AbilityCooldownData>(out var cd)) cooldown = cd.Cooldown;
                        if (castEntity.TryGetComponent<AbilityCastTimeData>(out var ct)) castTime = ct.MaxCastTime;
                    }
                }
            }

            return (cooldown, castTime, behaviorType, minRange, maxRange);
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[AbilityMetadata] ProbeRuntimePrefabFields failed for {abilityGroupGuid}: {ex.Message}");
            return (null, null, null, null, null);
        }
    }

    /// <summary>
    /// Substring search across shipped+override names. Returns up to <paramref name="maxResults"/>
    /// matches, sorted by exact-match then prefix-match then substring.
    /// </summary>
    public List<AbilityInfo> SearchByName(string fragment, int maxResults = 25)
    {
        if (string.IsNullOrWhiteSpace(fragment)) return new List<AbilityInfo>();
        fragment = fragment.Trim();

        var hits = new List<(int score, AbilityInfo info)>();
        var seen = new HashSet<int>();

        void TryAdd(int guid, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (!seen.Add(guid)) return;
            int score;
            if (name.Equals(fragment, StringComparison.OrdinalIgnoreCase)) score = 0;
            else if (name.StartsWith(fragment, StringComparison.OrdinalIgnoreCase)) score = 1;
            else if (name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) score = 2;
            else return;
            hits.Add((score, Resolve(guid)));
        }

        foreach (var (guid, entry) in _shipped) TryAdd(guid, entry.Name);
        foreach (var (guid, entry) in _overrides) TryAdd(guid, entry.Name);

        return hits.OrderBy(h => h.score).Take(maxResults).Select(h => h.info).ToList();
    }

    /// <summary>Save the current overrides dictionary back to disk.</summary>
    public void SaveOverrides()
    {
        try
        {
            var file = new AbilityMetadataFile
            {
                Version = 1,
                Abilities = _overrides.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            };
            string tmp = OverridesFilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(file, _json));
            if (File.Exists(OverridesFilePath)) File.Replace(tmp, OverridesFilePath, null);
            else File.Move(tmp, OverridesFilePath);
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[AbilityMetadata] SaveOverrides failed: {ex}");
        }
    }

    public void SetOverride(int abilityGroupGuid, AbilityMetadataEntry entry)
    {
        _overrides[abilityGroupGuid] = entry;
        SaveOverrides();
    }

    public bool RemoveOverride(int abilityGroupGuid)
    {
        if (!_overrides.Remove(abilityGroupGuid)) return false;
        SaveOverrides();
        return true;
    }
}

/// <summary>Top-level JSON wrapper for the metadata file.</summary>
internal sealed class AbilityMetadataFile
{
    public int Version { get; set; } = 1;
    public Dictionary<string, AbilityMetadataEntry> Abilities { get; set; } = new();
}

/// <summary>One ability's curated entry. Mirrors the schema produced by the scraper.</summary>
internal sealed class AbilityMetadataEntry
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string School { get; set; }
    public string Type { get; set; }
    public List<string> Categories { get; set; }
    public string Icon { get; set; }
    public List<NpcRef> SourceNpcs { get; set; }
    public Dictionary<string, string> Parameters { get; set; }

    /// <summary>
    /// v0.24.2: known-broken abilities — the cast animation fires but the
    /// effect doesn't complete properly when triggered by a player. Causes
    /// catalogued per <see cref="IncompatibleReason"/>. Surfaced as a
    /// warning in <c>.beelz info</c> / <c>.beelz active</c> so the player
    /// understands why the ability they slotted "doesn't seem to work".
    /// </summary>
    public bool Incompatible { get; set; }

    /// <summary>
    /// Why the ability is broken when cast by a player. Free-text but the
    /// well-known categories from the chain audit (project_ability_chain_audit.md):
    /// - "Offset" — projectile spawns at hardcoded NPC bone offset, lands at wrong height
    /// - "AnimRig" — TravelBuff + HideWeapon bound to NPC skeleton bones; player doesn't have them
    /// - "OwnerChain" — multi-step chain loses owner reference on intermediate entities
    /// - "TeamFilter" — spawned children filter on Team=1 (NPC), miss enemies
    /// - "AOE-Proxy" — ProxySpawner pattern with NPC team; projectiles fire but don't hit
    /// - "Corpse-Required" — natural chain animates existing corpses; player has none in range
    /// </summary>
    public string IncompatibleReason { get; set; }
}

internal sealed class NpcRef
{
    public int Guid { get; set; }
    public string Name { get; set; }
}

/// <summary>Merged record returned by <see cref="AbilityMetadataService.Resolve"/>.</summary>
internal sealed class AbilityInfo
{
    public int AbilityGroupGuid { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string School { get; set; }
    public string Type { get; set; }
    public List<string> Categories { get; set; }
    public string Icon { get; set; }
    public List<NpcRef> SourceNpcs { get; set; }
    public Dictionary<string, string> Parameters { get; set; }
    public float? CooldownSeconds { get; set; }
    public float? CastTimeSeconds { get; set; }
    public string BehaviorType { get; set; }
    public float? MinRange { get; set; }
    public float? MaxRange { get; set; }
    public bool HasShippedEntry { get; set; }
    public bool HasOverrideEntry { get; set; }
    public bool Incompatible { get; set; }
    public string IncompatibleReason { get; set; }
}
