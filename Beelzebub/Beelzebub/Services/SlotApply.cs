using System;
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
        if (Core.AbilityRules.IsTransformOnly(name, abilityGuid)) return false;

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
        if (Core.AbilityRules.IsTransformOnly(name, abilityGuid)) return false;
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

        if (!TryFindEquipBuff(character, out Entity buffEntity, out string equipName)) return false;
        var weapon = DetectFamily(equipName);
        bool ok = explicitWeaponBucket ? IsGrantUsable(ability._Value) : IsGrantCompatible(ability._Value, weapon);
        if (!ok) return false;

        try
        {
            var buffer = Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(buffEntity);
            ReplaceSlotEntry(buffer, slot, ability);
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
    /// when there was a buffer entry to clear. v0.14.0 Z1: no Modify call.
    /// </summary>
    public static bool ClearGrant(Entity character, int slot)
    {
        if (Core.AbilityRegistry.GetActiveTransform(character.GetSteamId()) is not null) return false;
        if (!TryFindEquipBuff(character, out Entity buffEntity, out _)) return false;

        try
        {
            var buffer = Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(buffEntity);
            bool removed = RemoveSlotEntries(buffer, slot);
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
        var slots = Core.AbilityRegistry.GetSlotsResolvedWithOrigin(steamId, weapon);
        try
        {
            var buffer = Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(buffEntity);
            int applied = 0;
            foreach (var (slot, entry) in slots)
            {
                // Explicit weapon-bucket binds are honored as-is; universal binds stay family-filtered.
                bool ok = entry.weaponSpecific ? IsGrantUsable(entry.abilityGuid) : IsGrantCompatible(entry.abilityGuid, weapon);
                if (!ok) continue;
                ReplaceSlotEntry(buffer, slot, new PrefabGUID(entry.abilityGuid));
                applied++;
            }
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

    static void ReplaceSlotEntry(DynamicBuffer<ReplaceAbilityOnSlotBuff> buffer, int slot, PrefabGUID ability)
    {
        RemoveSlotEntries(buffer, slot);
        buffer.Add(new ReplaceAbilityOnSlotBuff
        {
            Slot = slot,
            NewGroupId = ability,
            CopyCooldown = true,
            Priority = 0,
        });
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
