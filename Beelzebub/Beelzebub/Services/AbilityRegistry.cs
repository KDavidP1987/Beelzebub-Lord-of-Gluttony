using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Beelzebub.Services;

internal enum CaptureSource : byte
{
    Regular = 0,
    VBlood = 1,
}

internal enum Verbosity : byte
{
    Silent = 0,
    Summary = 1,
    Verbose = 2,
}

internal readonly record struct CapturedAbility(int UnitPrefabGuid, int AbilityPrefabGuid, CaptureSource Source);

internal sealed class AbilityRegistry
{
    // steamId → unitGuid → abilityGuid → source
    readonly ConcurrentDictionary<ulong, ConcurrentDictionary<int, ConcurrentDictionary<int, CaptureSource>>> _data = new();
    readonly ConcurrentDictionary<ulong, ConcurrentDictionary<int, int>> _slotAssignments = new();
    readonly ConcurrentDictionary<ulong, Verbosity> _verbosity = new();

    public int PlayerCount => _data.Count;

    public Verbosity GetVerbosity(ulong steamId, Verbosity fallback) =>
        _verbosity.TryGetValue(steamId, out var v) ? v : fallback;

    public void SetVerbosity(ulong steamId, Verbosity verbosity) => _verbosity[steamId] = verbosity;

    public IEnumerable<KeyValuePair<ulong, Verbosity>> VerbositySnapshot() => _verbosity;

    public void SetSlot(ulong steamId, int slot, int abilityGuid)
    {
        var slots = _slotAssignments.GetOrAdd(steamId, _ => new ConcurrentDictionary<int, int>());
        slots[slot] = abilityGuid;
    }

    public void ClearSlot(ulong steamId, int slot)
    {
        if (_slotAssignments.TryGetValue(steamId, out var slots))
        {
            slots.TryRemove(slot, out _);
        }
    }

    public IReadOnlyDictionary<int, int> GetSlots(ulong steamId) =>
        _slotAssignments.TryGetValue(steamId, out var slots)
            ? slots
            : new Dictionary<int, int>();

    public bool Add(ulong steamId, int unitPrefabGuid, int abilityPrefabGuid, CaptureSource source)
    {
        var byUnit = _data.GetOrAdd(steamId, _ => new ConcurrentDictionary<int, ConcurrentDictionary<int, CaptureSource>>());
        var abilities = byUnit.GetOrAdd(unitPrefabGuid, _ => new ConcurrentDictionary<int, CaptureSource>());

        if (abilities.TryAdd(abilityPrefabGuid, source)) return true;

        // Upgrade Regular → VBlood if a boss kill later captures the same ability (boss source wins).
        var existing = abilities[abilityPrefabGuid];
        if (existing == CaptureSource.Regular && source == CaptureSource.VBlood)
        {
            abilities[abilityPrefabGuid] = CaptureSource.VBlood;
        }
        return false;
    }

    public IReadOnlyList<CapturedAbility> ListFor(ulong steamId)
    {
        if (!_data.TryGetValue(steamId, out var byUnit)) return System.Array.Empty<CapturedAbility>();
        var result = new List<CapturedAbility>();
        foreach (var (unitGuid, abilities) in byUnit)
        {
            foreach (var (abilityGuid, source) in abilities)
            {
                result.Add(new CapturedAbility(unitGuid, abilityGuid, source));
            }
        }
        return result;
    }

    public bool Clear(ulong steamId)
    {
        _slotAssignments.TryRemove(steamId, out _);
        return _data.TryRemove(steamId, out _);
    }

    public Dictionary<ulong, Verbosity> AllVerbosity() => new(_verbosity);

    public IEnumerable<KeyValuePair<ulong, IReadOnlyList<CapturedAbility>>> Snapshot()
    {
        foreach (var (steamId, _) in _data)
        {
            yield return new KeyValuePair<ulong, IReadOnlyList<CapturedAbility>>(steamId, ListFor(steamId));
        }
    }

    public void LoadFromSnapshot(IEnumerable<KeyValuePair<ulong, IReadOnlyList<CapturedAbility>>> snapshot)
    {
        _data.Clear();
        foreach (var (steamId, abilities) in snapshot)
        {
            foreach (var ability in abilities)
            {
                Add(steamId, ability.UnitPrefabGuid, ability.AbilityPrefabGuid, ability.Source);
            }
        }
    }
}
