using System.Collections.Generic;
using BepInEx.Logging;
using Beelzebub.Services;
using ProjectM;
using ProjectM.Gameplay.Systems;
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
    public static DebugEventsSystem DebugEventsSystem { get; private set; }
    public static ReplaceAbilityOnSlotSystem ReplaceAbilityOnSlotSystem { get; private set; }
    public static ServerGameManager ServerGameManager => ServerScriptMapper.GetServerGameManager();

    public static AbilityRegistry AbilityRegistry { get; private set; }
    public static AbilityFilter AbilityFilter { get; private set; }
    public static AbilityRules AbilityRules { get; private set; }
    public static AbilityMetadataService AbilityMetadata { get; private set; }
    public static ChatNotifier Chat { get; private set; }
    public static TransformService Transforms { get; private set; }
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
            DebugEventsSystem = server.GetExistingSystemManaged<DebugEventsSystem>();
            ReplaceAbilityOnSlotSystem = server.GetExistingSystemManaged<ReplaceAbilityOnSlotSystem>();

            Persistence = new PersistenceService();
            AbilityRules = new AbilityRules();
            AbilityRules.Load();
            AbilityMetadata = new AbilityMetadataService();
            AbilityMetadata.Load();
            AbilityRegistry = new AbilityRegistry();
            AbilityFilter = new AbilityFilter();
            Chat = new ChatNotifier();
            Transforms = new TransformService();
            Persistence.LoadInto(AbilityRegistry);
            BuildPrefabNameMap();

            // v0.43.4: backfill signature summons for units players unlocked before this
            // build, so the standalone-summon feature applies retroactively. Idempotent.
            try
            {
                int backfilled = Services.SummonRegistry.BackfillAll();
                if (backfilled > 0) Log.LogInfo($"[Beelz] backfilled {backfilled} signature-summon ability(ies) into existing transform unlocks.");
            }
            catch (System.Exception ex) { Log.LogWarning($"[Beelz] signature-summon backfill failed: {ex.Message}"); }

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
