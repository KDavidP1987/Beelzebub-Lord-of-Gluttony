using System;
using System.Collections.Generic;
using System.Linq;

namespace Beelzebub.Logic;

// v0.137.0 (bar-reset D2 D3 D11 D12 D24 D27) — PURE plan + runner for the one layered action-bar reset.
// The bar resolves through five layers (saved binds → equip-buff rows → form/carrier/orphan buffs → the
// engine ModificationsRegistry GroupGuid mods → automatic re-inject on equip/login/form enter). A reset that
// skips a layer "doesn't work": the spells come back from the layer it skipped. The order below is fixed:
// saved binds go first so the re-inject hooks find nothing to put back; equip rows go before the mod pop so
// no removed row is re-applied; exactly one Empty push, after the pop. Game calls live behind IBarResetOps
// (Services/BarResetService.cs); everything here is unit-tested in Beelzebub.Tests.

public enum BarResetScope
{
    /// <summary>`.beelz resetbar` — the player's own binds + transform loadouts + baseline.</summary>
    PlayerReset,
    /// <summary>`.beelz admin reset-loadouts` — same data as PlayerReset, on any player.</summary>
    AdminLoadouts,
    /// <summary>`.beelz admin purge` — AdminLoadouts plus hotkeys.</summary>
    Purge,
}

public enum BarResetStep
{
    RevertTransform,
    ClearSavedBindings,
    ClearHotkeys,
    SaveBindings,
    ClearEquipEntries,
    DestroyOverrideSources,
    PopSlotMods,
    EmptyPush,
    Reapply,
    Readback,
}

public static class BarResetPlanner
{
    /// <summary>
    /// The ordered steps for one reset. <paramref name="online"/> false → saved-state steps only (the live bar
    /// resets on next login). <paramref name="liveReady"/> false (online, but the character lacks BuffBuffer /
    /// AbilityGroupSlotBuffer / a held EquipBuff_Weapon_*) → saved-state steps plus Readback, never a live step.
    /// ClearSavedBindings also drops an active or parked transform record. An unknown scope returns an empty
    /// plan, which the runner reports as an error (Clean=false).
    /// </summary>
    public static List<BarResetStep> Plan(BarResetScope scope, bool online, bool liveReady, bool transformActiveOrParked)
    {
        var steps = new List<BarResetStep>();
        if (!Enum.IsDefined(typeof(BarResetScope), scope)) return steps;

        bool live = online && liveReady;
        if (live && transformActiveOrParked) steps.Add(BarResetStep.RevertTransform);
        steps.Add(BarResetStep.ClearSavedBindings);
        if (ClearsHotkeys(scope)) steps.Add(BarResetStep.ClearHotkeys);
        steps.Add(BarResetStep.SaveBindings);
        if (live)
        {
            steps.Add(BarResetStep.ClearEquipEntries);
            steps.Add(BarResetStep.DestroyOverrideSources);
            steps.Add(BarResetStep.PopSlotMods);
            steps.Add(BarResetStep.EmptyPush);
            steps.Add(BarResetStep.Reapply);
        }
        if (online) steps.Add(BarResetStep.Readback);
        return steps;
    }

    /// <summary>Scope table: only Purge clears hotkeys; no scope clears captures, unlocks or presets.</summary>
    public static bool ClearsHotkeys(BarResetScope scope) => scope == BarResetScope.Purge;
}

/// <summary>v0.137.0 (bar-reset D32) — which bar slots the held weapon OWNS: the slots its equip-buff PREFAB carries a
/// row for (Sword: 0, 1, 4). The EmptyPush used to push Empty onto all nine slots with the equip buff as source; on a
/// slot the weapon does not own (2 Space, 3, 5 R, 6 C, 7 T, 8) that mod masks the stored base (the Space dash) and
/// blocks the spellbook pick, grows by one per reset, and survives a restart (bar-raw, 2026-09-30).</summary>
/// <summary>One `VBloodAbilityBuffEntry` of a character: the spell the spellbook says is on <see cref="SlotId"/>,
/// and whether the buff behind it still lives (exists and is not queued for destruction).</summary>
public readonly record struct SpellbookEntry(int Index, int SlotId, int AbilityGuid, bool BuffLive);

/// <summary>bar-reset D33 — `Buff_VBlood_Ability_Replace` is both Beelzebub's transform carrier and V Rising's own
/// equipped-spell buff. Only a buff no spellbook entry references is ours.</summary>
public static class SpellbookBuffs
{
    /// <param name="referencedIds">ids of every buff a spellbook entry references; null = the spellbook could not be
    /// read, and then no buff is ours (never destroy blind).</param>
    public static bool IsOwnCarrier(long buffId, ISet<long> referencedIds) =>
        referencedIds != null && !referencedIds.Contains(buffId);

    /// <summary>The entries to repair: those whose buff is gone or being destroyed, highest index first so removing
    /// one never shifts another still to be handled.</summary>
    public static List<SpellbookEntry> Dangling(IEnumerable<SpellbookEntry> entries) =>
        (entries ?? Enumerable.Empty<SpellbookEntry>()).Where(e => !e.BuffLive).OrderByDescending(e => e.Index).ToList();

    /// <summary>Entity (index, version) packed into one comparable id.</summary>
    public static long Id(int index, int version) => ((long)index << 32) | (uint)version;
}

public enum EmptyVerdict { NotLeak, Leak, Unknown }

public static class SlotOwnership
{
    /// <summary>Hard cap on PopSlotMods' re-pop rounds for one slot.</summary>
    public const int MaxPopRounds = 16;

    /// <summary>The distinct slots 0-<paramref name="maxSlot"/> with a prefab row, ascending. Null rows → none.</summary>
    public static List<int> PushTargets(IEnumerable<int> prefabRowSlots, int maxSlot) =>
        (prefabRowSlots ?? Enumerable.Empty<int>()).Where(s => s >= 0 && s <= maxSlot).Distinct().OrderBy(s => s).ToList();

    /// <summary>What an Empty mod sourced by a WEAPON equip buff (the EmptyPush's own source; armour or item gear never
    /// counts) is on this slot: a Leak on a slot the weapon does not own (counts as other, never clean), Unknown when the
    /// owned set could not be read (the slot is unreadable, never clean), NotLeak otherwise.</summary>
    public static EmptyVerdict ClassifyEmpty(SlotModEntry e, int slot, bool weaponBuffSource, ISet<int> owned)
    {
        if (!weaponBuffSource || e.SetToGuid != 0) return EmptyVerdict.NotLeak;
        if (owned == null) return EmptyVerdict.Unknown;
        return owned.Contains(slot) ? EmptyVerdict.NotLeak : EmptyVerdict.Leak;
    }

    /// <summary>The source prefab is a weapon equip buff (the only source ForceResetAbilitySlots ever used).</summary>
    public static bool IsWeaponBuff(string prefabName) =>
        !string.IsNullOrEmpty(prefabName) && prefabName.StartsWith("EquipBuff_Weapon", StringComparison.OrdinalIgnoreCase);

    /// <summary>Pop the slot again while its GroupGuid mod count keeps falling and the cap is not reached.</summary>
    public static bool PopAgain(int previousCount, int currentCount, int roundsDone) =>
        currentCount > 0 && currentCount < previousCount && roundsDone < MaxPopRounds;
}

/// <summary>Gear-sourced = the modification source's prefab name starts with EquipBuff or Item_ (the same test
/// DestroyOwnedAbilitySlotOrphans uses). Known limitation: a foreign override named that way counts as gear.</summary>
public static class GearRule
{
    public static bool IsGearSource(string prefabName) =>
        !string.IsNullOrEmpty(prefabName)
        && (prefabName.StartsWith("EquipBuff", StringComparison.OrdinalIgnoreCase)
            || prefabName.StartsWith("Item_", StringComparison.OrdinalIgnoreCase));
}

/// <summary>What PopSlotMods / DestroyOverrideSources may do to ONE slot, decided from its parsed dump.</summary>
public sealed class SlotPurgeDecision
{
    public List<int> ModIdsToPop { get; } = new();
    /// <summary>(index, version) of non-gear, non-protected sources — destroyed only if the slot survives the pop.</summary>
    public List<(int Index, int Version)> SourcesToDestroy { get; } = new();
    public bool Skipped { get; init; }

    /// <param name="sourcePrefabName">prefab name of an entry's source entity ("" when unknown).</param>
    /// <param name="isProtected">true for the character itself and its slot entities — never destroyed.</param>
    public static SlotPurgeDecision Decide(SlotModParse parse, Func<SlotModEntry, string> sourcePrefabName,
        Func<SlotModEntry, bool> isProtected)
    {
        // Unreadable → touch nothing on this slot (D24); the readback reports it and the reset is never clean.
        if (parse == null || !parse.Readable) return new SlotPurgeDecision { Skipped = true };
        var d = new SlotPurgeDecision();
        var seen = new HashSet<(int, int)>();
        var popIds = new HashSet<int>();
        foreach (var e in parse.Entries)
        {
            // Every GroupGuid mod is popped, gear ones too: Reapply + the single Empty push re-add the gear
            // mods, so the per-slot gear count stays equal run to run (no pile-up, D10). The engine dump repeats
            // a mod id; each id is popped ONCE — a second pop of a gone id is an engine LogError (D33). Real
            // stacked entries are caught by PopSlotModifications' re-pop rounds (D32).
            if (popIds.Add(e.ModId)) d.ModIdsToPop.Add(e.ModId);
            if (isProtected(e) || GearRule.IsGearSource(sourcePrefabName(e) ?? "")) continue;
            if (seen.Add((e.SourceIndex, e.SourceVersion))) d.SourcesToDestroy.Add((e.SourceIndex, e.SourceVersion));
        }
        return d;
    }
}

/// <summary>One bar slot as the diagnostic / readback sees it.</summary>
public sealed class BarSlotReading
{
    public int Slot { get; init; }
    /// <summary>The ability the slot resolves to right now (prefab name; "" when unknown) — shown by `admin bar`.</summary>
    public string Ability { get; init; } = "";
    /// <summary>"none", or the saved set holding a bind for this slot ("universal", "weapon:Sword", "form:Wolf").</summary>
    public string Bind { get; init; } = "none";
    /// <summary>A Beelzebub ReplaceAbilityOnSlotBuff row on the held equip buff.</summary>
    public bool Row { get; init; }
    public int Gear { get; init; }
    public int Other { get; init; }
    public bool Unreadable { get; init; }
    public List<string> GearSources { get; init; } = new();

    public bool Overridden => Unreadable || Other > 0 || Row || !string.Equals(Bind, "none", StringComparison.Ordinal);
}

public sealed class BarReadback
{
    public List<BarSlotReading> Slots { get; init; } = new();
    /// <summary>The target was offline: no live slots were read; SavedSets / Transform / Hotkeys describe the saved state.</summary>
    public bool Offline { get; init; }
    /// <summary>Carrier / form / shapeshift buffs on the character that can drive the bar (names).</summary>
    public List<string> OverrideBuffs { get; init; } = new();
    /// <summary>One entry per saved set with binds, e.g. "universal: slots 2" or "weapon:Sword: slots 1,4".</summary>
    public List<string> SavedSets { get; init; } = new();
    /// <summary>The active or parked transform record ("none" when there is none).</summary>
    public string Transform { get; init; } = "none";
    public int Hotkeys { get; init; }
    public bool Unreadable => Slots.Any(s => s.Unreadable);
    public int Binds => Slots.Count(s => !string.Equals(s.Bind, "none", StringComparison.Ordinal));
    public int Rows => Slots.Count(s => s.Row);
    public int Gear => Slots.Sum(s => s.Gear);
    public int Other => Slots.Sum(s => s.Other);
    public List<int> Survivors => Slots.Where(s => s.Overridden && !s.Unreadable).Select(s => s.Slot).ToList();
}

public interface IBarResetOps
{
    int RevertTransform();
    /// <param name="keepTransformRecord">true when this run's RevertTransform failed: the active/parked transform
    /// record is kept so a retry plans RevertTransform again (its record-driven cleanup is not lost).</param>
    int ClearSavedBindings(bool keepTransformRecord);
    int ClearHotkeys();
    /// <summary>Writes state.json synchronously; false when the write failed (never swallowed as success).</summary>
    bool SaveBindings();
    int ClearEquipEntries();
    int DestroyOverrideSources();
    int PopSlotMods();
    int EmptyPush();
    int Reapply();
    BarReadback Readback();
}

public readonly record struct BarResetStepResult(BarResetStep Step, int Count, string Error)
{
    public bool Failed => Error != null;
}

public sealed class BarResetResult
{
    public List<BarResetStepResult> Steps { get; } = new();
    public BarReadback Readback { get; set; }
    public bool Unreadable { get; set; }
    public bool Saved { get; set; }
    /// <summary>The target was connected when the reset ran (offline = saved state only).</summary>
    public bool Online { get; set; }
    /// <summary>The live components every live step needs were present (see BarResetPlanner.Plan).</summary>
    public bool LiveReady { get; set; }
    public List<int> Survivors => Readback?.Survivors ?? new List<int>();
    /// <summary>Override buffs (carrier / form / shapeshift) still on the character at the readback — each one can
    /// re-patch the bar through its own ReplaceAbilityOnSlotBuff, which no slot reading shows.</summary>
    public List<string> OverrideBuffsLeft => Readback?.OverrideBuffs ?? new List<string>();
    public bool AnyStepFailed => Steps.Any(s => s.Failed);
    public int CountOf(BarResetStep step) => Steps.Where(s => s.Step == step && !s.Failed).Sum(s => s.Count);

    /// <summary>Clean only when: a non-empty plan ran with no failed step, the binds were saved, the live bar was
    /// reachable and readable, and the readback shows no overridden slot and no override buff.</summary>
    public bool Clean { get; set; }
}

public static class BarResetRunner
{
    /// <summary>Runs every step in order. A step that throws is recorded as that step's failure and the rest still
    /// run (D12); a SaveBindings that returns false is a failure (D27).</summary>
    public static BarResetResult Run(IBarResetOps ops, IReadOnlyList<BarResetStep> steps, bool online, bool liveReady)
    {
        var r = new BarResetResult { Online = online, LiveReady = online && liveReady };
        foreach (var step in steps ?? Array.Empty<BarResetStep>())
        {
            try
            {
                switch (step)
                {
                    case BarResetStep.SaveBindings:
                        bool ok = ops.SaveBindings();
                        r.Saved = ok;
                        r.Steps.Add(new BarResetStepResult(step, ok ? 1 : 0, ok ? null : "not saved"));
                        break;
                    case BarResetStep.Readback:
                        // A null readback is a failed read, never an empty (clean) bar.
                        r.Readback = ops.Readback();
                        r.Steps.Add(r.Readback == null
                            ? new BarResetStepResult(step, 0, "no readback")
                            : new BarResetStepResult(step, r.Readback.Survivors.Count, null));
                        break;
                    default:
                        r.Steps.Add(new BarResetStepResult(step, Invoke(ops, step, r), null));
                        break;
                }
            }
            catch (Exception ex)
            {
                r.Steps.Add(new BarResetStepResult(step, 0, string.IsNullOrEmpty(ex.Message) ? ex.GetType().Name : ex.Message));
            }
        }

        bool readbackRan = r.Readback != null && !r.Steps.Any(s => s.Step == BarResetStep.Readback && s.Failed);
        r.Unreadable = online && (!liveReady || !readbackRan || r.Readback.Unreadable);
        // An empty plan (unknown scope) never runs SaveBindings, so r.Saved alone makes it an error result; a plan
        // that skipped a live layer (not planner-made) can never be clean either.
        bool complete = RequiredForClean.All(req => r.Steps.Any(s => s.Step == req));
        r.Clean = online && liveReady && complete
                  && !r.AnyStepFailed && r.Saved
                  && readbackRan && !r.Unreadable
                  && r.Survivors.Count == 0 && r.OverrideBuffsLeft.Count == 0;
        return r;
    }

    /// <summary>Every layer a clean live reset must have run (RevertTransform and ClearHotkeys are conditional).</summary>
    public static readonly IReadOnlyList<BarResetStep> RequiredForClean = new[]
    {
        BarResetStep.ClearSavedBindings, BarResetStep.SaveBindings, BarResetStep.ClearEquipEntries,
        BarResetStep.DestroyOverrideSources, BarResetStep.PopSlotMods, BarResetStep.EmptyPush,
        BarResetStep.Reapply, BarResetStep.Readback,
    };

    static int Invoke(IBarResetOps ops, BarResetStep step, BarResetResult r) => step switch
    {
        BarResetStep.RevertTransform => ops.RevertTransform(),
        BarResetStep.ClearSavedBindings => ops.ClearSavedBindings(
            keepTransformRecord: r.Steps.Any(s => s.Step == BarResetStep.RevertTransform && s.Failed)),
        BarResetStep.ClearHotkeys => ops.ClearHotkeys(),
        BarResetStep.ClearEquipEntries => ops.ClearEquipEntries(),
        BarResetStep.DestroyOverrideSources => ops.DestroyOverrideSources(),
        BarResetStep.PopSlotMods => ops.PopSlotMods(),
        BarResetStep.EmptyPush => ops.EmptyPush(),
        BarResetStep.Reapply => ops.Reapply(),
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "unknown reset step"),
    };
}
