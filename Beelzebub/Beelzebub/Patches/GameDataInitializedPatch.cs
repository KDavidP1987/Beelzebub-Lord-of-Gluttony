using HarmonyLib;
using ProjectM;

namespace Beelzebub.Patches;

[HarmonyPatch(typeof(SpawnTeamSystem_OnPersistenceLoad), nameof(SpawnTeamSystem_OnPersistenceLoad.OnUpdate))]
internal static class GameDataInitializedPatch
{
    [HarmonyPostfix]
    public static void OneShotInit()
    {
        Core.InitializeAfterLoaded();
        Plugin.Harmony.Unpatch(
            typeof(SpawnTeamSystem_OnPersistenceLoad).GetMethod(nameof(SpawnTeamSystem_OnPersistenceLoad.OnUpdate)),
            typeof(GameDataInitializedPatch).GetMethod(nameof(OneShotInit)));
    }
}
