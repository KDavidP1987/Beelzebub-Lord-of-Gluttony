using System.Collections.Generic;
using System.Linq;

namespace Beelzebub.Logic;

// v0.137.3 (mounted-bar-reset D1 D2) — the saddle bar's free keys. Every AB_Interact_Mount_Owner_Buff_* prefab ends
// slots 5 (R), 6 (C) and 7 (T) with an Empty row; 1 (Q leap), 2 (Space, the vampire horse's jump), 4 (E gallop) and the
// primary are the horse's. Slot 3 is never drawn by the client, so the old {3, 6, 7} set hid a slot-3 bind.

public enum MountedMigration { None, Moved, Dropped }

public static class MountedSlots
{
    /// <summary>The only slots a Mounted-form bind may use.</summary>
    public static readonly IReadOnlyList<int> Allowed = new[] { 5, 6, 7 };

    /// <summary>The pre-0.137.3 saddle slot no key shows; its binds move to <see cref="MigrateTo"/>.</summary>
    public const int LegacySlot = 3;
    public const int MigrateTo = 5;

    public static bool IsValid(int slot) => Allowed.Contains(slot);

    public const string Hint = "5, 6, 7 (the R, C, and Ultimate keys)";

    public static string RejectMessage() =>
        $"Mounted form only uses slots {Hint} — the other slots are riding controls (Q/E/space) and can't hold a saddle ability. Re-grant this to slot 5, 6, or 7.";

    /// <summary>Moves a saved slot-3 bind to slot 5 when slot 5 is free, else drops it; every other slot is left alone.
    /// Idempotent: a migrated map has no slot 3, so a second call returns <see cref="MountedMigration.None"/>.</summary>
    public static MountedMigration Migrate(IDictionary<int, int> mounted)
    {
        if (mounted == null || !mounted.TryGetValue(LegacySlot, out int ability)) return MountedMigration.None;
        mounted.Remove(LegacySlot);
        if (mounted.TryGetValue(MigrateTo, out int taken) && taken != 0) return MountedMigration.Dropped;
        if (ability == 0) return MountedMigration.Dropped;
        mounted[MigrateTo] = ability;
        return MountedMigration.Moved;
    }
}
