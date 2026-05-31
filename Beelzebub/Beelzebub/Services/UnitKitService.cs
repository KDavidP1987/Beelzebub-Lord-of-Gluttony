using System.Collections.Generic;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.99.0 — resolve a unit's FULL eligible ability kit across ALL its combat phases, for capture + Devour.
///
/// V Rising bosses swap their phase-2/phase-3 abilities in at runtime (phase buffs), so a boss prefab's base
/// <see cref="AbilityGroupSlotBuffer"/> is only its DEFAULT/phase-1 bar — and for bosses whose V-Blood prefab
/// is the human STARTING form (e.g. CHAR_Geomancer_Human_VBlood, CHAR_WerewolfChieftain_VBlood), that base bar
/// is the HUMAN kit, missing the creature abilities entirely. Reading only that buffer (the pre-v0.99 behavior)
/// therefore left most phase-gated abilities uncollectable.
///
/// This unions three sources, so it is strictly a SUPERSET of the old base-buffer behavior:
///   1. the prefab's base <see cref="AbilityGroupSlotBuffer"/> (the original source),
///   2. the curated metadata reverse-map (unit → every ability the scraper attributes to it, ALL phases),
///   3. for a registered transform boss, every ability across its curated <see cref="BossFormRegistry.BossForm.FormSets"/>
///      (guarantees the form's full multi-phase kit is collectable — e.g. devouring Dracula now also grants
///      his Bloodmage-phase spells, not just his phase-1 melee bar).
/// All filtered by the SAME capture rules a normal kill uses, so the result is exactly the set the player
/// could legitimately own from that unit. Order: base bar first, then the extras (stable, de-duplicated).
/// </summary>
internal static class UnitKitService
{
    public static List<int> FullEligibleKit(int unitGuid)
    {
        var result = new List<int>();
        if (unitGuid == 0) return result;
        var seen = new HashSet<int>();

        void Consider(int abilityGuid)
        {
            if (abilityGuid == 0 || !seen.Add(abilityGuid)) return;
            string name = new PrefabGUID(abilityGuid).GetPrefabName();
            if (!Core.AbilityFilter.ShouldCapture(name, abilityGuid, out _)) return;
            // Don't include transform-only abilities when enforcement is on (they can't be slotted/cast);
            // when enforcement is off (default), they're devourable like everything else.
            if (Core.AbilityRules.IsTransformOnlyEnforced(name, abilityGuid)) return;
            result.Add(abilityGuid);
        }

        // 1. Base prefab ability bar (the original, always-available source).
        var pg = new PrefabGUID(unitGuid);
        if (Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(pg, out Entity prefab)
            && Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(prefab))
        {
            var slots = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(prefab);
            for (int i = 0; i < slots.Length; i++) Consider(slots[i].BaseAbilityGroupOnSlot._Value);
        }

        // 2. Metadata reverse-map — every ability attributed to this unit across all phases.
        if (Core.AbilityMetadata != null)
            foreach (int a in Core.AbilityMetadata.GetAbilitiesForUnit(unitGuid)) Consider(a);

        // 3. Curated transform-form kit (guarantees the form's full multi-phase set is collectable).
        if (BossFormRegistry.TryGet(unitGuid, out var bossForm) && bossForm.FormSets != null)
            foreach (var set in bossForm.FormSets)
                if (set != null)
                    foreach (int a in set) Consider(a);

        return result;
    }
}
