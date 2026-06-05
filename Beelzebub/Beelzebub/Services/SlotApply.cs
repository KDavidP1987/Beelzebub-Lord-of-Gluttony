using System;
using System.Collections.Generic;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// W3 grant-injection helpers + weapon-family detection.
///
/// **v0.14.0 Z1 change**: the previous ApplyTransform/ApplyRevert pair (which mutated
/// the EquipBuff_Weapon's ReplaceAbilityOnSlotBuff directly and called
/// ServerGameManager.ModifyAbilityGroupOnSlot) is gone. Transforms now route through
/// <see cref="TransformBuffService"/>, which owns a dedicated buff entity for the
/// overrides. That eliminates the AbilityGroupSlot "modification source" console
/// warnings and lets V Rising re-resolve player spell-book slots cleanly on revert.
///
/// ApplyGrant/ClearGrant still write to the EquipBuff_Weapon's buffer (so saved
/// grants follow the wielded weapon), but no longer call ModifyAbilityGroupOnSlot —
/// we trigger ReplaceAbilityOnSlotSystem.OnUpdate() instead to apply this frame.
/// </summary>
internal static class SlotApply
{
    /// <summary>Find the player's active equip-buff entity (weapon, unarmed, or fishing pole).</summary>
    static bool TryFindEquipBuff(Entity character, out Entity buffEntity, out string equipName)
    {
        buffEntity = Entity.Null;
        equipName = null;
        if (!character.Exists() || !Core.EntityManager.HasBuffer<BuffBuffer>(character)) return false;

        var buffs = Core.EntityManager.GetBuffer<BuffBuffer>(character);
        for (int i = 0; i < buffs.Length; i++)
        {
            string name = buffs[i].PrefabGuid.GetPrefabName();
            if (name != null && name.StartsWith("EquipBuff_Weapon", StringComparison.OrdinalIgnoreCase))
            {
                buffEntity = buffs[i].Entity;
                equipName = name;
                return buffEntity.Exists() && Core.EntityManager.HasBuffer<ReplaceAbilityOnSlotBuff>(buffEntity);
            }
        }
        return false;
    }

    /// <summary>
    /// W3: detect the player's currently-equipped weapon family by walking their
    /// BuffBuffer for an active EquipBuff_Weapon_* and parsing its prefab name.
    /// Returns WeaponFamily.None if no equip-buff is present (rare — most players
    /// will at least have the Unarmed buff). Used by command handlers and the
    /// resolved-slot lookup.
    /// </summary>
    public static WeaponFamily GetCurrentWeapon(Entity character)
    {
        if (!TryFindEquipBuff(character, out _, out string equipName)) return WeaponFamily.None;
        return DetectFamily(equipName);
    }

    /// <summary>
    /// W2: parse an EquipBuff_Weapon_*_Base / _Unarmed_Start01 prefab name into a
    /// concrete WeaponFamily. Returns None if the buff name isn't recognized.
    /// </summary>
    public static WeaponFamily DetectFamily(string equipBuffName)
    {
        if (string.IsNullOrEmpty(equipBuffName)) return WeaponFamily.None;
        // Unarmed special case — name is `EquipBuff_Weapon_Unarmed_Start01`.
        if (equipBuffName.Contains("Unarmed", StringComparison.OrdinalIgnoreCase)) return WeaponFamily.Unarmed;
        if (equipBuffName.Contains("FishingPole", StringComparison.OrdinalIgnoreCase)) return WeaponFamily.FishingPole;
        if (equipBuffName.Contains("GreatSword", StringComparison.OrdinalIgnoreCase)) return WeaponFamily.GreatSword;
        // v0.49.0: DualHammers intentionally not detected — unobtainable cut content (see WeaponFamily).
        if (equipBuffName.Contains("Crossbow", StringComparison.OrdinalIgnoreCase)) return WeaponFamily.Crossbow;
        if (equipBuffName.Contains("Longbow", StringComparison.OrdinalIgnoreCase)) return WeaponFamily.Longbow;
        if (equipBuffName.Contains("TwinBlades", StringComparison.OrdinalIgnoreCase)) return WeaponFamily.TwinBlades;
        if (equipBuffName.Contains("Slashers", StringComparison.OrdinalIgnoreCase)) return WeaponFamily.Slashers;
        if (equipBuffName.Contains("Pollaxe", StringComparison.OrdinalIgnoreCase)) return WeaponFamily.Pollaxe;
        if (equipBuffName.Contains("Pistols", StringComparison.OrdinalIgnoreCase)) return WeaponFamily.Pistols;
        if (equipBuffName.Contains("Daggers", StringComparison.OrdinalIgnoreCase)) return WeaponFamily.Daggers;
        if (equipBuffName.Contains("Reaper", StringComparison.OrdinalIgnoreCase)) return WeaponFamily.Reaper;
        if (equipBuffName.Contains("Claws", StringComparison.OrdinalIgnoreCase)) return WeaponFamily.Claws;
        if (equipBuffName.Contains("Spear", StringComparison.OrdinalIgnoreCase)) return WeaponFamily.Spear;
        if (equipBuffName.Contains("Whip", StringComparison.OrdinalIgnoreCase)) return WeaponFamily.Whip;
        // Test more-specific tokens first, then the generic ones, so e.g. "GreatSword" doesn't match "Sword".
        if (equipBuffName.Contains("Sword", StringComparison.OrdinalIgnoreCase)) return WeaponFamily.Sword;
        if (equipBuffName.Contains("Mace", StringComparison.OrdinalIgnoreCase)) return WeaponFamily.Mace;
        if (equipBuffName.Contains("Axe", StringComparison.OrdinalIgnoreCase)) return WeaponFamily.Axe;
        return WeaponFamily.None;
    }

    /// <summary>
    /// W2: does this saved grant fire when the player is wielding <paramref name="weapon"/>?
    /// Rules:
    ///   - If the ability's WeaponFamily list contains <paramref name="weapon"/> directly: yes.
    ///   - If the ability is Magic (or has None / empty list): universal, fires regardless.
    ///   - Otherwise: no.
    /// Also enforces Enabled and TransformOnly (kill-switch + transform-reserved bypass).
    /// </summary>
    public static bool IsGrantCompatible(int abilityGuid, WeaponFamily weapon)
    {
        var ability = new PrefabGUID(abilityGuid);
        string name = ability.GetPrefabName();
        if (!Core.AbilityRules.IsEnabled(name, abilityGuid)) return false;
        if (Core.AbilityRules.IsTransformOnlyEnforced(name, abilityGuid)) return false;
        // v0.101.0: explicit weapon blacklist ("!Sword" in the ability's Weapons list) — refuse on that
        // weapon even if the ability is otherwise universal.
        if (Core.AbilityRules.IsWeaponBlocked(name, weapon)) return false;

        var families = Core.AbilityRules.ClassifyWeaponFamilies(name);
        // Universal: empty (None) or contains Magic — fires for any weapon, including unarmed.
        foreach (var fam in families)
        {
            if (fam == WeaponFamily.Magic || fam == WeaponFamily.None) return true;
            if (fam == weapon) return true;
        }
        return false;
    }

    /// <summary>
    /// v0.49.0: minimal gate for an EXPLICIT weapon-bucket grant. The player has already chosen
    /// to place this ability on that weapon's bar, so we honor it regardless of the ability's
    /// name-derived family (the heuristic that silently dropped Reaper-bucket binds). Still
    /// respects the admin kill-switch and the transform-only reservation.
    /// </summary>
    public static bool IsGrantUsable(int abilityGuid)
    {
        var ability = new PrefabGUID(abilityGuid);
        string name = ability.GetPrefabName();
        if (!Core.AbilityRules.IsEnabled(name, abilityGuid)) return false;
        if (Core.AbilityRules.IsTransformOnlyEnforced(name, abilityGuid)) return false;
        return true;
    }

    /// <summary>
    /// Apply a single Beelzebub-saved grant in-place. Returns true if the change was
    /// applied immediately (i.e. compatible with the player's currently-equipped weapon).
    /// Returns false otherwise — the saved assignment will activate on the next weapon
    /// swap if compatibility changes, via the ReplaceAbilityOnSlotSystemPatch.
    ///
    /// v0.49.0: <paramref name="explicitWeaponBucket"/> = true when the caller is binding into
    /// a specific weapon family the player named (e.g. `.beelz weapon-grant Reaper …`). In that
    /// case the family-compatibility heuristic is skipped (honor the explicit placement); only
    /// the universal-bucket path still family-filters.
    ///
    /// v0.14.0 Z1: no longer calls ServerGameManager.ModifyAbilityGroupOnSlot — that
    /// registered the EquipBuff entity as a modification source and produced
    /// "Clearing entity X" warnings when the EquipBuff later recycled. We just
    /// mutate the buffer and trigger a system update to apply this frame.
    /// </summary>
    public static bool ApplyGrant(Entity character, int slot, PrefabGUID ability, bool explicitWeaponBucket = false)
    {
        // While transformed the transform owns slots 1..6 — don't stomp it.
        if (Core.AbilityRegistry.GetActiveTransform(character.GetSteamId()) is not null) return false;

        // v0.61.0: never hand the engine a slot it can't index (would IndexOutOfRange in a Burst job).
        if (!AbilityRegistry.IsValidSlot(slot))
        {
            Core.Log.LogWarning($"[Beelz] ApplyGrant rejected corrupt slot={slot} (valid {AbilityRegistry.MinSlot}-{AbilityRegistry.MaxSlot}) ability={ability._Value} for {character.GetSteamId()}.");
            return false;
        }

        if (!TryFindEquipBuff(character, out Entity buffEntity, out string equipName)) return false;
        var weapon = DetectFamily(equipName);
        bool ok = explicitWeaponBucket ? IsGrantUsable(ability._Value) : IsGrantCompatible(ability._Value, weapon);
        if (!ok) return false;

        int slotBufLen = SlotBufferLength(character);
        if (slot >= slotBufLen)
        {
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] ApplyGrant defer slot={slot}: live slot buffer only {slotBufLen} long for {character.GetSteamId()}; applies on next bar resolve.");
            return false;
        }

        try
        {
            // v0.74.0 (#5): record the vanilla base we're now masking BEFORE we add the override, so a
            // later spellbook re-pick on this slot is detected as a deliberate change (auto-yield) rather
            // than re-masked. Previously the baseline was only captured on a weapon swap, so a
            // grant-then-repick with no intervening swap had nothing to compare against and the granted
            // ability kept coming back. (Base = BaseAbilityGroupOnSlot, the vanilla underneath our override.)
            if (TryGetSlotBase(character, slot, out var maskedBase) && maskedBase._Value != 0 && maskedBase._Value != ability._Value)
                Core.AbilityRegistry.SetSlotBaseline(character.GetSteamId(), weapon, slot, maskedBase._Value);

            var buffer = Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(buffEntity);
            ReplaceSlotEntry(character, buffer, slot, ability);
            // v0.63.0 (#5): force the client HUD to refresh this slot now (no weapon swap needed).
            // `buffer` is finished being used above, so the structural AddComponent is safe here.
            MarkSlotDirty(character, slot);
            if (Core.ReplaceAbilityOnSlotSystem != null) Core.ReplaceAbilityOnSlotSystem.OnUpdate();
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] SlotApply.ApplyGrant failed slot={slot} ability={ability._Value}: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Clear a Beelzebub-saved grant in-place (mirrors .beelz unslot). Returns true
    /// when there was a buffer entry to clear.
    ///
    /// v0.56.0: now actively RESTORES the slot's stored vanilla base afterward
    /// (<see cref="RestoreSlotBaseValue"/>) so the slot returns to its in-game ability
    /// immediately. Previously we only removed the override entry — but
    /// ReplaceAbilityOnSlotSystem only APPLIES override entries, it doesn't restore the base
    /// when one disappears, so the slot stayed stuck on the captured ability until a weapon
    /// swap rebuilt it from base (the "clearing is inconsistent" report).
    /// </summary>
    public static bool ClearGrant(Entity character, int slot)
    {
        if (Core.AbilityRegistry.GetActiveTransform(character.GetSteamId()) is not null) return false;
        if (!TryFindEquipBuff(character, out Entity buffEntity, out _)) return false;

        try
        {
            var buffer = Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(buffEntity);
            bool removed = RemoveSlotEntries(buffer, slot);
            RestoreSlotBaseValue(character, buffEntity, slot);
            if (Core.ReplaceAbilityOnSlotSystem != null) Core.ReplaceAbilityOnSlotSystem.OnUpdate();
            return removed;
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] SlotApply.ClearGrant failed slot={slot}: {ex}");
            return false;
        }
    }

    /// <summary>
    /// v0.41.0: re-inject the player's saved grant loadout (universal + current-weapon
    /// overrides) directly onto their live equip-buff — query-INDEPENDENT, so it works
    /// on revert / form-exit / `.beelz refresh` without waiting for a weapon swap to
    /// trigger ReplaceAbilityOnSlotSystemPatch. Mirrors that patch's per-swap injection.
    /// No-op while transformed (the transform buff owns the bar then). Returns the count applied.
    /// </summary>
    public static int RestoreResolvedGrants(Entity character)
    {
        if (!character.Exists()) return 0;
        ulong steamId = character.GetSteamId();
        if (Core.AbilityRegistry.GetActiveTransform(steamId) is not null) return 0;
        if (!TryFindEquipBuff(character, out Entity buffEntity, out string equipName)) return 0;

        var weapon = DetectFamily(equipName);
        try
        {
            var buffer = Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(buffEntity);
            int applied = ResolveAndInjectGrants(character, buffEntity, weapon, buffer);
            if (Core.ReplaceAbilityOnSlotSystem != null) Core.ReplaceAbilityOnSlotSystem.OnUpdate();
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] RestoreResolvedGrants: re-applied {applied} grant(s) for {steamId} (weapon={weapon}).");
            return applied;
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] SlotApply.RestoreResolvedGrants failed: {ex}");
            return 0;
        }
    }

    /// <summary>
    /// v0.56.0: the shared "resolve saved binds → inject overrides" core used by BOTH the
    /// per-weapon-swap patch (<c>ReplaceAbilityOnSlotSystemPatch</c>) and the query-independent
    /// <see cref="RestoreResolvedGrants"/>. For each resolved bind it either:
    ///   (a) AUTO-YIELDS the slot back to vanilla — when the player has used the in-game
    ///       spellbook to put a DIFFERENT ability on that slot (its base changed out from under
    ///       our override), we drop the bind and let their pick stand ("mix and match"); or
    ///   (b) injects the captured ability as a ReplaceAbilityOnSlotBuff override.
    /// Caller owns the surrounding ReplaceAbilityOnSlotSystem.OnUpdate() (the patch is itself a
    /// prefix to it; RestoreResolvedGrants drives it explicitly). Returns the count injected.
    /// </summary>
    internal static int ResolveAndInjectGrants(Entity character, Entity equipBuff, WeaponFamily weapon, DynamicBuffer<ReplaceAbilityOnSlotBuff> buffer)
    {
        ulong steamId = character.GetSteamId();
        var slots = Core.AbilityRegistry.GetSlotsResolvedWithOrigin(steamId, weapon);
        if (slots.Count == 0) return 0;

        int slotBufLen = SlotBufferLength(character);   // v0.61.0: engine's live slot count

        List<int> yielded = null;
        List<int> injectedSlots = null;
        int injected = 0;
        foreach (var (slot, entry) in slots)
        {
            // v0.61.0: never hand the engine a slot it can't index. A slot outside the bindable
            // 1-6 range is corrupt saved data (pre-guard or hand-edited) → drop it; a slot that's
            // valid but beyond the LIVE buffer is a transient bar state (e.g. mid-resolve / a
            // weapon with fewer slots) → defer, it injects on the next resolve when the bar is ready.
            if (!AbilityRegistry.IsValidSlot(slot))
            {
                Core.Log.LogWarning($"[Beelz] skip inject: corrupt slot={slot} (valid {AbilityRegistry.MinSlot}-{AbilityRegistry.MaxSlot}) ability={entry.abilityGuid} weapon={weapon} for {steamId} — ignoring saved bind.");
                continue;
            }
            if (slot >= slotBufLen)
            {
                if (Beelzebub.Config.Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz] defer inject slot={slot}: live slot buffer only {slotBufLen} long for {steamId} (weapon={weapon}); will apply when the bar is ready.");
                continue;
            }

            // Explicit weapon-bucket binds are honored as-is; universal binds stay family-filtered.
            bool ok = entry.weaponSpecific ? IsGrantUsable(entry.abilityGuid) : IsGrantCompatible(entry.abilityGuid, weapon);
            if (!ok) continue;

            if (ShouldYieldSlot(character, steamId, weapon, slot, entry.abilityGuid))
            {
                RemoveSlotEntries(buffer, slot);                       // strip any stale override (buffer-content only)
                ReleaseYieldedBind(character, steamId, weapon, slot, entry.weaponSpecific);
                (yielded ??= new List<int>()).Add(slot);
                continue;
            }

            buffer.Add(new ReplaceAbilityOnSlotBuff
            {
                Slot = slot,
                NewGroupId = new PrefabGUID(entry.abilityGuid),
                // v0.77.0: copy the slot's live cooldown ONLY when the same ability is re-resolving
                // (e.g. a weapon swap re-injecting the same loadout — preserves your in-progress
                // cooldown). If a DIFFERENT ability currently occupies the slot, do NOT copy — otherwise
                // a high cooldown (e.g. a 60s configured Ice Nova) BLEEDS onto the newly-placed ability
                // (the "all my abilities suddenly have a long cooldown" report). Cooldown follows the
                // ABILITY, not the slot.
                CopyCooldown = ShouldCopyCooldown(character, slot, entry.abilityGuid),
                // v0.120.0: configurable so an admin can make Beelzebub win (or yield) a slot another
                // mod (e.g. Bloodcraft) also writes. Default 0 = legacy/neutral. See Settings.Interop_*.
                Priority = Beelzebub.Config.Settings.Interop_SlotInjectionPriority.Value,
            });
            injected++;
            (injectedSlots ??= new List<int>()).Add(slot);
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] inject slot={slot} ability={new PrefabGUID(entry.abilityGuid).GetPrefabName()} weapon={weapon} explicit={entry.weaponSpecific} for {steamId}");
        }

        // Snap yielded slots to their (new) vanilla base AFTER the buffer loop — the DirtyTag
        // AddComponent inside RestoreSlotBaseValue is a STRUCTURAL change that would invalidate
        // the live `buffer` handle if done mid-iteration.
        if (yielded != null)
            foreach (int slot in yielded)
                RestoreSlotBaseValue(character, equipBuff, slot);

        // v0.63.0 (#5): mark freshly-injected slots dirty so the client HUD refreshes without a
        // weapon swap. Matters for the non-equip-event callers (`.beelz refresh`, login re-apply,
        // transform revert); the per-swap patch path is already rebuilding the bar. Deferred past
        // the buffer loop for the same structural-change reason as the yielded restore above.
        if (injectedSlots != null)
            foreach (int slot in injectedSlots)
                MarkSlotDirty(character, slot);

        return injected;
    }

    /// <summary>
    /// v0.56.0: decide whether the player has re-claimed this slot via the in-game spellbook.
    /// We lazily record the vanilla base our override masks (keyed by the CURRENT weapon) the
    /// first time we see the slot; on later resolves, if the live base differs from that
    /// recorded baseline, the player deliberately picked a different ability → yield. A base of
    /// 0 (load race / empty) or one that already equals our captured ability is ignored.
    /// </summary>
    static bool ShouldYieldSlot(Entity character, ulong steamId, WeaponFamily weapon, int slot, int abilityGuid)
    {
        if (!TryGetSlotBase(character, slot, out var liveBase)) return false;
        int liveBaseGuid = liveBase._Value;
        if (liveBaseGuid == 0 || liveBaseGuid == abilityGuid) return false;

        if (!Core.AbilityRegistry.TryGetSlotBaseline(steamId, weapon, slot, out int baseline))
        {
            // v0.74.0 (#5): first time we've masked this slot on THIS weapon. If other weapons have
            // consistently masked the same vanilla base — i.e. this is a weapon-INDEPENDENT (spell) slot
            // — and the live base now differs from that agreed value, the player re-picked via the
            // in-game spellbook (on another weapon) → yield here too, instead of recording the re-picked
            // base as this weapon's baseline and re-masking it. This is the "swap weapons and the granted
            // ability comes back over my vanilla pick" bug: each fresh weapon used to lazily adopt the
            // re-picked base and re-inject. Weapon-ABILITY slots differ per weapon, never agree across
            // weapons, so TryGetConsistentSlotBaseline returns false for them → no false yield.
            if (Core.AbilityRegistry.TryGetConsistentSlotBaseline(steamId, slot, out int common) && liveBaseGuid != common)
                return true;
            Core.AbilityRegistry.SetSlotBaseline(steamId, weapon, slot, liveBaseGuid);
            return false;
        }
        return baseline != liveBaseGuid;
    }

    /// <summary>
    /// v0.56.0: drop the released slot's saved bind + baseline, persist, and tell BCH.
    /// v0.63.0 (#1): a vanilla spellbook re-pick now wins across ALL weapon groups — clear the
    /// slot's bind from the universal bucket AND every weapon family (forms are intentionally NOT
    /// touched), not just the bucket that happened to be active. Without this, swapping weapons
    /// re-masked the player's vanilla pick with the captured ability (the "switch weapons and back
    /// restores the modded ability" report): each weapon bucket lazily re-recorded the post-pick
    /// base as its own baseline and kept re-injecting.
    /// </summary>
    static void ReleaseYieldedBind(Entity character, ulong steamId, WeaponFamily weapon, int slot, bool weaponSpecific)
    {
        int cleared = Core.AbilityRegistry.ClearSlotAllBuckets(steamId, slot);
        Core.AbilityRegistry.ClearSlotBaseline(steamId, slot);
        Core.Persistence.RequestSave();

        if (Beelzebub.Config.Settings.VerboseLogging.Value)
            Core.Log.LogInfo($"[Beelz] auto-yield slot={slot} (active bucket {(weaponSpecific ? weapon.ToString() : "universal")}): player re-picked their spellbook ability — cleared {cleared} bind(s) across all weapon groups for {steamId}.");

        // Reuse the existing slot-cleared event so BCH refreshes its loadout view (no wire change).
        try { Core.Chat.SendEvent(character, $"[BEELZ:event] type=slot-cleared slot={slot}"); }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz] auto-yield slot-cleared event failed slot={slot}: {ex.Message}"); }
    }

    /// <summary>
    /// v0.56.0: read the player's stored VANILLA base ability for a slot from their
    /// AbilityGroupSlotBuffer — what the in-game spellbook / equipped weapon set, independent of
    /// any Beelzebub override (the override writes the resolved AbilityGroupSlot, not the base).
    /// Slot numbering matches ReplaceAbilityOnSlotBuff.Slot / ModifyAbilityGroupOnSlot.
    /// </summary>
    public static bool TryGetSlotBase(Entity character, int slot, out PrefabGUID baseAbility)
    {
        baseAbility = default;
        if (!character.Exists() || !Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(character)) return false;
        var buf = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(character);
        if (slot < 0 || slot >= buf.Length) return false;
        baseAbility = buf[slot].BaseAbilityGroupOnSlot;
        return true;
    }

    /// <summary>
    /// v0.56.0: reliably return a slot to its stored vanilla base on the LIVE bar — no weapon
    /// swap or relog needed. Writes the base back through the engine's own slot-setter (the
    /// proven <c>rebuildslots</c> mechanism) and marks the slot dirty so the bar re-resolves.
    /// <paramref name="equipBuff"/> is the modification source ReplaceAbilityOnSlotSystem expects.
    /// Caller must NOT be mid-iteration over the equip buff's ReplaceAbilityOnSlotBuff (the
    /// DirtyTag AddComponent is structural).
    /// </summary>
    static void RestoreSlotBaseValue(Entity character, Entity equipBuff, int slot)
    {
        try
        {
            if (!TryGetSlotBase(character, slot, out var baseAbility)) return;
            Core.ServerGameManager.ModifyAbilityGroupOnSlot(equipBuff, character, slot, baseAbility);
            MarkSlotDirty(character, slot);
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz] RestoreSlotBaseValue slot={slot} failed: {ex.Message}");
        }
    }

    /// <summary>
    /// v0.63.0 (#5): add the engine's <c>AbilityGroupSlot.DirtyTag</c> to a slot so the client HUD
    /// re-renders it THIS resolve. Injecting a ReplaceAbilityOnSlotBuff override + running the system
    /// updates the slot SERVER-side, but without the dirty tag the client action bar doesn't refresh
    /// until the whole bar rebuilds on a weapon swap — the "I have to swap weapons back and forth for
    /// a granted ability to show up" report. Structural change (AddComponent): callers must NOT be
    /// mid-iteration over a live DynamicBuffer when they call this.
    /// </summary>
    static void MarkSlotDirty(Entity character, int slot)
    {
        if (!Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(character)) return;
        var sb = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(character);
        if (slot < 0 || slot >= sb.Length) return;
        Entity slotEnt = sb[slot].GroupSlotEntity._Entity;
        var dirty = Unity.Entities.ComponentType.ReadWrite(Il2CppInterop.Runtime.Il2CppType.Of<AbilityGroupSlot.DirtyTag>());
        if (slotEnt.Exists() && !Core.EntityManager.HasComponent(slotEnt, dirty))
            Core.EntityManager.AddComponent(slotEnt, dirty);
    }

    /// <summary>
    /// v0.61.0: live length of the engine's <c>AbilityGroupSlotBuffer</c> — the bound the engine's
    /// ReplaceAbilityOnSlotSystem indexes with <c>ReplaceAbilityOnSlotBuff.Slot</c>. We check a slot
    /// against this BEFORE injecting so an out-of-range slot can't reach the engine and throw an
    /// IndexOutOfRange inside a Burst job (uncatchable, no managed trace). Returns 0 if the buffer is
    /// absent (→ nothing to inject onto yet).
    /// </summary>
    static int SlotBufferLength(Entity character)
    {
        if (!character.Exists() || !Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(character)) return 0;
        return Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(character).Length;
    }

    static void ReplaceSlotEntry(Entity character, DynamicBuffer<ReplaceAbilityOnSlotBuff> buffer, int slot, PrefabGUID ability)
    {
        // v0.77.0: read the slot's current occupant BEFORE we strip overrides, so CopyCooldown reflects
        // whether the SAME ability is being (re)placed (preserve its cooldown) or a DIFFERENT one is
        // (don't bleed the prior ability's cooldown — see ShouldCopyCooldown).
        bool copyCd = ShouldCopyCooldown(character, slot, ability._Value);
        RemoveSlotEntries(buffer, slot);
        buffer.Add(new ReplaceAbilityOnSlotBuff
        {
            Slot = slot,
            NewGroupId = ability,
            CopyCooldown = copyCd,
            // v0.120.0: configurable cross-mod slot priority (see Settings.Interop_SlotInjectionPriority).
            Priority = Beelzebub.Config.Settings.Interop_SlotInjectionPriority.Value,
        });
    }

    /// <summary>
    /// v0.77.0: should a slot injection copy the slot's live cooldown onto the incoming ability?
    /// YES only when the slot is empty OR already resolves to the SAME ability group (a weapon-swap
    /// re-resolve — keep the in-progress cooldown). NO when a DIFFERENT ability currently occupies the
    /// slot, so a high configured cooldown can't bleed onto the newly-placed ability. Defaults to true
    /// (status-quo) if the slot state can't be read, so this never resets a cooldown it shouldn't.
    /// </summary>
    static bool ShouldCopyCooldown(Entity character, int slot, int newGroupGuid)
    {
        int resolved = CurrentSlotResolvedGuid(character, slot);
        return resolved == 0 || resolved == newGroupGuid;
    }

    /// <summary>The ability-group GUID currently resolved on a slot (AbilityGroupSlot.StateEntity prefab
    /// GUID), or 0 if it can't be read. Same chain the cooldown enforcer walks.</summary>
    static int CurrentSlotResolvedGuid(Entity character, int slot)
    {
        try
        {
            if (!Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(character)) return 0;
            var slots = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(character);
            if (slot < 0 || slot >= slots.Length) return 0;
            Entity slotEnt = slots[slot].GroupSlotEntity._Entity;
            if (!slotEnt.Exists() || !Core.EntityManager.HasComponent<AbilityGroupSlot>(slotEnt)) return 0;
            var ags = Core.EntityManager.GetComponentData<AbilityGroupSlot>(slotEnt);
            Entity stateEnt = ags.StateEntity._Entity;
            if (!stateEnt.Exists()) return 0;
            return stateEnt.GetPrefabGuid()._Value;
        }
        catch { return 0; }
    }

    static bool RemoveSlotEntries(DynamicBuffer<ReplaceAbilityOnSlotBuff> buffer, int slot)
    {
        bool removed = false;
        for (int i = buffer.Length - 1; i >= 0; i--)
        {
            if (buffer[i].Slot == slot) { buffer.RemoveAt(i); removed = true; }
        }
        return removed;
    }
}
