using System;
using System.Collections.Generic;
using System.Linq;

namespace Beelzebub.Logic;

/// <summary>
/// grant-refresh (v0.137.1): a row added to the HELD weapon equip buff's ReplaceAbilityOnSlotBuff is never applied by
/// the engine — ReplaceAbilityOnSlotSystem only reads the rows when the buff spawns, so a fresh grant showed only after
/// a weapon swap (`[Beelz GRANTRAW]`, 2026-10-03: the slot kept resolving Whirlwind next frame and 2 s later). The live
/// path therefore also sets the slot through the engine's own setter, the held equip buff as the source. Before each
/// push the slot's earlier mods from that same buff are popped, so re-grants never stack.
/// </summary>
public static class GrantPush
{
    /// <summary>The distinct mod ids on the slot whose source is the held equip buff — our own earlier pushes (and a
    /// bar reset's Reapply on that slot), never a spellbook or another source. Empty for an unreadable dump.</summary>
    public static List<int> OwnModIds(SlotModParse parse, int equipBuffIndex, int equipBuffVersion)
    {
        if (parse == null || !parse.Readable) return new List<int>();
        return parse.Entries
            .Where(e => e.SourceIndex == equipBuffIndex && e.SourceVersion == equipBuffVersion)
            .Select(e => e.ModId)
            .Distinct()
            .ToList();
    }

    /// <summary>Mods to pop when a grant leaves a slot (unslot, yield, lock): <see cref="OwnModIds"/> plus every mod that
    /// sets <paramref name="removedAbility"/> — the engine applies a buff's rows at equip time under another source entity
    /// (GRANTRAW: `from Entity(581913:259)` vs the held buff 581914:252), so a grant applied on equip is not "own".
    /// <paramref name="keep"/> (a spellbook spell's mod) is never popped. Empty for an unreadable dump.</summary>
    public static List<int> ModsToPop(SlotModParse parse, int equipBuffIndex, int equipBuffVersion, int removedAbility,
                                      Func<SlotModEntry, bool> keep)
    {
        if (parse == null || !parse.Readable) return new List<int>();
        return parse.Entries
            .Where(e => (e.SourceIndex == equipBuffIndex && e.SourceVersion == equipBuffVersion)
                        || (removedAbility != 0 && e.SetToGuid == removedAbility && (keep == null || !keep(e))))
            .Select(e => e.ModId)
            .Distinct()
            .ToList();
    }

    /// <summary>Codex round 2: whether a mod belongs to another LIVE buff on the character — a form, transform, mount or
    /// spellbook buff (<paramref name="liveBuffs"/> = the character's buffs that carry a prefab). Such a mod is never popped
    /// by a by-ability match: unslotting a weapon grant while a form maps the same ability must leave the form's mod. The
    /// held equip buff itself is not "another" buff (its own mods are popped by <see cref="OwnModIds"/>).</summary>
    public static bool OwnedByOtherBuff(SlotModEntry e, ICollection<(int Index, int Version)> liveBuffs,
                                        int equipBuffIndex, int equipBuffVersion)
    {
        if (e == null || liveBuffs == null) return false;
        if (e.SourceIndex == equipBuffIndex && e.SourceVersion == equipBuffVersion) return false;
        return liveBuffs.Contains((e.SourceIndex, e.SourceVersion));
    }

    /// <summary>The weapon's own ability for <paramref name="slot"/> from its prefab rows: the highest Priority row, the
    /// later one on a tie (the same rule as the bar reset's Reapply); 0 when the weapon has no row there.</summary>
    public static int WeaponRow(IEnumerable<(int Slot, int Guid, int Priority)> rows, int slot)
    {
        int ability = 0, best = int.MinValue;
        if (rows == null) return 0;
        foreach (var r in rows)
            if (r.Slot == slot && r.Guid != 0 && r.Priority >= best) { ability = r.Guid; best = r.Priority; }
        return ability;
    }

    /// <summary>Whether to push <paramref name="abilityGuid"/> after the pop. A 0 (Empty) push masks the slot's base and
    /// blocks a spellbook pick (D32), so restoring a slot whose base is 0 only pops.</summary>
    public static bool ShouldPush(int abilityGuid) => abilityGuid != 0;
}
