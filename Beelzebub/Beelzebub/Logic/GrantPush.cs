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

    /// <summary>Whether to push <paramref name="abilityGuid"/> after the pop. A 0 (Empty) push masks the slot's base and
    /// blocks a spellbook pick (D32), so restoring a slot whose base is 0 only pops.</summary>
    public static bool ShouldPush(int abilityGuid) => abilityGuid != 0;
}
