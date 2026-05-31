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
            RemoveInternal(character); // clear any prior carrier/form buff (also clears stale pending form)

            var formBuff = new PrefabGUID(formBuffGuid);

            // v0.43.1 CRASH FIX: some form/shapeshift buff prefabs do NOT bake a
            // LifeTime component — Morgana's AB_Blackfang_Morgana_Transformation_
            // SnakePhaseBuff (-1859425781) is one. V Rising's immediate buff-spawn
            // path (TryInstantiateBuffEntityImmediate → BuffUtility.SpawnBuff) ASSERTS
            // the buff entity already has a LifeTime and throws
            //   "A component with type:ProjectM.LifeTime has not been added to the entity"
            // for those prefabs. That aborted the Morgana transform and, with the boss's
            // half-applied form context, cascaded into a Burst-job server crash on the
            // next chained ability cast. There is no instantiate overload that skips the
            // assertion (confirmed against the reference assemblies — neither
            // Instantiate nor TryInstantiate takes a duration to suppress it).
            //
            // Fix mirrors Bloodcraft's shapeshift recipe (Shapeshifts.ModifyShapeshiftBuff):
            // for a LifeTime-less prefab, apply the buff via the ASYNC
            // DebugEventsSystem.ApplyBuff path (which does NOT write a duration during
            // spawn → no LifeTime assertion) and ENRICH it on the spawn hook, where we
            // AddComponent<LifeTime> after the entity exists. Prefabs that already carry
            // a LifeTime (Dracula's SpellPhase, the carrier buff) keep the proven
            // same-frame immediate path untouched.
            bool prefabHasLifeTime =
                Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(formBuff, out var prefabEntity)
                && prefabEntity.Has<LifeTime>();

            if (prefabHasLifeTime)
            {
                if (!Core.ServerGameManager.TryInstantiateBuffEntityImmediate(character, character, formBuff, out Entity buffEntity)
                    || !buffEntity.Exists())
                {
                    Core.Log.LogError($"[Beelz] ApplyForm: TryInstantiateBuffEntityImmediate failed for form buff {formBuffGuid}.");
                    return false;
                }

                EnrichFormBuff(buffEntity, abilities, durationSeconds);

                if (Core.ReplaceAbilityOnSlotSystem != null)
                    Core.ReplaceAbilityOnSlotSystem.OnUpdate();

                Core.Log.LogInfo($"[Beelz] ApplyForm: applied form buff {formBuff.GetPrefabName()} + {abilities.Count} abilities to {character} (immediate; duration={DurationLabel(durationSeconds)}).");
                return true;
            }

            // Async path for LifeTime-less form prefabs. Queue the buff and record a
            // pending enrichment; the spawn hooks (BuffSpawnServerPatch /
            // ScriptSpawnServerPatch) call TryEnrichSpawnedForm / TryEnrichPendingByPoll
            // once the buff entity exists, where we add LifeTime + slot the abilities.
            ulong steamId = character.GetSteamId();
            if (steamId == 0)
            {
                Core.Log.LogError($"[Beelz] ApplyForm: could not resolve steamId for {character}; LifeTime-less form {formBuffGuid} not applied.");
                return false;
            }

            var abilityCopy = new int[abilities.Count];
            for (int i = 0; i < abilityCopy.Length; i++) abilityCopy[i] = abilities[i];
            _pendingForms[steamId] = new PendingForm { FormBuffGuid = formBuffGuid, Abilities = abilityCopy, Duration = durationSeconds };

            var applyEvent = new ApplyBuffDebugEvent { BuffPrefabGUID = formBuff, Who = GetNetworkId(character) };
            var fromCharacter = new FromCharacter { Character = character, User = GetUserEntity(character) };
            Core.DebugEventsSystem.ApplyBuff(fromCharacter, applyEvent);

            Core.Log.LogInfo($"[Beelz] ApplyForm: queued LifeTime-less form buff {formBuff.GetPrefabName()} (async) for player {steamId}; will enrich + slot {abilities.Count} abilities on spawn (duration={DurationLabel(durationSeconds)}).");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] ApplyForm failed: {ex}");
            return false;
        }
    }

    static string DurationLabel(float? durationSeconds) =>
        durationSeconds.HasValue ? durationSeconds.Value.ToString("F0") + "s" : "toggle";

    /// <summary>
    /// Shapeshift enrichment shared by the immediate path (Dracula et al.) and the
    /// async spawn-hook path (Morgana et al.): neuter the form's chained phase-advance
    /// buff, add the slot/shapeshift script tokens, set the category + buff type +
    /// LifeTime, and slot the curated abilities on the form's 0-7 bar. Mirrors
    /// Bloodcraft's <c>Shapeshifts.ModifyShapeshiftBuff</c>. Safe to run on an already
    /// fully-spawned buff entity (uses AddComponent for anything the prefab lacks).
    /// </summary>
    static void EnrichFormBuff(Entity buffEntity, IReadOnlyList<int> abilities, float? durationSeconds)
    {
        // Neuter the form's own chained "advance to next phase" buff so applying
        // it to a player doesn't kick off the boss's scripted phase sequence.
        if (Core.EntityManager.HasBuffer<ApplyBuffOnGameplayEvent>(buffEntity))
        {
            var ab = Core.EntityManager.GetBuffer<ApplyBuffOnGameplayEvent>(buffEntity);
            if (ab.Length > 0) { var e0 = ab[0]; e0.Buff0 = PrefabGUID.Empty; ab[0] = e0; }
        }

        // The two script/data tags make slot replacement integrate with the
        // shapeshift; categorize as a shapeshift that auto-clears on disconnect;
        // Block buff type.
        if (!Core.EntityManager.HasComponent<ReplaceAbilityOnSlotData>(buffEntity))
            Core.EntityManager.AddComponent<ReplaceAbilityOnSlotData>(buffEntity);
        if (!Core.EntityManager.HasComponent<Script_Buff_Shapeshift_DataShared>(buffEntity))
            Core.EntityManager.AddComponent<Script_Buff_Shapeshift_DataShared>(buffEntity);
        // v0.43.2 FIX (frog/native-form "kept dropping"): the native shapeshift buffs
        // (Wolf/Bear/Toad/…) bake DestroyOnAbilityEnd + RemoveOnDamageTaken — so the form
        // self-exits the instant the player casts or takes a hit. That destroy fires the
        // native-shapeshift-exit handler, which re-applies → re-adds the buff → flaps, and
        // the player never visually stays in form. Clear both so OUR transform form persists
        // until revert (boss forms like Dracula/Morgana don't set these, so this is a no-op
        // for them). Mirrors how a persistent ExoForm must behave.
        buffEntity.With((ref Script_Buff_Shapeshift_DataShared s) =>
        {
            s.DestroyOnAbilityEnd = false;
            s.RemoveOnDamageTaken = false;
        });
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
    }

    // ---------------------------------------------------------------------
    // v0.43.1: pending async form enrichment. A LifeTime-less form buff
    // (e.g. Morgana's SnakePhase) is applied via DebugEventsSystem.ApplyBuff
    // and enriched once it spawns. The registry bridges the apply (a command
    // tick) and the enrich (a later spawn-hook tick). Keyed by steamId so it
    // survives the player-entity churn that can happen across ticks.
    // ---------------------------------------------------------------------
    internal struct PendingForm { public int FormBuffGuid; public int[] Abilities; public float? Duration; }
    static readonly Dictionary<ulong, PendingForm> _pendingForms = new();

    /// <summary>True if any player has an async form buff awaiting spawn-hook enrichment.</summary>
    public static bool HasPendingForms => _pendingForms.Count > 0;

    /// <summary>
    /// v0.49.0: true if THIS player has an async form buff still mid-spawn (queued but not yet
    /// enriched). The transform activation path uses it to refuse a re-activation while a form
    /// is in flight — otherwise the Revert→ApplyForm sequence destroys the not-yet-collected
    /// form buff twice (deferred <c>DestroyTag</c>) and crashes the server inside Burst.
    /// </summary>
    public static bool HasPendingForm(ulong steamId) => steamId != 0 && _pendingForms.ContainsKey(steamId);

    /// <summary>
    /// Spawn-hook entry: if <paramref name="buffEntity"/> is <paramref name="targetPlayer"/>'s
    /// pending async form buff, enrich it now and clear the pending entry. Idempotent —
    /// the registry removal means only the first matching spawn wins. Returns true if enriched.
    /// </summary>
    public static bool TryEnrichSpawnedForm(Entity buffEntity, Entity targetPlayer)
    {
        if (_pendingForms.Count == 0) return false;
        if (!buffEntity.Exists() || !targetPlayer.Exists()) return false;
        ulong steamId = targetPlayer.GetSteamId();
        if (steamId == 0 || !_pendingForms.TryGetValue(steamId, out var pending)) return false;
        if (buffEntity.GetPrefabGuid()._Value != pending.FormBuffGuid) return false;

        try
        {
            EnrichFormBuff(buffEntity, pending.Abilities, pending.Duration);
            _pendingForms.Remove(steamId);
            if (Core.ReplaceAbilityOnSlotSystem != null)
                Core.ReplaceAbilityOnSlotSystem.OnUpdate();
            Core.Log.LogInfo($"[Beelz] ApplyForm: enriched async form buff {new PrefabGUID(pending.FormBuffGuid).GetPrefabName()} on spawn for player {steamId} ({pending.Abilities.Length} abilities).");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] TryEnrichSpawnedForm failed for player {steamId}: {ex}");
            _pendingForms.Remove(steamId); // don't retry a broken entry forever
            return false;
        }
    }

    /// <summary>
    /// Fallback enricher: poll each pending player for their form buff via TryGetBuff
    /// and enrich if it has spawned. Hook-agnostic — covers the case where the buff
    /// isn't in the spawn-query a given hook iterates. Cheap: no-ops unless something
    /// is pending. Call from a frequently-firing hook.
    /// </summary>
    public static void TryEnrichPendingByPoll()
    {
        if (_pendingForms.Count == 0) return;
        var keys = new List<ulong>(_pendingForms.Keys);
        foreach (var steamId in keys)
        {
            if (!_pendingForms.TryGetValue(steamId, out var pending)) continue;
            Entity player = EntityExtensions.FindCharacterBySteamId(steamId);
            if (!player.Exists()) continue;
            if (Core.ServerGameManager.TryGetBuff(player, new PrefabGUID(pending.FormBuffGuid).ToIdentifier(), out Entity buffEntity)
                && buffEntity.Exists())
            {
                TryEnrichSpawnedForm(buffEntity, player);
            }
        }
    }

    /// <summary>Drop a player's pending async form (e.g. on revert before the buff spawned).</summary>
    public static void ClearPendingForm(ulong steamId) => _pendingForms.Remove(steamId);

    // v0.43.2: revert-orphan guard. A revert clears the pending entry, but a
    // DebugEventsSystem.ApplyBuff queued just before the revert can still spawn the
    // form buff a tick LATER — stranding the player in an untracked form they can't
    // revert (the "reverted but bar didn't change / stuck as toad" bug). Revert marks
    // the player here; the spawn hook destroys any of OUR form buffs that land while the
    // mark is fresh AND the player has no active transform. Time-boxed so a legit later
    // shapeshift (normal wolf form, a fresh re-transform) is never touched.
    static readonly Dictionary<ulong, DateTime> _recentlyReverted = new();
    const double RevertGuardSeconds = 3.0;

    /// <summary>Revert calls this so a late async form-buff spawn can be cleaned up.</summary>
    public static void MarkReverted(ulong steamId) { if (steamId != 0) _recentlyReverted[steamId] = DateTime.UtcNow; }

    /// <summary>True if any player reverted recently enough to still need orphan-form cleanup.</summary>
    public static bool HasRecentReverts
    {
        get
        {
            if (_recentlyReverted.Count == 0) return false;
            var now = DateTime.UtcNow;
            foreach (var t in _recentlyReverted.Values) if ((now - t).TotalSeconds < RevertGuardSeconds) return true;
            return false;
        }
    }

    /// <summary>
    /// Spawn-hook entry: if a form buff lands on a player who just reverted (so it's an
    /// orphan from an in-flight async apply) and they're no longer transformed, destroy it.
    /// Returns true if it destroyed an orphan. Idempotent / safe.
    /// </summary>
    public static bool TryDestroyOrphanForm(Entity buffEntity, Entity targetPlayer)
    {
        if (_recentlyReverted.Count == 0) return false;
        if (!buffEntity.Exists() || !targetPlayer.Exists()) return false;
        ulong steamId = targetPlayer.GetSteamId();
        if (steamId == 0) return false;
        if (!_recentlyReverted.TryGetValue(steamId, out var t) || (DateTime.UtcNow - t).TotalSeconds >= RevertGuardSeconds)
            return false;
        // Only OUR form buffs, and only if the player genuinely has no active transform
        // (a fresh re-transform during the guard window sets one → leave that alone).
        int guid = buffEntity.GetPrefabGuid()._Value;
        bool isOurForm = false;
        foreach (int g in Services.BossFormRegistry.FormBuffGuids) { if (g == guid) { isOurForm = true; break; } }
        if (!isOurForm) return false;
        if (Core.AbilityRegistry?.GetActiveTransform(steamId) is not null) return false;
        if (_pendingForms.ContainsKey(steamId)) return false; // a new transform is mid-apply

        try
        {
            if (!SafeDestroyBuff(buffEntity)) return false; // already queued for destruction
            Core.Log.LogInfo($"[Beelz] ApplyForm: destroyed orphan form buff {new PrefabGUID(guid).GetPrefabName()} that spawned after revert for player {steamId}.");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz] TryDestroyOrphanForm failed for player {steamId}: {ex.Message}");
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
    /// v0.99.1: swap a form's ability set IN PLACE on the already-active form buff — for `.beelz phase`
    /// when the new phase wears the SAME model (form buff GUID unchanged). The native shapeshift forms
    /// (Werewolf/Golem/Gargoyle) have NO LifeTime, so <see cref="ApplyForm"/> takes the async destroy+
    /// re-spawn path, which races on a mid-transform phase switch — the buff the player is wearing isn't
    /// reaped before the re-apply, so phase 2 silently never landed. Editing the existing form buff's
    /// ReplaceAbilityOnSlotBuff buffer + re-running the slot system avoids the respawn entirely (the same
    /// in-place technique <see cref="ShapeshiftAbilityService.ApplyFormLoadout"/> uses, which renders fine).
    /// Returns false if the form buff isn't currently on the player (caller then does a full ApplyForm).
    /// </summary>
    public static bool ReapplyFormAbilitiesInPlace(Entity character, int formBuffGuid, IReadOnlyList<int> abilities)
    {
        if (!character.Exists() || formBuffGuid == 0 || abilities == null || abilities.Count == 0) return false;
        if (!Core.ServerGameManager.TryGetBuff(character, new PrefabGUID(formBuffGuid).ToIdentifier(), out Entity buffEntity)
            || !buffEntity.Exists())
            return false;   // not currently in this form → let the caller apply it fresh
        try
        {
            if (!Core.EntityManager.HasComponent<ReplaceAbilityOnSlotData>(buffEntity))
                Core.EntityManager.AddComponent<ReplaceAbilityOnSlotData>(buffEntity);
            DynamicBuffer<ReplaceAbilityOnSlotBuff> buffer = Core.EntityManager.HasBuffer<ReplaceAbilityOnSlotBuff>(buffEntity)
                ? Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(buffEntity)
                : Core.EntityManager.AddBuffer<ReplaceAbilityOnSlotBuff>(buffEntity);
            buffer.Clear();
            for (int i = 0; i < abilities.Count && i < 8; i++)
            {
                buffer.Add(new ReplaceAbilityOnSlotBuff
                {
                    Target = ReplaceAbilityTarget.BuffTarget,
                    Slot = i,
                    NewGroupId = new PrefabGUID(abilities[i]),
                    Priority = 99,
                    CopyCooldown = true,
                    CastBlockType = GroupSlotModificationCastBlockType.WholeCast,
                });
            }
            if (Core.ReplaceAbilityOnSlotSystem != null) Core.ReplaceAbilityOnSlotSystem.OnUpdate();
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] phase swap in-place on {new PrefabGUID(formBuffGuid).GetPrefabName()} → {abilities.Count} abilities (no respawn).");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] ReapplyFormAbilitiesInPlace failed: {ex}");
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

    /// <summary>
    /// v0.49.0 CRASH FIX: <see cref="DestroyUtility.Destroy"/> is DEFERRED — it stamps a
    /// <c>DestroyTag</c> and the entity is reaped by a later cleanup pass, so TryGetBuff /
    /// BuffBuffer can still surface a buff that is already queued for destruction. Destroying
    /// it a SECOND time double-applies the component-record removal and crashes the server
    /// inside a Burst job (<c>AppendRemovedComponentRecordError</c>) — the Morgana-transform
    /// crash. This guard skips any entity already tagged, making every teardown path here
    /// idempotent across the deferred-destroy window. Returns true only if it issued a destroy.
    /// </summary>
    static bool SafeDestroyBuff(Entity buffEntity)
    {
        if (!buffEntity.Exists()) return false;
        if (buffEntity.Has<DestroyTag>()) return false; // already queued for destruction — never double-destroy
        DestroyUtility.Destroy(Core.EntityManager, buffEntity, DestroyDebugReason.TryRemoveBuff);
        return true;
    }

    static bool RemoveInternal(Entity character)
    {
        bool removed = false;

        // v0.43.1: drop any pending async form enrichment for this player so a revert
        // (or a re-apply) can't enrich a later-spawned form buff against a stale entry.
        ulong sid = character.GetSteamId();
        if (sid != 0) _pendingForms.Remove(sid);

        // Default ability-only carrier buff.
        if (Core.ServerGameManager.TryGetBuff(character, CarrierBuff.ToIdentifier(), out Entity buffEntity)
            && SafeDestroyBuff(buffEntity))
        {
            removed = true;
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] TransformBuffService.Remove: destroyed carrier buff {buffEntity}.");
        }

        // v0.31.0: ExoForm-style form buffs (Dracula/Morgana/…). Destroy whichever
        // is active so the player drops the shapeshift on revert.
        foreach (int formGuid in Services.BossFormRegistry.FormBuffGuids)
        {
            if (Core.ServerGameManager.TryGetBuff(character, new PrefabGUID(formGuid).ToIdentifier(), out Entity formBuff)
                && SafeDestroyBuff(formBuff))
            {
                removed = true;
                if (Beelzebub.Config.Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz] TransformBuffService.Remove: destroyed form buff {formBuff} ({new PrefabGUID(formGuid).GetPrefabName()}).");
            }
        }

        return removed;
    }

    /// <summary>
    /// v0.43.9: hardened teardown for `.beelz resetbar`. Unlike <see cref="Remove"/>
    /// (which looks each buff up by identifier via TryGetBuff — and can MISS a buff that
    /// is actually present), this walks the character's live <see cref="BuffBuffer"/>
    /// directly and destroys: our carrier buff, every registered boss/native form buff,
    /// and ANY buff whose prefab name contains "Shapeshift" or "_Transformation_" (a stuck
    /// vanilla shapeshift / boss-phase form that left the player wearing a creature bar).
    /// The player's EquipBuff_Weapon is never touched. Returns the number destroyed.
    /// </summary>
    public static int RemoveAllFormsAndShapeshifts(Entity character)
    {
        if (!character.Exists() || !Core.EntityManager.HasBuffer<BuffBuffer>(character)) return 0;

        // Drop any pending async form so a late spawn can't re-enrich after this.
        ulong sid = character.GetSteamId();
        if (sid != 0) _pendingForms.Remove(sid);

        var toDestroy = new List<Entity>();
        var buffs = Core.EntityManager.GetBuffer<BuffBuffer>(character);
        for (int i = 0; i < buffs.Length; i++)
        {
            Entity be = buffs[i].Entity;
            if (!be.Exists()) continue;
            int guid = buffs[i].PrefabGuid._Value;
            string name = buffs[i].PrefabGuid.GetPrefabName() ?? "";

            if (name.StartsWith("EquipBuff", StringComparison.OrdinalIgnoreCase)) continue; // never touch the weapon equip buff

            bool ours = guid == CarrierBuff._Value;
            if (!ours)
                foreach (int g in Services.BossFormRegistry.FormBuffGuids) { if (g == guid) { ours = true; break; } }

            bool shapeshifty = name.IndexOf("Shapeshift", StringComparison.OrdinalIgnoreCase) >= 0
                            || name.IndexOf("_Transformation_", StringComparison.OrdinalIgnoreCase) >= 0;

            if (ours || shapeshifty) toDestroy.Add(be);
        }

        int destroyed = 0;
        foreach (Entity be in toDestroy)
        {
            if (!be.Exists()) continue;
            try
            {
                if (SafeDestroyBuff(be))
                {
                    Core.Log.LogInfo($"[Beelz] resetbar: destroyed buff {be.GetPrefabGuid().GetPrefabName()} (#{be.GetPrefabGuid()._Value}).");
                    destroyed++;
                }
            }
            catch (Exception ex)
            {
                Core.Log.LogWarning($"[Beelz] resetbar: failed destroying a buff: {ex.Message}");
            }
        }

        if (destroyed > 0 && Core.ReplaceAbilityOnSlotSystem != null)
            Core.ReplaceAbilityOnSlotSystem.OnUpdate();
        return destroyed;
    }

    /// <summary>
    /// v0.43.12: the deep fix for a FROZEN ability bar. V Rising's ReplaceAbilityOnSlotSystem
    /// resolves a player's bar from EVERY entity that is owned by them (EntityOwner.Owner ==
    /// character) and carries a ReplaceAbilityOnSlotBuff — NOT just buffs in their BuffBuffer.
    /// A transform's carrier/form buff that got unlinked from the BuffBuffer (e.g. logout mid-
    /// form) but still exists and is still owned by the player keeps injecting its abilities at
    /// Priority 99, pinning the bar and blocking spellbook/weapon resolution — and it's invisible
    /// to BuffBuffer-based cleanup. This walks ALL such owned sources and destroys the ones that
    /// aren't legitimate gear/jewel sources (EquipBuff_* / Item_*), then forces a re-resolve.
    /// Returns the number destroyed. Logs each (also used by the diagnostic, read-only variant).
    /// </summary>
    public static int DestroyOwnedAbilitySlotOrphans(Entity character)
    {
        if (!character.Exists()) return 0;
        int destroyed = 0;
        try
        {
            EntityQuery q = Core.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ReplaceAbilityOnSlotBuff>(),
                ComponentType.ReadOnly<EntityOwner>());
            var arr = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            var toDestroy = new List<Entity>();
            for (int i = 0; i < arr.Length; i++)
            {
                Entity e = arr[i];
                if (!e.Exists()) continue;
                if (!e.TryGetComponent<EntityOwner>(out var owner) || owner.Owner != character) continue;
                string name = e.GetPrefabGuid().GetPrefabName() ?? "";
                // Keep the legitimate vanilla bar sources: equipped gear + magic-source jewels.
                if (name.StartsWith("EquipBuff", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.StartsWith("Item_", StringComparison.OrdinalIgnoreCase)) continue;
                toDestroy.Add(e);
            }
            arr.Dispose();

            foreach (Entity e in toDestroy)
            {
                if (!e.Exists()) continue;
                try
                {
                    if (SafeDestroyBuff(e))
                    {
                        Core.Log.LogInfo($"[Beelz] resetbar: destroyed owned ability-slot source {e.GetPrefabGuid().GetPrefabName()} (#{e.GetPrefabGuid()._Value}).");
                        destroyed++;
                    }
                }
                catch (Exception ex) { Core.Log.LogWarning($"[Beelz] orphan-source destroy failed: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz] DestroyOwnedAbilitySlotOrphans failed: {ex.Message}");
        }

        if (destroyed > 0 && Core.ReplaceAbilityOnSlotSystem != null)
            Core.ReplaceAbilityOnSlotSystem.OnUpdate();
        return destroyed;
    }

    /// <summary>
    /// v0.43.13: authoritatively reset the player's resolved ability slots via
    /// <c>ServerGameManager.ModifyAbilityGroupOnSlot</c> (the engine's own slot setter, as used
    /// by Bloodcraft). This is the cure for a FROZEN bar: a transform applies abilities at
    /// Priority 99; if the bar's resolved/cached value keeps that priority after the source is
    /// gone, low-priority vanilla sources (weapon/spellbook, Priority 0) can't overwrite it and
    /// the bar is stuck (only another Priority-99 transform changes it). Modify clears each slot
    /// authoritatively and networks the change to the client, forcing a clean re-resolve from the
    /// current equipment + spellbook. Returns the number of slots cleared.
    /// </summary>
    public static int ForceResetAbilitySlots(Entity character)
    {
        if (!character.Exists() || !Core.EntityManager.HasBuffer<BuffBuffer>(character)) return 0;

        // ModifyAbilityGroupOnSlot needs a modification-source buff; the equipped-weapon buff is
        // always present (even Unarmed) and is the natural owner of the player's weapon slots.
        Entity equipBuff = Entity.Null;
        var buffs = Core.EntityManager.GetBuffer<BuffBuffer>(character);
        for (int i = 0; i < buffs.Length; i++)
        {
            string n = buffs[i].PrefabGuid.GetPrefabName();
            if (n != null && n.StartsWith("EquipBuff_Weapon", StringComparison.OrdinalIgnoreCase))
            {
                equipBuff = buffs[i].Entity;
                break;
            }
        }
        if (!equipBuff.Exists())
        {
            Core.Log.LogWarning("[Beelz] ForceResetAbilitySlots: no EquipBuff_Weapon found; cannot reset slots.");
            return 0;
        }

        int cleared = 0;
        var sgm = Core.ServerGameManager;
        for (int slot = 0; slot <= 8; slot++)
        {
            try { sgm.ModifyAbilityGroupOnSlot(equipBuff, character, slot, PrefabGUID.Empty); cleared++; }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz] ForceResetAbilitySlots slot {slot} failed: {ex.Message}"); }
        }

        if (Core.ReplaceAbilityOnSlotSystem != null) Core.ReplaceAbilityOnSlotSystem.OnUpdate();
        Core.Log.LogInfo($"[Beelz] resetbar: force-cleared {cleared} ability slot(s) via ModifyAbilityGroupOnSlot (authoritative client push).");
        return cleared;
    }

    /// <summary>
    /// v0.43.14: force V Rising to REBUILD the player's ability bar from scratch. The engine
    /// builds the resolved bar once, gated by the enableable <c>AbilityBarInitializationState</c>
    /// tag (ProjectM). A shapeshift triggers a rebuild for the form; if the player left the form
    /// abnormally (logout mid-form), the resolved bar can stay stuck on the creature kit and never
    /// re-derive from the vampire's equipment — a "frozen bar" that ModifyAbilityGroupOnSlot and
    /// buff/source cleanup can't fix because the inputs are already correct, only the cached
    /// resolution is stale. ENABLING the init-state tag makes the ability-bar init system re-run
    /// and rebuild from the current equipment + spells. Returns true if the tag was toggled.
    /// </summary>
    public static bool ForceAbilityBarReinit(Entity character)
    {
        if (!character.Exists()) return false;
        try
        {
            var ctInit = Unity.Entities.ComponentType.ReadWrite(
                Il2CppInterop.Runtime.Il2CppType.Of<AbilityBarInitializationState>());
            bool hasInit = Core.EntityManager.HasComponent(character, ctInit);
            bool hasServerBar = Core.EntityManager.HasComponent(character,
                Unity.Entities.ComponentType.ReadOnly(Il2CppInterop.Runtime.Il2CppType.Of<AbilityBar_Server>()));
            Core.Log.LogInfo($"[Beelz] rebuildbar: hasInitState={hasInit} hasServerBar={hasServerBar}.");

            // NOTE (v0.43.15): AbilityBarInitializationState is NOT an IEnableableComponent
            // (confirmed at runtime — SetComponentEnabled throws), so it can't be toggled to force
            // a rebuild. Diagnostic only. The working rebuild is `.beelz admin respawn`
            // (ServerBootstrapSystem.RespawnCharacter), which constructs a FRESH character entity
            // with clean AbilityGroupSlot entities, discarding the stuck modifications.
            return false;
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz] ForceAbilityBarReinit failed: {ex.Message}");
            return false;
        }
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
