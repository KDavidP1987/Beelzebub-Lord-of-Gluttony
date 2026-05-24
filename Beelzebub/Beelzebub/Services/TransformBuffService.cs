using System;
using System.Collections.Generic;
using ProjectM;
using ProjectM.Gameplay.Scripting;
using ProjectM.Network;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// Z1: applies the transform spell-bar via a custom carrier buff (Bloodcraft pattern).
/// We hijack V Rising's own slot-replacement carrier `Buff_VBlood_Ability_Replace`
/// (the buff the game uses when feeding a V-Blood temporarily overlays the player's
/// third slot). We add our own ReplaceAbilityOnSlotBuff entries (Target=BuffTarget,
/// high priority) to the buff entity itself.
///
/// Why this matters:
///   * Pre-Z1 (v0.13.0 and earlier): we mutated the EquipBuff_Weapon entity's buffer
///     and called ServerGameManager.ModifyAbilityGroupOnSlot. That registered the
///     EquipBuff as a modification source, so V Rising flooded the console with
///     "Clearing entity X which is a modification source ... AbilityGroupSlot"
///     warnings every time the EquipBuff entity recycled (weapon swap / save tick).
///     It also broke revert for player spell-book slots (5/6) because calling
///     Modify with PrefabGUID.Empty pins the slot to *literally empty* rather than
///     restoring the spell-book choice.
///   * Post-Z1: a single custom buff owns the overrides for the whole transform.
///     Apply = ApplyBuffDebugEvent + enrich the resulting buff. Revert = destroy
///     the buff and let V Rising re-resolve the slot stack naturally. Spell-book
///     slots come back automatically.
/// </summary>
internal static class TransformBuffService
{
    // Buff_VBlood_Ability_Replace — V Rising's own carrier for slot replacement.
    // Confirmed via the prefab dump: has BuffCategory + BuffType + ReplaceAbility
    // tokens. Non-visual, doesn't change appearance, and stable across weapon swaps
    // (the buff entity lives independent of EquipBuff_Weapon).
    static readonly PrefabGUID CarrierBuff = new(1171608023);

    /// <summary>
    /// Apply the transform's slot overrides via a custom carrier buff.
    /// Returns true if the buff was applied and overlays were attached.
    /// </summary>
    public static bool Apply(Entity character, IReadOnlyList<int> abilities, float? durationSeconds = null, int unitPrefabGuid = 0)
    {
        if (!character.Exists() || abilities is null || abilities.Count == 0) return false;

        try
        {
            // First clean up any stale Beelzebub buff (defensive — should never fire,
            // but covers crash-recovery / double-activate edge cases).
            RemoveInternal(character);

            // **v0.17.1 fix**: use TryInstantiateBuffEntityImmediate (synchronous)
            // instead of DebugEventsSystem.ApplyBuff (asynchronous). The async path
            // queues an ApplyBuffDebugEvent for the next system tick, so a
            // TryGetBuff call this frame would return false, the enrichment would
            // never happen, and the player would see a "transformed" state with no
            // spell-bar change. The immediate variant creates the buff entity in
            // the current frame and hands us the entity back to enrich directly.
            // Pattern confirmed by Bloodcraft FamiliarBindingSystem.cs:795.
            Entity buffEntity;
            if (!Core.ServerGameManager.TryInstantiateBuffEntityImmediate(character, character, CarrierBuff, out buffEntity)
                || !buffEntity.Exists())
            {
                Core.Log.LogError("[Beelz] TransformBuffService.Apply: TryInstantiateBuffEntityImmediate failed for carrier buff; transform aborted.");
                return false;
            }

            // Tag the buff so revert can find it even if our cache is lost across server restart.
            // (V Rising auto-destroys buffs on disconnect via the RemoveOnDisconnect flag below.)
            if (Core.EntityManager.HasComponent<BuffCategory>(buffEntity))
            {
                buffEntity.With((ref BuffCategory cat) =>
                {
                    cat.Groups = cat.Groups | BuffCategoryFlag.RemoveOnDisconnect;
                });
            }

            // Lifetime: timed transforms get an explicit duration; Toggle transforms
            // stay forever (we destroy on revert).
            if (durationSeconds.HasValue && durationSeconds.Value > 0f)
            {
                if (!Core.EntityManager.HasComponent<LifeTime>(buffEntity))
                {
                    Core.EntityManager.AddComponent<LifeTime>(buffEntity);
                }
                buffEntity.With((ref LifeTime lt) =>
                {
                    lt.Duration = durationSeconds.Value;
                    lt.EndAction = LifeTimeEndAction.Destroy;
                });
            }
            else
            {
                // Toggle mode: explicit infinite lifetime (overrides whatever the
                // carrier prefab baked in — by default Buff_VBlood_Ability_Replace
                // has a short timer for the feed-overlay use case).
                if (!Core.EntityManager.HasComponent<LifeTime>(buffEntity))
                {
                    Core.EntityManager.AddComponent<LifeTime>(buffEntity);
                }
                buffEntity.With((ref LifeTime lt) =>
                {
                    lt.Duration = -1f;
                    lt.EndAction = LifeTimeEndAction.None;
                });
            }

            // Attach a ReplaceAbilityOnSlotBuff buffer to the buff entity and fill it
            // with the transform's per-slot overrides. Target=BuffTarget routes the
            // replacement through the buff to the player; Priority=99 wins over
            // weapon-natural abilities and any other overlay short of admin debug.
            DynamicBuffer<ReplaceAbilityOnSlotBuff> replaceBuffer;
            if (Core.EntityManager.HasBuffer<ReplaceAbilityOnSlotBuff>(buffEntity))
            {
                replaceBuffer = Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(buffEntity);
                replaceBuffer.Clear(); // The carrier prefab seeds an entry for slot 3 — drop it.
            }
            else
            {
                replaceBuffer = Core.EntityManager.AddBuffer<ReplaceAbilityOnSlotBuff>(buffEntity);
            }

            for (int i = 0; i < abilities.Count && i < 6; i++)
            {
                int slot = i + 1;
                replaceBuffer.Add(new ReplaceAbilityOnSlotBuff
                {
                    Target = ReplaceAbilityTarget.BuffTarget,
                    Slot = slot,
                    NewGroupId = new PrefabGUID(abilities[i]),
                    Priority = 99,
                    CopyCooldown = true,
                    CastBlockType = GroupSlotModificationCastBlockType.WholeCast,
                });
            }

            // TX6+TX7+TX7-extended: attach per-transformation stat scaling to the
            // carrier buff, routing through the admin-selected power-scaling mode
            // (CuratedScales / PrefabAbsolute / PlayerScaled / PlayerLeveled).
            // PlayerLeveled (v0.19.0) needs the character entity for UnitLevel read.
            // Bonuses auto-revert when the buff is destroyed.
            int statBonusCount = AttachStatScales(buffEntity, unitPrefabGuid, character);

            // TX8: lock weapon swap while FullReplace is active. Uses V Rising's
            // native BlockEquipmentSwapping component — the engine respects it for
            // the lifetime of the buff. Auto-clears when the buff is destroyed.
            if (unitPrefabGuid != 0 && Core.AbilityRules.IsTransformFullReplace(unitPrefabGuid))
            {
                AttachWeaponSwapLock(buffEntity);
            }

            // Force V Rising to re-resolve overlays this frame instead of next tick.
            if (Core.ReplaceAbilityOnSlotSystem != null)
            {
                Core.ReplaceAbilityOnSlotSystem.OnUpdate();
            }

            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] TransformBuffService.Apply: buffEntity={buffEntity} abilities={abilities.Count} stats={statBonusCount} duration={(durationSeconds.HasValue ? durationSeconds.Value.ToString("F0") + "s" : "toggle")}");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] TransformBuffService.Apply failed: {ex}");
            return false;
        }
    }

    /// <summary>
    /// v0.31.0 — ExoForm-style transform: apply the unit's actual FORM/shapeshift
    /// buff (so the player takes on the boss's rig → animation-bound + chained
    /// abilities fire) and slot a curated 8-slot ability set on it. Mirrors
    /// Bloodcraft's <c>Shapeshifts.ModifyShapeshiftBuff</c>. Used for units in
    /// <see cref="BossFormRegistry"/>; other units use the ability-only <see cref="Apply"/>.
    /// </summary>
    public static bool ApplyForm(Entity character, int formBuffGuid, IReadOnlyList<int> abilities, float? durationSeconds = null)
    {
        if (!character.Exists() || formBuffGuid == 0 || abilities is null || abilities.Count == 0) return false;

        try
        {
            RemoveInternal(character); // clear any prior carrier/form buff

            var formBuff = new PrefabGUID(formBuffGuid);
            if (!Core.ServerGameManager.TryInstantiateBuffEntityImmediate(character, character, formBuff, out Entity buffEntity)
                || !buffEntity.Exists())
            {
                Core.Log.LogError($"[Beelz] ApplyForm: TryInstantiateBuffEntityImmediate failed for form buff {formBuffGuid}.");
                return false;
            }

            // Neuter the form's own chained "advance to next phase" buff so applying
            // it to a player doesn't kick off the boss's scripted phase sequence.
            if (Core.EntityManager.HasBuffer<ApplyBuffOnGameplayEvent>(buffEntity))
            {
                var ab = Core.EntityManager.GetBuffer<ApplyBuffOnGameplayEvent>(buffEntity);
                if (ab.Length > 0) { var e0 = ab[0]; e0.Buff0 = PrefabGUID.Empty; ab[0] = e0; }
            }

            // Shapeshift enrichment (Bloodcraft recipe): the two script/data tags make
            // slot replacement integrate with the shapeshift; categorize as a
            // shapeshift that auto-clears on disconnect; Block buff type.
            if (!Core.EntityManager.HasComponent<ReplaceAbilityOnSlotData>(buffEntity))
                Core.EntityManager.AddComponent<ReplaceAbilityOnSlotData>(buffEntity);
            if (!Core.EntityManager.HasComponent<Script_Buff_Shapeshift_DataShared>(buffEntity))
                Core.EntityManager.AddComponent<Script_Buff_Shapeshift_DataShared>(buffEntity);
            if (Core.EntityManager.HasComponent<BuffCategory>(buffEntity))
                buffEntity.With((ref BuffCategory cat) => cat.Groups = BuffCategoryFlag.Shapeshift | BuffCategoryFlag.RemoveOnDisconnect);
            if (Core.EntityManager.HasComponent<Buff>(buffEntity))
                buffEntity.With((ref Buff b) => b.BuffType = BuffType.Block);

            // Lifetime: timed → Destroy after duration; toggle → infinite (revert destroys).
            if (!Core.EntityManager.HasComponent<LifeTime>(buffEntity))
                Core.EntityManager.AddComponent<LifeTime>(buffEntity);
            if (durationSeconds.HasValue && durationSeconds.Value > 0f)
                buffEntity.With((ref LifeTime lt) => { lt.Duration = durationSeconds.Value; lt.EndAction = LifeTimeEndAction.Destroy; });
            else
                buffEntity.With((ref LifeTime lt) => { lt.Duration = -1f; lt.EndAction = LifeTimeEndAction.None; });

            // Slot the curated abilities on the form's 0-7 bar (forms expose 8 slots,
            // unlike the player's normal 1-6; Bloodcraft slots 0-7 here).
            DynamicBuffer<ReplaceAbilityOnSlotBuff> replaceBuffer;
            if (Core.EntityManager.HasBuffer<ReplaceAbilityOnSlotBuff>(buffEntity))
            {
                replaceBuffer = Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(buffEntity);
                replaceBuffer.Clear();
            }
            else
            {
                replaceBuffer = Core.EntityManager.AddBuffer<ReplaceAbilityOnSlotBuff>(buffEntity);
            }

            for (int i = 0; i < abilities.Count && i < 8; i++)
            {
                replaceBuffer.Add(new ReplaceAbilityOnSlotBuff
                {
                    Target = ReplaceAbilityTarget.BuffTarget,
                    Slot = i,
                    NewGroupId = new PrefabGUID(abilities[i]),
                    Priority = 99,
                    CopyCooldown = true,
                    CastBlockType = GroupSlotModificationCastBlockType.WholeCast,
                });
            }

            if (Core.ReplaceAbilityOnSlotSystem != null)
                Core.ReplaceAbilityOnSlotSystem.OnUpdate();

            Core.Log.LogInfo($"[Beelz] ApplyForm: applied form buff {formBuff.GetPrefabName()} + {abilities.Count} abilities to {character} (duration={(durationSeconds.HasValue ? durationSeconds.Value.ToString("F0") + "s" : "toggle")}).");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] ApplyForm failed: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Replace the carrier buff's overrides in-place — used by `.beelz phase` to swap
    /// boss-phase loadouts without dropping the buff (and losing animation state).
    /// Falls back to a fresh Apply if the buff isn't found.
    /// </summary>
    public static bool Reapply(Entity character, IReadOnlyList<int> abilities)
    {
        if (!character.Exists() || abilities is null || abilities.Count == 0) return false;

        // v0.27.1 FIX: in-place mutation of the carrier buff's ReplaceAbilityOnSlotBuff
        // buffer does NOT make V Rising re-resolve the player's LIVE ability bar —
        // slot replacements are only resolved when the carrier buff is freshly
        // applied/spawned. Result: a phase swap computed the correct (different)
        // abilities and reported success, but the visible bar never changed
        // (confirmed by the Dracula phase test — this was the first real
        // multi-phase unit, so the latent bug had never surfaced). Fix: drop the
        // existing carrier and re-Apply a fresh one — the exact path the initial
        // transform uses, which DOES update the bar. The brief sub-frame gap is
        // acceptable for a phase swap, and Apply re-attaches stat overlays anyway.
        try
        {
            Remove(character);
            bool ok = Apply(character, abilities);
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] TransformBuffService.Reapply: re-applied carrier with {System.Math.Min(abilities.Count, 6)} slot ability(ies) (ok={ok}).");
            return ok;
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] TransformBuffService.Reapply failed: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Destroy the carrier buff, letting V Rising re-resolve the player's slot stack
    /// from natural sources (weapon + spell-book + any other overlays). Returns true
    /// if a buff was found and destroyed.
    /// </summary>
    public static bool Remove(Entity character)
    {
        if (!character.Exists()) return false;
        try
        {
            bool removed = RemoveInternal(character);
            if (removed && Core.ReplaceAbilityOnSlotSystem != null)
            {
                Core.ReplaceAbilityOnSlotSystem.OnUpdate();
            }
            return removed;
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] TransformBuffService.Remove failed: {ex}");
            return false;
        }
    }

    static bool RemoveInternal(Entity character)
    {
        bool removed = false;

        // Default ability-only carrier buff.
        if (Core.ServerGameManager.TryGetBuff(character, CarrierBuff.ToIdentifier(), out Entity buffEntity)
            && buffEntity.Exists())
        {
            DestroyUtility.Destroy(Core.EntityManager, buffEntity, DestroyDebugReason.TryRemoveBuff);
            removed = true;
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] TransformBuffService.Remove: destroyed carrier buff {buffEntity}.");
        }

        // v0.31.0: ExoForm-style form buffs (Dracula/Morgana/…). Destroy whichever
        // is active so the player drops the shapeshift on revert.
        foreach (int formGuid in Services.BossFormRegistry.FormBuffGuids)
        {
            if (Core.ServerGameManager.TryGetBuff(character, new PrefabGUID(formGuid).ToIdentifier(), out Entity formBuff)
                && formBuff.Exists())
            {
                DestroyUtility.Destroy(Core.EntityManager, formBuff, DestroyDebugReason.TryRemoveBuff);
                removed = true;
                if (Beelzebub.Config.Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz] TransformBuffService.Remove: destroyed form buff {formBuff} ({new PrefabGUID(formGuid).GetPrefabName()}).");
            }
        }

        return removed;
    }

    static NetworkId GetNetworkId(Entity entity) =>
        entity.TryGetComponent<NetworkId>(out var id) ? id : default;

    static Entity GetUserEntity(Entity entity)
    {
        if (entity.TryGetComponent<PlayerCharacter>(out var pc)) return pc.UserEntity;
        return entity;
    }

    /// <summary>
    /// TX6+TX7: route stat scaling through the admin-selected power-scaling mode.
    /// Returns the number of ModifyUnitStatBuff_DOTS entries added.
    ///
    /// Mode dispatch:
    ///   CuratedScales — admin DamageScale/HealthScale/MovementSpeedScale/CooldownScale
    ///                   from the TransformMap entry (the TX6 v0.15.0 behavior).
    ///   PrefabAbsolute — read the CHAR_ prefab UnitStats and apply matching
    ///                    PhysicalPower / SpellPower / MaxHealth / MovementSpeed
    ///                    as Add (not MultiplyBaseAdd) so the player's stats
    ///                    become roughly the boss's. Player hits as hard as
    ///                    the boss does.
    ///   PlayerScaled  — apply nothing. V Rising's vanilla damage formula uses
    ///                   the player's natural stats; the captured ability's
    ///                   base damage is multiplied by the player's existing
    ///                   PhysicalPower/SpellPower (including Bloodcraft buffs).
    ///                   Auto-tracks player progression + prestige resets.
    /// </summary>
    static int AttachStatScales(Entity buffEntity, int unitPrefabGuid, Entity character)
    {
        if (unitPrefabGuid == 0) return 0;

        var mode = Core.AbilityRules.GetTransformPowerScalingMode(unitPrefabGuid);
        switch (mode)
        {
            case PowerScalingMode.PlayerScaled:
                // Intentional no-op. Vanilla V Rising scales the ability for us
                // using the player's existing PhysicalPower / SpellPower (which
                // Bloodcraft also modifies via its own buffs). Returning 0
                // signals "no stat overlay" to the verbose-log line.
                return 0;
            case PowerScalingMode.PrefabAbsolute:
                return AttachPrefabAbsolute(buffEntity, unitPrefabGuid, scaleFactor: 1.0f);
            case PowerScalingMode.PlayerLeveled:
                // v0.19.0: same prefab-read path as PrefabAbsolute but with a
                // level-progression multiplier on every stat. Falls back to
                // PrefabAbsolute behavior if we can't read UnitLevel.
                float factor = ComputePlayerLevelFactor(character);
                return AttachPrefabAbsolute(buffEntity, unitPrefabGuid, scaleFactor: factor);
            case PowerScalingMode.CuratedScales:
            default:
                return AttachCuratedScales(buffEntity, unitPrefabGuid);
        }
    }

    /// <summary>
    /// v0.19.0 PlayerLeveled: compute the level-progression factor from the
    /// player's current UnitLevel and the Transform_PlayerLeveled_MaxLevel
    /// config anchor. Returns 1.0 if the level can't be read (defensive — same
    /// as PrefabAbsolute in that case).
    /// </summary>
    static float ComputePlayerLevelFactor(Entity character)
    {
        if (!character.Exists()) return 1.0f;
        if (!character.TryGetComponent<UnitLevel>(out var ul)) return 1.0f;
        int maxLevel = Beelzebub.Config.Settings.Transform_PlayerLeveled_MaxLevel.Value;
        if (maxLevel <= 0) return 1.0f;
        float raw = (float)ul.Level._Value / (float)maxLevel;
        if (raw < 0f) raw = 0f;
        if (raw > 1f) raw = 1f;
        return raw;
    }

    /// <summary>
    /// CuratedScales path — the TX6 (v0.15.0) behavior, factored out so the
    /// mode-dispatcher in AttachStatScales is easy to read.
    /// </summary>
    static int AttachCuratedScales(Entity buffEntity, int unitPrefabGuid)
    {
        if (!Core.AbilityRules.HasTransformScales(unitPrefabGuid)) return 0;

        var statBuffer = GetOrAddStatBuffer(buffEntity);
        int added = 0;
        float damageScale = Core.AbilityRules.GetTransformDamageScale(unitPrefabGuid);
        float cooldownScale = Core.AbilityRules.GetTransformCooldownScale(unitPrefabGuid);
        float healthScale = Core.AbilityRules.GetTransformHealthScale(unitPrefabGuid);
        float movementScale = Core.AbilityRules.GetTransformMovementSpeedScale(unitPrefabGuid);

        if (System.Math.Abs(damageScale - 1f) > 0.0001f)
        {
            float delta = damageScale - 1f;
            AddStat(statBuffer, UnitStatType.PhysicalPower, delta, ModificationType.MultiplyBaseAdd);
            AddStat(statBuffer, UnitStatType.SpellPower, delta, ModificationType.MultiplyBaseAdd);
            added += 2;
        }
        if (System.Math.Abs(healthScale - 1f) > 0.0001f)
        {
            AddStat(statBuffer, UnitStatType.MaxHealth, healthScale - 1f, ModificationType.MultiplyBaseAdd);
            added++;
        }
        if (System.Math.Abs(movementScale - 1f) > 0.0001f)
        {
            AddStat(statBuffer, UnitStatType.MovementSpeed, movementScale - 1f, ModificationType.MultiplyBaseAdd);
            added++;
        }
        if (System.Math.Abs(cooldownScale - 1f) > 0.0001f)
        {
            // CooldownScale 1.5 (admin nerf) → recovery delta = 1/1.5 - 1 = -0.333
            // CooldownScale 0.5 (admin buff) → recovery delta = 1/0.5 - 1 = +1.000
            float recoveryDelta = (1f / cooldownScale) - 1f;
            AddStat(statBuffer, UnitStatType.SpellCooldownRecoveryRate, recoveryDelta, ModificationType.MultiplyBaseAdd);
            AddStat(statBuffer, UnitStatType.WeaponCooldownRecoveryRate, recoveryDelta, ModificationType.MultiplyBaseAdd);
            added += 2;
        }
        return added;
    }

    /// <summary>
    /// PrefabAbsolute (TX7) / PlayerLeveled (v0.19.0) — read the CHAR_ prefab's
    /// UnitStats component and apply matching boss-tier stats to the player,
    /// pre-multiplied by `scaleFactor`. `scaleFactor = 1.0` is the
    /// PrefabAbsolute default; PlayerLeveled passes
    /// `clamp(player_level / max_level, 0, 1)`. We use ModificationType.Add
    /// (additive delta) because the prefab values are absolute, not multipliers
    /// — we want the player's effective stat to approximate `player_natural +
    /// (boss × factor)`.
    /// </summary>
    static int AttachPrefabAbsolute(Entity buffEntity, int unitPrefabGuid, float scaleFactor)
    {
        var pg = new PrefabGUID(unitPrefabGuid);
        if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(pg, out Entity prefabEntity)) return 0;
        if (!prefabEntity.TryGetComponent<UnitStats>(out var bossStats)) return 0;

        var statBuffer = GetOrAddStatBuffer(buffEntity);

        // Only stats that are reliably exposed on the V Rising UnitStats wrapper
        // are read directly. Crit chances / crit damage are not exposed by
        // straightforward field names — admins who want crit-tier scaling on a
        // specific transform should layer CuratedScales-mode adjustments on top.
        int added = 0;
        AddStat(statBuffer, UnitStatType.PhysicalPower, bossStats.PhysicalPower._Value * scaleFactor, ModificationType.Add); added++;
        AddStat(statBuffer, UnitStatType.SpellPower, bossStats.SpellPower._Value * scaleFactor, ModificationType.Add); added++;
        AddStat(statBuffer, UnitStatType.PhysicalResistance, bossStats.PhysicalResistance._Value * scaleFactor, ModificationType.Add); added++;
        AddStat(statBuffer, UnitStatType.SpellResistance, bossStats.SpellResistance._Value * scaleFactor, ModificationType.Add); added++;

        // Health from the boss's Health component when available.
        if (prefabEntity.TryGetComponent<Health>(out var bossHealth))
        {
            AddStat(statBuffer, UnitStatType.MaxHealth, bossHealth.MaxHealth._Value * scaleFactor, ModificationType.Add); added++;
        }
        return added;
    }

    static DynamicBuffer<ModifyUnitStatBuff_DOTS> GetOrAddStatBuffer(Entity buffEntity)
    {
        if (Core.EntityManager.HasBuffer<ModifyUnitStatBuff_DOTS>(buffEntity))
        {
            return Core.EntityManager.GetBuffer<ModifyUnitStatBuff_DOTS>(buffEntity);
        }
        return Core.EntityManager.AddBuffer<ModifyUnitStatBuff_DOTS>(buffEntity);
    }

    /// <summary>
    /// TX8 (v0.17.0): block weapon swap while the carrier buff is active.
    /// V Rising's native BlockEquipmentSwapping component is respected by the
    /// engine; adding it to the buff entity prevents the wielding player from
    /// changing weapons until the buff is destroyed. Auto-clears on revert.
    /// </summary>
    static void AttachWeaponSwapLock(Entity buffEntity)
    {
        try
        {
            if (!Core.EntityManager.HasComponent<BlockEquipmentSwapping>(buffEntity))
            {
                Core.EntityManager.AddComponent<BlockEquipmentSwapping>(buffEntity);
            }
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] TransformBuffService: applied BlockEquipmentSwapping to carrier buff.");
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz] TransformBuffService.AttachWeaponSwapLock failed (BlockEquipmentSwapping component unavailable?): {ex}");
        }
    }

    static void AddStat(DynamicBuffer<ModifyUnitStatBuff_DOTS> buffer, UnitStatType stat, float value, ModificationType mod)
    {
        buffer.Add(new ModifyUnitStatBuff_DOTS
        {
            StatType = stat,
            ModificationType = mod,
            Value = value,
            Modifier = 1,
            IncreaseByStacks = false,
            ValueByStacks = 0,
            Priority = 0,
            Id = ModificationIDs.Create().NewModificationId(),
        });
    }
}
