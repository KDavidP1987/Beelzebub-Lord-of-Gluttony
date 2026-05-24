using System;
using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.40.0: force-cast an arbitrary ability group on a player on demand, via
/// <c>DebugEventsSystem.CastAbilityServerDebugEvent</c> (the same server-authoritative
/// entry point Bloodcraft's <c>VExtensions.CastAbility</c> uses). The cast enters the
/// normal ability pipeline — it fires a real AbilityCastStartedEvent, runs the
/// behavior tree, animations and chained spawns.
///
/// WHY this matters: it's how players get MORE abilities than V Rising's 6-slot bar.
/// Bind a captured ability to a named hotkey (<c>.beelz hotkey set</c>) and fire it
/// with <c>.beelz cast &lt;name&gt;</c> (or a BloodCraftHub button). It needs no slot,
/// which also sidesteps the ULTIMATE-slot limitation — the ultimate (slot 7) is not
/// replaceable on the normal bar, but force-cast doesn't use a slot at all.
///
/// Caveats (see docs/CHAIN_AUDIT.md): rig/animation-bound boss abilities still need
/// the unit's form to read correctly; a few self-validating abilities no-op. Cooldown
/// is NOT auto-gated by a bar slot, so callers enforce their own per-ability cooldown.
/// </summary>
internal static class ForceCastService
{
    /// <summary>Force the player to cast <paramref name="abilityGroup"/>. AimPosition is left
    /// null so the engine uses the player's live facing/aim. Returns false on failure.</summary>
    public static bool Cast(Entity character, PrefabGUID abilityGroup)
    {
        if (!character.Exists() || !character.IsPlayer()) return false;
        try
        {
            Entity userEntity = GetUserEntity(character);
            if (!userEntity.TryGetComponent<User>(out var user)) return false;

            var castEvent = new CastAbilityServerDebugEvent
            {
                AbilityGroup = abilityGroup,
                Who = GetNetworkId(character),
                // AimPosition intentionally unset (null) → uses the player's current aim.
            };
            var fromCharacter = new FromCharacter
            {
                Character = character,
                User = userEntity,
            };
            Core.DebugEventsSystem.CastAbilityServerDebugEvent(user.Index, ref castEvent, ref fromCharacter);

            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] force-cast {abilityGroup.GetPrefabName()} for {character.GetSteamId()}.");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] ForceCastService.Cast({abilityGroup._Value}) failed: {ex}");
            return false;
        }
    }

    static NetworkId GetNetworkId(Entity entity) =>
        entity.TryGetComponent<NetworkId>(out var id) ? id : default;

    static Entity GetUserEntity(Entity entity) =>
        entity.TryGetComponent<PlayerCharacter>(out var pc) ? pc.UserEntity : entity;
}
