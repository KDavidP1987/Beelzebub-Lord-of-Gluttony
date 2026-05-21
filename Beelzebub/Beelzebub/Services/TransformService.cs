using System;
using System.Collections.Generic;
using Beelzebub.Config;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

internal enum TransformMode : byte
{
    Toggle = 0,
    Timed = 1,
    Disabled = 2,
}

internal sealed class TransformService
{
    public TransformMode ModeFor(CaptureSource source)
    {
        string raw = (source == CaptureSource.VBlood
            ? Settings.Transform_Mode_VBlood.Value
            : Settings.Transform_Mode_Regular.Value) ?? "Toggle";
        return raw.Trim().ToLowerInvariant() switch
        {
            "disabled" => TransformMode.Disabled,
            "timed" => TransformMode.Timed,
            _ => TransformMode.Toggle,
        };
    }

    public float DurationSecondsFor(CaptureSource source) =>
        source == CaptureSource.VBlood
            ? Settings.Transform_DurationSeconds_VBlood.Value
            : Settings.Transform_DurationSeconds_Regular.Value;

    public float CooldownSecondsFor(CaptureSource source) =>
        source == CaptureSource.VBlood
            ? Settings.Transform_CooldownSeconds_VBlood.Value
            : Settings.Transform_CooldownSeconds_Regular.Value;

    /// <summary>
    /// Activate a transformation. Returns (success, message). Caller is responsible for
    /// the chat reply and prompting the player to swap a weapon to apply the new spell bar.
    /// </summary>
    public (bool ok, string message) TryActivate(ulong steamId, int unitPrefabGuid)
    {
        if (!Core.AbilityRegistry.HasTransformUnlock(steamId, unitPrefabGuid))
        {
            return (false, "You haven't unlocked transformation for that unit.");
        }

        // Look up the source classification from the unlock record.
        var unlocks = Core.AbilityRegistry.ListTransforms(steamId);
        CaptureSource source = CaptureSource.Regular;
        foreach (var u in unlocks)
        {
            if (u.UnitPrefabGuid == unitPrefabGuid) { source = u.Source; break; }
        }

        TransformMode mode = ModeFor(source);
        if (mode == TransformMode.Disabled)
        {
            return (false, $"Transformations are disabled for {(source == CaptureSource.VBlood ? "V-Bloods" : "regular mobs")} by the server admin.");
        }

        // Cooldown check.
        var now = DateTime.UtcNow;
        var cooldownUntil = Core.AbilityRegistry.CooldownUntil(steamId, source);
        if (cooldownUntil > now)
        {
            var remaining = (cooldownUntil - now).TotalSeconds;
            return (false, $"On cooldown. {remaining:F0}s remaining.");
        }

        // If already transformed, revert first.
        var current = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (current is not null)
        {
            Revert(steamId, "Switching transformation.");
        }

        // Verify we can read the unit's ability list.
        var pgUnit = new PrefabGUID(unitPrefabGuid);
        var abilityList = GetTransformAbilities(pgUnit);
        if (abilityList.Count == 0)
        {
            return (false, $"Could not load abilities for {pgUnit.GetPrefabName()}.");
        }

        var active = new ActiveTransform
        {
            UnitPrefabGuid = unitPrefabGuid,
            Source = source,
            ActivatedAtUtc = now,
            Duration = mode == TransformMode.Timed ? TimeSpan.FromSeconds(DurationSecondsFor(source)) : null,
        };
        Core.AbilityRegistry.SetActiveTransform(steamId, active);

        return (true, $"Transformed into {pgUnit.GetPrefabName()} ({abilityList.Count} ability slots). Swap a weapon to apply. Use .beelz revert to end.");
    }

    /// <summary>
    /// End a player's active transformation. Returns true if there was one to revert.
    /// </summary>
    public bool Revert(ulong steamId, string reason = null)
    {
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active is null) return false;

        Core.AbilityRegistry.ClearActiveTransform(steamId);

        TransformMode mode = ModeFor(active.Source);
        float cooldownSec = CooldownSecondsFor(active.Source);
        if (mode == TransformMode.Timed && cooldownSec > 0f)
        {
            Core.AbilityRegistry.SetCooldownUntil(steamId, active.Source, DateTime.UtcNow.AddSeconds(cooldownSec));
        }
        return true;
    }

    /// <summary>
    /// Tick called per frame to auto-revert Timed transforms whose duration has elapsed.
    /// </summary>
    public void Tick()
    {
        var now = DateTime.UtcNow;
        foreach (var (steamId, active) in Core.AbilityRegistry.AllActiveTransforms())
        {
            if (!active.Duration.HasValue) continue;
            if (now - active.ActivatedAtUtc < active.Duration.Value) continue;

            Core.AbilityRegistry.ClearActiveTransform(steamId);
            float cooldownSec = CooldownSecondsFor(active.Source);
            if (cooldownSec > 0f)
            {
                Core.AbilityRegistry.SetCooldownUntil(steamId, active.Source, now.AddSeconds(cooldownSec));
            }

            string unitName = new PrefabGUID(active.UnitPrefabGuid).GetPrefabName();
            Core.Log.LogInfo($"[Beelz] auto-revert {steamId} from {unitName} after {active.Duration.Value.TotalSeconds:F0}s.");
            // Need an Entity to send chat; resolve from the player's user entity via the registered admin/player lookups.
            // Skipped here for simplicity; the player will discover via their next .beelz list or chat command.
        }
    }

    /// <summary>
    /// Returns the up-to-6 ability GUIDs to grant slots 1..6 when the player is transformed
    /// into the given unit. Walks the unit prefab's AbilityGroupSlotBuffer, filters via AbilityFilter,
    /// and stops at 6 entries.
    /// </summary>
    public List<int> GetTransformAbilities(PrefabGUID unitGuid)
    {
        var result = new List<int>(6);
        if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(unitGuid, out Entity prefabEntity)) return result;
        if (!Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(prefabEntity)) return result;

        var slots = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(prefabEntity);
        for (int i = 0; i < slots.Length && result.Count < 6; i++)
        {
            PrefabGUID ability = slots[i].BaseAbilityGroupOnSlot;
            if (ability._Value == 0) continue;
            if (!Core.AbilityFilter.ShouldCapture(ability.GetPrefabName(), ability._Value, out _)) continue;
            result.Add(ability._Value);
        }
        return result;
    }
}
