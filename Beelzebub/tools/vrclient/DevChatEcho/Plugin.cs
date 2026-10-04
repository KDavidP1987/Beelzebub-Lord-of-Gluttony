using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using VampireCommandFramework;

namespace DevChatEcho;

// DEV-ONLY (tools/vrclient): echoes every chat-command reply into LogOutput.log as
//   [CHAT> <character>] <text>
// so an automated in-game test reads replies exactly from the log instead of through screen OCR.
//
// It hooks VampireCommandFramework's ChatCommandContext.Reply(string) — a managed method, so the full C# string
// arrives intact, for EVERY mod that answers commands through VCF. (Hooking the IL2CPP
// ServerChatUtils.SendSystemMessageToClient was tried first: the ref FixedString512Bytes argument arrives
// truncated to its first 8 bytes through the IL2CPP detour, so the text is unusable.) Messages a mod sends
// directly with SendSystemMessageToClient (broadcasts, notifications) are therefore NOT echoed — check those
// with on-screen OCR (vrclient expect-chat). Read-only: it never changes or blocks a reply.
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("gg.deca.VampireCommandFramework")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource Logger;
    Harmony _harmony;

    public override void Load()
    {
        if (Application.productName != "VRisingServer") return;
        Logger = Log;
        _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo($"DevChatEcho loaded: {System.Linq.Enumerable.Count(_harmony.GetPatchedMethods())} method(s) patched - command replies echo as [CHAT> name] lines.");
    }

    public override bool Unload() { _harmony?.UnpatchSelf(); return true; }
}

[HarmonyPatch(typeof(ChatCommandContext), nameof(ChatCommandContext.Reply))]
static class ReplyPatch
{
    static void Prefix(ChatCommandContext __instance, string v)
    {
        try
        {
            string name = __instance?.Event != null ? __instance.Event.User.CharacterName.ToString() : "?";
            Plugin.Logger?.LogInfo($"[CHAT> {name}] {(v ?? "").Replace("\n", " \\n ")}");
        }
        catch { /* never break a reply */ }
    }
}
