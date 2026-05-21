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
    /// Locate a player's character entity by SteamId. Walks all User entities each call —
    /// fine for low-frequency uses (auto-revert, admin lookups); cache if called per-frame.
    /// </summary>
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
