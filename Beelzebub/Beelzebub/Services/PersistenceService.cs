using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx;

namespace Beelzebub.Services;

internal sealed class PersistenceService
{
    static readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    static readonly TimeSpan _saveDebounceWindow = TimeSpan.FromSeconds(1);

    public string StateFilePath { get; }

    int _dirty;          // 0 = clean, 1 = save requested
    DateTime _lastSavedUtc = DateTime.MinValue;

    public PersistenceService()
    {
        var dir = Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_GUID);
        Directory.CreateDirectory(dir);
        StateFilePath = Path.Combine(dir, "state.json");
    }

    /// <summary>
    /// Mark state as needing a save. The actual write is deferred to the next
    /// per-frame tick at most once per debounce window.
    /// </summary>
    public void RequestSave() => System.Threading.Interlocked.Exchange(ref _dirty, 1);

    /// <summary>
    /// Called from the per-frame tick. Performs a save if state is dirty and
    /// the debounce window has elapsed since the last write.
    /// </summary>
    public void MaybeSave()
    {
        if (System.Threading.Volatile.Read(ref _dirty) == 0) return;
        if (DateTime.UtcNow - _lastSavedUtc < _saveDebounceWindow) return;
        SaveSync();
    }

    public void LoadInto(AbilityRegistry registry)
    {
        if (!File.Exists(StateFilePath))
        {
            Core.Log.LogInfo($"No persisted state at {StateFilePath} — starting fresh.");
            return;
        }

        try
        {
            var json = File.ReadAllText(StateFilePath);
            var dto = JsonSerializer.Deserialize<StateDto>(json, _json);
            if (dto?.Players is null) return;

            var snapshot = dto.Players.Select(kv =>
                new KeyValuePair<ulong, IReadOnlyList<CapturedAbility>>(
                    ulong.Parse(kv.Key),
                    kv.Value.Captured.Select(c => new CapturedAbility(c.Unit, c.Ability, MapSource(c.Source))).ToList()));

            registry.LoadFromSnapshot(snapshot);

            int slotCount = 0;
            int verbositySet = 0;
            int transformCount = 0;
            foreach (var (key, player) in dto.Players)
            {
                ulong steamId = ulong.Parse(key);
                if (player.Slots is not null)
                {
                    foreach (var (slotStr, abilityGuid) in player.Slots)
                    {
                        if (int.TryParse(slotStr, out int slot))
                        {
                            registry.SetSlot(steamId, slot, abilityGuid);
                            slotCount++;
                        }
                    }
                }
                if (player.Verbosity.HasValue)
                {
                    registry.SetVerbosity(steamId, (Verbosity)player.Verbosity.Value);
                    verbositySet++;
                }
                if (player.Transforms is not null)
                {
                    foreach (var t in player.Transforms)
                    {
                        registry.AddTransformUnlock(steamId, t.Unit, MapSource(t.Source));
                        transformCount++;
                    }
                }
            }

            Core.Log.LogInfo($"Loaded {registry.PlayerCount} player(s), {slotCount} slot(s), {verbositySet} verbosity, {transformCount} transform unlock(s) from {StateFilePath}.");
        }
        catch (Exception e)
        {
            Core.Log.LogError($"Failed to load state from {StateFilePath}: {e}");
        }
    }

    public void SaveSync()
    {
        if (Core.AbilityRegistry is null) return;
        try
        {
            var allVerbosity = Core.AbilityRegistry.AllVerbosity();
            var transformSnapshot = Core.AbilityRegistry.TransformSnapshot()
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            var playerIds = new HashSet<ulong>(Core.AbilityRegistry.Snapshot().Select(kv => kv.Key));
            foreach (var sid in allVerbosity.Keys) playerIds.Add(sid);
            foreach (var sid in transformSnapshot.Keys) playerIds.Add(sid);

            var players = new Dictionary<string, PlayerDto>();
            foreach (var steamId in playerIds)
            {
                var captured = Core.AbilityRegistry.ListFor(steamId);
                var slots = Core.AbilityRegistry.GetSlots(steamId);
                allVerbosity.TryGetValue(steamId, out var verbosity);
                transformSnapshot.TryGetValue(steamId, out var transforms);
                players[steamId.ToString()] = new PlayerDto
                {
                    Captured = captured.Select(c => new CapturedDto
                    {
                        Unit = c.UnitPrefabGuid,
                        Ability = c.AbilityPrefabGuid,
                        Source = (byte)c.Source,
                    }).ToList(),
                    Slots = slots.ToDictionary(s => s.Key.ToString(), s => s.Value),
                    Verbosity = allVerbosity.ContainsKey(steamId) ? (byte?)verbosity : null,
                    Transforms = transforms?.Select(t => new TransformDto
                    {
                        Unit = t.UnitPrefabGuid,
                        Source = (byte)t.Source,
                    }).ToList(),
                };
            }

            var dto = new StateDto
            {
                Version = 4,
                Players = players,
            };

            var json = JsonSerializer.Serialize(dto, _json);
            var tmp = StateFilePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(StateFilePath)) File.Replace(tmp, StateFilePath, null);
            else File.Move(tmp, StateFilePath);
            _lastSavedUtc = DateTime.UtcNow;
            System.Threading.Interlocked.Exchange(ref _dirty, 0);
        }
        catch (Exception e)
        {
            Core.Log.LogError($"Failed to save state to {StateFilePath}: {e}");
        }
    }

    sealed class StateDto
    {
        public int Version { get; set; }
        public Dictionary<string, PlayerDto> Players { get; set; }
    }

    sealed class PlayerDto
    {
        public List<CapturedDto> Captured { get; set; } = new();
        public Dictionary<string, int> Slots { get; set; }
        public byte? Verbosity { get; set; }
        public List<TransformDto> Transforms { get; set; }
    }

    sealed class TransformDto
    {
        public int Unit { get; set; }
        public byte Source { get; set; }
    }

    sealed class CapturedDto
    {
        public int Unit { get; set; }
        public int Ability { get; set; }
        public byte Source { get; set; } // 0=Regular, 1=VBlood. Missing in v1 state.json → defaults to 0/Regular.
    }

    static CaptureSource MapSource(byte raw) => raw switch
    {
        (byte)CaptureSource.VBlood => CaptureSource.VBlood,
        _ => CaptureSource.Regular,
    };
}
