using System;
using Beelzebub.Services;
using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using EnterShapeshiftEvent = ProjectM.Network.EnterShapeshiftEvent;

namespace Beelzebub.Patches;

/// <summary>
/// v0.26.0 — auto-stash summons when a transformed player enters bat form.
///
/// Bat-form travel is the same problem as waygate travel: V Rising changes the
/// player's form/position and doesn't carry NPC followers along, so an active
/// summon horde gets left behind (or blocks the form via the "subdued enemies"
/// check). We mirror Bloodcraft's <c>ShapeshiftSystemPatch</c> exactly — hook
/// <c>ShapeshiftSystem.OnUpdate</c> Prefix, watch the <c>EnterShapeshiftEvent</c>
/// for <c>AB_Shapeshift_Bat_Group</c>, and stash the player's summons.
///
/// Restore happens on landing: <c>BuffSpawnServerPatch</c> watches for
/// <c>AB_Shapeshift_Bat_Landing_Travel</c> (-371745443) and calls the same
/// RestoreAll path used for waygate arrival. (Bloodcraft restores via its
/// AutoCallMap consumed at the bat-landing buff; we restore directly since our
/// stash state lives on the ActiveTransform.)
///
/// Bloodcraft also stashes on other surfaces (DominatingPresence/Psychic form,
/// the recall emote, combat entry). Those are not implemented here — bat form
/// is the requested case and the only travel-style form that strands summons.
///
/// v0.86.0 (Bug A): this hook ALSO drives custom-ability form ENTRY now. The form-state buff
/// never appears in <c>BuffSystem_Spawn_Server.EntityQueries[0]</c> (confirmed in-game: form
/// entry injected nothing while the exit hook fired every time), so the old BuffSpawnServer
/// detection silently no-op'd. <c>EnterShapeshiftEvent</c> is the reliable enter signal — we
/// record a pending loadout-apply (<see cref="ShapeshiftAbilityService.NotifyEnterShapeshift"/>)
/// and the heartbeat injects once the form buff materializes. This runs independent of the
/// summons-ally setting (it's gated by <c>Forms_CustomAbilities_Enabled</c> internally).
/// </summary>
[HarmonyPatch(typeof(ShapeshiftSystem), nameof(ShapeshiftSystem.OnUpdate))]
internal static class ShapeshiftSystemPatch
{
    // AB_Shapeshift_Bat_Group — entering this form triggers a stash.
    static readonly PrefabGUID BatForm = new(-104327922);

    [HarmonyPrefix]
    public static void OnUpdatePrefix(ShapeshiftSystem __instance)
    {
        if (!Core.IsReady) return;

        NativeArray<Entity> entities;
        try
        {
            entities = __instance._Query.ToEntityArray(Allocator.Temp);
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz FORM] ShapeshiftSystem query failed: {ex.Message}");
            return;
        }

        bool summonsAllies = Beelzebub.Config.Settings.Transform_SummonsAreAllies.Value;

        try
        {
            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];
                if (!e.TryGetComponent<FromCharacter>(out var fromChar)) continue;
                if (!e.TryGetComponent<EnterShapeshiftEvent>(out var evt)) continue;

                Entity player = fromChar.Character;
                if (!player.Exists() || !player.IsPlayer()) continue;
                ulong steamId = player.GetSteamId();
                if (steamId == 0) continue;

                // v0.86.0 (Bug A): record a pending custom-ability form-loadout apply on ANY shapeshift
                // entry. Gated internally by Forms_CustomAbilities_Enabled; the injection happens in the
                // heartbeat once the form buff appears. Independent of the summons-ally setting.
                try { Services.ShapeshiftAbilityService.NotifyEnterShapeshift(player); }
                catch (Exception ex) { Core.Log.LogWarning($"[Beelz FORM] NotifyEnterShapeshift failed for {steamId}: {ex.Message}"); }

                // --- Bat-form summon stash (requires the summons-ally system) ---
                if (!summonsAllies || Core.AbilityRegistry is null) continue;
                if (evt.Shapeshift._Value != BatForm._Value) continue;

                var active = Core.AbilityRegistry.GetSummonOwner(steamId, createIfMissing: false);
                if (active == null) continue;
                if (active.SummonedMinions == null || active.SummonedMinions.Count == 0) continue;

                try
                {
                    int stashed = SummonAllyService.StashAll(active, player);
                    if (stashed > 0)
                    {
                        active.SummonsDisabled = true;
                        Core.Log.LogInfo($"[Beelz SUMMON] auto-stash on bat form: stashed {stashed} for player {steamId}. Will restore on landing.");
                    }
                }
                catch (Exception ex)
                {
                    Core.Log.LogError($"[Beelz SUMMON] bat-form stash failed for {steamId}: {ex}");
                }
            }
        }
        finally
        {
            entities.Dispose();
        }
    }
}
