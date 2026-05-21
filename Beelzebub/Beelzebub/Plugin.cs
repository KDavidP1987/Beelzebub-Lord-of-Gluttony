using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using VampireCommandFramework;

namespace Beelzebub;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("gg.deca.VampireCommandFramework")]
public class Plugin : BasePlugin
{
    internal static Harmony Harmony;
    internal static ManualLogSource Logger;

    public override void Load()
    {
        if (Application.productName != "VRisingServer")
            return;

        Logger = Log;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} loaded.");

        Harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        Harmony.PatchAll(System.Reflection.Assembly.GetExecutingAssembly());

        CommandRegistry.RegisterAll();
    }

    public override bool Unload()
    {
        CommandRegistry.UnregisterAssembly();
        Harmony?.UnpatchSelf();
        return true;
    }
}
