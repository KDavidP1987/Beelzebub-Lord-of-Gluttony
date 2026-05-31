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

    /// <summary>
    /// Every eligible (capture-allowed, slottable) ability-group GUID a unit has. v0.99.0: this is now the
    /// FULL CROSS-PHASE kit (base bar ∪ metadata reverse-map ∪ curated transform-form sets), not just the
    /// prefab's base/phase-1 bar — so devouring a multi-phase boss grants its phase-2/3 abilities too.
    /// See <see cref="UnitKitService.FullEligibleKit"/>.
    /// </summary>
    public static List<int> EligibleAbilitiesFor(PrefabGUID unit) => UnitKitService.FullEligibleKit(unit._Value);

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
