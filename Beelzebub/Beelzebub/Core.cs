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

    static bool _initInProgress;
    static int _initAttempts;

    internal static void InitializeAfterLoaded() => TryInitialize("InitializeAfterLoaded");

    internal static void TryInitialize(string trigger)
    {
        if (IsReady || _initInProgress) return;
        _initInProgress = true;
        _initAttempts++;
        try
        {
            var server = FindServerWorld();
            if (server is null)
            {
                if (_initAttempts == 1)
                    Log.LogInfo($"Beelzebub init ({trigger}): Server world not yet present; will retry.");
                return;
            }

            var prefabSystem = server.GetExistingSystemManaged<PrefabCollectionSystem>();
            if (prefabSystem is null || prefabSystem.SpawnableNameToPrefabGuidDictionary.Count == 0)
            {
                if (_initAttempts == 1)
                    Log.LogInfo($"Beelzebub init ({trigger}): PrefabCollectionSystem not yet populated; will retry.");
                return;
            }

            Server = server;
            EntityManager = server.EntityManager;
            PrefabCollectionSystem = prefabSystem;
            ServerScriptMapper = server.GetExistingSystemManaged<ServerScriptMapper>();

            Persistence = new PersistenceService();
            AbilityRegistry = new AbilityRegistry();
            AbilityFilter = new AbilityFilter();
            Persistence.LoadInto(AbilityRegistry);

            IsReady = true;
            Log.LogInfo($"Beelzebub initialized via {trigger} (attempt #{_initAttempts}). Registry size: {AbilityRegistry.PlayerCount} player(s). Prefab map has {prefabSystem.SpawnableNameToPrefabGuidDictionary.Count} entries.");
        }
        catch (System.Exception ex)
        {
            Log.LogError($"Beelzebub init ({trigger}) FAILED on attempt #{_initAttempts}: {ex}");
        }
        finally
        {
            _initInProgress = false;
        }
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
