using System;
using System.Collections.Generic;
using ProjectM;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.43.5 (the deferred "W5" runtime): make GRANTED-ability power scaling actually
/// apply. V Rising already scales a player-cast ability's damage by the caster's
/// PhysicalPower / SpellPower / crit (vanilla) — that's the default and needs nothing.
/// This service layers an OPTIONAL admin multiplier on top, for two cases:
///   * Global mode <c>Grant_PowerScalingMode = Boosted</c> → every granted ability is
///     scaled by <c>Grant_PowerScalingFactor</c>.
///   * Per-ability <c>DamageScale</c> (AbilityMap) → fine-tune a specific ability.
///
/// HOW: the DealDamageEvent fields are read-only in the IL2CPP interop wrapper, so we
/// can't multiply the damage number directly. Instead — mirroring how transform stat
/// scaling works — we briefly apply a PhysicalPower+SpellPower modifier buff to the
/// caster for the duration of the cast, then let it expire. The carrier is the consumable
/// PhysicalPower potion buff (it already ships a LifeTime + ModifyUnitStatBuff_DOTS, so it
/// instantiates cleanly THIS tick — unlike a LifeTime-less set-bonus buff). A single-active
/// guard per player prevents stacking on rapid casts.
///
/// SCOPE / caveats:
///   * Only fires for abilities the player CAPTURED from Beelzebub, and only when NOT
///     transformed (transforms have their own <c>Transform_PowerScalingMode</c>).
///   * The buff is a short power WINDOW, not a surgical per-hit multiply — other actions
///     in that ~1.5s window are also scaled. Acceptable for an admin tuning knob.
///   * Safe-by-default: global mode defaults to PlayerScaled (no-op) and per-ability
///     DamageScale defaults to 1.0, so nothing changes until an admin opts in.
/// </summary>
internal static class GrantPowerScalingService
{
    // Consumable PhysicalPower potion buff — ships a LifeTime + ModifyUnitStatBuff_DOTS,
    // so immediate-instantiate works and we can repurpose its stat buffer.
    static readonly PrefabGUID PowerCarrierBuff = new(-1591883586); // AB_Consumable_PhysicalPowerPotion_T02_Buff

    const float BuffLifeSeconds = 1.5f;

    // Single-active guard: one scaling buff per player at a time (prevents stacking on
    // rapid casts). Maps steamId → UTC time the current window ends.
    static readonly Dictionary<ulong, DateTime> _windowEnds = new();

    /// <summary>
    /// Called on every player ability cast-start. No-ops unless the ability is a captured
    /// (Beelzebub-granted) ability cast in normal form and an effective scale ≠ 1 applies.
    /// </summary>
    public static void OnGrantedCast(Entity caster, PrefabGUID abilityGroup)
    {
        if (!caster.Exists() || !caster.IsPlayer()) return;
        ulong steamId = caster.GetSteamId();
        if (steamId == 0) return;

        // Transforms scale via Transform_PowerScalingMode — don't double-apply here.
        if (Core.AbilityRegistry.GetActiveTransform(steamId) is not null) return;
        // Only scale abilities the player obtained from Beelzebub (leaves innate spells/weapons alone).
        if (!Core.AbilityRegistry.HasCaptured(steamId, abilityGroup._Value)) return;

        float scale = ComputeEffectiveScale(abilityGroup);
        if (Math.Abs(scale - 1f) < 0.001f) return; // PlayerScaled / no per-ability tweak → vanilla handles it

        var now = DateTime.UtcNow;
        if (_windowEnds.TryGetValue(steamId, out var until) && until > now) return; // a window is already live

        if (ApplyPowerWindow(caster, scale))
        {
            _windowEnds[steamId] = now.AddSeconds(BuffLifeSeconds);
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] grant power-scale x{scale:F2} for {steamId} on {abilityGroup.GetPrefabName()}.");
        }
    }

    /// <summary>global mode component × per-ability DamageScale. Clamped to a sane floor.</summary>
    static float ComputeEffectiveScale(PrefabGUID abilityGroup)
    {
        float global = 1f;
        string mode = Beelzebub.Config.Settings.Grant_PowerScalingMode.Value;
        if (!string.IsNullOrWhiteSpace(mode) && mode.Trim().Equals("Boosted", StringComparison.OrdinalIgnoreCase))
        {
            float f = Beelzebub.Config.Settings.Grant_PowerScalingFactor.Value;
            if (f > 0f) global = f;
        }
        float perAbility = Core.AbilityRules.GetDamageScale(abilityGroup.GetPrefabName());
        float s = global * perAbility;
        if (s < 0.05f) s = 0.05f; // never zero/negate the player's power
        return s;
    }

    static bool ApplyPowerWindow(Entity caster, float scale)
    {
        try
        {
            if (!Core.ServerGameManager.TryInstantiateBuffEntityImmediate(caster, caster, PowerCarrierBuff, out Entity buff)
                || !buff.Exists())
                return false;

            // Short, self-destroying window.
            if (!Core.EntityManager.HasComponent<LifeTime>(buff))
                Core.EntityManager.AddComponent<LifeTime>(buff);
            buff.With((ref LifeTime lt) => { lt.Duration = BuffLifeSeconds; lt.EndAction = LifeTimeEndAction.Destroy; });

            // Auto-clear on disconnect (defensive — it's short-lived anyway).
            if (Core.EntityManager.HasComponent<BuffCategory>(buff))
                buff.With((ref BuffCategory c) => c.Groups |= BuffCategoryFlag.RemoveOnDisconnect);

            // Replace the potion's own stat bonus with our power scale (both Physical AND
            // Spell power, so it lifts the ability regardless of which it scales off).
            DynamicBuffer<ModifyUnitStatBuff_DOTS> stats = Core.EntityManager.HasBuffer<ModifyUnitStatBuff_DOTS>(buff)
                ? Core.EntityManager.GetBuffer<ModifyUnitStatBuff_DOTS>(buff)
                : Core.EntityManager.AddBuffer<ModifyUnitStatBuff_DOTS>(buff);
            stats.Clear();
            float delta = scale - 1f;
            AddStat(stats, UnitStatType.PhysicalPower, delta);
            AddStat(stats, UnitStatType.SpellPower, delta);
            return true;
        }
        catch (Exception ex)
        {
            // If the carrier ever fails to instantiate (prefab change, etc.), fail safe.
            Core.Log.LogWarning($"[Beelz] GrantPowerScalingService.ApplyPowerWindow failed: {ex.Message}");
            return false;
        }
    }

    static void AddStat(DynamicBuffer<ModifyUnitStatBuff_DOTS> buffer, UnitStatType stat, float value)
    {
        buffer.Add(new ModifyUnitStatBuff_DOTS
        {
            StatType = stat,
            ModificationType = ModificationType.MultiplyBaseAdd,
            Value = value,
            Modifier = 1,
            IncreaseByStacks = false,
            ValueByStacks = 0,
            Priority = 0,
            Id = ModificationIDs.Create().NewModificationId(),
        });
    }
}
