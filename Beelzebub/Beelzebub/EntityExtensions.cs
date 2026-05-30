using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub;

internal static class EntityExtensions
{
    public static bool Exists(this Entity entity) =>
        entity != Entity.Null && Core.EntityManager.Exists(entity);

    public static bool TryGetComponent<T>(this Entity entity, out T component) where T : unmanaged
    {
        if (!entity.Exists() || !Core.EntityManager.HasComponent<T>(entity))
        {
            component = default;
            return false;
        }
        component = Core.EntityManager.GetComponentData<T>(entity);
        return true;
    }

    public static bool Has<T>(this Entity entity) =>
        entity.Exists() && Core.EntityManager.HasComponent<T>(entity);

    /// <summary>
    /// Borrow a component, mutate via callback, write it back. Mirrors Bloodcraft's
    /// VExtensions.With pattern for IL2CPP-safe in-place struct edits. No-op (and
    /// logs a warning) if the component is missing — call AddComponent first if you
    /// need to introduce it.
    /// </summary>
    public delegate void RefAction<T>(ref T value) where T : unmanaged;
    public static void With<T>(this Entity entity, RefAction<T> mutator) where T : unmanaged
    {
        if (!entity.Has<T>())
        {
            Core.Log.LogWarning($"[Beelz] Entity.With<{typeof(T).Name}>: component missing on {entity}");
            return;
        }
        T value = Core.EntityManager.GetComponentData<T>(entity);
        mutator(ref value);
        Core.EntityManager.SetComponentData(entity, value);
    }

    public static bool IsPlayer(this Entity entity) =>
        entity.Has<PlayerCharacter>();

    public static bool TryGetPlayer(this Entity entity, out Entity playerCharacter)
    {
        if (entity.IsPlayer()) { playerCharacter = entity; return true; }
        playerCharacter = Entity.Null;
        return false;
    }

    public static ulong GetSteamId(this Entity playerCharacter)
    {
        if (playerCharacter.TryGetComponent<PlayerCharacter>(out var pc)
            && pc.UserEntity.TryGetComponent<User>(out var user))
        {
            return user.PlatformId;
        }
        return 0;
    }

    public static PrefabGUID GetPrefabGuid(this Entity entity) =>
        entity.TryGetComponent<PrefabGUID>(out var g) ? g : default;

    public static string GetPrefabName(this PrefabGUID guid) =>
        Core.PrefabNames.TryGetValue(guid._Value, out var name) ? name : $"PrefabGuid({guid._Value})";

    /// <summary>
    /// Produce a human-friendly label from a raw prefab name. Strips the well-known
    /// AB_/CHAR_/Buff_ prefixes plus _Group/_AbilityGroup suffixes, then splits
    /// CamelCase / underscore-joined tokens so "AB_Bandit_BombThrow_AbilityGroup"
    /// reads as "Bandit Bomb Throw". Not localized — that needs LocalizationManager.
    /// </summary>
    public static string Humanize(this string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName)) return "";
        string s = prefabName;
        foreach (var prefix in new[] { "AB_", "CHAR_", "Buff_", "Item_", "TM_", "SpellMod_" })
        {
            if (s.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
            { s = s.Substring(prefix.Length); break; }
        }
        foreach (var suffix in new[] { "_AbilityGroup", "_Group", "_Cast", "_Throw", "_VBlood" })
        {
            if (s.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
            { s = s.Substring(0, s.Length - suffix.Length); break; }
        }
        s = s.Replace('_', ' ');
        // Split CamelCase: insert space before each uppercase that follows a lowercase or digit.
        var sb = new System.Text.StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(s[i - 1]) || char.IsDigit(s[i - 1])))
                sb.Append(' ');
            sb.Append(c);
        }
        // Collapse multiple spaces.
        var collapsed = new System.Text.StringBuilder(sb.Length);
        char prev = ' ';
        foreach (char c in sb.ToString())
        {
            if (c == ' ' && prev == ' ') continue;
            collapsed.Append(c);
            prev = c;
        }
        return collapsed.ToString().Trim();
    }

    /// <summary>
    /// Locate a player's character entity by SteamId. Walks all User entities each call —
    /// fine for low-frequency uses (auto-revert, admin lookups); cache if called per-frame.
    /// </summary>
    /// <summary>
    /// Enumerate online PlayerCharacter entities within `radius` of the given position.
    /// Returns the killer first (if killer is itself a player and within range),
    /// then any other players in proximity. Caller is responsible for filtering
    /// out duplicates if needed.
    /// </summary>
    public static System.Collections.Generic.List<Entity> FindPlayersNear(Unity.Mathematics.float3 position, float radius)
    {
        var result = new System.Collections.Generic.List<Entity>();
        if (Core.EntityManager.World is null) return result;
        var query = Core.EntityManager.CreateEntityQuery(
            Unity.Entities.ComponentType.ReadOnly<ProjectM.PlayerCharacter>(),
            Unity.Entities.ComponentType.ReadOnly<Unity.Transforms.LocalToWorld>());
        var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        try
        {
            float r2 = radius * radius;
            for (int i = 0; i < entities.Length; i++)
            {
                if (!entities[i].TryGetComponent<Unity.Transforms.LocalToWorld>(out var ltw)) continue;
                var p = ltw.Position;
                float dx = p.x - position.x;
                float dz = p.z - position.z; // V Rising is XZ-plane; Y is height
                if (dx * dx + dz * dz <= r2) result.Add(entities[i]);
            }
        }
        finally
        {
            entities.Dispose();
        }
        return result;
    }

    /// <summary>
    /// Find a player's character entity by case-insensitive substring match on their CharacterName.
    /// Returns Entity.Null if no match or multiple ambiguous matches.
    /// </summary>
    public static Entity FindCharacterByName(string nameFragment, out ulong steamId, out string fullName)
    {
        steamId = 0;
        fullName = null;
        if (string.IsNullOrWhiteSpace(nameFragment) || Core.EntityManager.World is null) return Entity.Null;
        var query = Core.EntityManager.CreateEntityQuery(Unity.Entities.ComponentType.ReadOnly<ProjectM.Network.User>());
        var users = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        Entity match = Entity.Null;
        int matchCount = 0;
        try
        {
            for (int i = 0; i < users.Length; i++)
            {
                if (!users[i].TryGetComponent<ProjectM.Network.User>(out var u)) continue;
                string name = u.CharacterName.ToString();
                if (string.IsNullOrEmpty(name)) continue;
                if (name.Contains(nameFragment, System.StringComparison.OrdinalIgnoreCase))
                {
                    Entity character = u.LocalCharacter._Entity;
                    if (!character.Exists()) continue;
                    match = character;
                    steamId = u.PlatformId;
                    fullName = name;
                    matchCount++;
                }
            }
        }
        finally
        {
            users.Dispose();
        }
        return matchCount == 1 ? match : Entity.Null;
    }

    /// <summary>
    /// v0.83.0 (#8): enumerate every known user (online + persisted offline) with their steamId, character
    /// name, and admin flag — for the collection leaderboard (which excludes admins).
    /// </summary>
    public static System.Collections.Generic.List<(ulong steamId, string name, bool isAdmin)> AllUsers()
    {
        var result = new System.Collections.Generic.List<(ulong, string, bool)>();
        if (Core.EntityManager.World is null) return result;
        var query = Core.EntityManager.CreateEntityQuery(Unity.Entities.ComponentType.ReadOnly<ProjectM.Network.User>());
        var users = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        try
        {
            for (int i = 0; i < users.Length; i++)
            {
                if (!users[i].TryGetComponent<ProjectM.Network.User>(out var u)) continue;
                ulong sid = u.PlatformId;
                if (sid == 0) continue;
                string name = u.CharacterName.ToString();
                result.Add((sid, string.IsNullOrEmpty(name) ? sid.ToString() : name, u.IsAdmin));
            }
        }
        finally { users.Dispose(); }
        return result;
    }

    /// <summary>
    /// C3: classify a killed unit's UnitLevel into a tier multiplier from settings.
    /// Returns 1.0 if the unit has no UnitLevel component (no effect).
    /// </summary>
    public static float ResolveTierMultiplier(this Unity.Entities.Entity died)
    {
        if (!died.TryGetComponent<ProjectM.UnitLevel>(out var ul)) return 1f;
        int level = ul.Level._Value;
        if (level >= Beelzebub.Config.Settings.Capture_TierHighThreshold.Value)
            return Beelzebub.Config.Settings.Capture_TierMultiplier_High.Value;
        if (level >= Beelzebub.Config.Settings.Capture_TierMidThreshold.Value)
            return Beelzebub.Config.Settings.Capture_TierMultiplier_Mid.Value;
        return Beelzebub.Config.Settings.Capture_TierMultiplier_Low.Value;
    }

    /// <summary>
    /// Determine whether the given unit prefab is a V-Blood by checking its prefab entity
    /// for VBloodUnit or VBloodConsumeSource components.
    /// </summary>
    public static bool IsVBloodUnit(this PrefabGUID unitGuid)
    {
        if (Core.PrefabCollectionSystem is null) return false;
        if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(unitGuid, out Entity prefabEntity)) return false;
        return prefabEntity.Has<ProjectM.VBloodUnit>() || prefabEntity.Has<ProjectM.VBloodConsumeSource>();
    }

    public static Entity FindCharacterBySteamId(ulong steamId)
    {
        if (steamId == 0 || Core.EntityManager.World is null) return Entity.Null;
        var query = Core.EntityManager.CreateEntityQuery(Unity.Entities.ComponentType.ReadOnly<ProjectM.Network.User>());
        var users = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        try
        {
            for (int i = 0; i < users.Length; i++)
            {
                if (!users[i].TryGetComponent<ProjectM.Network.User>(out var user)) continue;
                if (user.PlatformId != steamId) continue;
                Entity character = user.LocalCharacter._Entity;
                if (character.Exists()) return character;
            }
            return Entity.Null;
        }
        finally
        {
            users.Dispose();
        }
    }
}
