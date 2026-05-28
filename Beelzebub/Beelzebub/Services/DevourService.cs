using System.Collections.Generic;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.44.0 — "Devour": grant a player EVERY eligible ability a unit has, in one shot.
///
/// This is the per-ability-baseline replacement for the old "transform into the unit"
/// jackpot (for the units that can't render a real form — i.e. everything except the
/// Dracula and Morgana exo-forms). It reads the unit's prefab <c>AbilityGroupSlotBuffer</c>
/// and applies the same capture filter a normal kill uses, so a Devour grants exactly the
/// set of abilities the player could have captured one at a time from that unit.
///
/// Used by the kill-jackpot roll, the one-time migration of legacy transform unlocks, and
/// the admin bulk-grant command (<c>.beelz admin devour</c>).
/// </summary>
internal static class DevourService
{
    /// <summary>
    /// True if the unit's prefab + ability buffer can be read this run. The migration uses
    /// this to avoid dropping a legacy unlock it can't yet convert (prefab map not ready /
    /// unit removed) — preventing silent data loss; the unlock is left for a later run.
    /// </summary>
    public static bool CanResolveKit(PrefabGUID unit) =>
        unit._Value != 0
        && Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(unit, out Entity prefab)
        && Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(prefab);

    /// <summary>Every eligible (capture-allowed, slottable) ability-group GUID on a unit's prefab.</summary>
    public static List<int> EligibleAbilitiesFor(PrefabGUID unit)
    {
        var result = new List<int>();
        if (unit._Value == 0) return result;
        if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(unit, out Entity prefab)) return result;
        if (!Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(prefab)) return result;

        var slots = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(prefab);
        for (int i = 0; i < slots.Length; i++)
        {
            PrefabGUID ability = slots[i].BaseAbilityGroupOnSlot;
            if (ability._Value == 0) continue;
            string name = ability.GetPrefabName();
            if (!Core.AbilityFilter.ShouldCapture(name, ability._Value, out _)) continue;
            // v0.44.0: don't Devour transform-only abilities — they can't be slotted or
            // .beelz cast (both refuse IsTransformOnly), and transformation is now
            // Dracula/Morgana-only (curated kits, not captured), so they'd be dead entries.
            // v0.50.0: only when transform-only enforcement is on (default off → devour them too).
            if (Core.AbilityRules.IsTransformOnlyEnforced(name, ability._Value)) continue;
            if (!result.Contains(ability._Value)) result.Add(ability._Value);
        }
        return result;
    }

    /// <summary>
    /// Grant the player every eligible ability of a unit (reads the unit's prefab).
    /// Returns the count NEWLY added. Caller handles chat + save.
    /// </summary>
    public static int Devour(ulong steamId, PrefabGUID unit, CaptureSource source)
    {
        var eligible = EligibleAbilitiesFor(unit);
        return Core.AbilityRegistry.DevourAbilities(steamId, unit._Value, eligible, source);
    }
}
