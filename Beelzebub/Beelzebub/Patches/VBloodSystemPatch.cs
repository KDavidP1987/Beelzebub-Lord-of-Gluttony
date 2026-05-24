using System;
using System.Collections.Generic;
using Beelzebub.Config;
using Beelzebub.Services;
using HarmonyLib;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Patches;

[HarmonyPatch(typeof(VBloodSystem), nameof(VBloodSystem.OnUpdate))]
internal static class VBloodSystemPatch
{
    static readonly Dictionary<ulong, DateTime> _lastCaptureAt = new();
    static readonly TimeSpan _dedupeWindow = TimeSpan.FromSeconds(5);

    [HarmonyPrefix]
    public static void OnUpdatePrefix(VBloodSystem __instance)
    {
        if (!Core.IsReady || !Settings.CaptureOnKill.Value) return;

        Unity.Collections.NativeList<VBloodConsumed> events;
        try
        {
            events = __instance.EventList;
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] VBloodSystemPatch failed to read EventList: {ex}");
            return;
        }

        for (int i = 0; i < events.Length; i++)
        {
            try
            {
                ProcessVBloodConsumed(events[i]);
            }
            catch (Exception ex)
            {
                Core.Log.LogError($"[Beelz] VBloodConsumed Process failed: {ex}");
            }
        }
    }

    static void ProcessVBloodConsumed(VBloodConsumed evt)
    {
        Entity playerCharacter = evt.Target;
        if (!playerCharacter.IsPlayer()) return;

        ulong steamId = playerCharacter.GetSteamId();
        if (steamId == 0) return;

        var now = DateTime.UtcNow;
        if (_lastCaptureAt.TryGetValue(steamId, out var last) && now - last < _dedupeWindow)
        {
            return;
        }
        _lastCaptureAt[steamId] = now;

        PrefabGUID vBloodGuid = evt.Source;
        if (vBloodGuid._Value == 0) return;

        if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(vBloodGuid, out Entity vBloodPrefabEntity))
        {
            if (Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] V-Blood {vBloodGuid.GetPrefabName()} not found in prefab map.");
            return;
        }

        if (!Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(vBloodPrefabEntity))
        {
            if (Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] V-Blood {vBloodGuid.GetPrefabName()} has no AbilityGroupSlotBuffer.");
            return;
        }

        var slots = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(vBloodPrefabEntity);
        int captured = 0, skipped = 0;
        // v0.38.0 pity (V-Blood ability source).
        float pityAbility = Core.AbilityRegistry.GetPityBonus(steamId, CaptureSource.VBlood, PityKind.Ability);
        int abilityRolls = 0, abilityWins = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            PrefabGUID ability = slots[i].BaseAbilityGroupOnSlot;
            if (ability._Value == 0) continue;

            string abilityName = ability.GetPrefabName();
            if (!Core.AbilityFilter.ShouldCapture(abilityName, ability._Value, out string reason))
            {
                skipped++;
                if (Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz] skip (VBlood) {abilityName}: {reason}");
                continue;
            }

            // C2 + C3: per-ability override and unit-tier multiplier.
            float chance = Core.AbilityRules.TryGetRateOverride(abilityName, out _, out var orV)
                ? orV
                : Settings.DropChance_Ability_VBlood.Value;
            chance *= vBloodPrefabEntity.ResolveTierMultiplier();
            if (chance <= 0f) continue;
            chance += pityAbility;
            abilityRolls++;
            if (chance < 1f && System.Random.Shared.NextDouble() > chance) continue;

            if (Core.AbilityRegistry.Add(steamId, vBloodGuid._Value, ability._Value, CaptureSource.VBlood))
            {
                abilityWins++;
                captured++;
                string vbName = vBloodGuid.GetPrefabName();
                if (Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz] capture (VBlood) {abilityName} from {vbName} for {steamId}");
                Core.Chat.Send(playerCharacter, Verbosity.Verbose, $"Acquired V-Blood ability: {abilityName} (from {vbName}).");
                Core.Chat.SendEvent(playerCharacter,
                    $"[BEELZ:event] type=capture s=V u={vBloodGuid._Value} un={vbName} a={ability._Value} an={abilityName}");
            }
        }

        // v0.38.0 pity (ability, V-Blood source): reset on a new capture, else bump.
        if (abilityRolls > 0)
        {
            if (abilityWins > 0)
                Core.AbilityRegistry.ResetPity(steamId, CaptureSource.VBlood, PityKind.Ability);
            else
                Core.AbilityRegistry.BumpPity(steamId, CaptureSource.VBlood, PityKind.Ability,
                    Settings.Capture_PityIncrementPerKill.Value, Settings.Capture_PityMaxBonus.Value);
        }

        // V-Blood transform unlock roll (typically rarer than ability captures).
        // TX1: respect the per-unit admin kill-switch in TransformMap.
        // TX4: skip if the V-Blood's transform is Brutal-only on a Basic server.
        // AUDIT-7 (v0.20.1): gate-boss variants (e.g. CHAR_Bandit_StoneBreaker_VBlood_GateBoss_Minor)
        // skip the transform-unlock roll — see DeathEventListenerSystemPatch for full rationale.
        // Ability captures (above) are unaffected.
        string vbloodNameOuter = vBloodGuid.GetPrefabName();
        bool isGateBossVariant = !string.IsNullOrEmpty(vbloodNameOuter)
            && vbloodNameOuter.IndexOf("_GateBoss_", System.StringComparison.OrdinalIgnoreCase) >= 0;
        if (isGateBossVariant && Settings.VerboseLogging.Value)
            Core.Log.LogInfo($"[Beelz] skip V-Blood transform-roll for gate-boss variant {vbloodNameOuter}");

        float transformChance = Settings.DropChance_Transform_VBlood.Value * vBloodPrefabEntity.ResolveTierMultiplier();
        bool gotTransform = false;
        bool transformRollEligible = !isGateBossVariant
            && transformChance > 0f
            && Core.AbilityRules.IsTransformUnitEnabled(vBloodGuid._Value)
            && Beelzebub.Services.AbilityRules.IsDifficultyAllowed(
                Core.AbilityRules.GetTransformDifficulty(vBloodGuid._Value),
                Beelzebub.Services.AbilityRules.GetServerDifficulty());
        if (transformRollEligible)
        {
            // v0.38.0: apply + resolve transform pity for the V-Blood source.
            transformChance += Core.AbilityRegistry.GetPityBonus(steamId, CaptureSource.VBlood, PityKind.Transform);
            if (System.Random.Shared.NextDouble() <= transformChance)
            {
                Core.AbilityRegistry.ResetPity(steamId, CaptureSource.VBlood, PityKind.Transform);
                if (Core.AbilityRegistry.AddTransformUnlock(steamId, vBloodGuid._Value, CaptureSource.VBlood))
                {
                    gotTransform = true;
                    string unitName = vBloodGuid.GetPrefabName();
                    Core.Log.LogInfo($"[Beelz] {steamId} unlocked V-Blood transform: {unitName}.");
                    Core.Chat.Send(playerCharacter, Verbosity.Summary,
                        $"Unlocked V-Blood transformation: {unitName}!");
                    Core.Chat.SendEvent(playerCharacter,
                        $"[BEELZ:event] type=transform-unlock s=V u={vBloodGuid._Value} un={unitName}");
                }
            }
            else
            {
                Core.AbilityRegistry.BumpPity(steamId, CaptureSource.VBlood, PityKind.Transform,
                    Settings.Capture_PityIncrementPerKill.Value, Settings.Capture_PityMaxBonus.Value);
            }
        }

        if (captured > 0 || gotTransform)
        {
            if (captured > 0)
            {
                string unitName = vBloodGuid.GetPrefabName();
                Core.Log.LogInfo($"[Beelz] {steamId} defeated V-Blood {unitName}: captured {captured} ability(ies), skipped {skipped}.");
                Core.Chat.Send(playerCharacter, Verbosity.Summary,
                    captured == 1
                        ? $"Defeated V-Blood {unitName}: acquired 1 new ability."
                        : $"Defeated V-Blood {unitName}: acquired {captured} new abilities.");
            }
            Core.Persistence.RequestSave();
        }
    }
}
