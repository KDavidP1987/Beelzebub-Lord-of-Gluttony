using BepInEx.Logging;
using Beelzebub.Services;
using ProjectM;
using ProjectM.Scripting;
using Unity.Entities;

namespace Beelzebub;

internal static class Core
{
    public static World Server { get; private set; }
    public static EntityManager EntityManager { get; private set; }
    public static PrefabCollectionSystem PrefabCollectionSystem { get; private set; }
    public static ServerScriptMapper ServerScriptMapper { get; private set; }
    public static ServerGameManager ServerGameManager => ServerScriptMapper.GetServerGameManager();

    public static AbilityRegistry AbilityRegistry { get; private set; }
    public static AbilityFilter AbilityFilter { get; private set; }
    public static PersistenceService Persistence { get; private set; }

    public static ManualLogSource Log => Plugin.PluginLog;
    public static bool IsReady { get; private set; }

    internal static void InitializeAfterLoaded()
    {
        if (IsReady) return;

        Server = FindServerWorld();
        if (Server is null)
        {
            Log.LogError("Beelzebub init: Server world not found. Aborting.");
            return;
        }

        EntityManager = Server.EntityManager;
        PrefabCollectionSystem = Server.GetExistingSystemManaged<PrefabCollectionSystem>();
        ServerScriptMapper = Server.GetExistingSystemManaged<ServerScriptMapper>();

        Persistence = new PersistenceService();
        AbilityRegistry = new AbilityRegistry();
        AbilityFilter = new AbilityFilter();

        Persistence.LoadInto(AbilityRegistry);

        IsReady = true;
        Log.LogInfo($"Beelzebub initialized. Registry size: {AbilityRegistry.PlayerCount} player(s).");
    }

    static World FindServerWorld()
    {
        foreach (var world in World.s_AllWorlds)
        {
            if (world.Name == "Server") return world;
        }
        return null;
    }
}
