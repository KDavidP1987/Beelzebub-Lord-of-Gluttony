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
        foreach (var e in parse.Entries)
        {
            // Every GroupGuid mod is popped, gear ones too: Reapply + the single Empty push re-add the gear
            // mods, so the per-slot gear count stays equal run to run (no pile-up, D10).
            d.ModIdsToPop.Add(e.ModId);
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
    int ClearSavedBindings();
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
    public List<int> Survivors => Readback?.Survivors ?? new List<int>();
    public bool AnyStepFailed => Steps.Any(s => s.Failed);
    public int CountOf(BarResetStep step) => Steps.Where(s => s.Step == step && !s.Failed).Sum(s => s.Count);

    /// <summary>Clean only when: a non-empty plan ran with no failed step, the binds were saved, the live bar was
    /// reachable and readable, and the readback shows no overridden slot.</summary>
    public bool Clean { get; set; }
}

public static class BarResetRunner
{
    /// <summary>Runs every step in order. A step that throws is recorded as that step's failure and the rest still
    /// run (D12); a SaveBindings that returns false is a failure (D27).</summary>
    public static BarResetResult Run(IBarResetOps ops, IReadOnlyList<BarResetStep> steps, bool online, bool liveReady)
    {
        var r = new BarResetResult();
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
                        r.Steps.Add(new BarResetStepResult(step, Invoke(ops, step), null));
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
                  && r.Survivors.Count == 0;
        return r;
    }

    /// <summary>Every layer a clean live reset must have run (RevertTransform and ClearHotkeys are conditional).</summary>
    public static readonly IReadOnlyList<BarResetStep> RequiredForClean = new[]
    {
        BarResetStep.ClearSavedBindings, BarResetStep.SaveBindings, BarResetStep.ClearEquipEntries,
        BarResetStep.DestroyOverrideSources, BarResetStep.PopSlotMods, BarResetStep.EmptyPush,
        BarResetStep.Reapply, BarResetStep.Readback,
    };

    static int Invoke(IBarResetOps ops, BarResetStep step) => step switch
    {
        BarResetStep.RevertTransform => ops.RevertTransform(),
        BarResetStep.ClearSavedBindings => ops.ClearSavedBindings(),
        BarResetStep.ClearHotkeys => ops.ClearHotkeys(),
        BarResetStep.ClearEquipEntries => ops.ClearEquipEntries(),
        BarResetStep.DestroyOverrideSources => ops.DestroyOverrideSources(),
        BarResetStep.PopSlotMods => ops.PopSlotMods(),
        BarResetStep.EmptyPush => ops.EmptyPush(),
        BarResetStep.Reapply => ops.Reapply(),
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "unknown reset step"),
    };
}
