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

        int captured = 0, skipped = 0;
        // v0.43.23: friendly in-game name for player-facing chat (the [BEELZ:event]
        // wire lines keep the raw, space-free prefab name for BCH parsing).
        string vbDisplay = FriendlyUnit(vBloodGuid);
        // v0.38.0 pity (V-Blood ability source).
        float pityAbility = Core.AbilityRegistry.GetPityBonus(steamId, CaptureSource.VBlood, PityKind.Ability);
        int abilityRolls = 0, abilityWins = 0;
        // v0.99.0: roll against the unit's FULL cross-phase kit (base bar ∪ metadata reverse-map ∪ curated
        // transform-form sets) instead of just the prefab's base/phase-1 bar — so phase-gated abilities are
        // capturable from any kill. FullEligibleKit already applies ShouldCapture; the re-check below is a
        // harmless belt-and-braces that also yields the skip reason for the verbose log.
        foreach (int abilityVal in Services.UnitKitService.FullEligibleKit(vBloodGuid._Value))
        {
            PrefabGUID ability = new PrefabGUID(abilityVal);
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
                Core.Chat.Send(playerCharacter, Verbosity.Verbose, $"Acquired V-Blood ability: {FriendlyAbility(ability)} (from {vbDisplay}).");
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

        // v0.44.0 — jackpot roll (rarer than per-ability captures). TIER-1 bosses
        // (Dracula/Morgana) are the only units the client can render as a real form, so they
        // keep the "transform unlock". EVERY OTHER V-Blood is DEVOURED on the jackpot — all
        // of its eligible abilities granted at once into the player's pool (per-ability
        // baseline; arbitrary-unit transformation is a postponed phase-two feature).
        // AUDIT-7: gate-boss variants skip the jackpot (full kit off an easy copy trivializes
        // the real fight). Per-ability captures above are unaffected.
        string vbloodNameOuter = vBloodGuid.GetPrefabName();
        bool isTier1 = BossFormRegistry.Has(vBloodGuid._Value);
        bool isGateBossVariant = !string.IsNullOrEmpty(vbloodNameOuter)
            && vbloodNameOuter.IndexOf("_GateBoss_", System.StringComparison.OrdinalIgnoreCase) >= 0;

        bool gotJackpot = false;
        float tierMult = vBloodPrefabEntity.ResolveTierMultiplier();

        // v0.98.0 — THREE INDEPENDENT REWARDS. Per-ability captures already happened above (Ability pity).
        // Now roll the DEVOUR jackpot and the TRANSFORMATION unlock SEPARATELY, each with its own chance +
        // pity track. A transform boss can therefore be devoured (whole kit at once) AND, on a rarer roll,
        // unlock its form — and a Devour NEVER hands out the form for free. A non-transform unit rolls only
        // Devour. This is what gates transformations as their own hard-won prize.

        // --- DEVOUR roll (all units except gate-boss variants — an easy copy shouldn't hand the full kit). ---
        float devourChance = Settings.DropChance_Devour_VBlood.Value * tierMult;
        if (!isGateBossVariant && devourChance > 0f)
        {
            devourChance += Core.AbilityRegistry.GetPityBonus(steamId, CaptureSource.VBlood, PityKind.Devour);
            if (System.Random.Shared.NextDouble() <= devourChance)
            {
                Core.AbilityRegistry.ResetPity(steamId, CaptureSource.VBlood, PityKind.Devour);
                int learned = DevourService.Devour(steamId, vBloodGuid, CaptureSource.VBlood);
                gotJackpot = true;
                Core.Log.LogInfo($"[Beelz] {steamId} DEVOURED V-Blood {vbloodNameOuter}: granted {learned} new ability(ies).");
                Core.Chat.Send(playerCharacter, Verbosity.Summary,
                    learned > 0
                        ? $"⭐ DEVOURED {vbDisplay} — learned all {learned} of its abilities at once! Slot them with .beelz grant."
                        : $"⭐ DEVOURED {vbDisplay} — you already knew all of its abilities.");
                Core.Chat.SendEvent(playerCharacter,
                    $"[BEELZ:event] type=devour s=V u={vBloodGuid._Value} un={vbloodNameOuter} count={learned}");
                SummonRegistry.GrantAndNotify(playerCharacter, steamId, vBloodGuid, CaptureSource.VBlood);
            }
            else
            {
                Core.AbilityRegistry.BumpPity(steamId, CaptureSource.VBlood, PityKind.Devour,
                    Settings.Capture_PityIncrement_Devour.Value, Settings.Capture_PityMax_Devour.Value);
            }
        }

        // --- TRANSFORMATION roll (only registered transform bosses; own chance + pity; the rarest prize).
        //     The gate-boss exclusion does NOT apply here — the Werewolf Chieftain ships only as a gate-boss
        //     variant and we still want its transform. Honors the transform admin/difficulty gates. ---
        bool txEligible = isTier1
            && Core.AbilityRules.IsTransformUnitEnabled(vBloodGuid._Value)
            && Beelzebub.Services.AbilityRules.IsDifficultyAllowed(
                Core.AbilityRules.GetTransformDifficulty(vBloodGuid._Value),
                Beelzebub.Services.AbilityRules.GetServerDifficulty());
        float txChance = Settings.DropChance_TransformUnlock_VBlood.Value * tierMult;
        if (txEligible && txChance > 0f)
        {
            txChance += Core.AbilityRegistry.GetPityBonus(steamId, CaptureSource.VBlood, PityKind.Transform);
            if (System.Random.Shared.NextDouble() <= txChance)
            {
                Core.AbilityRegistry.ResetPity(steamId, CaptureSource.VBlood, PityKind.Transform);
                if (Core.AbilityRegistry.AddTransformUnlock(steamId, vBloodGuid._Value, CaptureSource.VBlood))
                {
                    gotJackpot = true;
                    Core.Log.LogInfo($"[Beelz] {steamId} unlocked TRANSFORMATION: {vbloodNameOuter}.");
                    Core.Chat.Send(playerCharacter, Verbosity.Summary,
                        $"⭐ Unlocked TRANSFORMATION: {vbDisplay}! Use .beelz transform {vbDisplay}.");
                    Core.Chat.SendEvent(playerCharacter,
                        $"[BEELZ:event] type=transform-unlock s=V u={vBloodGuid._Value} un={vbloodNameOuter}");
                    SummonRegistry.GrantAndNotify(playerCharacter, steamId, vBloodGuid, CaptureSource.VBlood);
                }
            }
            else
            {
                Core.AbilityRegistry.BumpPity(steamId, CaptureSource.VBlood, PityKind.Transform,
                    Settings.Capture_PityIncrement_Transform.Value, Settings.Capture_PityMax_Transform.Value);
            }
        }

        if (captured > 0 || gotJackpot)
        {
            if (captured > 0)
            {
                string unitName = vBloodGuid.GetPrefabName();
                Core.Log.LogInfo($"[Beelz] {steamId} defeated V-Blood {unitName}: captured {captured} ability(ies), skipped {skipped}.");
                Core.Chat.Send(playerCharacter, Verbosity.Summary,
                    captured == 1
                        ? $"Defeated V-Blood {vbDisplay}: acquired 1 new ability."
                        : $"Defeated V-Blood {vbDisplay}: acquired {captured} new abilities.");
            }
            Core.Persistence.RequestSave();
        }
    }

    /// <summary>v0.43.23: friendly in-game unit name for chat (falls back to the raw prefab name).</summary>
    static string FriendlyUnit(PrefabGUID unit) =>
        Core.AbilityMetadata?.ResolveUnitName(unit._Value) ?? unit.GetPrefabName();

    /// <summary>v0.43.23: friendly in-game ability name for chat (falls back to the raw prefab name).</summary>
    static string FriendlyAbility(PrefabGUID ability)
    {
        var i = Core.AbilityMetadata?.Resolve(ability._Value);
        return (i != null && !string.IsNullOrEmpty(i.Name)) ? i.Name : ability.GetPrefabName();
    }
}
