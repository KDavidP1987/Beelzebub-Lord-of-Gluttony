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
            // v0.137.0 (bar-reset D33): the carrier prefab is also the spellbook's spell buff — read the spellbook first so
            // an unreadable one aborts before anything is created (a carrier made then would be unremovable).
            var spellbookBefore = SpellbookBuffIds(character);
            if (spellbookBefore == null)
            {
                Core.Log.LogError("[Beelz SPELLBUF] Apply: the spellbook cannot be read; transform aborted before creating a carrier.");
                return false;
            }
            Entity buffEntity;
            if (!Core.ServerGameManager.TryInstantiateBuffEntityImmediate(character, character, CarrierBuff, out buffEntity)
                || !buffEntity.Exists())
            {
                Core.Log.LogError("[Beelz] TransformBuffService.Apply: TryInstantiateBuffEntityImmediate failed for carrier buff; transform aborted.");
                return false;
            }
            // If the engine handed back a buff a spellbook entry already referenced, enriching it would overwrite the
            // player's spell — abort and leave it alone (it is theirs, not a carrier this call created).
            if (!IsOwnCarrier(buffEntity, spellbookBefore))
            {
                Core.Log.LogError($"[Beelz SPELLBUF] Apply: the new carrier {buffEntity} is a spellbook spell's buff; transform aborted.");
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
        LogIfSpellbookBuff(buffEntity);
        DestroyUtility.Destroy(Core.EntityManager, buffEntity, DestroyDebugReason.TryRemoveBuff);
        return true;
    }

    /// <summary>v0.137.3 (mounted-bar-reset D4): destroys every live mount control buff on <paramref name="character"/>
    /// (<see cref="ShapeshiftAbilityService.IsMountBuff"/>) through the double-destroy guard — the same call the orphan
    /// sweep used to make, now deliberate and counted. <paramref name="stillLive"/> counts mount buffs that are neither
    /// queued for destruction nor gone afterwards.</summary>
    internal static int DestroyMountBuffs(Entity character, out int stillLive)
    {
        int destroyed = 0;
        stillLive = 0;
        if (!character.Exists() || !Core.EntityManager.HasBuffer<BuffBuffer>(character)) return 0;
        var mounts = new List<Entity>();
        var buffs = Core.EntityManager.GetBuffer<BuffBuffer>(character);
        for (int i = 0; i < buffs.Length; i++)
            if (ShapeshiftAbilityService.IsMountBuff(buffs[i].PrefabGuid._Value, buffs[i].PrefabGuid.GetPrefabName()))
                mounts.Add(buffs[i].Entity);
        foreach (var m in mounts)
        {
            bool tagBefore = m.Exists() && m.Has<DestroyTag>();
            bool issued = SafeDestroyBuff(m);
            if (issued) destroyed++;
            bool exists = m.Exists(), tagAfter = exists && m.Has<DestroyTag>();
            if (exists && !tagAfter) stillLive++;
            // A1 diagnostic (mounted-bar-reset): which entity Dismount saw and what the destroy did to it.
            Entity target = exists && m.Has<Buff>() ? Core.EntityManager.GetComponentData<Buff>(m).Target : Entity.Null;
            Entity owner = exists && m.Has<EntityOwner>() ? Core.EntityManager.GetComponentData<EntityOwner>(m).Owner : Entity.Null;
            Core.Log.LogInfo($"[Beelz DISMOUNT] buff={m} prefab={(exists ? m.GetPrefabGuid().GetPrefabName() : "?")} target={target} owner={owner} "
                + $"character={character} tagBefore={tagBefore} issued={issued} existsAfter={exists} tagAfter={tagAfter} slotRows={(exists && m.Has<ReplaceAbilityOnSlotBuff>())}");
        }
        if (mounts.Count == 0) Core.Log.LogInfo($"[Beelz DISMOUNT] no mount buff in {character}'s BuffBuffer");
        return destroyed;
    }

    /// <summary>v0.137.0 diagnostic (bar-reset A-spellbuff): Buff_VBlood_Ability_Replace is BOTH our carrier and
    /// V Rising's own equipped-spell buff (VBloodAbilityBuffEntry.ActiveBuff). Read-only: logs when a buff about to be
    /// destroyed is one the character's spellbook references, so the log names whose buff a teardown removed.</summary>
    static void LogIfSpellbookBuff(Entity buffEntity)
    {
        try
        {
            if (!buffEntity.Has<Buff>()) return;
            Entity owner = Core.EntityManager.GetComponentData<Buff>(buffEntity).Target;
            if (!owner.Exists() || !Core.EntityManager.HasBuffer<VBloodAbilityBuffEntry>(owner)) return;
            var entries = Core.EntityManager.GetBuffer<VBloodAbilityBuffEntry>(owner);
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].ActiveBuff == buffEntity)
                    Core.Log.LogWarning($"[Beelz SPELLBUF] destroying spellbook buff {buffEntity} target={owner.GetSteamId()} slot={entries[i].SlotId} ability={entries[i].ActiveAbility.GetPrefabName()}");
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz SPELLBUF] check failed: {ex.Message}"); }
    }

    /// <summary>v0.137.0 (bar-reset D33): ids of every buff the character's spellbook (VBloodAbilityBuffEntry.ActiveBuff)
    /// references; empty when the character has no spellbook buffer, null when it cannot be read (then nothing of this
    /// prefab counts as ours).</summary>
    public static HashSet<long> SpellbookBuffIds(Entity character)
    {
        try
        {
            var ids = new HashSet<long>();
            if (!character.Exists() || !Core.EntityManager.HasBuffer<VBloodAbilityBuffEntry>(character)) return ids;
            var entries = Core.EntityManager.GetBuffer<VBloodAbilityBuffEntry>(character);
            for (int i = 0; i < entries.Length; i++)
            {
                Entity b = entries[i].ActiveBuff;
                if (b != Entity.Null) ids.Add(Beelzebub.Logic.SpellbookBuffs.Id(b.Index, b.Version));
            }
            return ids;
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz SPELLBUF] spellbook read failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>D33: slot → the ability the character's spellbook has equipped there; null when unreadable.</summary>
    public static Dictionary<int, int> SpellbookSpellsBySlot(Entity character)
    {
        try
        {
            var map = new Dictionary<int, int>();
            if (!character.Exists() || !Core.EntityManager.HasBuffer<VBloodAbilityBuffEntry>(character)) return map;
            var entries = Core.EntityManager.GetBuffer<VBloodAbilityBuffEntry>(character);
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].ActiveAbility._Value != 0) map[entries[i].SlotId] = entries[i].ActiveAbility._Value;
            return map;
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz SPELLBUF] spellbook read failed: {ex.Message}");
            return null;
        }
    }

    static bool IsOwnCarrier(Entity buff, HashSet<long> spellbookIds) =>
        Beelzebub.Logic.SpellbookBuffs.IsOwnCarrier(Beelzebub.Logic.SpellbookBuffs.Id(buff.Index, buff.Version), spellbookIds);

    /// <summary>Beelzebub's own carrier buffs on the character: Buff_VBlood_Ability_Replace instances no spellbook
    /// entry references (D33).</summary>
    static List<Entity> OwnCarriers(Entity character)
    {
        var result = new List<Entity>();
        if (!character.Exists() || !Core.EntityManager.HasBuffer<BuffBuffer>(character)) return result;
        var spellbookIds = SpellbookBuffIds(character);
        var buffs = Core.EntityManager.GetBuffer<BuffBuffer>(character);
        for (int i = 0; i < buffs.Length; i++)
            if (buffs[i].PrefabGuid._Value == CarrierBuff._Value && buffs[i].Entity.Exists() && IsOwnCarrier(buffs[i].Entity, spellbookIds))
                result.Add(buffs[i].Entity);
        return result;
    }

    /// <summary>v0.137.0 (bar-reset D33): heals spellbook entries whose buff is gone (destroyed by pre-0.137 teardown):
    /// the spellbook shows the spell, the bar slot is blank, and picking the same spell again is a no-op. Each dangling
    /// entry is removed and re-created with V Rising's own equip sequence (VBloodAbilityUtilities.InstantiateBuff, as
    /// Bloodcraft's spell equip); when that fails the entry stays removed, so the player can pick the spell again.
    /// Returns the number of entries handled.</summary>
    public static int RepairSpellbook(Entity character, string context)
    {
        if (!character.Exists() || !Core.EntityManager.HasBuffer<VBloodAbilityBuffEntry>(character)) return 0;
        var em = Core.EntityManager;
        var snapshot = new List<Beelzebub.Logic.SpellbookEntry>();
        try
        {
            var entries = em.GetBuffer<VBloodAbilityBuffEntry>(character);
            var raw = new List<(int Slot, int Ability, bool Live)>();
            for (int i = 0; i < entries.Length; i++)
                raw.Add((entries[i].SlotId, entries[i].ActiveAbility._Value, IsLive(entries[i].ActiveBuff)));
            for (int i = 0; i < raw.Count; i++)
                snapshot.Add(new Beelzebub.Logic.SpellbookEntry(i, raw[i].Slot, raw[i].Ability, raw[i].Live,
                    raw[i].Live && SpellModKnownMissing(character, raw[i].Slot, raw[i].Ability)));
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz SPELLBUF] repair: spellbook read failed ({context}): {ex.Message}"); return 0; }
        var dangling = Beelzebub.Logic.SpellbookBuffs.Dangling(snapshot);
        if (dangling.Count == 0) return 0;

        ulong sid = character.GetSteamId();
        ProjectM.Gameplay.Systems.ActivateVBloodAbilitySystem system = null;
        Entity buffPrefab = Entity.Null;
        try
        {
            system = Core.Server.GetExistingSystemManaged<ProjectM.Gameplay.Systems.ActivateVBloodAbilitySystem>();
            Core.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(CarrierBuff, out buffPrefab);
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz SPELLBUF] repair setup failed: {ex.Message}"); }

        foreach (var d in dangling)
        {
            var ability = new PrefabGUID(d.AbilityGuid);
            string abilityName = ability.GetPrefabName() ?? d.AbilityGuid.ToString();
            Entity oldBuff = Entity.Null;
            VBloodAbilityBuffEntry oldEntry = default;
            try
            {
                // Re-validate: the entry at the captured index must still be this slot's spell, in the captured state.
                var buf = em.GetBuffer<VBloodAbilityBuffEntry>(character);
                if (d.Index >= buf.Length || buf[d.Index].SlotId != d.SlotId || buf[d.Index].ActiveAbility._Value != d.AbilityGuid
                    || IsLive(buf[d.Index].ActiveBuff) != d.BuffLive)
                {
                    Core.Log.LogWarning($"[Beelz SPELLBUF] repair skipped slot={d.SlotId} ability={abilityName} ({context}): the entry changed");
                    continue;
                }
                oldEntry = buf[d.Index];
                oldBuff = oldEntry.ActiveBuff;
                buf.RemoveAt(d.Index);   // highest index first: no shift
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz SPELLBUF] repair skipped slot={d.SlotId} ({context}): {ex.Message}"); continue; }

            if (d.AbilityGuid != 0 && system != null && buffPrefab != Entity.Null)
            {
                try { VBloodAbilityUtilities.InstantiateBuff(em, system._BuffSpawnerSystemData, character, buffPrefab, ability, d.SlotId); }
                catch (Exception ex) { Core.Log.LogWarning($"[Beelz SPELLBUF] re-create threw slot={d.SlotId} ability={abilityName}: {ex.Message}"); }
            }
            // Verify by re-scan, whatever the call did: a valid new entry is configured; a partial one (entry without a
            // live buff) is removed so the slot really is free to pick again.
            bool repaired = false;
            try { repaired = FinishRepair(character, ability, d.SlotId); }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz SPELLBUF] repair check failed slot={d.SlotId}: {ex.Message}"); }
            // A live old buff (its slot had lost the spell's mod): once a live replacement exists it is unreferenced and is
            // destroyed, as vanilla's spell swap does (Bloodcraft Classes.cs: RemoveAt then Destroy); a failed re-create
            // puts the old entry back instead, so the spell is never lost to a repair (review round 3).
            bool restored = false;
            if (Beelzebub.Logic.SpellbookBuffs.DestroyOldBuff(d.BuffLive, repaired))
            {
                try { SafeDestroyBuff(oldBuff); }
                catch (Exception ex) { Core.Log.LogWarning($"[Beelz SPELLBUF] old buff destroy failed slot={d.SlotId}: {ex.Message}"); }
            }
            else if (Beelzebub.Logic.SpellbookBuffs.RestoreOldEntry(d.BuffLive, repaired) && IsLive(oldBuff))
            {
                try { em.GetBuffer<VBloodAbilityBuffEntry>(character).Add(oldEntry); restored = true; }
                catch (Exception ex) { Core.Log.LogWarning($"[Beelz SPELLBUF] old entry restore failed slot={d.SlotId}: {ex.Message}"); }
            }
            string why = d.BuffLive ? "slot lacked the spell's mod" : "buff missing";
            Core.Log.LogInfo(repaired
                ? $"[Beelz SPELLBUF] repaired target={sid} slot={d.SlotId} ability={abilityName} ({context}; {why})"
                : restored
                    ? $"[Beelz SPELLBUF] repair failed, kept target={sid} slot={d.SlotId} ability={abilityName} ({context}; {why}): the spell is unchanged"
                    : $"[Beelz SPELLBUF] removed target={sid} slot={d.SlotId} ability={abilityName} ({context}; {why}): the spellbook slot is free to pick again");
        }
        Core.ReplaceAbilityOnSlotSystem?.OnUpdate();
        return dangling.Count;
    }

    static bool IsLive(Entity buff) => buff.Exists() && !buff.Has<DestroyTag>();

    /// <summary>D33: true only when the slot's dump is READABLE and carries no non-gear mod setting it to the spell —
    /// an unreadable or missing slot is never "known missing" (no repair on a guess).</summary>
    static bool SpellModKnownMissing(Entity character, int slotId, int abilityGuid)
    {
        try
        {
            if (abilityGuid == 0) return false;
            foreach (var (idx, slot) in SnapshotSlots(character))
            {
                if (idx != slotId) continue;
                if (slot == Entity.Null || !slot.Exists()) return false;
                var parse = ParseSlot(Core.ServerGameManager.Modifications, Core.EntityManager, slot);
                var map = new Dictionary<int, int> { [slotId] = abilityGuid };
                bool hasSpellMod = parse.Entries.Any(e => Beelzebub.Logic.SpellbookBuffs.IsSpellbookMod(e, slotId, map, SourcePrefabName(e)));
                int active = 0;
                if (Core.EntityManager.HasComponent<AbilityGroupSlot>(slot))
                {
                    Entity st = Core.EntityManager.GetComponentData<AbilityGroupSlot>(slot).StateEntity._Entity;
                    if (st.Exists()) active = st.GetPrefabGuid()._Value;
                }
                return Beelzebub.Logic.SpellbookBuffs.ModKnownMissing(abilityGuid, parse.Readable, hasSpellMod, active);
            }
            return false;
        }
        catch { return false; }
    }

    /// <summary>After a re-create attempt: the slot's entry with a live buff gets the ability's VBloodAbilityData.AbilityType
    /// (Bloodcraft's equip sequence) → true; an entry left without a live buff is removed → false.</summary>
    static bool FinishRepair(Entity character, PrefabGUID ability, int slotId)
    {
        var em = Core.EntityManager;
        var entries = em.GetBuffer<VBloodAbilityBuffEntry>(character);
        for (int i = entries.Length - 1; i >= 0; i--)
        {
            if (entries[i].SlotId != slotId || entries[i].ActiveAbility._Value != ability._Value) continue;
            Entity b = entries[i].ActiveBuff;
            if (!IsLive(b)) { entries.RemoveAt(i); return false; }
            if (Core.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(ability, out Entity abilityPrefab)
                && em.HasComponent<VBloodAbilityData>(abilityPrefab) && em.HasComponent<VBloodAbilityReplaceBuff>(b))
            {
                var replace = em.GetComponentData<VBloodAbilityReplaceBuff>(b);
                replace.AbilityType = em.GetComponentData<VBloodAbilityData>(abilityPrefab).AbilityType;
                em.SetComponentData(b, replace);
            }
            return true;
        }
        return false;
    }

    /// <summary>v0.137.0 diagnostic: the character's equipped-spell entries — slot, ability, and whether the
    /// ActiveBuff behind it still exists. A dangling entry (buff gone) is a spell the spellbook shows but the bar lacks.</summary>
    public static List<string> SpellbookEntries(Entity character)
    {
        var lines = new List<string>();
        if (!character.Exists() || !Core.EntityManager.HasBuffer<VBloodAbilityBuffEntry>(character)) { lines.Add("no VBloodAbilityBuffEntry buffer"); return lines; }
        var entries = Core.EntityManager.GetBuffer<VBloodAbilityBuffEntry>(character);
        for (int i = 0; i < entries.Length; i++)
        {
            Entity b = entries[i].ActiveBuff;
            string state = !b.Exists() ? "MISSING" : b.Has<DestroyTag>() ? "destroying" : "ok";
            lines.Add($"entry={i} slot={entries[i].SlotId} ability={entries[i].ActiveAbility.GetPrefabName()} ({entries[i].ActiveAbility._Value}) buff={b} state={state}");
        }
        if (entries.Length == 0) lines.Add("buffer empty");
        return lines;
    }

    static bool RemoveInternal(Entity character)
    {
        bool removed = false;

        // v0.43.1: drop any pending async form enrichment for this player so a revert
        // (or a re-apply) can't enrich a later-spawned form buff against a stale entry.
        ulong sid = character.GetSteamId();
        if (sid != 0) _pendingForms.Remove(sid);

        // Default ability-only carrier buff. v0.137.0 (bar-reset D33): the carrier prefab is ALSO V Rising's own
        // equipped-spell buff, so TryGetBuff by prefab could return (and this destroyed) a spellbook spell — at every
        // login. Walk the BuffBuffer and destroy only the carriers no spellbook entry references.
        foreach (Entity buffEntity in OwnCarriers(character))
        {
            if (!SafeDestroyBuff(buffEntity)) continue;
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

    /// <summary>A buff that can drive the bar: our carrier, a boss-form buff, or any shapeshift/transformation buff —
    /// never the weapon equip buff. Shared by <see cref="RemoveAllFormsAndShapeshifts"/> and the read-only
    /// <see cref="ListOverrideBuffs"/> so `admin bar` shows exactly what a reset destroys.</summary>
    static bool IsBarOverrideBuff(int guid, string name, Entity buff, HashSet<long> spellbookIds)
    {
        if (name.StartsWith("EquipBuff", StringComparison.OrdinalIgnoreCase)) return false; // never the weapon equip buff
        if (guid == CarrierBuff._Value) return IsOwnCarrier(buff, spellbookIds);           // never a spellbook spell (D33)
        foreach (int g in Services.BossFormRegistry.FormBuffGuids) if (g == guid) return true;
        return name.IndexOf("Shapeshift", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Transformation_", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>v0.137.0 (bar-reset D6): read-only — the prefab names of the buffs on the character that
    /// <see cref="RemoveAllFormsAndShapeshifts"/> would destroy.</summary>
    public static List<string> ListOverrideBuffs(Entity character)
    {
        var names = new List<string>();
        if (!character.Exists() || !Core.EntityManager.HasBuffer<BuffBuffer>(character)) return names;
        var spellbookIds = SpellbookBuffIds(character);
        var buffs = Core.EntityManager.GetBuffer<BuffBuffer>(character);
        for (int i = 0; i < buffs.Length; i++)
        {
            if (!buffs[i].Entity.Exists() || buffs[i].Entity.Has<DestroyTag>()) continue;   // queued: destroyed this frame
            string name = buffs[i].PrefabGuid.GetPrefabName() ?? "";
            if (IsBarOverrideBuff(buffs[i].PrefabGuid._Value, name, buffs[i].Entity, spellbookIds))
                names.Add(name.Length > 0 ? name : buffs[i].PrefabGuid._Value.ToString());
        }
        return names;
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
        var spellbookIds = SpellbookBuffIds(character);
        var buffs = Core.EntityManager.GetBuffer<BuffBuffer>(character);
        for (int i = 0; i < buffs.Length; i++)
        {
            Entity be = buffs[i].Entity;
            if (!be.Exists()) continue;
            int guid = buffs[i].PrefabGuid._Value;
            string name = buffs[i].PrefabGuid.GetPrefabName() ?? "";

            if (IsBarOverrideBuff(guid, name, be, spellbookIds)) toDestroy.Add(be);
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
            var spellbookIds = SpellbookBuffIds(character);
            for (int i = 0; i < arr.Length; i++)
            {
                Entity e = arr[i];
                if (!e.Exists()) continue;
                if (!e.TryGetComponent<EntityOwner>(out var owner) || owner.Owner != character) continue;
                // v0.137.0 (D33): a spellbook spell's own buff is a legitimate vanilla bar source.
                if (e.GetPrefabGuid()._Value == CarrierBuff._Value && !IsOwnCarrier(e, spellbookIds)) continue;
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
                        Core.Log.LogInfo($"[Beelz] resetbar: destroyed owned ability-slot source {e.GetPrefabGuid().GetPrefabName()} (#{e.GetPrefabGuid()._Value}) {e}.");
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
    ///
    /// v0.137.0 (bar-reset D32): ONLY the slots the held weapon owns (its equip-buff prefab rows) are pushed. The Empty
    /// mod this adds is sourced by the equip buff and never replaces an earlier one; on a slot the weapon does not own
    /// (2 Space, 3, 5 R, 6 C, 7 T, 8) it masked the stored base (the Space dash) and blocked the spellbook pick, one more
    /// per call, surviving a restart (bar-raw, 2026-09-30). Unknown prefab rows → nothing is pushed.
    /// </summary>
    public static int ForceResetAbilitySlots(Entity character) => ForceResetAbilitySlots(character, out _);

    /// <param name="expected">the number of slots the weapon owns, or -1 when its prefab rows cannot be read.</param>
    public static int ForceResetAbilitySlots(Entity character, out int expected)
    {
        expected = -1;
        if (!character.Exists() || !Core.EntityManager.HasBuffer<BuffBuffer>(character)) return 0;
        if (!SlotApply.TryGetOwnedSlots(character, 8, out var owned))
        {
            Core.Log.LogWarning("[Beelz] ForceResetAbilitySlots: the held weapon's prefab rows are unknown; no slot pushed.");
            return 0;
        }
        expected = owned.Count;
        var targets = new List<int>(owned);
        targets.Sort();

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
        foreach (int slot in targets)
        {
            try { sgm.ModifyAbilityGroupOnSlot(equipBuff, character, slot, PrefabGUID.Empty); cleared++; }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz] ForceResetAbilitySlots slot {slot} failed: {ex.Message}"); }
        }

        if (Core.ReplaceAbilityOnSlotSystem != null) Core.ReplaceAbilityOnSlotSystem.OnUpdate();
        Core.Log.LogInfo($"[Beelz] resetbar: force-cleared {cleared}/{owned.Count} weapon slot(s) [{string.Join(",", targets)}] via ModifyAbilityGroupOnSlot (authoritative client push).");
        return cleared;
    }

    /// <summary>(index, entity) of every slot in the character's AbilityGroupSlotBuffer, snapshotted before any registry
    /// change (removing modifications can be structural and invalidate the live buffer handle).</summary>
    static List<(int idx, Entity slot)> SnapshotSlots(Entity character)
    {
        var slots = new List<(int idx, Entity slot)>();
        var buf = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(character);
        for (int i = 0; i < buf.Length; i++) slots.Add((i, buf[i].GroupSlotEntity._Entity));
        return slots;
    }

    /// <summary>v0.137.0 DIAGNOSTIC (read-only, `admin bar-raw`): for each bar slot 0-<paramref name="maxSlot"/>, the
    /// engine's raw modification dump lines (current + Base GroupGuid, every modification) followed by one parsed line
    /// per GroupGuid mod naming the set ability and the source prefab. Changes nothing.</summary>
    public static List<(int Slot, List<string> Lines)> RawSlotDumps(Entity character, int maxSlot = 8)
    {
        var result = new List<(int, List<string>)>();
        if (!character.Exists() || !Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(character)) return result;
        var reg = Core.ServerGameManager.Modifications;
        foreach (var (idx, slot) in SnapshotSlots(character))
        {
            if (idx > maxSlot) break;
            var lines = new List<string>();
            if (slot == Entity.Null || !slot.Exists()) { lines.Add("no slot entity"); result.Add((idx, lines)); continue; }
            if (!TryFormatEntityModifications(reg, Core.EntityManager, slot, out string dump)) { lines.Add("dump failed"); result.Add((idx, lines)); continue; }
            foreach (var raw in dump.Split('\n'))
                if (raw.Trim().Length > 0) lines.Add(raw.Trim());
            foreach (var e in Beelzebub.Logic.SlotModDump.ParseGroupGuid(dump).Entries)
            {
                string set = e.SetToGuid == 0 ? "Empty" : (new PrefabGUID(e.SetToGuid).GetPrefabName() ?? e.SetToGuid.ToString());
                lines.Add($"parsed mod={e.ModId} set={set} source={e.SourceIndex}:{e.SourceVersion} {SourcePrefabName(e)}");
            }
            result.Add((idx, lines));
        }
        return result;
    }

    /// <summary>grant-refresh: a mod whose source is a `Buff_VBlood_Ability_Replace` buff — a spellbook spell (or our
    /// carrier). Never popped when a grant leaves a slot.</summary>
    internal static bool IsSpellbookSourcedMod(Beelzebub.Logic.SlotModEntry e)
    {
        var src = new Entity { Index = e.SourceIndex, Version = e.SourceVersion };
        return src.Exists() && src.GetPrefabGuid()._Value == CarrierBuff._Value;
    }

    /// <summary>grant-refresh (Codex round 2): the mods a grant leaving a slot must keep — a spellbook spell's, and any
    /// mod sourced by another live buff with a prefab (form, transform, mount, override). The equip-time source of a
    /// weapon's own rows has no prefab and is not in the BuffBuffer, so it stays poppable.</summary>
    internal static Func<Beelzebub.Logic.SlotModEntry, bool> KeepForeignMods(Entity character, Entity equipBuff)
    {
        var live = new HashSet<(int Index, int Version)>();
        if (character.Exists() && Core.EntityManager.HasBuffer<BuffBuffer>(character))
        {
            var buffs = Core.EntityManager.GetBuffer<BuffBuffer>(character);
            for (int i = 0; i < buffs.Length; i++)
            {
                var b = buffs[i].Entity;
                if (b.Exists() && Core.EntityManager.HasComponent<PrefabGUID>(b)) live.Add((b.Index, b.Version));
            }
        }
        return e => IsSpellbookSourcedMod(e)
                    || Beelzebub.Logic.GrantPush.OwnedByOtherBuff(e, live, equipBuff.Index, equipBuff.Version);
    }

    static string SourcePrefabName(Beelzebub.Logic.SlotModEntry e)
    {
        var src = new Entity { Index = e.SourceIndex, Version = e.SourceVersion };
        return src.Exists() ? (src.GetPrefabGuid().GetPrefabName() ?? "") : "";
    }

    /// <summary>
    /// v0.137.0 (bar-reset PopSlotMods, D10 D24): pop EVERY AbilityGroupSlot.GroupGuid modification by id on every slot,
    /// gear-sourced ones too (Reapply + the one Empty push re-add those, so the gear count is stable run to run). A slot
    /// whose dump is Unreadable is skipped entirely — nothing popped, nothing destroyed — and the readback reports it.
    /// A slot that still carries mods after the pop gets its non-gear, non-protected sources destroyed (the engine then
    /// patches the slot on a later tick). Returns the number of mods popped.
    /// </summary>
    public static int PopSlotModifications(Entity character)
    {
        if (!character.Exists() || !Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(character)) return 0;
        var sgm = Core.ServerGameManager;
        var reg = sgm.Modifications;
        var em = Core.EntityManager;
        var slots = SnapshotSlots(character);
        var protectedSet = new HashSet<Entity> { character };
        foreach (var (_, se) in slots) if (se != Entity.Null) protectedSet.Add(se);
        bool IsProtected(Beelzebub.Logic.SlotModEntry e) =>
            protectedSet.Contains(new Entity { Index = e.SourceIndex, Version = e.SourceVersion });

        int popped = 0, destroyed = 0, skipped = 0;
        var failures = new List<string>();
        var toDestroy = new HashSet<Entity>();
        // D33: the mod that puts a slot's spellbook spell on it is the player's own pick — never popped. An unreadable
        // spellbook keeps nothing extra (the mods are then popped as before, and the readback still reports them).
        var spellBySlot = SpellbookSpellsBySlot(character);
        foreach (var (idx, slot) in slots)
        {
            if (slot == Entity.Null || !slot.Exists()) continue;
            var parse = ParseSlot(reg, em, slot);
            bool Keep(Beelzebub.Logic.SlotModEntry e) => Beelzebub.Logic.SpellbookBuffs.IsSpellbookMod(e, idx, spellBySlot, SourcePrefabName(e));
            var d = Beelzebub.Logic.SlotPurgeDecision.Decide(parse, SourcePrefabName, IsProtected, Keep);
            if (d.Skipped)
            {
                // Nothing popped or destroyed (D24) — and the step fails: the readback only covers the bar, so an
                // unreadable slot outside it would otherwise leave the reset looking clean.
                Core.Log.LogWarning($"[Beelz PURGE] slot[{idx}] {slot} dump unreadable — slot skipped.");
                failures.Add($"slot {idx} dump unreadable");
                skipped++;
                continue;
            }
            int slotPopped = 0;
            void Pop(int id)
            {
                try { sgm.RemoveAbilityGroupModificationOnSlot(character, idx, ModificationId.NewId(id)); popped++; slotPopped++; }
                catch (Exception ex)
                {
                    Core.Log.LogWarning($"[Beelz PURGE] slot[{idx}] remove ModId {id} failed: {ex.Message}");
                    failures.Add($"slot {idx} mod {id}");
                }
            }
            foreach (int id in d.ModIdsToPop) Pop(id);
            if (d.ModIdsToPop.Count == 0) continue;
            if (!slot.Exists()) { failures.Add($"slot {idx} entity gone after the pop"); continue; }
            // Destroy backstop only for a slot the pop did not clear (a readable dump that still has mods).
            var after = ParseSlot(reg, em, slot);
            // D32: a stack of the same mod id (the leaked weapon-buff Empties) can need several pops — pop again while
            // the count keeps falling, capped; [Beelz LEAK] records what each bar slot went through.
            int Poppable(Beelzebub.Logic.SlotModParse p) => p.Entries.Count(e => !Keep(e));
            int prev = Poppable(parse), rounds = 1;
            while (after.Readable && Beelzebub.Logic.SlotOwnership.PopAgain(prev, Poppable(after), rounds))
            {
                prev = Poppable(after);
                foreach (int id in after.Entries.Where(e => !Keep(e)).Select(e => e.ModId).Distinct()) Pop(id);
                rounds++;
                if (!slot.Exists()) break;
                after = ParseSlot(reg, em, slot);
            }
            if (idx <= 8)
                Core.Log.LogInfo($"[Beelz LEAK] target={character.GetSteamId()} slot={idx} before={parse.Entries.Count} popped={slotPopped} after={(after.Readable ? after.Entries.Count.ToString() : "unreadable")} kept={(after.Readable ? after.Entries.Count(Keep).ToString() : "?")} rounds={rounds}");
            if (!slot.Exists()) { failures.Add($"slot {idx} entity gone after the pop"); continue; }
            if (!after.Readable) { failures.Add($"slot {idx} dump unreadable after the pop"); skipped++; continue; }
            if (Poppable(after) > 0)
                foreach (var (i, v) in d.SourcesToDestroy) toDestroy.Add(new Entity { Index = i, Version = v });
        }
        // An unreadable slot's sources are unknown, so any source queued here might also drive it: with a skipped
        // slot the destroy pass is not run at all (D24 — nothing is destroyed for an unreadable slot).
        if (skipped > 0 && toDestroy.Count > 0)
        {
            Core.Log.LogWarning($"[Beelz PURGE] {skipped} unreadable slot(s) — destroy backstop skipped for {toDestroy.Count} source(s).");
            toDestroy.Clear();
        }
        foreach (Entity src in toDestroy)
        {
            if (!src.Exists() || src.Has<DestroyTag>() || protectedSet.Contains(src)) continue;
            try
            {
                string sn = src.GetPrefabGuid().GetPrefabName();
                DestroyUtility.Destroy(em, src, DestroyDebugReason.TryRemoveBuff);
                destroyed++;
                Core.Log.LogInfo($"[Beelz PURGE] destroyed modification source {src} ({(string.IsNullOrEmpty(sn) ? "no prefab" : sn)}).");
            }
            catch (Exception ex)
            {
                Core.Log.LogWarning($"[Beelz PURGE] destroy source {src} failed: {ex.Message}");
                failures.Add($"destroy {src}");
            }
        }
        if (Core.ReplaceAbilityOnSlotSystem != null) Core.ReplaceAbilityOnSlotSystem.OnUpdate();
        // Every slot was processed first; a failure then makes the whole step an ERR (never a silent partial pop).
        if (failures.Count > 0)
            throw new InvalidOperationException($"{failures.Count} pop failure(s): {string.Join("; ", failures.GetRange(0, Math.Min(3, failures.Count)))}");
        return popped;
    }

    /// <summary>One slot's GroupGuid mods as the bar readback sees them.</summary>
    public readonly record struct SlotModReading(int Slot, int Gear, int Other, bool Unreadable, List<string> GearSources);

    /// <summary>
    /// v0.137.0 (bar-reset ReadBar, D6): read-only — the GroupGuid mods of the BAR slots 0-<paramref name="maxSlot"/>
    /// (the buffer holds ~300 slot entities; only the bar is shown or judged), split by the gear-source rule
    /// (Logic/GearRule: EquipBuff* / Item_*) with the gear source names kept. A mod sourced by the character itself or a
    /// slot entity is the engine's own and counts as vanilla ("character"), never as other. Changes nothing.
    /// </summary>
    public static List<SlotModReading> ReadSlotMods(Entity character, int maxSlot = 8)
    {
        var list = new List<SlotModReading>();
        if (!character.Exists() || !Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(character)) return list;
        var reg = Core.ServerGameManager.Modifications;
        var all = SnapshotSlots(character);
        var own = new HashSet<Entity> { character };
        foreach (var (_, se) in all) if (se != Entity.Null) own.Add(se);
        // D32: a weapon-buff Empty on a slot the weapon does not own is a leak — counted as other, never clean
        SlotApply.TryGetOwnedSlots(character, maxSlot, out var owned);
        var spellBySlot = SpellbookSpellsBySlot(character);   // D33: the spellbook's own pick is legitimate, never other
        foreach (var (idx, slot) in all)
        {
            if (idx > maxSlot) break;
            if (slot == Entity.Null || !slot.Exists()) continue;   // no slot entity → nothing can modify it
            var parse = ParseSlot(reg, Core.EntityManager, slot);
            if (!parse.Readable) { list.Add(new SlotModReading(idx, 0, 0, true, new List<string>())); continue; }
            int gear = 0, other = 0;
            bool unknown = false;
            var names = new List<string>();
            foreach (var e in parse.Entries)
            {
                bool engineOwn = own.Contains(new Entity { Index = e.SourceIndex, Version = e.SourceVersion });
                string n = engineOwn ? "character" : SourcePrefabName(e);
                var verdict = Beelzebub.Logic.SlotOwnership.ClassifyEmpty(e, idx, !engineOwn && Beelzebub.Logic.SlotOwnership.IsWeaponBuff(n), owned);
                if (!engineOwn && Beelzebub.Logic.SpellbookBuffs.IsSpellbookMod(e, idx, spellBySlot, n)) { gear++; if (!names.Contains("spellbook")) names.Add("spellbook"); continue; }
                if (verdict == Beelzebub.Logic.EmptyVerdict.Leak) other++;
                else if (verdict == Beelzebub.Logic.EmptyVerdict.Unknown) unknown = true;
                else if (engineOwn || Beelzebub.Logic.GearRule.IsGearSource(n)) { gear++; if (!names.Contains(n)) names.Add(n); }
                else other++;
            }
            list.Add(unknown ? new SlotModReading(idx, 0, 0, true, new List<string>()) : new SlotModReading(idx, gear, other, false, names));
        }
        return list;
    }

    /// <summary>v0.137.0 (bar-reset D24): the dump, or false when the engine formatter throws — the bar-reset callers
    /// treat that as Unreadable (an empty string would parse as a readable dump with no mods, i.e. falsely clean).</summary>
    static bool TryFormatEntityModifications(ModificationsRegistry reg, EntityManager em, Entity entity, out string dump)
    {
        try
        {
            var sb = new Il2CppSystem.Text.StringBuilder();
            reg.GetFormattedEntityModificationsMessage(sb, em, entity);
            dump = sb.ToString();
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz PURGE] FormatEntityModifications({entity}) failed: {ex.Message}");
            dump = "";
            return false;
        }
    }

    /// <summary>grant-refresh: one slot's GroupGuid parse by bar index (Unreadable when the slot entity is missing).</summary>
    internal static Beelzebub.Logic.SlotModParse ParseSlotMods(Entity character, int slot)
    {
        foreach (var (idx, se) in SnapshotSlots(character))
            if (idx == slot && se != Entity.Null && se.Exists())
                return ParseSlot(Core.ServerGameManager.Modifications, Core.EntityManager, se);
        return Beelzebub.Logic.SlotModParse.Failed();
    }

    /// <summary>The slot's GroupGuid parse; a formatter failure yields an Unreadable parse.</summary>
    static Beelzebub.Logic.SlotModParse ParseSlot(ModificationsRegistry reg, EntityManager em, Entity slot) =>
        TryFormatEntityModifications(reg, em, slot, out string dump)
            ? Beelzebub.Logic.SlotModDump.ParseGroupGuid(dump)
            : Beelzebub.Logic.SlotModParse.Failed();

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
