using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Beelzebub.Logic;

// v0.137.0 (bar-reset D1) — PURE parser for the engine's per-slot modification dump
// (ModificationsRegistry.GetFormattedEntityModificationsMessage). The formatter prints, per modifiable field:
//   - AbilityGroupSlot.GroupGuid: PrefabGuid(X) (Base: PrefabGuid(Y))
//       [ModId 5066] Set PrefabGuid(Z) from Entity(326806:1) (No PrefabGUID, Entity Name '')
// Only the GroupGuid field (the ability override) is read — CopyCooldown / SpellModsSource are ignored.
// A GroupGuid `[ModId` line that does not match the Set/from shape makes the dump Unreadable: callers must
// then neither pop nor destroy for that slot, and must never report it clean (bar-reset D24).

public readonly record struct SlotModEntry(int ModId, int SetToGuid, int SourceIndex, int SourceVersion);

public sealed class SlotModParse
{
    public List<SlotModEntry> Entries { get; } = new();
    public bool Readable { get; internal set; } = true;
    public bool HasMods => Entries.Count > 0;
}

public static class SlotModDump
{
    // Any "- " line is a field header: the GroupGuid section ends at the next one, whatever component it names.
    const string FieldPrefix = "- ";
    // Exact field token: "- AbilityGroupSlot.GroupGuid:" (never GroupGuidBackup or another GroupGuid* field).
    const string GroupGuidField = "- AbilityGroupSlot.GroupGuid:";

    // Anchored and single-line: no nested quantifiers, so no catastrophic backtracking on hostile text.
    static readonly Regex _entryRx = new(
        @"^\[ModId\s+(\d+)\]\s+Set\s+PrefabGuid\((-?\d+)\)\s+from\s+Entity\((\d+):(\d+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static SlotModParse ParseGroupGuid(string dump)
    {
        var result = new SlotModParse();
        if (string.IsNullOrEmpty(dump)) return result;

        bool inGroupGuid = false;
        foreach (string raw in dump.Split('\n'))
        {
            string t = raw.Trim();
            if (t.StartsWith(FieldPrefix, StringComparison.Ordinal))
            {
                inGroupGuid = t.StartsWith(GroupGuidField, StringComparison.Ordinal);
                continue;
            }
            if (!inGroupGuid || !t.StartsWith("[ModId", StringComparison.Ordinal)) continue;

            var m = _entryRx.Match(t);
            if (m.Success
                && int.TryParse(m.Groups[1].Value, out int id) && id > 0
                && int.TryParse(m.Groups[2].Value, out int setTo)
                && int.TryParse(m.Groups[3].Value, out int srcIdx)
                && int.TryParse(m.Groups[4].Value, out int srcVer))
            {
                result.Entries.Add(new SlotModEntry(id, setTo, srcIdx, srcVer));
            }
            else
            {
                result.Readable = false;
            }
        }
        return result;
    }
}
