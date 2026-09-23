using System;
using System.Collections.Generic;
using System.Linq;
using Beelzebub.Logic;
using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.135.0 — admin-defined INCOMPATIBILITY LOCKS (exclusion groups) wired to the game.
///
/// A group ("at most Max of these abilities in one player's active loadout") lives in
/// <c>ability_rules.json → ExclusionGroups</c>, ships empty, and is resolved by the pure
/// <see cref="ExclusionResolver"/>. The ACTIVE loadout is one canonical list: the active bar source (form bar when
/// in a form, saddle bar when mounted, else the resolved weapon bar) in slot order, then hotkeys in ordinal name
/// order — earlier wins. Every enforcement point (slot/weapon/form grant, hotkey set, bar resolve, form/saddle
/// apply, `.beelz cast`) goes through here, so they all agree.
///
/// Suppression never touches the player's saved binds: a suppressed ability just isn't put on the live bar (and
/// can't be force-cast) while the conflict exists, and comes back by itself when the lock is removed. Transforms
/// (whole boss kits) are exempt.
/// </summary>
internal static class ExclusionService
{
    // ---- resolved groups (rebuilt when the rules snapshot changes or on Invalidate) ------------------------
    static List<ExclusionGroupDef> _groups = new();
    static object _builtFor;
    static int _version, _builtVersion = -1;
    static readonly HashSet<string> _warned = new(StringComparer.OrdinalIgnoreCase);

    public static void Invalidate() => _version++;

    public static IReadOnlyList<ExclusionGroupDef> Groups
    {
        get
        {
            var cur = Core.AbilityRules?.Current;
            if (cur == null) return Array.Empty<ExclusionGroupDef>();
            if (!ReferenceEquals(cur, _builtFor) || _builtVersion != _version)
            {
                _groups = Build(cur.ExclusionGroups);
                _builtFor = cur;
                _builtVersion = _version;
            }
            return _groups;
        }
    }

    public static bool Any => Groups.Count > 0;

    static List<ExclusionGroupDef> Build(Dictionary<string, AbilityRules.ExclusionGroupDto> raw)
    {
        var list = new List<ExclusionGroupDef>();
        if (raw == null) return list;
        foreach (var (name, g) in raw)
        {
            if (g == null || string.IsNullOrWhiteSpace(name)) continue;
            var def = new ExclusionGroupDef { Name = name.Trim(), Max = Math.Max(1, g.Max) };
            foreach (var m in g.Members ?? new List<string>())
            {
                if (TryParseMember(m, out int guid, out string cat))
                {
                    if (guid != 0) def.Guids.Add(guid);
                    else def.Categories.Add(cat);
                }
                else if (_warned.Add(name + "|" + m))
                    Core.Log.LogWarning($"[Beelz LOCK] group '{name}': member '{m}' doesn't match any ability or category — kept in the file but inactive until it resolves.");
            }
            list.Add(def);
        }
        return list;
    }

    /// <summary>Resolve one member token: an ability name / numeric ID → guid, or <c>cat:&lt;AbilityCategory&gt;</c>.</summary>
    public static bool TryParseMember(string raw, out int guid, out string category)
    {
        guid = 0; category = null;
        string m = (raw ?? "").Trim();
        if (m.Length == 0) return false;
        if (m.StartsWith("cat:", StringComparison.OrdinalIgnoreCase))
        {
            string c = m.Substring(4).Trim();
            if (!Enum.TryParse<AbilityCategory>(c, ignoreCase: true, out var ac)) return false;
            category = ac.ToString();
            return true;
        }
        string key = AbilityRules.ResolveAbilityKey(m);
        if (key == null) return false;
        guid = AbilityRules.ResolveAbilityGuid(key);
        return guid != 0;
    }

    /// <summary>The category a guid is counted under (admin override, else the name heuristic).</summary>
    public static string CategoryOf(int guid)
    {
        string name = new PrefabGUID(guid).GetPrefabName();
        var cat = Core.AbilityRules.GetAbilityCategoryOverride(name) ?? Categorization.ClassifyAbility(name);
        return cat.ToString();
    }

    // ---- canonical loadout --------------------------------------------------------------------------------
    public static string SlotKey(int slot) => "slot:" + slot;
    public static string HotkeyKey(string name) => "hotkey:" + name;
    public const string CastKey = "cast:";

    /// <summary>Which bar source is active for this character, and its slot → ability map.</summary>
    public static (string src, Dictionary<int, int> bar) ActiveBar(Entity character)
    {
        // Built by the SAME helpers the appliers use, so a check sees exactly the bar that would render.
        ulong steamId = character.GetSteamId();
        if (ShapeshiftAbilityService.IsMounted(steamId))
            return ("mount", ShapeshiftAbilityService.BuildMountedBar(steamId));
        var form = ShapeshiftAbilityService.GetCurrentForm(character);
        if (form != ShapeshiftForm.None && Beelzebub.Config.Settings.Forms_CustomAbilities_Enabled.Value)
            return ("form", ShapeshiftAbilityService.BuildFormBar(steamId, form, out _));
        var bar = new Dictionary<int, int>();
        var weapon = SlotApply.CurrentWeapon(character);
        foreach (var (s, e) in Core.AbilityRegistry.GetSlotsResolvedWithOrigin(steamId, weapon))
        {
            if (e.abilityGuid == 0 || !AbilityRegistry.IsValidSlot(s)) continue;
            bool ok = e.weaponSpecific ? SlotApply.IsGrantUsable(e.abilityGuid, weapon) : SlotApply.IsGrantCompatible(e.abilityGuid, weapon);
            if (ok) bar[s] = e.abilityGuid;
        }
        return ("slot", bar);
    }

    /// <summary>
    /// Apply the locks to a bar that's about to be rendered (<paramref name="src"/> = slot|form|mount): returns the
    /// slots that survive, and announces suppression changes. Hotkeys count toward the groups but come after the
    /// bar, so a bar ability always wins over a hotkey.
    /// Notices SEND CHAT (a structural change — it creates entities): a caller holding a live DynamicBuffer handle
    /// must pass <paramref name="deferred"/> and run the queued actions after it is done with the buffer.
    /// </summary>
    public static Dictionary<int, int> FilterBar(Entity character, string src, Dictionary<int, int> bar, List<Action> deferred = null)
    {
        void Notify(IEnumerable<Suppression> sup, ExclusionResult res, List<LoadoutBinding> lo)
        {
            var list = sup.ToList();
            if (deferred != null) deferred.Add(() => Report(character, src, list, res, lo));
            else Report(character, src, list, res, lo);
        }
        if (!Any || bar == null || bar.Count == 0)
        {
            if (bar != null && _last.Count > 0) Notify(Array.Empty<Suppression>(), new ExclusionResult(), new List<LoadoutBinding>());
            return bar ?? new Dictionary<int, int>();
        }
        ulong steamId = character.GetSteamId();
        var loadout = Loadout(steamId, bar);
        var r = Resolve(loadout);
        var kept = new Dictionary<int, int>();
        foreach (var (s, a) in bar) if (r.IsKept(SlotKey(s))) kept[s] = a;
        Notify(r.Suppressed.Where(x => x.Key.StartsWith("slot:", StringComparison.Ordinal)), r, loadout);
        return kept;
    }

    /// <summary>Bar (slot order) then hotkeys (ordinal name order), optionally overriding one hotkey.</summary>
    public static List<LoadoutBinding> Loadout(ulong steamId, IReadOnlyDictionary<int, int> bar,
        string hotkeyOverride = null, int hotkeyGuid = 0)
    {
        var list = new List<LoadoutBinding>();
        foreach (var s in bar.Keys.OrderBy(k => k)) list.Add(new LoadoutBinding(SlotKey(s), bar[s]));
        var hot = new Dictionary<string, int>(Core.AbilityRegistry.ListHotkeys(steamId), StringComparer.Ordinal);
        if (hotkeyOverride != null) hot[hotkeyOverride] = hotkeyGuid;
        foreach (var n in hot.Keys.OrderBy(k => k, StringComparer.Ordinal)) list.Add(new LoadoutBinding(HotkeyKey(n), hot[n]));
        return list;
    }

    public static ExclusionResult Resolve(List<LoadoutBinding> loadout) => ExclusionResolver.Resolve(loadout, Groups, CategoryOf);

    /// <summary>Human text for a refusal/suppression: which group, its max, and what it's already holding.</summary>
    public static string Explain(Suppression s, ExclusionResult r, List<LoadoutBinding> loadout)
    {
        var g = Groups.FirstOrDefault(x => string.Equals(x.Name, s.Primary, StringComparison.OrdinalIgnoreCase));
        var holders = new List<string>();
        if (g != null)
            foreach (var b in loadout)
                if (b.Guid != s.Guid && r.KeptGuids.Contains(b.Guid) && g.Contains(b.Guid, CategoryOf))
                {
                    string n = Pretty(b.Guid);
                    if (!holders.Contains(n)) holders.Add(n);
                }
        return $"{Pretty(s.Guid)} is locked by group '{s.Primary}' (max {g?.Max ?? 1})" +
               (holders.Count > 0 ? $" — kept {string.Join(", ", holders)}" : "") +
               (s.Groups.Count > 1 ? $" (also: {string.Join(", ", s.Groups.Skip(1))})" : "");
    }

    static string Pretty(int guid)
    {
        var meta = Core.AbilityMetadata?.Resolve(guid);
        return !string.IsNullOrEmpty(meta?.Name) ? meta.Name : new PrefabGUID(guid).GetPrefabName();
    }

    /// <summary>
    /// Prospective check for ONE new binding: <paramref name="bar"/> must already contain the candidate (for a slot)
    /// or pass it via <paramref name="hotkeyOverride"/>. Returns null when allowed, else the player-facing reason.
    /// </summary>
    public static string CheckProspective(ulong steamId, IReadOnlyDictionary<int, int> bar, string candidateKey,
        string hotkeyOverride = null, int hotkeyGuid = 0)
    {
        if (!Any) return null;
        var loadout = Loadout(steamId, bar, hotkeyOverride, hotkeyGuid);
        var r = Resolve(loadout);
        var s = r.Find(candidateKey);
        return s == null ? null : Explain(s, r, loadout);
    }

    /// <summary>
    /// Command-time refusal for binding <paramref name="guid"/> to <paramref name="slot"/> on <paramref name="bucket"/>
    /// (the bar that bucket produces, with the candidate placed). Null = allowed.
    /// </summary>
    public static string CheckBind(ulong steamId, IReadOnlyDictionary<int, int> bucket, int slot, int guid)
    {
        if (!Any) return null;
        var bar = new Dictionary<int, int>();
        if (bucket != null) foreach (var (s, a) in bucket) if (a != 0) bar[s] = a;
        bar[slot] = guid;
        return CheckProspective(steamId, bar, SlotKey(slot));
    }

    /// <summary>Command-time refusal for binding a hotkey against the player's live bar. Null = allowed.</summary>
    public static string CheckHotkey(Entity character, string hotkey, int guid)
    {
        if (!Any) return null;
        var (_, bar) = ActiveBar(character);
        return CheckProspective(character.GetSteamId(), bar, HotkeyKey(hotkey), hotkey, guid);
    }

    /// <summary>`.beelz cast`: may this ability be force-cast now? Null = allowed, else the reason.</summary>
    public static string CheckCast(Entity character, int abilityGuid)
    {
        if (!Any) return null;
        ulong steamId = character.GetSteamId();
        if (Core.AbilityRegistry.GetActiveTransform(steamId) is not null) return null;   // transforms are exempt
        var (_, bar) = ActiveBar(character);
        var loadout = Loadout(steamId, bar);
        string key = null;
        foreach (var b in loadout) if (b.Guid == abilityGuid) { key = b.Key; break; }
        if (key == null) { key = CastKey + abilityGuid; loadout.Add(new LoadoutBinding(key, abilityGuid)); }
        var r = Resolve(loadout);
        var s = r.Find(key);
        if (s == null) return null;
        Emit(character, "cast", s.Key, s.Guid, s.Primary, locked: true);
        return Explain(s, r, loadout);
    }

    // ---- transition-deduped notices ------------------------------------------------------------------------
    // Per player + source: key → (guid, group) currently suppressed. Only CHANGES are announced.
    static readonly Dictionary<(ulong, string), Dictionary<string, (int guid, string group)>> _last = new();

    /// <summary>
    /// Announce suppression changes for one bar source after a batch resolve. <paramref name="suppressed"/> holds
    /// the bindings of THIS source that were kept off the bar.
    /// </summary>
    public static void Report(Entity character, string src, IEnumerable<Suppression> suppressed, ExclusionResult r, List<LoadoutBinding> loadout)
    {
        ulong steamId = character.GetSteamId();
        if (steamId == 0) return;
        // Only the ACTIVE bar source keeps notice state: leaving a form/mount drops the old source's state, so
        // returning to it re-announces instead of staying silent (or unlocking later against the wrong bar).
        if (src is "slot" or "form" or "mount")
        {
            List<(ulong, string)> stale = null;
            foreach (var k in _last.Keys)
                if (k.Item1 == steamId && k.Item2 != src && k.Item2 is "slot" or "form" or "mount") (stale ??= new()).Add(k);
            if (stale != null) foreach (var k in stale) _last.Remove(k);
        }
        var now = new Dictionary<string, (int, string)>(StringComparer.Ordinal);
        foreach (var s in suppressed) now[s.Key] = (s.Guid, s.Primary);
        _last.TryGetValue((steamId, src), out var before);
        before ??= new Dictionary<string, (int, string)>();
        foreach (var (k, v) in now)
        {
            if (before.TryGetValue(k, out var old) && old.Equals(v)) continue;
            var sup = r.Find(k);
            string text = sup != null ? Explain(sup, r, loadout) : $"{Pretty(v.Item1)} is locked by group '{v.Item2}'";
            try { Core.Chat.Send(character, Verbosity.Summary, $"🔒 {text}. Unslot the other one or ask an admin (.beelz admin lock list)."); } catch { }
            Emit(character, src, k, v.Item1, v.Item2, locked: true);
        }
        foreach (var (k, v) in before)
        {
            if (now.ContainsKey(k)) continue;
            try { Core.Chat.Send(character, Verbosity.Summary, $"🔓 {Pretty(v.guid)} is no longer locked."); } catch { }
            Emit(character, src, k, v.guid, v.group, locked: false);
        }
        if (now.Count == 0) _last.Remove((steamId, src));
        else _last[(steamId, src)] = now;
    }

    static void Emit(Entity character, string src, string key, int guid, string group, bool locked)
    {
        string k = key.Contains(':') ? key.Substring(key.IndexOf(':') + 1) : key;
        try
        {
            Core.Chat.SendEvent(character,
                $"[BEELZ:event] type=ability-locked src={src} key={Token(k)} a={guid} group={Token(group)} locked={(locked ? 1 : 0)}");
        }
        catch { }
    }

    static string Token(string s) => string.IsNullOrEmpty(s) ? "-" : s.Replace(' ', '_').Replace('=', '-');

    public static void OnDisconnect(ulong steamId)
    {
        List<(ulong, string)> dead = null;
        foreach (var k in _last.Keys) if (k.Item1 == steamId) (dead ??= new()).Add(k);
        if (dead != null) foreach (var k in dead) _last.Remove(k);
    }

    // ---- re-resolve every online player after a lock change ----------------------------------------------
    static EntityQuery _players;
    static bool _playersReady;

    /// <summary>Re-apply every connected player's weapon bar, and their live form/saddle bar if they're in one.</summary>
    public static int ReapplyAllOnline()
    {
        int n = 0;
        try
        {
            if (!_playersReady)
            {
                _players = Core.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerCharacter>());
                _playersReady = true;
            }
            var chars = _players.ToEntityArray(Unity.Collections.Allocator.Temp);
            try
            {
                for (int i = 0; i < chars.Length; i++)
                {
                    Entity c = chars[i];
                    if (!c.Exists() || !c.TryGetComponent<PlayerCharacter>(out var pc)) continue;
                    if (!pc.UserEntity.TryGetComponent<User>(out var user) || !user.IsConnected) continue;
                    try { ReapplyOne(c); n++; }
                    catch (Exception ex) { Core.Log.LogWarning($"[Beelz LOCK] re-resolve failed for {c.GetSteamId()}: {ex.Message}"); }
                }
            }
            finally { chars.Dispose(); }
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz LOCK] ReapplyAllOnline failed: {ex.Message}"); }
        return n;
    }

    public static void ReapplyOne(Entity character)
    {
        SlotApply.RestoreResolvedGrants(character);
        if (!Core.EntityManager.HasBuffer<BuffBuffer>(character)) return;
        var buffs = Core.EntityManager.GetBuffer<BuffBuffer>(character);
        Entity formBuff = Entity.Null, mountBuff = Entity.Null;
        int formGuid = 0;
        for (int i = 0; i < buffs.Length; i++)
        {
            var pg = buffs[i].PrefabGuid;
            string nm = pg.GetPrefabName();
            if (formBuff == Entity.Null && ShapeshiftAbilityService.IsSupportedForm(pg._Value, nm)) { formBuff = buffs[i].Entity; formGuid = pg._Value; }
            if (mountBuff == Entity.Null && ShapeshiftAbilityService.IsMountBuff(pg._Value, nm)) mountBuff = buffs[i].Entity;
        }
        // (buffer handle is not used past this point — the applies below are structural)
        if (formBuff.Exists()) ShapeshiftAbilityService.ApplyFormLoadout(formBuff, character, formGuid);
        if (mountBuff.Exists() && ShapeshiftAbilityService.IsMounted(character.GetSteamId()))
            ShapeshiftAbilityService.ApplyMountedLoadout(mountBuff, character);
    }

    /// <summary>`lock check`: a readable report of a player's active loadout against every group.</summary>
    public static List<string> Describe(Entity character)
    {
        var lines = new List<string>();
        ulong steamId = character.GetSteamId();
        var (src, bar) = ActiveBar(character);
        var loadout = Loadout(steamId, bar);
        var r = Resolve(loadout);
        lines.Add($"active bar: {src} ({bar.Count} bound) + {loadout.Count - bar.Count} hotkey(s); {Groups.Count} lock group(s)");
        if (r.Suppressed.Count == 0) lines.Add("no locked abilities.");
        foreach (var s in r.Suppressed) lines.Add($"  {s.Key}: {Explain(s, r, loadout)}");
        return lines;
    }
}
