using System;
using System.Collections.Generic;
using ProjectM;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.71.0 — make the per-ability COOLDOWN config actually apply to a granted ability the player
/// casts from their bar.
///
/// WHY this exists: editing the prefab's <c>AbilityCooldownData.Cooldown</c> (what AbilityTuningService
/// does) is read by <c>api info</c> + fresh NPC casts, but it does NOT change the cooldown a player
/// experiences for a GRANTED (ReplaceAbilityOnSlot) ability — the live cooldown comes from per-player
/// slot state (<c>AbilityGroupSlot → StateEntity → AbilityStateBuffer → AbilityCooldownState</c>), which
/// the engine drives independently (confirmed in-game: prefab edit had no effect even after a relog).
///
/// HOW: when a player casts a captured ability that has a configured cooldown, we record it; then on the
/// next server tick (after the engine has started its own cooldown) we OVERRIDE that slot's
/// AbilityCooldownState. We don't need the engine's absolute time-base — we re-anchor off the cooldown
/// the engine just set: the cooldown began at <c>CooldownEndTime - CurrentCooldown</c>, so the new end is
/// <c>begin + configured</c>. This lengthens or shortens the live cooldown to the configured value
/// regardless of the ability's natural cooldown or the player's cooldown-reduction stats.
///
/// SCOPE: only adjusts an ability that already HAS a cooldown ticking (it re-anchors an existing one);
/// it can't add a cooldown to a truly cooldown-less ability (no time-base). Server-wide value (same for
/// every player). Per-player STATE write, uniform POLICY.
/// </summary>
internal static class AbilityCooldownEnforcer
{
    sealed class Pending
    {
        public Entity Character;
        public int AbilityGuid;
        public float ConfiguredCd;
        public DateTime ExpireUtc;
        public bool Logged;   // v0.71.1: emit the per-slot diagnostic scan only once per pending
    }

    // Keyed by (steamId, abilityGuid) so a re-cast just refreshes the pending override.
    static readonly Dictionary<(ulong, int), Pending> _pending = new();
    const float EnforceWindowSeconds = 6f;   // long enough to cover slow casts before the cooldown starts

    /// <summary>
    /// Called on every player ability cast-start. Records a pending cooldown override if the cast
    /// ability is a captured ability with a configured cooldown (per-ability override or global floor).
    /// No-op while transformed (the transform owns the bar).
    /// </summary>
    public static void OnCast(Entity caster, PrefabGUID abilityGroup)
    {
        try
        {
            if (!caster.Exists() || !caster.IsPlayer()) return;
            ulong steamId = caster.GetSteamId();
            if (steamId == 0) return;
            if (Core.AbilityRegistry.GetActiveTransform(steamId) is not null) return;

            bool captured = Core.AbilityRegistry.HasCaptured(steamId, abilityGroup._Value);
            float? configured = ResolveConfiguredCooldown(abilityGroup);
            bool diag = Beelzebub.Config.Settings.VerboseLogging.Value;

            // v0.71.1: a configured cooldown applies whether the ability was "captured" or not — the
            // capture gate was over-strict (a slotted ability may not register as captured). Record if
            // a cooldown is configured at all.
            if (configured == null)
            {
                if (diag && (captured || configured != null))
                    Core.Log.LogInfo($"[Beelz CD][diag] OnCast {abilityGroup.GetPrefabName()} ({abilityGroup._Value}) captured={captured} configured=none → skip.");
                return;
            }

            _pending[(steamId, abilityGroup._Value)] = new Pending
            {
                Character = caster,
                AbilityGuid = abilityGroup._Value,
                ConfiguredCd = configured.Value,
                ExpireUtc = DateTime.UtcNow.AddSeconds(EnforceWindowSeconds),
                Logged = false,
            };
            if (diag) Core.Log.LogInfo($"[Beelz CD][diag] OnCast recorded {abilityGroup.GetPrefabName()} ({abilityGroup._Value}) captured={captured} configured={configured.Value:F1}s.");
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz CD] OnCast failed: {ex.Message}"); }
    }

    /// <summary>The configured server-wide cooldown for an ability, or null if none applies.</summary>
    static float? ResolveConfiguredCooldown(PrefabGUID abilityGroup)
    {
        string name = abilityGroup.GetPrefabName();
        float? over = Core.AbilityRules.GetCooldownOverride(name);
        float globalMin = Math.Max(0f, Beelzebub.Config.Settings.Grant_MinimumCooldownSeconds.Value);
        if (over.HasValue) return globalMin > 0f && over.Value < globalMin ? globalMin : over.Value;
        if (globalMin > 0f) return globalMin;   // floor abilities with no explicit override
        return null;
    }

    enum EnforceResult { Applied, AlreadySet, NoLiveCooldown, NotFound }

    /// <summary>Process pending overrides each server tick. Cheap no-op when nothing is pending.</summary>
    public static void Tick()
    {
        if (_pending.Count == 0) return;
        var now = DateTime.UtcNow;
        List<(ulong, int)> done = null;
        foreach (var kv in _pending)
        {
            var p = kv.Value;
            if (!p.Character.Exists()) { (done ??= new()).Add(kv.Key); continue; }
            if (now > p.ExpireUtc)
            {
                Core.Log.LogWarning($"[Beelz CD] gave up enforcing {new PrefabGUID(p.AbilityGuid).GetPrefabName()} for {p.Character.GetSteamId()} — never found a live cooldown to re-anchor within {EnforceWindowSeconds:F0}s.");
                (done ??= new()).Add(kv.Key);
                continue;
            }
            var r = TryEnforce(p);
            if (r == EnforceResult.Applied || r == EnforceResult.AlreadySet) (done ??= new()).Add(kv.Key);
        }
        if (done != null) foreach (var k in done) _pending.Remove(k);
    }

    /// <summary>
    /// Walk the player's ability slots, find the one holding the pending ability, and re-anchor its
    /// live cooldown to the configured value. Emits a one-shot per-slot diagnostic (verbose) so we can
    /// see the slot GUIDs + cooldown values when it isn't matching.
    /// </summary>
    static EnforceResult TryEnforce(Pending p)
    {
        Entity character = p.Character;
        int abilityGuid = p.AbilityGuid;
        float configuredCd = p.ConfiguredCd;
        bool diag = Beelzebub.Config.Settings.VerboseLogging.Value && !p.Logged;
        var diagLine = diag ? new System.Text.StringBuilder() : null;
        var result = EnforceResult.NotFound;
        try
        {
            if (!Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(character)) return EnforceResult.NotFound;
            var slots = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(character);
            for (int i = 0; i < slots.Length; i++)
            {
                Entity slotEnt = slots[i].GroupSlotEntity._Entity;
                if (!slotEnt.Exists() || !Core.EntityManager.HasComponent<AbilityGroupSlot>(slotEnt)) continue;
                var ags = Core.EntityManager.GetComponentData<AbilityGroupSlot>(slotEnt);
                Entity stateEnt = ags.StateEntity._Entity;
                if (!stateEnt.Exists()) continue;
                int stateGuid = stateEnt.GetPrefabGuid()._Value;
                diagLine?.Append($" slot{i}=state:{stateGuid}");
                if (stateGuid != abilityGuid) continue;   // not this ability's slot
                if (!Core.EntityManager.HasBuffer<AbilityStateBuffer>(stateEnt)) continue;

                var stateBuf = Core.EntityManager.GetBuffer<AbilityStateBuffer>(stateEnt);
                for (int j = 0; j < stateBuf.Length; j++)
                {
                    Entity asEnt = stateBuf[j].StateEntity._Entity;
                    if (!asEnt.Exists() || !Core.EntityManager.HasComponent<AbilityCooldownState>(asEnt)) continue;
                    var cd = Core.EntityManager.GetComponentData<AbilityCooldownState>(asEnt);
                    diagLine?.Append($" (cur={cd.CurrentCooldown:F2},end={cd.CooldownEndTime:F2})");

                    if (cd.CurrentCooldown <= 0.01f) { result = EnforceResult.NoLiveCooldown; continue; }   // wait for the cooldown to start
                    if (Math.Abs(cd.CurrentCooldown - configuredCd) < 0.01f) { result = EnforceResult.AlreadySet; continue; }

                    double begin = cd.CooldownEndTime - cd.CurrentCooldown;              // when the cooldown started
                    cd.CurrentCooldown = configuredCd;
                    cd.CooldownEndTime = begin + configuredCd;
                    Core.EntityManager.SetComponentData(asEnt, cd);
                    Core.Log.LogInfo($"[Beelz CD] enforced cooldown {configuredCd:F2}s on {new PrefabGUID(abilityGuid).GetPrefabName()} for {character.GetSteamId()}.");
                    return EnforceResult.Applied;
                }
            }
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz CD] TryEnforce failed: {ex.Message}"); }
        finally
        {
            if (diagLine != null) { Core.Log.LogInfo($"[Beelz CD][diag] scan {new PrefabGUID(abilityGuid).GetPrefabName()} → result={result}{diagLine}"); p.Logged = true; }
        }
        return result;
    }
}
