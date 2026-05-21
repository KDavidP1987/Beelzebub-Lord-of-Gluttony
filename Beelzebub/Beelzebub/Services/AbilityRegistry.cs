using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Beelzebub.Services;

internal sealed class AbilityRegistry
{
    readonly ConcurrentDictionary<ulong, ConcurrentDictionary<int, ConcurrentDictionary<int, byte>>> _data = new();
    readonly ConcurrentDictionary<ulong, ConcurrentDictionary<int, int>> _slotAssignments = new();

    public int PlayerCount => _data.Count;

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

    public bool Add(ulong steamId, int unitPrefabGuid, int abilityPrefabGuid)
    {
        var byUnit = _data.GetOrAdd(steamId, _ => new ConcurrentDictionary<int, ConcurrentDictionary<int, byte>>());
        var abilities = byUnit.GetOrAdd(unitPrefabGuid, _ => new ConcurrentDictionary<int, byte>());
        return abilities.TryAdd(abilityPrefabGuid, 0);
    }

    public IReadOnlyList<CapturedAbility> ListFor(ulong steamId)
    {
        if (!_data.TryGetValue(steamId, out var byUnit)) return System.Array.Empty<CapturedAbility>();
        var result = new List<CapturedAbility>();
        foreach (var (unitGuid, abilities) in byUnit)
        {
            foreach (var abilityGuid in abilities.Keys)
            {
                result.Add(new CapturedAbility(unitGuid, abilityGuid));
            }
        }
        return result;
    }

    public bool Clear(ulong steamId)
    {
        _slotAssignments.TryRemove(steamId, out _);
        return _data.TryRemove(steamId, out _);
    }

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
                Add(steamId, ability.UnitPrefabGuid, ability.AbilityPrefabGuid);
            }
        }
    }
}

internal readonly record struct CapturedAbility(int UnitPrefabGuid, int AbilityPrefabGuid);
