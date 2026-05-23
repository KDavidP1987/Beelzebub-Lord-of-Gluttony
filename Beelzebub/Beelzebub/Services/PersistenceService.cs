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
            int weaponSlotCount = 0;
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
                // W3 / state.json v5: per-weapon-family slot bindings.
                if (player.WeaponSlots is not null)
                {
                    var perWeapon = new Dictionary<WeaponFamily, Dictionary<int, int>>();
                    foreach (var (weaponStr, slotMap) in player.WeaponSlots)
                    {
                        if (!Enum.TryParse<WeaponFamily>(weaponStr, ignoreCase: true, out var weapon)) continue;
                        var slots = new Dictionary<int, int>();
                        foreach (var (slotStr, abilityGuid) in slotMap)
                        {
                            if (int.TryParse(slotStr, out int slot)) slots[slot] = abilityGuid;
                        }
                        if (slots.Count > 0) perWeapon[weapon] = slots;
                        weaponSlotCount += slots.Count;
                    }
                    if (perWeapon.Count > 0)
                    {
                        registry.LoadWeaponSlotsSnapshot(steamId, perWeapon);
                    }
                }
                if (player.Verbosity.HasValue)
                {
                    registry.SetVerbosity(steamId, (Verbosity)player.Verbosity.Value);
                    verbositySet++;
                }
                if (player.EmitApiEvents == true)
                {
                    registry.SetEmitApiEvents(steamId, true);
                }
                if (player.Transforms is not null)
                {
                    foreach (var t in player.Transforms)
                    {
                        registry.AddTransformUnlock(steamId, t.Unit, MapSource(t.Source));
                        transformCount++;
                    }
                }
                if (player.Presets is not null)
                {
                    // Deserialize per-name slot maps; keys arrive as strings via JSON, convert back.
                    var converted = new Dictionary<string, Dictionary<int, int>>();
                    foreach (var (name, slotMap) in player.Presets)
                    {
                        var dict = new Dictionary<int, int>();
                        foreach (var (slotStr, abilityGuid) in slotMap)
                        {
                            if (int.TryParse(slotStr, out int slot)) dict[slot] = abilityGuid;
                        }
                        converted[name] = dict;
                    }
                    registry.LoadPresetsSnapshot(steamId, converted);
                }
                // W4 / v6: named hotkey bindings.
                if (player.Hotkeys is not null && player.Hotkeys.Count > 0)
                {
                    registry.LoadHotkeysSnapshot(steamId, new Dictionary<string, int>(player.Hotkeys));
                }
            }

            Core.Log.LogInfo($"Loaded {registry.PlayerCount} player(s), {slotCount} universal slot(s), {weaponSlotCount} weapon-specific slot(s), {verbositySet} verbosity, {transformCount} transform unlock(s) from {StateFilePath}.");
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
            var allEmitEvents = Core.AbilityRegistry.AllEmitApiEvents();
            var transformSnapshot = Core.AbilityRegistry.TransformSnapshot()
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            var presetsSnapshot = Core.AbilityRegistry.PresetsSnapshot();
            var weaponSlotsSnapshot = Core.AbilityRegistry.WeaponSlotsSnapshot();
            var hotkeysSnapshot = Core.AbilityRegistry.HotkeysSnapshot();

            var playerIds = new HashSet<ulong>(Core.AbilityRegistry.Snapshot().Select(kv => kv.Key));
            foreach (var sid in allVerbosity.Keys) playerIds.Add(sid);
            foreach (var sid in allEmitEvents.Keys) playerIds.Add(sid);
            foreach (var sid in transformSnapshot.Keys) playerIds.Add(sid);
            foreach (var sid in presetsSnapshot.Keys) playerIds.Add(sid);
            foreach (var sid in weaponSlotsSnapshot.Keys) playerIds.Add(sid);
            foreach (var sid in hotkeysSnapshot.Keys) playerIds.Add(sid);

            var players = new Dictionary<string, PlayerDto>();
            foreach (var steamId in playerIds)
            {
                var captured = Core.AbilityRegistry.ListFor(steamId);
                var slots = Core.AbilityRegistry.GetSlots(steamId);
                allVerbosity.TryGetValue(steamId, out var verbosity);
                transformSnapshot.TryGetValue(steamId, out var transforms);
                Dictionary<string, Dictionary<string, int>> presetsForPlayer = null;
                if (presetsSnapshot.TryGetValue(steamId, out var presetsRaw) && presetsRaw.Count > 0)
                {
                    presetsForPlayer = new Dictionary<string, Dictionary<string, int>>();
                    foreach (var (name, slotMap) in presetsRaw)
                    {
                        presetsForPlayer[name] = slotMap.ToDictionary(s => s.Key.ToString(), s => s.Value);
                    }
                }

                // W3: serialize per-weapon-family slot bindings. Outer key = enum name.
                Dictionary<string, Dictionary<string, int>> weaponSlotsForPlayer = null;
                if (weaponSlotsSnapshot.TryGetValue(steamId, out var weaponSlotsRaw) && weaponSlotsRaw.Count > 0)
                {
                    weaponSlotsForPlayer = new Dictionary<string, Dictionary<string, int>>();
                    foreach (var (weapon, slotMap) in weaponSlotsRaw)
                    {
                        weaponSlotsForPlayer[weapon.ToString()] = slotMap.ToDictionary(s => s.Key.ToString(), s => s.Value);
                    }
                }
                // W4: named hotkeys, name → ability guid.
                Dictionary<string, int> hotkeysForPlayer = null;
                if (hotkeysSnapshot.TryGetValue(steamId, out var hotkeysRaw) && hotkeysRaw.Count > 0)
                {
                    hotkeysForPlayer = new Dictionary<string, int>(hotkeysRaw);
                }

                players[steamId.ToString()] = new PlayerDto
                {
                    Captured = captured.Select(c => new CapturedDto
                    {
                        Unit = c.UnitPrefabGuid,
                        Ability = c.AbilityPrefabGuid,
                        Source = (byte)c.Source,
                    }).ToList(),
                    Slots = slots.ToDictionary(s => s.Key.ToString(), s => s.Value),
                    WeaponSlots = weaponSlotsForPlayer,
                    Verbosity = allVerbosity.ContainsKey(steamId) ? (byte?)verbosity : null,
                    EmitApiEvents = allEmitEvents.TryGetValue(steamId, out var e) && e ? true : (bool?)null,
                    Transforms = transforms?.Select(t => new TransformDto
                    {
                        Unit = t.UnitPrefabGuid,
                        Source = (byte)t.Source,
                    }).ToList(),
                    Presets = presetsForPlayer,
                    Hotkeys = hotkeysForPlayer,
                };
            }

            var dto = new StateDto
            {
                Version = 6,
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
        // W3 / v5: weapon-family-specific slot bindings.
        // Outer key = WeaponFamily enum name; inner key = slot stringified (JSON).
        public Dictionary<string, Dictionary<string, int>> WeaponSlots { get; set; }
        public byte? Verbosity { get; set; }
        public bool? EmitApiEvents { get; set; }
        public List<TransformDto> Transforms { get; set; }
        // C1: per-name preset → slot → abilityGuid. Slot keys are stringified ints (JSON requirement).
        public Dictionary<string, Dictionary<string, int>> Presets { get; set; }
        // W4 / v6: named hotkey bindings, hotkey-name → ability prefab GUID.
        public Dictionary<string, int> Hotkeys { get; set; }
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
