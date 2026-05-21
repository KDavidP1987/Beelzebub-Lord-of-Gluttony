using HarmonyLib;
using ProjectM;

namespace Beelzebub.Patches;

[HarmonyPatch(typeof(SpawnTeamSystem_OnPersistenceLoad), nameof(SpawnTeamSystem_OnPersistenceLoad.OnUpdate))]
internal static class GameDataInitializedPatch
{
    [HarmonyPostfix]
    public static void OneShotInit()
    {
        Core.TryInitialize(nameof(GameDataInitializedPatch));
    }
}
