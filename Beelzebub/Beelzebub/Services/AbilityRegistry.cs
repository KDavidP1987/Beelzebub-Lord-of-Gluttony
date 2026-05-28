using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Beelzebub.Services;

internal enum CaptureSource : byte
{
    Regular = 0,
    VBlood = 1,
}

/// <summary>v0.38.0: the two independent pity (bad-luck-protection) tracks.</summary>
internal enum PityKind : byte
{
    Ability = 0,
    Transform = 1,
}

/// <summary>
/// v0.39.0: transform-settings + cooldown category. Separates the shard bosses
/// (Dracula, Adam, Solarus, Talzur/Winged Horror, Megara the Serpent Queen, Gorecrusher
/// — configurable via Transform_ShardBossNames) from other V-Bloods so they
/// can have their own mode/duration/cooldown and an independent cooldown bucket.
/// </summary>
internal enum TransformCategory : byte
{
    Regular = 0,
    VBlood = 1,
    ShardBoss = 2,
}

internal enum Verbosity : byte
{
    Silent = 0,
    Summary = 1,
    Verbose = 2,
}

internal readonly record struct CapturedAbility(int UnitPrefabGuid, int AbilityPrefabGuid, CaptureSource Source);

internal readonly record struct UnlockedTransform(int UnitPrefabGuid, CaptureSource Source);

/// <summary>
/// v0.45.0: per-player summon-tracking state, decoupled from transformation so summon
/// abilities cast in NORMAL form (the v0.44.0 per-ability playstyle) get the same ally
/// setup, owner-rebind, cap accounting, leashing and despawn lifecycle as summons cast
/// while transformed. <see cref="ActiveTransform"/> derives from this, so the transform
/// path keeps every field it had; untransformed summoners get a bare instance from
/// <see cref="AbilityRegistry.GetSummonOwner"/>. Runtime-only (never persisted).
/// </summary>
internal class SummonOwnerState
{
    // v0.20.0: minion entities spawned by summon abilities. AbilityCastStartedSystemPatch
    // + LinkMinionToOwnerOnSpawnSystemPatch append here as they rebind freshly-spawned
    // minions. v0.23.0: no cap (was 10); Revert / despawn move these into DespawnQueue and
    // drain across frames so we don't crash V Rising by destroying 100 entities in a tick.
    public System.Collections.Generic.List<Unity.Entities.Entity> SummonedMinions;

    // v0.23.6: per-CAST stack tracking. Each cast of a summon ability creates a
    // new inner list (a "cast group") in SummonStacks[abilityGuid]. The cap
    // refuses based on the number of LIVE GROUPS (= number of active uses of
    // the ability), not the number of live entities. Example: cast RaiseHorde
    // 3 times = 3 groups = 3/3 uses, even though each cast spawned 10 entities.
    // A group is "alive" if any entity in it still exists. Lazy cleanup on read.
    public System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<System.Collections.Generic.List<Unity.Entities.Entity>>> SummonStacks
        = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<System.Collections.Generic.List<Unity.Entities.Entity>>>();

    // v0.23.16 (B1 fix): parallel to SummonStacks — SummonStackTimes[abilityGuid][i]
    // is the creation timestamp of the cast group at SummonStacks[abilityGuid][i].
    // Without this, LiveCastCount prunes empty groups immediately — and any cast
    // whose natural-chain spawns arrive AFTER the attribution window (Priest's
    // channeled RaiseHorde/RaiseDead can take 2-3s) leaves an empty group that
    // gets pruned before attribution lands, breaking the cap entirely. With
    // this, an empty group is only pruneable after the attribution window
    // expires (= confirmed no attribution will arrive). BeginCastGroup must
    // keep these two dictionaries index-aligned per ability.
    public System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<System.DateTime>> SummonStackTimes
        = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<System.DateTime>>();

    // v0.23.0: staged-despawn queue. Revert moves SummonedMinions here in one shot;
    // TransformService.Tick drains <=N entities/frame to avoid the batch-destroy
    // server crash (v0.22.0 testing crashed at ~36 entities/frame).
    public System.Collections.Generic.Queue<Unity.Entities.Entity> DespawnQueue
        = new System.Collections.Generic.Queue<Unity.Entities.Entity>();

    // v0.23.0: `.beelz summons off` master kill-switch per player. When true:
    // cast-intercept refuses new summons, natural-chain rebind skips, and any
    // future re-summon-on-teleport is suppressed.
    public bool SummonsDisabled;

    // v0.23.0: teleport snapshot. PlayerTeleportSystemPatch moves live summons here
    // and adds <Disabled> components on teleport-start; on teleport-finish we drain
    // back into SummonedMinions and remove <Disabled>. Avoids leaving allies behind
    // in the source zone (V Rising's teleport flow doesn't carry NPC followers).
    public System.Collections.Generic.List<Unity.Entities.Entity> SuspendedSummons
        = new System.Collections.Generic.List<Unity.Entities.Entity>();

    // v0.23.8: manual stash/restore for waygate teleport. Bloodcraft-pattern —
    // when player runs `.beelz summons stash`, all live summons get <Disabled>
    // component + move from SummonedMinions to StashedSummons. They're out of
    // the world (don't trigger V Rising's "subdued enemy" pre-check) but still
    // tied to this owner's runtime state. `.beelz summons restore` reverses:
    // remove <Disabled>, teleport to current player position, move back to
    // SummonedMinions list.
    public System.Collections.Generic.List<Unity.Entities.Entity> StashedSummons
        = new System.Collections.Generic.List<Unity.Entities.Entity>();
}

internal sealed class ActiveTransform : SummonOwnerState
{
    public int UnitPrefabGuid;
    public CaptureSource Source;
    public System.DateTime ActivatedAtUtc;
    public System.TimeSpan? Duration; // null = Toggle mode
    // Z1 (v0.14.0): StashedOverrides is gone. Transform slot overrides now live on
    // a dedicated carrier buff (TransformBuffService). When the buff is destroyed
    // on revert V Rising re-resolves the player's spell-book + weapon naturals
    // automatically — no stash-and-restore dance needed.
    // A4/Z2: the native shapeshift form buff we applied (Wolf/Bear/Rat/Spider/Toad)
    // for the visual model swap. 0 = no native form applied (default since v0.14.0
    // when Transform_NativeShapeshift_Enabled is false). Used by revert to know
    // which buff to destroy. Runtime-only.
    public int AppliedShapeshiftForm;
    // v0.47.0 (Phase 1 native-form test): when non-zero, this transform is a NATIVE shapeshift
    // form (Wolf/Bear/…) applied via the persistent ExoForm recipe with a custom ability set,
    // NOT a curated boss form. Marks it exempt from the non-boss "stale transform" guard in
    // ReapplyActiveTransform (so it resumes on reconnect/refresh instead of being reverted), and
    // carries the set so reapply re-runs ApplyForm. Runtime-only. 0 = not a native-form test.
    public int NativeFormBuffGuid;
    public int[] NativeFormSet;
    // TX5: which boss-phase loadout is currently on the spell bar. Default 1.
    // Switched via `.beelz phase <n>`.
    public int CurrentPhase = 1;
    // v0.27.0: auto-phase (Transform_PhaseMode = Auto). InCombat is maintained by
    // the PvE combat-buff hooks (BuffSpawnServerPatch start / UpdateBuffsBuffer
    // DestroyPatch end). Character is captured at combat-enter so the Tick
    // auto-monitor can read Health without a steamId→entity lookup. CurrentPhase
    // doubles as the one-way ratchet: it only climbs while InCombat, and resets
    // to 1 when combat ends (mirrors a boss leash-reset).
    public bool InCombat;
    public Unity.Entities.Entity Character;
    // v0.43.7: when set, the owner is DISCONNECTED and this transform is parked in the
    // reconnect-grace window (Transform_ReconnectGraceSeconds). TransformService.Tick
    // reverts it once the grace elapses; a reconnect clears this and resumes the form.
    // null = owner connected / transform active normally. Runtime-only (never persisted).
    public System.DateTime? DisconnectedAtUtc;
}

internal sealed class AbilityRegistry
{
    // steamId → unitGuid → abilityGuid → source
    readonly ConcurrentDictionary<ulong, ConcurrentDictionary<int, ConcurrentDictionary<int, CaptureSource>>> _data = new();
    // Universal slot bindings: steamId → slot → abilityGuid. Activate on any
    // weapon if the ability's family is Magic/None or matches the equipped weapon.
    readonly ConcurrentDictionary<ulong, ConcurrentDictionary<int, int>> _slotAssignments = new();
    // W3: weapon-family-specific slot bindings: steamId → weapon → slot → abilityGuid.
    // Win over universal bindings when the player wields that weapon family.
    readonly ConcurrentDictionary<ulong, ConcurrentDictionary<WeaponFamily, ConcurrentDictionary<int, int>>> _weaponSlots = new();
    readonly ConcurrentDictionary<ulong, Verbosity> _verbosity = new();
    readonly ConcurrentDictionary<ulong, bool> _emitApiEvents = new();

    // C1: per-player slot-loadout presets, keyed by case-sensitive name.
    readonly ConcurrentDictionary<ulong, ConcurrentDictionary<string, ConcurrentDictionary<int, int>>> _presets = new();

    // W4: per-player named hotkey bindings. Key = hotkey name (admin/player-chosen,
    // free text like "Q1", "Heal", "Burst"), value = ability prefab GUID. These are
    // EXTRA slots beyond V Rising's 6 — the actual fire mechanism is BCH-side
    // (server stores the binding + exposes it via .beelz api hotkeys; BCH client
    // renders buttons and triggers casts).
    readonly ConcurrentDictionary<ulong, ConcurrentDictionary<string, int>> _hotkeys = new();

    // Phase 5: transforms.
    readonly ConcurrentDictionary<ulong, ConcurrentDictionary<int, CaptureSource>> _transformUnlocks = new(); // unitGuid → source
    readonly ConcurrentDictionary<ulong, ActiveTransform> _activeTransforms = new(); // runtime-only
    // v0.45.0: per-player summon state for players who are NOT transformed. Lets summon
    // abilities cast in normal form be tracked/allied/despawned exactly like transform
    // summons. Created on demand by GetSummonOwner; runtime-only (never persisted).
    readonly ConcurrentDictionary<ulong, SummonOwnerState> _standaloneSummons = new();
    readonly ConcurrentDictionary<ulong, System.DateTime> _cooldownRegularUntil = new(); // runtime-only
    readonly ConcurrentDictionary<ulong, System.DateTime> _cooldownVBloodUntil = new(); // runtime-only
    readonly ConcurrentDictionary<ulong, System.DateTime> _cooldownShardUntil = new(); // runtime-only (v0.39.0)

    public int PlayerCount => _data.Count;

    public Verbosity GetVerbosity(ulong steamId, Verbosity fallback) =>
        _verbosity.TryGetValue(steamId, out var v) ? v : fallback;

    public void SetVerbosity(ulong steamId, Verbosity verbosity) => _verbosity[steamId] = verbosity;

    public IEnumerable<KeyValuePair<ulong, Verbosity>> VerbositySnapshot() => _verbosity;

    public bool GetEmitApiEvents(ulong steamId) =>
        _emitApiEvents.TryGetValue(steamId, out var b) && b;

    public void SetEmitApiEvents(ulong steamId, bool value)
    {
        if (value) _emitApiEvents[steamId] = true;
        else _emitApiEvents.TryRemove(steamId, out _);
    }

    public Dictionary<ulong, bool> AllEmitApiEvents() => new(_emitApiEvents);

    // --- Slot loadout presets (C1) ---
    public void SavePreset(ulong steamId, string name)
    {
        var byName = _presets.GetOrAdd(steamId, _ => new ConcurrentDictionary<string, ConcurrentDictionary<int, int>>());
        var snapshot = new ConcurrentDictionary<int, int>();
        foreach (var (slot, abilityGuid) in GetSlots(steamId))
        {
            snapshot[slot] = abilityGuid;
        }
        byName[name] = snapshot;
    }

    /// <summary>
    /// Returns true if a preset was loaded and slot assignments were replaced. Returns false
    /// if the preset doesn't exist.
    /// </summary>
    public bool LoadPreset(ulong steamId, string name)
    {
        if (!_presets.TryGetValue(steamId, out var byName)) return false;
        if (!byName.TryGetValue(name, out var snapshot)) return false;
        var slots = _slotAssignments.GetOrAdd(steamId, _ => new ConcurrentDictionary<int, int>());
        slots.Clear();
        foreach (var (slot, abilityGuid) in snapshot)
        {
            slots[slot] = abilityGuid;
        }
        return true;
    }

    public bool DeletePreset(ulong steamId, string name)
    {
        if (!_presets.TryGetValue(steamId, out var byName)) return false;
        return byName.TryRemove(name, out _);
    }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> ListPresets(ulong steamId)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<int, int>>();
        if (_presets.TryGetValue(steamId, out var byName))
        {
            foreach (var (name, snapshot) in byName)
            {
                result[name] = new Dictionary<int, int>(snapshot);
            }
        }
        return result;
    }

    public Dictionary<ulong, Dictionary<string, Dictionary<int, int>>> PresetsSnapshot()
    {
        var result = new Dictionary<ulong, Dictionary<string, Dictionary<int, int>>>();
        foreach (var (steamId, byName) in _presets)
        {
            var nested = new Dictionary<string, Dictionary<int, int>>();
            foreach (var (name, slots) in byName)
            {
                nested[name] = new Dictionary<int, int>(slots);
            }
            result[steamId] = nested;
        }
        return result;
    }

    public void LoadPresetsSnapshot(ulong steamId, Dictionary<string, Dictionary<int, int>> presets)
    {
        var byName = _presets.GetOrAdd(steamId, _ => new ConcurrentDictionary<string, ConcurrentDictionary<int, int>>());
        byName.Clear();
        foreach (var (name, slots) in presets)
        {
            var snapshot = new ConcurrentDictionary<int, int>();
            foreach (var (slot, abilityGuid) in slots) snapshot[slot] = abilityGuid;
            byName[name] = snapshot;
        }
    }

    // --- W4: named hotkey bindings (BCH-facing) ---

    /// <summary>
    /// Bind a hotkey name to an ability GUID for a player. Names are case-insensitive
    /// and trimmed; bindings overwrite. Returns false if name is empty/whitespace.
    /// </summary>
    public bool SetHotkey(ulong steamId, string name, int abilityGuid)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var byName = _hotkeys.GetOrAdd(steamId, _ => new ConcurrentDictionary<string, int>(System.StringComparer.OrdinalIgnoreCase));
        byName[name.Trim()] = abilityGuid;
        return true;
    }

    public bool ClearHotkey(ulong steamId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (!_hotkeys.TryGetValue(steamId, out var byName)) return false;
        return byName.TryRemove(name.Trim(), out _);
    }

    /// <summary>Returns the ability GUID bound to <paramref name="name"/>, or 0 if unbound.</summary>
    public int GetHotkey(ulong steamId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        if (_hotkeys.TryGetValue(steamId, out var byName)
            && byName.TryGetValue(name.Trim(), out int guid)) return guid;
        return 0;
    }

    public int HotkeyCount(ulong steamId) =>
        _hotkeys.TryGetValue(steamId, out var byName) ? byName.Count : 0;

    public IReadOnlyDictionary<string, int> ListHotkeys(ulong steamId)
    {
        if (!_hotkeys.TryGetValue(steamId, out var byName)) return new Dictionary<string, int>();
        return new Dictionary<string, int>(byName);
    }

    public Dictionary<ulong, Dictionary<string, int>> HotkeysSnapshot()
    {
        var result = new Dictionary<ulong, Dictionary<string, int>>();
        foreach (var (steamId, byName) in _hotkeys)
        {
            if (byName.IsEmpty) continue;
            result[steamId] = new Dictionary<string, int>(byName);
        }
        return result;
    }

    public void LoadHotkeysSnapshot(ulong steamId, Dictionary<string, int> snapshot)
    {
        var byName = _hotkeys.GetOrAdd(steamId, _ => new ConcurrentDictionary<string, int>(System.StringComparer.OrdinalIgnoreCase));
        byName.Clear();
        foreach (var (name, abilityGuid) in snapshot)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            byName[name.Trim()] = abilityGuid;
        }
    }

    /// <summary>
    /// Universal slot bind. Backward-compatible overload — equivalent to
    /// SetSlot(steamId, WeaponFamily.None, slot, abilityGuid).
    /// </summary>
    public void SetSlot(ulong steamId, int slot, int abilityGuid) =>
        SetSlot(steamId, WeaponFamily.None, slot, abilityGuid);

    /// <summary>
    /// W3: bind an ability to a slot for a specific weapon family.
    /// Use WeaponFamily.None (or .Magic) for the universal bucket — fires on any weapon
    /// (subject to the ability's own compatibility). Any other family creates a
    /// weapon-specific binding that wins over the universal bucket when wielded.
    /// </summary>
    public void SetSlot(ulong steamId, WeaponFamily weapon, int slot, int abilityGuid)
    {
        if (IsUniversalBucket(weapon))
        {
            var slots = _slotAssignments.GetOrAdd(steamId, _ => new ConcurrentDictionary<int, int>());
            slots[slot] = abilityGuid;
        }
        else
        {
            var byWeapon = _weaponSlots.GetOrAdd(steamId, _ => new ConcurrentDictionary<WeaponFamily, ConcurrentDictionary<int, int>>());
            var slots = byWeapon.GetOrAdd(weapon, _ => new ConcurrentDictionary<int, int>());
            slots[slot] = abilityGuid;
        }
    }

    public void ClearSlot(ulong steamId, int slot) =>
        ClearSlot(steamId, WeaponFamily.None, slot);

    public void ClearSlot(ulong steamId, WeaponFamily weapon, int slot)
    {
        if (IsUniversalBucket(weapon))
        {
            if (_slotAssignments.TryGetValue(steamId, out var slots)) slots.TryRemove(slot, out _);
        }
        else if (_weaponSlots.TryGetValue(steamId, out var byWeapon)
                 && byWeapon.TryGetValue(weapon, out var slots))
        {
            slots.TryRemove(slot, out _);
        }
    }

    /// <summary>
    /// v0.43.8: drop EVERY slot binding for a player — both the universal bucket and
    /// all weapon-specific buckets — returning the number of bindings removed. Backs
    /// <c>.beelz resetbar</c>: returns the player's action bar to its vanilla in-game
    /// state. Captured abilities and transform unlocks are untouched (re-grant anytime).
    /// </summary>
    public int ClearAllSlots(ulong steamId)
    {
        int removed = 0;
        if (_slotAssignments.TryRemove(steamId, out var uni)) removed += uni.Count;
        if (_weaponSlots.TryRemove(steamId, out var byWeapon))
        {
            foreach (var slots in byWeapon.Values) removed += slots.Count;
        }
        return removed;
    }

    /// <summary>
    /// Returns the universal-bucket slots. Backward-compatible — pre-W3 callers
    /// (like `.beelz list`, presets) keep seeing only the universal bindings.
    /// </summary>
    public IReadOnlyDictionary<int, int> GetSlots(ulong steamId) =>
        _slotAssignments.TryGetValue(steamId, out var slots)
            ? slots
            : new Dictionary<int, int>();

    /// <summary>
    /// W3: returns the resolved slot map for a given currently-equipped weapon family.
    /// Weapon-specific binds win on a slot if present; otherwise the universal bind wins.
    /// Use this when injecting into the live ability bar.
    /// </summary>
    public IReadOnlyDictionary<int, int> GetSlotsResolved(ulong steamId, WeaponFamily currentWeapon)
    {
        var result = new Dictionary<int, int>();
        // Universal first.
        if (_slotAssignments.TryGetValue(steamId, out var uni))
        {
            foreach (var (slot, abilityGuid) in uni) result[slot] = abilityGuid;
        }
        // Weapon-specific overrides.
        if (!IsUniversalBucket(currentWeapon)
            && _weaponSlots.TryGetValue(steamId, out var byWeapon)
            && byWeapon.TryGetValue(currentWeapon, out var w))
        {
            foreach (var (slot, abilityGuid) in w) result[slot] = abilityGuid;
        }
        return result;
    }

    /// <summary>
    /// v0.49.0: like <see cref="GetSlotsResolved"/> but tags each resolved slot with whether
    /// it came from a WEAPON-SPECIFIC bucket (an explicit player placement) or the universal
    /// bucket. The slot injector uses the tag to HONOR explicit weapon-bucket binds (gate only
    /// on Enabled / transform-only) while still family-filtering universal binds — the fix for
    /// "abilities assigned to the Reaper group don't register when I equip a reaper" (the old
    /// path re-derived family from the ability NAME and silently dropped the bind).
    /// </summary>
    public IReadOnlyDictionary<int, (int abilityGuid, bool weaponSpecific)> GetSlotsResolvedWithOrigin(ulong steamId, WeaponFamily currentWeapon)
    {
        var result = new Dictionary<int, (int, bool)>();
        if (_slotAssignments.TryGetValue(steamId, out var uni))
        {
            foreach (var (slot, abilityGuid) in uni) result[slot] = (abilityGuid, false);
        }
        if (!IsUniversalBucket(currentWeapon)
            && _weaponSlots.TryGetValue(steamId, out var byWeapon)
            && byWeapon.TryGetValue(currentWeapon, out var w))
        {
            // Weapon-specific binds win on a slot over the universal bind.
            foreach (var (slot, abilityGuid) in w) result[slot] = (abilityGuid, true);
        }
        return result;
    }

    /// <summary>
    /// W3: return the bindings for one specific weapon family. Empty if none set.
    /// </summary>
    public IReadOnlyDictionary<int, int> GetWeaponSlots(ulong steamId, WeaponFamily weapon)
    {
        if (IsUniversalBucket(weapon)) return GetSlots(steamId);
        if (_weaponSlots.TryGetValue(steamId, out var byWeapon)
            && byWeapon.TryGetValue(weapon, out var slots)) return slots;
        return new Dictionary<int, int>();
    }

    /// <summary>
    /// W3: snapshot of every weapon-specific bucket for a player (excludes universal).
    /// Returns empty dict if nothing weapon-specific is bound.
    /// </summary>
    public Dictionary<WeaponFamily, Dictionary<int, int>> AllWeaponSlots(ulong steamId)
    {
        var result = new Dictionary<WeaponFamily, Dictionary<int, int>>();
        if (_weaponSlots.TryGetValue(steamId, out var byWeapon))
        {
            foreach (var (weapon, slots) in byWeapon)
            {
                if (slots.IsEmpty) continue;
                result[weapon] = new Dictionary<int, int>(slots);
            }
        }
        return result;
    }

    /// <summary>
    /// W3: persistence snapshot of every player's weapon-specific bindings.
    /// Outer key = steamId; mid key = weapon family; inner = slot → ability.
    /// </summary>
    public Dictionary<ulong, Dictionary<WeaponFamily, Dictionary<int, int>>> WeaponSlotsSnapshot()
    {
        var result = new Dictionary<ulong, Dictionary<WeaponFamily, Dictionary<int, int>>>();
        foreach (var (steamId, _) in _weaponSlots)
        {
            var nested = AllWeaponSlots(steamId);
            if (nested.Count > 0) result[steamId] = nested;
        }
        return result;
    }

    public void LoadWeaponSlotsSnapshot(ulong steamId, Dictionary<WeaponFamily, Dictionary<int, int>> snapshot)
    {
        var byWeapon = _weaponSlots.GetOrAdd(steamId, _ => new ConcurrentDictionary<WeaponFamily, ConcurrentDictionary<int, int>>());
        foreach (var (weapon, slots) in snapshot)
        {
            var bucket = byWeapon.GetOrAdd(weapon, _ => new ConcurrentDictionary<int, int>());
            bucket.Clear();
            foreach (var (slot, abilityGuid) in slots) bucket[slot] = abilityGuid;
        }
    }

    static bool IsUniversalBucket(WeaponFamily weapon) =>
        weapon == WeaponFamily.None || weapon == WeaponFamily.Magic;

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

    /// <summary>
    /// v0.44.0 "Devour": add many abilities from one unit in a single shot (the jackpot
    /// that replaced the per-unit transform unlock for non-renderable units). Returns the
    /// count NEWLY added (already-captured ones are skipped / upgraded Regular→VBlood).
    /// Caller is responsible for pre-filtering eligibility (AbilityFilter) and saving.
    /// </summary>
    public int DevourAbilities(ulong steamId, int unitPrefabGuid, IEnumerable<int> abilityPrefabGuids, CaptureSource source)
    {
        if (abilityPrefabGuids == null) return 0;
        int added = 0;
        foreach (int a in abilityPrefabGuids)
            if (a != 0 && Add(steamId, unitPrefabGuid, a, source)) added++;
        return added;
    }

    /// <summary>v0.43.5: true if the player has captured this ability group from any unit.</summary>
    public bool HasCaptured(ulong steamId, int abilityPrefabGuid)
    {
        if (!_data.TryGetValue(steamId, out var byUnit)) return false;
        foreach (var abilities in byUnit.Values)
            if (abilities.ContainsKey(abilityPrefabGuid)) return true;
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

    /// <summary>
    /// AUDIT-6 (v0.20.1): bulk wipe. Returns (players, abilities, transforms) wiped.
    /// Clears every internal dictionary in one shot; intended only for the
    /// `.beelz admin wipe-all` operation. Caller is responsible for triggering
    /// persistence save afterward.
    /// </summary>
    public (int players, int abilities, int transforms) WipeAll()
    {
        int players = _data.Count;
        int abilities = 0;
        foreach (var byUnit in _data.Values)
            foreach (var abs in byUnit.Values)
                abilities += abs.Count;
        int transforms = 0;
        foreach (var byUnit in _transformUnlocks.Values) transforms += byUnit.Count;

        _data.Clear();
        _slotAssignments.Clear();
        _weaponSlots.Clear();
        _hotkeys.Clear();
        _presets.Clear();
        _transformUnlocks.Clear();
        _activeTransforms.Clear();
        _standaloneSummons.Clear();
        _cooldownRegularUntil.Clear();
        _cooldownVBloodUntil.Clear();
        _cooldownShardUntil.Clear();
        _verbosity.Clear();
        _emitApiEvents.Clear();
        _pity.Clear();
        return (players, abilities, transforms);
    }

    public bool Clear(ulong steamId)
    {
        _slotAssignments.TryRemove(steamId, out _);
        _weaponSlots.TryRemove(steamId, out _);
        _hotkeys.TryRemove(steamId, out _);
        _transformUnlocks.TryRemove(steamId, out _);
        _activeTransforms.TryRemove(steamId, out _);
        _standaloneSummons.TryRemove(steamId, out _);
        _pity.TryRemove(steamId, out _);
        return _data.TryRemove(steamId, out _);
    }

    /// <summary>
    /// Remove a single captured ability (per unit source). If the resulting per-unit
    /// set is empty, the unit entry is also removed. Slot assignments referencing the
    /// forgotten ability are cleared so the player isn't stuck with a dangling slot.
    /// </summary>
    public bool Forget(ulong steamId, int unitPrefabGuid, int abilityPrefabGuid)
    {
        if (!_data.TryGetValue(steamId, out var byUnit)) return false;
        if (!byUnit.TryGetValue(unitPrefabGuid, out var abilities)) return false;
        if (!abilities.TryRemove(abilityPrefabGuid, out _)) return false;
        if (abilities.IsEmpty) byUnit.TryRemove(unitPrefabGuid, out _);
        // Clear any slot assignments pointing at the forgotten ability.
        if (_slotAssignments.TryGetValue(steamId, out var slots))
        {
            foreach (var (slot, abilityGuid) in slots)
            {
                if (abilityGuid == abilityPrefabGuid) slots.TryRemove(slot, out _);
            }
        }
        return true;
    }

    public bool ForgetTransform(ulong steamId, int unitPrefabGuid)
    {
        if (!_transformUnlocks.TryGetValue(steamId, out var byUnit)) return false;
        // If they're currently transformed into this unit, revert first.
        if (_activeTransforms.TryGetValue(steamId, out var active) && active.UnitPrefabGuid == unitPrefabGuid)
        {
            _activeTransforms.TryRemove(steamId, out _);
        }
        return byUnit.TryRemove(unitPrefabGuid, out _);
    }

    public Dictionary<ulong, Verbosity> AllVerbosity() => new(_verbosity);

    // --- Transform unlocks (persisted) ---
    public bool AddTransformUnlock(ulong steamId, int unitPrefabGuid, CaptureSource source)
    {
        var byUnit = _transformUnlocks.GetOrAdd(steamId, _ => new ConcurrentDictionary<int, CaptureSource>());
        if (byUnit.TryAdd(unitPrefabGuid, source)) return true;
        // Upgrade Regular -> VBlood if the same unit is later unlocked from a V-Blood kill.
        if (byUnit[unitPrefabGuid] == CaptureSource.Regular && source == CaptureSource.VBlood)
        {
            byUnit[unitPrefabGuid] = CaptureSource.VBlood;
        }
        return false;
    }

    public bool HasTransformUnlock(ulong steamId, int unitPrefabGuid) =>
        _transformUnlocks.TryGetValue(steamId, out var byUnit) && byUnit.ContainsKey(unitPrefabGuid);

    public IReadOnlyList<UnlockedTransform> ListTransforms(ulong steamId)
    {
        if (!_transformUnlocks.TryGetValue(steamId, out var byUnit)) return System.Array.Empty<UnlockedTransform>();
        var list = new List<UnlockedTransform>(byUnit.Count);
        foreach (var (unit, source) in byUnit)
        {
            list.Add(new UnlockedTransform(unit, source));
        }
        return list;
    }

    public IEnumerable<KeyValuePair<ulong, IReadOnlyList<UnlockedTransform>>> TransformSnapshot()
    {
        foreach (var (steamId, _) in _transformUnlocks)
        {
            yield return new KeyValuePair<ulong, IReadOnlyList<UnlockedTransform>>(steamId, ListTransforms(steamId));
        }
    }

    public void LoadTransformsFromSnapshot(IEnumerable<KeyValuePair<ulong, IReadOnlyList<UnlockedTransform>>> snapshot)
    {
        _transformUnlocks.Clear();
        foreach (var (steamId, list) in snapshot)
        {
            foreach (var t in list)
            {
                AddTransformUnlock(steamId, t.UnitPrefabGuid, t.Source);
            }
        }
    }

    // --- Active transform (runtime) ---
    public ActiveTransform GetActiveTransform(ulong steamId) =>
        _activeTransforms.TryGetValue(steamId, out var at) ? at : null;

    public void SetActiveTransform(ulong steamId, ActiveTransform state) => _activeTransforms[steamId] = state;
    public void ClearActiveTransform(ulong steamId) => _activeTransforms.TryRemove(steamId, out _);
    public IEnumerable<KeyValuePair<ulong, ActiveTransform>> AllActiveTransforms() => _activeTransforms;

    // --- v0.45.0: summon owners (transform OR standalone) ---

    /// <summary>
    /// Resolve the summon-tracking container for a player. While transformed this is the
    /// player's <see cref="ActiveTransform"/> (so behavior is byte-identical to before);
    /// otherwise it's a standalone <see cref="SummonOwnerState"/>. Behaviour/iteration
    /// call-sites pass <paramref name="createIfMissing"/>=false and keep their null-guard
    /// (no allocation churn); only the summon-catch hooks (cast / spawn-link) create on
    /// demand, since that's where a summon is actually being made.
    /// </summary>
    public SummonOwnerState GetSummonOwner(ulong steamId, bool createIfMissing)
    {
        if (_activeTransforms.TryGetValue(steamId, out var at)) return at;
        if (createIfMissing) return _standaloneSummons.GetOrAdd(steamId, _ => new SummonOwnerState());
        return _standaloneSummons.TryGetValue(steamId, out var s) ? s : null;
    }

    /// <summary>
    /// Every live summon owner: all active transforms, then standalone owners (skipping any
    /// player who also has a transform — that container is the live one). Used by the
    /// per-frame summon behaviour ticks (aggro-sync, leash, despawn-drain, prune, lifespan).
    /// </summary>
    public IEnumerable<KeyValuePair<ulong, SummonOwnerState>> AllSummonOwners()
    {
        foreach (var kv in _activeTransforms)
            yield return new KeyValuePair<ulong, SummonOwnerState>(kv.Key, kv.Value);
        foreach (var kv in _standaloneSummons)
        {
            if (_activeTransforms.ContainsKey(kv.Key)) continue;
            yield return new KeyValuePair<ulong, SummonOwnerState>(kv.Key, kv.Value);
        }
    }

    public void ClearStandaloneSummons(ulong steamId) => _standaloneSummons.TryRemove(steamId, out _);

    // v0.23.0: post-revert despawn pending records. When a transform reverts with
    // live summons we move the ActiveTransform here (instead of dropping it) until
    // its DespawnQueue drains — TransformService.Tick processes both these and
    // currently-active records via SummonAllyService.DrainDespawnQueues. Allows the
    // staged destroy to keep running even after the player switched / reverted.
    readonly ConcurrentDictionary<ulong, ActiveTransform> _pendingDespawns = new();

    public void StashPendingDespawn(ulong steamId, ActiveTransform state) => _pendingDespawns[steamId] = state;
    public IEnumerable<ActiveTransform> AllPendingDespawns() => _pendingDespawns.Values;
    public void ClearDrainedPendingDespawns()
    {
        foreach (var kv in _pendingDespawns)
        {
            if (kv.Value.DespawnQueue == null || kv.Value.DespawnQueue.Count == 0)
            {
                _pendingDespawns.TryRemove(kv.Key, out _);
            }
        }
    }

    // --- Cooldowns (runtime) ---
    // v0.39.0: cooldowns are now bucketed by TransformCategory (Regular/VBlood/ShardBoss)
    // so a shard-boss cooldown doesn't lock out regular V-Blood transforms and vice-versa.
    ConcurrentDictionary<ulong, System.DateTime> CooldownDict(TransformCategory cat) => cat switch
    {
        TransformCategory.ShardBoss => _cooldownShardUntil,
        TransformCategory.VBlood => _cooldownVBloodUntil,
        _ => _cooldownRegularUntil,
    };

    public System.DateTime CooldownUntil(ulong steamId, TransformCategory category)
        => CooldownDict(category).TryGetValue(steamId, out var ts) ? ts : System.DateTime.MinValue;

    public void SetCooldownUntil(ulong steamId, TransformCategory category, System.DateTime untilUtc)
        => CooldownDict(category)[steamId] = untilUtc;

    // --- Escalating pity / bad-luck protection (v0.38.0, in-memory) ---
    // Per-player accumulated drop-chance bonus, indexed [source*2 + kind]:
    //   0 Regular+Ability · 1 Regular+Transform · 2 VBlood+Ability · 3 VBlood+Transform.
    // Bumped on a kill whose roll of that kind failed; reset to 0 on a success — so a
    // dry streak gradually raises the chance until it pays out, then drops to baseline.
    // v0.44.0: PERSISTED across restarts (PitySnapshot / LoadPity ↔ PersistenceService),
    // so a long dry streak isn't wiped by a server reboot.
    readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, float[]> _pity = new();

    static int PityIndex(CaptureSource source, PityKind kind)
        => ((source == CaptureSource.VBlood) ? 2 : 0) + (int)kind;

    public float GetPityBonus(ulong steamId, CaptureSource source, PityKind kind)
        => _pity.TryGetValue(steamId, out var arr) ? arr[PityIndex(source, kind)] : 0f;

    public void BumpPity(ulong steamId, CaptureSource source, PityKind kind, float step, float max)
    {
        if (step <= 0f) return;
        var arr = _pity.GetOrAdd(steamId, _ => new float[4]);
        int i = PityIndex(source, kind);
        float v = arr[i] + step;
        if (max > 0f && v > max) v = max;
        arr[i] = v;
    }

    public void ResetPity(ulong steamId, CaptureSource source, PityKind kind)
    {
        if (_pity.TryGetValue(steamId, out var arr)) arr[PityIndex(source, kind)] = 0f;
    }

    /// <summary>v0.44.0: snapshot per-player pity arrays for persistence (skips all-zero players).</summary>
    public IEnumerable<KeyValuePair<ulong, float[]>> PitySnapshot()
    {
        foreach (var (sid, arr) in _pity)
        {
            bool any = false;
            foreach (var v in arr) if (v != 0f) { any = true; break; }
            if (any) yield return new KeyValuePair<ulong, float[]>(sid, (float[])arr.Clone());
        }
    }

    /// <summary>v0.44.0: restore a player's pity array from persistence (length-tolerant).</summary>
    public void LoadPity(ulong steamId, float[] values)
    {
        if (values == null || values.Length == 0) return;
        var arr = _pity.GetOrAdd(steamId, _ => new float[4]);
        for (int i = 0; i < arr.Length && i < values.Length; i++) arr[i] = values[i];
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
                Add(steamId, ability.UnitPrefabGuid, ability.AbilityPrefabGuid, ability.Source);
            }
        }
    }
}
