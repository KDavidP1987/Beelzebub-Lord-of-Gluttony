using System.Collections.Generic;
using BepInEx.Logging;
using Beelzebub.Services;
using ProjectM;
using ProjectM.Scripting;
using Stunlock.Core;
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

    public static Dictionary<int, string> PrefabNames { get; } = new();

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
            BuildPrefabNameMap();

            IsReady = true;
            Log.LogInfo($"Beelzebub initialized via {trigger} (attempt #{_initAttempts}). Registry size: {AbilityRegistry.PlayerCount} player(s). Prefab map has {prefabSystem.SpawnableNameToPrefabGuidDictionary.Count} entries. Built reverse name map with {PrefabNames.Count} entries.");
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

    static void BuildPrefabNameMap()
    {
        PrefabNames.Clear();
        int fromEmbedded = LoadEmbeddedPrefabNames();
        int fromRuntime = 0, abEmbedded = 0, abRuntime = 0;
        try
        {
            foreach (var kvp in PrefabNames)
            {
                if (kvp.Value.StartsWith("AB_")) abEmbedded++;
            }
            var map = PrefabCollectionSystem.SpawnableNameToPrefabGuidDictionary;
            foreach (var kvp in map)
            {
                string name = kvp.Key.ToString();
                int guid = kvp.Value._Value;
                if (PrefabNames.TryAdd(guid, name))
                {
                    fromRuntime++;
                    if (name.StartsWith("AB_")) abRuntime++;
                }
            }
            Log.LogInfo($"Beelzebub: name map built. Embedded={fromEmbedded} (AB_={abEmbedded}), added from runtime={fromRuntime} (AB_={abRuntime}). Total={PrefabNames.Count}.");
        }
        catch (System.Exception ex)
        {
            Log.LogError($"Beelzebub: failed building name map (runtime merge): {ex}");
        }
    }

    static int LoadEmbeddedPrefabNames()
    {
        const string resource = "Beelzebub.Resources.prefab_names.tsv";
        try
        {
            var asm = typeof(Core).Assembly;
            using var stream = asm.GetManifestResourceStream(resource);
            if (stream is null)
            {
                Log.LogWarning($"Beelzebub: embedded resource '{resource}' not found.");
                return 0;
            }
            using var reader = new System.IO.StreamReader(stream);
            int count = 0;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                int tab = line.IndexOf('\t');
                if (tab <= 0) continue;
                if (!int.TryParse(line.AsSpan(0, tab), out int guid)) continue;
                PrefabNames[guid] = line.Substring(tab + 1);
                count++;
            }
            return count;
        }
        catch (System.Exception ex)
        {
            Log.LogError($"Beelzebub: failed loading embedded prefab names: {ex}");
            return 0;
        }
    }
}
