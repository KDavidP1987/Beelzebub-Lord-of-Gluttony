using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Beelzebub.Config;
using HarmonyLib;
using ProjectM;
using UnityEngine;
using VampireCommandFramework;

namespace Beelzebub;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("gg.deca.VampireCommandFramework")]
public class Plugin : BasePlugin
{
    internal static Harmony Harmony;
    internal static ManualLogSource PluginLog;
    internal static Plugin Instance { get; private set; }

    public override void Load()
    {
        if (Application.productName != "VRisingServer") return;

        Instance = this;
        PluginLog = Log;
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} loading...");

        Settings.Initialize(Config);

        Harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        Harmony.PatchAll(System.Reflection.Assembly.GetExecutingAssembly());

        CommandRegistry.RegisterAll();

        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} loaded. Awaiting game data init.");
    }

    public override bool Unload()
    {
        CommandRegistry.UnregisterAssembly();
        Core.Persistence?.SaveSync();
        Harmony?.UnpatchSelf();
        return true;
    }

    public void OnGameInitialized()
    {
        if (Core.Server is null && !HasGameDataLoaded())
        {
            Log.LogDebug("OnGameInitialized fired but PrefabCollectionSystem is empty; deferring to InitializationPatch.");
            return;
        }
        Core.InitializeAfterLoaded();
    }

    static bool HasGameDataLoaded()
    {
        foreach (var world in Unity.Entities.World.s_AllWorlds)
        {
            if (world.Name != "Server") continue;
            var collection = world.GetExistingSystemManaged<PrefabCollectionSystem>();
            return collection?.SpawnableNameToPrefabGuidDictionary.Count > 0;
        }
        return false;
    }
}
