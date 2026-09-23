using System;
using System.Collections.Generic;
using ProjectM;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.71.0, reworked v0.134.0 (P3) — the ONLY place a per-ability cooldown rule reaches the game.
///
/// WHY runtime: a granted ability's live cooldown comes from the player's own slot state
/// (<c>AbilityGroupSlot.StateEntity → AbilityStateBuffer → AbilityCooldownState</c>), not from the prefab. The old
/// baked prefab write also changed the SOURCE BOSS's cooldown; since v0.134.0 nothing is baked and bosses keep their
/// shipped cooldowns.
///
/// WHAT is enforced (per cast, exactly once): a CAPTURED ability cast FROM A BAR SLOT
/// (<see cref="CastHistoryService.CastRecord.FromSlot"/> — structural, so a `.beelz cast` force-cast is excluded;
/// that path has its own tracker using the same <see cref="Logic.CooldownMath"/>). Precedence:
/// absolute <c>cooldown</c> ?? live cooldown × <c>cooldownscale</c> (so gear/buff cooldown reduction still counts),
/// then the Grant_MinimumCooldownSeconds floor. Charge-based abilities are skipped entirely (warned once).
/// Transformed players are not touched (the transform owns the bar and its own CooldownScale).
///
/// HOW: at cast start the slot state's current <c>CooldownEndTime</c> is snapshotted and the cast joins a FIFO per
/// (player, ability). Each tick, a CHANGED end time with a positive <c>CurrentCooldown</c> is a newly started
/// cooldown: the oldest pending cast consumes it, and the cooldown is re-anchored to <c>begin + target</c> where
/// <c>begin = CooldownEndTime - CurrentCooldown</c>. Only the slot whose StateEntity IS the recorded group entity
/// is touched. Entries expire after <see cref="EnforceWindowSeconds"/> (fail safe: vanilla cooldown).
///
/// v0.136.0 — CREATE mode: an absolute <c>cooldown</c> on an ability whose own cooldown is 0 (the game never starts
/// one, so there is nothing to re-anchor) is STARTED here at cast time on the player's slot state
/// (<c>CooldownEndTime = now + cooldown</c>), then guarded for <see cref="GuardSeconds"/>: if the game's cast-end
/// resets it, it is written back. Still player slot state only — no prefab is touched, bosses stay vanilla.
/// </summary>
internal static class AbilityCooldownEnforcer
{
    sealed class Pending
    {
        public long CastId;
        public Entity Character;
        public Entity GroupEntity;
        public int AbilityGuid;
        public float? Absolute;
        public float Scale;
        public float Floor;
        public double[] PreEnd;     // CooldownEndTime per AbilityStateBuffer element at cast start
        public DateTime ExpireUtc;
    }

    static readonly Dictionary<(ulong, int, Entity), Queue<Pending>> _pending = new();   // per slot state: one slot never blocks another
    static readonly HashSet<int> _chargeWarned = new();
    const float EnforceWindowSeconds = 6f;   // long enough to cover slow casts before the cooldown starts
    const float GuardSeconds = 12f;          // CREATE mode: re-assert a started cooldown this long (covers long casts)

    // CREATE mode guards: slot state element → (end time we wrote, stop guarding at).
    static readonly Dictionary<Entity, (double End, float Seconds, DateTime Until)> _guards = new();
    static readonly Dictionary<int, bool> _zeroNative = new();

    // Counters for `damage-stats`-style diagnostics (ability-inspect shows them).
    public static long Applied { get; private set; }
    public static long Expired { get; private set; }
    public static long Created { get; private set; }

    /// <summary>
    /// Called on every player cast-start, AFTER <see cref="CastHistoryService.RecordCast"/>. Queues a pending
    /// enforcement when a cooldown rule applies to this captured slot cast.
    /// </summary>
    public static void OnCast(Entity caster, PrefabGUID abilityGroup, CastHistoryService.CastRecord rec)
    {
        try
        {
            if (!caster.Exists() || !caster.IsPlayer()) return;
            ulong steamId = caster.GetSteamId();
            if (steamId == 0) return;
            if (Core.AbilityRegistry.GetActiveTransform(steamId) is not null) return;

            // The record created for THIS event (never a stale Latest — a failed record means no enforcement).
            if (rec == null || rec.Group != abilityGroup._Value || !rec.Captured || !rec.FromSlot) return;

            string name = abilityGroup.GetPrefabName();
            float? abs = Core.AbilityRules.GetCooldownOverride(name);
            float scale = Core.AbilityRules.GetCooldownScale(name);
            float floor = Math.Max(0f, Beelzebub.Config.Settings.Grant_MinimumCooldownSeconds.Value);
            bool scaleIsIdentity = !float.IsFinite(scale) || scale <= 0f || Math.Abs(scale - 1f) < 1e-4f;
            if (abs == null && scaleIsIdentity && floor <= 0f) return;   // no rule → vanilla

            if (HasCharges(abilityGroup._Value))
            {
                if (_chargeWarned.Add(abilityGroup._Value))
                    Core.Log.LogWarning($"[Beelz CD] {name} uses charges — cooldown/cooldownscale/floor are not applied to charge abilities (tune 'chargetime' instead).");
                return;
            }

            var pre = ReadEndTimes(rec.GroupEntity);
            if (pre == null) return;   // no cooldown state on this slot — nothing to re-anchor

            // v0.136.0 CREATE mode: the game will never start a cooldown for a zero-cooldown ability — start it here.
            if (abs is float absSec && absSec > 0.01f && NativeCooldownIsZero(abilityGroup._Value))
            {
                float t = Math.Max(absSec, floor);
                if (StartCooldown(rec.GroupEntity, t) && Beelzebub.Config.Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz CD] cast #{rec.CastId} {name}: no native cooldown — started {t:F2}s.");
                return;
            }

            var key = (steamId, abilityGroup._Value, rec.GroupEntity);
            if (!_pending.TryGetValue(key, out var q)) _pending[key] = q = new Queue<Pending>();
            q.Enqueue(new Pending
            {
                CastId = rec.CastId,
                Character = caster,
                GroupEntity = rec.GroupEntity,
                AbilityGuid = abilityGroup._Value,
                Absolute = abs,
                Scale = scaleIsIdentity ? 1f : scale,
                Floor = floor,
                PreEnd = pre,
                ExpireUtc = DateTime.UtcNow.AddSeconds(EnforceWindowSeconds),
            });
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz CD] queued cast #{rec.CastId} {name} abs={abs?.ToString("0.##") ?? "-"} scale={scale:0.##} floor={floor:0.##}");
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz CD] OnCast failed: {ex.Message}"); }
    }

    /// <summary>
    /// v0.136.0: does neither the group nor any cast it starts directly carry a positive AbilityCooldownData? Then the
    /// game never starts a cooldown for it, and an absolute `cooldown` rule must CREATE one. Cached per group.
    /// </summary>
    static bool NativeCooldownIsZero(int groupGuid)
    {
        if (_zeroNative.TryGetValue(groupGuid, out bool z)) return z;
        z = true;
        try
        {
            foreach (var n in AbilityChainGraph.GetOrWalk(groupGuid).Nodes)
            {
                if (n.Guid != groupGuid && !(n.Via == AbilityChainGraph.EdgeKind.StartAbility && n.Parent == groupGuid)) continue;
                if (Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(new PrefabGUID(n.Guid), out var p)
                    && p.Exists() && p.TryGetComponent<AbilityCooldownData>(out var cdd)
                    && cdd.Cooldown._Value > 0.01f) { z = false; break; }
            }
        }
        catch { z = false; }   // unknown → behave as before (re-anchor only)
        _zeroNative[groupGuid] = z;
        return z;
    }

    /// <summary>CREATE mode: start a <paramref name="seconds"/> cooldown on every cooldown state of this slot, and guard it.</summary>
    static bool StartCooldown(Entity groupEntity, float seconds)
    {
        if (!Core.EntityManager.HasBuffer<AbilityStateBuffer>(groupEntity)) return false;
        double now = Core.ServerGameManager.ServerTime;
        var targets = new List<Entity>();
        var buf = Core.EntityManager.GetBuffer<AbilityStateBuffer>(groupEntity);
        for (int j = 0; j < buf.Length; j++)
        {
            Entity asEnt = buf[j].StateEntity._Entity;
            if (asEnt.Exists() && Core.EntityManager.HasComponent<AbilityCooldownState>(asEnt)) targets.Add(asEnt);
        }
        foreach (var asEnt in targets)
        {
            var cd = Core.EntityManager.GetComponentData<AbilityCooldownState>(asEnt);
            cd.CurrentCooldown = seconds;
            cd.CooldownEndTime = now + seconds;
            Core.EntityManager.SetComponentData(asEnt, cd);
            _guards[asEnt] = (now + seconds, seconds, DateTime.UtcNow.AddSeconds(Math.Min(GuardSeconds, seconds)));
        }
        if (targets.Count > 0) Created++;
        return targets.Count > 0;
    }

    /// <summary>CREATE mode: if the game's cast-end reset one of our started cooldowns, write it back.</summary>
    static void TickGuards()
    {
        if (_guards.Count == 0) return;
        var nowUtc = DateTime.UtcNow;
        List<Entity> done = null;
        foreach (var (asEnt, g) in _guards)
        {
            if (nowUtc > g.Until || !asEnt.Exists() || !Core.EntityManager.HasComponent<AbilityCooldownState>(asEnt)) { (done ??= new()).Add(asEnt); continue; }
            var cd = Core.EntityManager.GetComponentData<AbilityCooldownState>(asEnt);
            if (cd.CooldownEndTime < g.End - 0.05)
            {
                cd.CooldownEndTime = g.End;
                cd.CurrentCooldown = g.Seconds;
                Core.EntityManager.SetComponentData(asEnt, cd);
            }
        }
        if (done != null) foreach (var e in done) _guards.Remove(e);
    }

    /// <summary>Does the ability group, or any cast it starts directly, use charges? (shared with `.beelz cast`)</summary>
    public static bool HasCharges(int groupGuid)
    {
        if (groupGuid == 0) return false;
        try
        {
            foreach (var n in AbilityChainGraph.GetOrWalk(groupGuid).Nodes)
            {
                if (n.Guid != groupGuid && !(n.Via == AbilityChainGraph.EdgeKind.StartAbility && n.Parent == groupGuid)) continue;
                if (Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(new PrefabGUID(n.Guid), out var p)
                    && p.Exists() && p.Has<AbilityChargesData>()) return true;
            }
        }
        catch { }
        return false;
    }

    static double[] ReadEndTimes(Entity stateEnt)
    {
        if (!stateEnt.Exists() || !Core.EntityManager.HasBuffer<AbilityStateBuffer>(stateEnt)) return null;
        var buf = Core.EntityManager.GetBuffer<AbilityStateBuffer>(stateEnt);
        var ends = new double[buf.Length];
        bool any = false;
        for (int j = 0; j < buf.Length; j++)
        {
            Entity asEnt = buf[j].StateEntity._Entity;
            if (asEnt.Exists() && Core.EntityManager.HasComponent<AbilityCooldownState>(asEnt))
            {
                ends[j] = Core.EntityManager.GetComponentData<AbilityCooldownState>(asEnt).CooldownEndTime;
                any = true;
            }
            else ends[j] = double.NaN;
        }
        return any ? ends : null;
    }

    /// <summary>Process pending enforcements each server tick. Cheap no-op when nothing is pending.</summary>
    public static void Tick()
    {
        try { TickGuards(); } catch (Exception ex) { Core.Log.LogWarning($"[Beelz CD] guard tick failed: {ex.Message}"); _guards.Clear(); }
        if (_pending.Count == 0) return;
        var now = DateTime.UtcNow;
        List<(ulong, int, Entity)> empty = null;
        foreach (var kv in _pending)
        {
            var q = kv.Value;
            while (q.Count > 0)
            {
                var p = q.Peek();
                if (!p.Character.Exists() || !p.GroupEntity.Exists() || now > p.ExpireUtc)
                {
                    q.Dequeue();
                    Expired++;
                    if (Beelzebub.Config.Settings.VerboseLogging.Value)
                        Core.Log.LogInfo($"[Beelz CD] cast #{p.CastId} {new PrefabGUID(p.AbilityGuid).GetPrefabName()}: no new cooldown seen within {EnforceWindowSeconds:0}s — left vanilla.");
                    continue;
                }
                if (!TryEnforce(p, out double writtenEnd, out int index)) break;   // not started yet — wait
                q.Dequeue();
                // Our own write changed the end time: re-base the remaining casts so they don't consume it.
                // The queue is per slot state, so every remaining cast shares that end time.
                foreach (var rest in q)
                    if (index < rest.PreEnd.Length) rest.PreEnd[index] = writtenEnd;
            }
            if (q.Count == 0) (empty ??= new()).Add(kv.Key);
        }
        if (empty != null) foreach (var k in empty) _pending.Remove(k);
    }

    /// <summary>
    /// Look for a NEWLY started cooldown on the recorded slot state (end time differs from the cast-start snapshot
    /// and a cooldown is running). Returns true when this cast is done (enforced, or started with nothing to change).
    /// </summary>
    static bool TryEnforce(Pending p, out double writtenEnd, out int index)
    {
        writtenEnd = 0; index = -1;
        try
        {
            if (!Core.EntityManager.HasBuffer<AbilityStateBuffer>(p.GroupEntity)) return false;
            var stateBuf = Core.EntityManager.GetBuffer<AbilityStateBuffer>(p.GroupEntity);
            for (int j = 0; j < stateBuf.Length && j < p.PreEnd.Length; j++)
            {
                Entity asEnt = stateBuf[j].StateEntity._Entity;
                if (!asEnt.Exists() || !Core.EntityManager.HasComponent<AbilityCooldownState>(asEnt)) continue;
                var cd = Core.EntityManager.GetComponentData<AbilityCooldownState>(asEnt);
                if (cd.CurrentCooldown <= 0.01f) continue;
                if (!double.IsNaN(p.PreEnd[j]) && Math.Abs(cd.CooldownEndTime - p.PreEnd[j]) < 1e-3) continue;   // not new

                index = j;
                writtenEnd = cd.CooldownEndTime;
                float? target = Logic.CooldownMath.Resolve(cd.CurrentCooldown, p.Absolute, p.Scale, p.Floor, hasCharges: false);
                if (target is not float t) return true;   // started, nothing to change

                double begin = cd.CooldownEndTime - cd.CurrentCooldown;   // when the cooldown started
                cd.CurrentCooldown = t;
                cd.CooldownEndTime = begin + t;
                Core.EntityManager.SetComponentData(asEnt, cd);
                writtenEnd = cd.CooldownEndTime;
                Applied++;
                if (Beelzebub.Config.Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz CD] cast #{p.CastId} {new PrefabGUID(p.AbilityGuid).GetPrefabName()}: cooldown -> {t:F2}s.");
                return true;
            }
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz CD] TryEnforce failed: {ex.Message}"); return true; }
        return false;
    }

    /// <summary>Drop a player's pending entries (disconnect).</summary>
    public static void OnDisconnect(ulong steamId)
    {
        List<(ulong, int, Entity)> dead = null;
        foreach (var k in _pending.Keys) if (k.Item1 == steamId) (dead ??= new()).Add(k);
        if (dead != null) foreach (var k in dead) _pending.Remove(k);
    }
}
