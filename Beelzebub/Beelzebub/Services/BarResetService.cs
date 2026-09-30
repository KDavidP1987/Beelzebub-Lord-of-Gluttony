using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Beelzebub.Logic;
using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
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
    readonly bool _notLive;

    BarResetService(Entity character, ulong steamId, BarResetScope scope, bool notLive)
    {
        _character = character;
        _steamId = steamId;
        _scope = scope;
        _notLive = notLive;
    }

    static int _runCounter;

    /// <summary>The bar the player sees: slots 0 (primary) to 8.</summary>
    const int BarMaxSlot = 8;

    /// <summary>Resets <paramref name="character"/>'s bar for <paramref name="scope"/>, logs the `[Beelz RESET]` and
    /// `[Beelz BAR]` lines, schedules the next-tick late re-read, and returns the result for the reply.</summary>
    public static BarResetResult FullReset(Entity character, ulong steamId, string name, BarResetScope scope)
    {
        var sw = Stopwatch.StartNew();
        bool online = IsOnline(character);
        bool liveReady = online && IsLiveReady(character);
        bool transform = Core.AbilityRegistry.GetActiveTransform(steamId) is not null;
        var steps = BarResetPlanner.Plan(scope, online, liveReady, transform);

        var result = BarResetRunner.Run(new BarResetService(character, steamId, scope, online && !liveReady), steps, online, liveReady);
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

    /// <summary>Read-only readback. Online: every bar slot 0-8 (resolved ability, saved bind, injected equip row,
    /// gear/other GroupGuid mods) plus the override buffs. Offline: the saved state only (sets, transform, hotkeys).
    /// It changes nothing.</summary>
    public static BarReadback ReadBar(Entity character, ulong steamId)
    {
        string transform = TransformName(steamId);
        int hotkeys = Core.AbilityRegistry.ListHotkeys(steamId).Count;
        if (!IsOnline(character))
            return new BarReadback { Offline = true, SavedSets = SavedSets(steamId), Transform = transform, Hotkeys = hotkeys };

        var readback = new BarReadback
        {
            Transform = transform,
            Hotkeys = hotkeys,
            OverrideBuffs = TransformBuffService.ListOverrideBuffs(character),
        };
        var binds = BindOrigins(character, steamId);
        var injected = SlotApply.InjectedRowSlots(character, out var anyRow);
        var mods = TransformBuffService.ReadSlotMods(character, BarMaxSlot).ToDictionary(m => m.Slot);
        bool noEquip = injected == null && !SlotApply.HasEquipBuff(character);   // mid-swap / respawn: rows unknown

        for (int slot = 0; slot <= BarMaxSlot; slot++)   // the bar only (Business rules 5)
        {
            mods.TryGetValue(slot, out var m);
            bool rowsUnknown = injected == null && (noEquip || anyRow.Contains(slot));
            int resolved = SlotApply.CurrentSlotResolvedGuid(character, slot);
            readback.Slots.Add(new BarSlotReading
            {
                Slot = slot,
                Ability = resolved == 0 ? "" : (new PrefabGUID(resolved).GetPrefabName() ?? resolved.ToString()),
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

    /// <summary>slot → the saved set holding a bind for it (the current weapon's set over universal over a form set,
    /// the order the bar resolves them).</summary>
    static Dictionary<int, string> BindOrigins(Entity character, ulong steamId)
    {
        var d = new Dictionary<int, string>();
        foreach (var (form, slots) in Core.AbilityRegistry.AllFormSlots(steamId))
            foreach (int s in slots.Keys) d[s] = $"form:{form}";
        foreach (int s in Core.AbilityRegistry.GetSlots(steamId).Keys) d[s] = "universal";
        var weapon = SlotApply.CurrentWeapon(character);
        if (!AbilityRegistry.IsUniversalBucket(weapon))
            foreach (int s in Core.AbilityRegistry.GetWeaponSlots(steamId, weapon).Keys) d[s] = $"weapon:{weapon}";
        return d;
    }

    /// <summary>One entry per saved set that has binds: "universal: slots 2", "weapon:Sword: slots 1,4", "form:Wolf: …".</summary>
    static List<string> SavedSets(ulong steamId)
    {
        static string Line(string set, IEnumerable<int> slots) =>
            $"{set}: slots {string.Join(",", slots.OrderBy(s => s).Select(s => BarResetReply.SlotLabel(s).Replace("slot ", "")))}";
        var sets = new List<string>();
        var universal = Core.AbilityRegistry.GetSlots(steamId);
        if (universal.Count > 0) sets.Add(Line("universal", universal.Keys));
        foreach (var (weapon, slots) in Core.AbilityRegistry.AllWeaponSlots(steamId).OrderBy(kv => kv.Key.ToString()))
            if (slots.Count > 0) sets.Add(Line($"weapon:{weapon}", slots.Keys));
        foreach (var (form, slots) in Core.AbilityRegistry.AllFormSlots(steamId).OrderBy(kv => kv.Key.ToString()))
            if (slots.Count > 0) sets.Add(Line($"form:{form}", slots.Keys));
        return sets;
    }

    /// <summary>The active or parked transform record's unit prefab name, or "none".</summary>
    static string TransformName(ulong steamId)
    {
        var at = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (at == null) return "none";
        return new PrefabGUID(at.UnitPrefabGuid).GetPrefabName() ?? at.UnitPrefabGuid.ToString();
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
        // An online character whose live bar is unreachable gets no RevertTransform, so its record is KEPT: dropping it
        // would leave the form buff and summons with nothing left to end them. The reset is not clean (Unreadable) and
        // the reply sends the admin to relog/respawn and re-run, which reverts it.
        if (!_notLive && Core.AbilityRegistry.GetActiveTransform(_steamId) is not null)
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

    public int ClearEquipEntries() => SlotApply.RemoveInjectedRows(RequireEquipBuff());

    public int DestroyOverrideSources() =>
        TransformBuffService.RemoveAllFormsAndShapeshifts(_character)
        + TransformBuffService.DestroyOwnedAbilitySlotOrphans(_character);

    public int PopSlotMods() => TransformBuffService.PopSlotModifications(_character);

    public int EmptyPush() => TransformBuffService.ForceResetAbilitySlots(RequireEquipBuff());

    public int Reapply() => SlotApply.ReapplyEquipRows(RequireEquipBuff());

    /// <summary>The live steps need the held equip buff; one that vanished after planning (a swap, a respawn) is an ERR
    /// for the step, never a 0 that reads as success.</summary>
    Entity RequireEquipBuff() =>
        SlotApply.HasEquipBuff(_character) ? _character : throw new InvalidOperationException("no held equip buff");

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
