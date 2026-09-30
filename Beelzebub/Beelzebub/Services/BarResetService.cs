using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Beelzebub.Logic;
using ProjectM;
using ProjectM.Network;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.137.0 (bar-reset) — the ONE layered action-bar reset. `.beelz resetbar`, `.beelz admin reset-loadouts` and
/// `.beelz admin purge` all call <see cref="FullReset"/>; `.beelz admin bar` calls <see cref="ReadBar"/>. Commands never
/// call a layer helper directly (tools/check_bar_reset.py `commands` enforces the allowlist).
///
/// A slot resolves through five layers: (L1) saved Steam-keyed binds, (L2) ReplaceAbilityOnSlotBuff rows on the held
/// EquipBuff_Weapon_*, (L3) form/carrier/orphan override buffs, (L4) ModificationsRegistry GroupGuid mods, (L5) the
/// automatic re-inject on equip/login/form enter. The order is planned by the pure, unit-tested
/// <see cref="BarResetPlanner"/> and run by <see cref="BarResetRunner"/>; this class supplies the game calls.
/// </summary>
internal sealed class BarResetService : IBarResetOps
{
    readonly Entity _character;
    readonly ulong _steamId;
    readonly BarResetScope _scope;

    BarResetService(Entity character, ulong steamId, BarResetScope scope)
    {
        _character = character;
        _steamId = steamId;
        _scope = scope;
    }

    static int _runCounter;

    /// <summary>Resets <paramref name="character"/>'s bar for <paramref name="scope"/>, logs the `[Beelz RESET]` and
    /// `[Beelz BAR]` lines, schedules the next-tick late re-read, and returns the result for the reply.</summary>
    public static BarResetResult FullReset(Entity character, ulong steamId, string name, BarResetScope scope)
    {
        var sw = Stopwatch.StartNew();
        bool online = IsOnline(character);
        bool liveReady = online && IsLiveReady(character);
        bool transform = Core.AbilityRegistry.GetActiveTransform(steamId) is not null;
        var steps = BarResetPlanner.Plan(scope, online, liveReady, transform);

        var result = BarResetRunner.Run(new BarResetService(character, steamId, scope), steps, online, liveReady);
        sw.Stop();

        int run = ++_runCounter;
        foreach (var s in result.Steps.Where(s => s.Failed))
            Core.Log.LogWarning($"[Beelz RESET] run={run} step {s.Step} failed: {LogSafe.Field(s.Error)}");
        Core.Log.LogInfo(BarResetLog.Format(result, scope, name, steamId, sw.ElapsedMilliseconds, run));
        if (result.Readback != null)
        {
            foreach (string line in BarResetLog.FormatBar(result.Readback, name, steamId)) Core.Log.LogInfo(line);
            if (liveReady) ScheduleLate(character, steamId, name, run, result.Readback);
        }
        return result;
    }

    /// <summary>Read-only readback of every bar slot: saved bind, injected equip row, gear/other GroupGuid mods.</summary>
    public static BarReadback ReadBar(Entity character, ulong steamId)
    {
        var readback = new BarReadback();
        if (!character.Exists()) return readback;

        var binds = BindOrigins(character, steamId);
        var injected = SlotApply.InjectedRowSlots(character, out var anyRow);
        var mods = TransformBuffService.ReadSlotMods(character).ToDictionary(m => m.Slot);

        var slots = new SortedSet<int>(mods.Keys);
        foreach (int s in binds.Keys) slots.Add(s);
        foreach (int s in anyRow) slots.Add(s);
        foreach (int slot in slots)
        {
            mods.TryGetValue(slot, out var m);
            bool rowsUnknown = injected == null && anyRow.Contains(slot);
            readback.Slots.Add(new BarSlotReading
            {
                Slot = slot,
                Bind = binds.TryGetValue(slot, out var b) ? b : "none",
                Row = injected != null && injected.Contains(slot),
                Gear = m.Gear,
                Other = m.Other,
                Unreadable = m.Unreadable || rowsUnknown,
                GearSources = m.GearSources ?? new List<string>(),
            });
        }
        return readback;
    }

    /// <summary>slot → the saved set holding a bind for it ("weapon:Sword" before "universal" before "form:Wolf").</summary>
    static Dictionary<int, string> BindOrigins(Entity character, ulong steamId)
    {
        var d = new Dictionary<int, string>();
        foreach (var (form, slots) in Core.AbilityRegistry.AllFormSlots(steamId))
            foreach (int s in slots.Keys) d[s] = $"form:{form}";
        foreach (int s in Core.AbilityRegistry.GetSlots(steamId).Keys) d[s] = "universal";
        var weapon = SlotApply.CurrentWeapon(character);
        if (weapon != WeaponFamily.None)
            foreach (int s in Core.AbilityRegistry.GetWeaponSlots(steamId, weapon).Keys) d[s] = $"weapon:{weapon}";
        return d;
    }

    internal static bool IsOnline(Entity character) =>
        character.Exists()
        && character.TryGetComponent<PlayerCharacter>(out var pc)
        && pc.UserEntity.TryGetComponent<User>(out var user)
        && user.IsConnected;

    /// <summary>The live components every live step needs: BuffBuffer, AbilityGroupSlotBuffer and a held
    /// EquipBuff_Weapon_* (missing mid-respawn, for example).</summary>
    static bool IsLiveReady(Entity character) =>
        Core.EntityManager.HasBuffer<BuffBuffer>(character)
        && Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(character)
        && SlotApply.HasEquipBuff(character);

    // ── IBarResetOps: one method per layer step; each returns a count, throws on failure ────────────────────────

    public int RevertTransform()
    {
        var (reverted, _) = Core.Transforms.Revert(_steamId, $"bar reset ({_scope})", restoreBar: false);
        return reverted ? 1 : 0;
    }

    public int ClearSavedBindings()
    {
        int n = Core.AbilityRegistry.ClearAllLoadouts(_steamId);   // universal + weapon + form binds, transform loadouts, baseline
        if (Core.AbilityRegistry.GetActiveTransform(_steamId) is not null)
        {
            Core.AbilityRegistry.ClearActiveTransform(_steamId);    // an active or parked (disconnect-grace) record
            n++;
        }
        return n;
    }

    public int ClearHotkeys()
    {
        int n = 0;
        foreach (string hk in Core.AbilityRegistry.ListHotkeys(_steamId).Keys.ToList())
            if (Core.AbilityRegistry.ClearHotkey(_steamId, hk)) n++;
        return n;
    }

    public bool SaveBindings() => Core.Persistence.TrySaveSync();

    public int ClearEquipEntries() => SlotApply.RemoveInjectedRows(_character);

    public int DestroyOverrideSources() =>
        TransformBuffService.RemoveAllFormsAndShapeshifts(_character)
        + TransformBuffService.DestroyOwnedAbilitySlotOrphans(_character);

    public int PopSlotMods() => TransformBuffService.PopSlotModifications(_character);

    public int EmptyPush() => TransformBuffService.ForceResetAbilitySlots(_character);

    public int Reapply() => SlotApply.ReapplyEquipRows(_character);

    public BarReadback Readback() => ReadBar(_character, _steamId);

    // ── late re-read: a cast already in flight can re-dirty a slot after the readback said clean ────────────────

    sealed class PendingLate
    {
        public Entity Character;
        public ulong SteamId;
        public string Name;
        public int Run;
        public int Frame;
        public HashSet<int> Overridden;
    }

    static readonly List<PendingLate> _late = new();

    static void ScheduleLate(Entity character, ulong steamId, string name, int run, BarReadback readback) =>
        _late.Add(new PendingLate
        {
            Character = character, SteamId = steamId, Name = name, Run = run,
            Frame = UnityEngine.Time.frameCount,
            Overridden = new HashSet<int>(readback.Slots.Where(s => s.Overridden).Select(s => s.Slot)),
        });

    /// <summary>Per-frame (HeartbeatBehaviour): one frame after a reset, re-read its bar and log a `late-survivor` line
    /// for every slot that became overridden since the readback. A no-op when nothing is pending.</summary>
    public static void TickLate()
    {
        if (_late.Count == 0) return;
        int frame = UnityEngine.Time.frameCount;
        for (int i = _late.Count - 1; i >= 0; i--)
        {
            var p = _late[i];
            if (frame <= p.Frame) continue;
            _late.RemoveAt(i);
            try
            {
                if (!p.Character.Exists()) continue;
                foreach (var s in ReadBar(p.Character, p.SteamId).Slots)
                    if (s.Overridden && !p.Overridden.Contains(s.Slot))
                        Core.Log.LogWarning(BarResetLog.FormatLate(p.Name, p.SteamId, p.Run, s.Slot));
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz RESET] late re-read run={p.Run} failed: {ex.Message}"); }
        }
    }
}
