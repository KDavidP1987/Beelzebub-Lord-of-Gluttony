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

    public string StateFilePath { get; }

    public PersistenceService()
    {
        var dir = Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_GUID);
        Directory.CreateDirectory(dir);
        StateFilePath = Path.Combine(dir, "state.json");
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
            foreach (var (key, player) in dto.Players)
            {
                if (player.Slots is null) continue;
                ulong steamId = ulong.Parse(key);
                foreach (var (slotStr, abilityGuid) in player.Slots)
                {
                    if (int.TryParse(slotStr, out int slot))
                    {
                        registry.SetSlot(steamId, slot, abilityGuid);
                        slotCount++;
                    }
                }
            }

            Core.Log.LogInfo($"Loaded {registry.PlayerCount} player(s) and {slotCount} slot assignment(s) from {StateFilePath}.");
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
            var dto = new StateDto
            {
                Version = 2,
                Players = Core.AbilityRegistry.Snapshot().ToDictionary(
                    kv => kv.Key.ToString(),
                    kv => new PlayerDto
                    {
                        Captured = kv.Value.Select(c => new CapturedDto
                        {
                            Unit = c.UnitPrefabGuid,
                            Ability = c.AbilityPrefabGuid,
                            Source = (byte)c.Source,
                        }).ToList(),
                        Slots = Core.AbilityRegistry.GetSlots(kv.Key).ToDictionary(s => s.Key.ToString(), s => s.Value)
                    })
            };

            var json = JsonSerializer.Serialize(dto, _json);
            var tmp = StateFilePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(StateFilePath)) File.Replace(tmp, StateFilePath, null);
            else File.Move(tmp, StateFilePath);
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
