using System;
using System.Collections.Generic;
using System.Text;
using ProjectM;
using ProjectM.Gameplay.Scripting;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.46.0 — per-ability BAKED tuning (cast interruption, movement, cooldown, range, charges, AoE,
/// projectile speed, leap height, effect duration, healing).
///
/// These live on an ability's GROUP / CAST / spawned prefabs. Players and admins curate by ability-GROUP
/// name (what <c>.beelz list</c> shows). v0.132.0: the tuner now walks the ONE chain graph
/// (<see cref="AbilityChainGraph"/>) from the curated group instead of a name-stem scan plus two ad-hoc
/// walkers, and every write passes the SHARED-PREFAB GUARD (<see cref="Allowed"/>): a prefab that another
/// ability or boss also reaches is not edited unless all owners agree or the entry opts in with
/// <c>AllowGlobalSharedEdit</c> on a fully-decoded chain.
///
/// The edit is GLOBAL (the prefab is shared with the original NPC/boss), config-gated behind
/// <c>Abilities_ApplyConfig</c>, and re-applied on <c>.beelz admin reload</c>. Every write captures the
/// shipped value first (<see cref="CaptureOriginal"/>) so `defaults` / reload / restart can undo it.
/// No structural prefab edits (Add/RemoveComponent) — see docs/ABILITY_CHANGE_IMPACT.md §4b. v0.132.0
/// moved <c>forcetimeout</c> to a runtime instance timer (<see cref="CastHistoryService"/>).
/// </summary>
internal static class AbilityTuningService
{
    /// <summary>
    /// Apply every curated tuning entry to its ability chain. No-op unless <c>Abilities_ApplyConfig</c>.
    /// Called at init (after the prefab map + rules are ready) and on <c>.beelz admin reload</c>.
    /// Fully guarded — never throws into the caller.
    /// </summary>
    public static int ApplyAll()
    {
        if (!Beelzebub.Config.Settings.Abilities_ApplyConfig.Value) return 0;
        if (!AbilityChainGraph.Built) AbilityChainGraph.Build();
        bool graphOk = AbilityChainGraph.Built;
        if (!graphOk)
            Core.Log.LogError("[Beelz TUNE] ability chain graph unavailable — curated baked tuning SKIPPED (fail closed). Check the [Beelz GRAPH] error above.");

        // v0.134.0 (P3): cooldowns are no longer baked (they were written onto the boss's own prefab). The
        // per-ability `cooldown`/`cooldownscale` and the global Grant_MinimumCooldownSeconds floor are enforced at
        // runtime on captured slot casts only (AbilityCooldownEnforcer), so bosses keep their shipped cooldowns.

        // v0.69.0: heal any GUID-keyed entries (pre-fix `.beelz admin ability <guid> ...`) to their
        // prefab-name key so the matcher below can find them. Cheap no-op once migrated.
        Core.AbilityRules?.NormalizeNumericKeys();

        var map = Core.AbilityRules?.Current?.AbilityMap;

        // v0.132.0: resolve each entry that requests a BAKED change to its root prefab and walk the chain
        // graph from it. Requests are indexed per prefab first so the shared-prefab guard can see every
        // entry that wants to write a given prefab before any write happens (order-independent).
        var nameToGuid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in Core.PrefabNames) if (!string.IsNullOrEmpty(kv.Value)) nameToGuid.TryAdd(kv.Value, kv.Key);
        _requests.Clear();
        _decisions.Clear();
        _skipped.Clear();
        var tuned = new List<(string key, int root, AbilityRules.AbilityEntry entry, AbilityChainGraph.Chain chain)>();
        if (map != null && graphOk)
            foreach (var kv in map)
            {
                var e = kv.Value;
                if (e == null || !WantsBaked(e)) continue;
                if (!nameToGuid.TryGetValue(kv.Key, out int root))
                {
                    // v0.70.0: almost always a wrong ability name/ID — helps admins self-diagnose.
                    Core.Log.LogWarning($"[Beelz TUNE] tuned entry '{kv.Key}' matched NO ability prefab — check the ability name/ID (use .beelz list / api list).");
                    continue;
                }
                var chain = AbilityChainGraph.GetOrWalk(root);
                tuned.Add((kv.Key, root, e, chain));
                foreach (var n in chain.Nodes)
                {
                    if (!_requests.TryGetValue(n.Guid, out var list)) _requests[n.Guid] = list = new List<(int, AbilityRules.AbilityEntry)>();
                    list.Add((root, e));
                }
            }
        if (tuned.Count == 0)
        {
            Core.Log.LogInfo("[Beelz TUNE] Abilities_ApplyConfig is on, but no AbilityMap entries request baked tuning.");
            return 0;
        }

        int prefabsTuned = 0;
        try
        {
            // Curated entries, in root-GUID order so logs and outcomes are deterministic.
            tuned.Sort((a, b) => a.root.CompareTo(b.root));
            foreach (var t in tuned)
                prefabsTuned += ApplyChain(t.key, t.root, t.entry, t.chain);
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz TUNE] apply failed: {ex.Message}");
        }

        if (_skipped.Count > 0)
            Core.Log.LogWarning($"[Beelz TUNE] {_skipped.Count} baked write(s) SKIPPED on prefabs shared with other abilities/bosses (or in a chain that isn't fully decoded). `.beelz admin ability-inspect <ability>` shows where; `allowglobalsharededit on` opts in.");
        if (Beelzebub.Config.Settings.VerboseLogging.Value)   // v0.95.0: gate the batch summary behind verbose (BCH never reads it)
            Core.Log.LogInfo($"[Beelz TUNE] applied tuning to {prefabsTuned} prefab(s) from {tuned.Count} curated entry(ies).");
        return prefabsTuned;
    }

    /// <summary>
    /// v0.120.0: restore every tuned prefab to its shipped baseline, THEN re-apply the current rules — so a
    /// reload/edit is fully idempotent. <see cref="ApplyAll"/> alone only WRITES fields that still have a value,
    /// so LOWERING or CLEARING a tuned field left the previously-baked value in place until a server restart.
    /// Restoring first computes the net result from baseline and from ALL owning rules every time.
    /// </summary>
    public static int ReapplyAll()
    {
        RestoreAll();        // back to shipped baseline (no-op for anything never tuned this session)
        return ApplyAll();   // re-bake the current rules from that clean baseline
    }

    /// <summary>Does this entry request any BAKED prefab change? (forcetimeout is runtime since v0.132.0.)</summary>
    static bool WantsBaked(AbilityRules.AbilityEntry e)
        => e.Interruptible != null || e.FreeMoveAfterCast || e.CastMovementSpeed != null
           || e.FreeMoveAfterSeconds != null || e.InterruptOnHit != null
           || e.MaxRangeOverride != null
           || e.ChargesMax != null || e.ChargeTimeSeconds != null
           || e.AoeRadius != null || e.ProjectileSpeed != null || e.LeapHeight != null
           || e.EffectDurationSeconds != null || e.HealingMultiplier != null
           || e.MaxStacks != null || e.ProjectileCount != null || e.KnockbackScale != null
           || e.LifetimeSeconds != null || e.CastTimeScale != null;   // v0.134.0

    // ---------------------------------------------------------------------
    // v0.132.0 (P1 step 6) — SHARED-PREFAB GUARD.
    //   * exclusive to the tuned ability → write.
    //   * owned by a chain that isn't fully decoded (depth cut / decode failure) → never (no override).
    //   * shared, and the requesting entries disagree on the value → rejected for ALL of them.
    //   * shared, every owner requests the same value → write.
    //   * shared, a requester set AllowGlobalSharedEdit → write, logging every other group it changes.
    //   * otherwise → skip + log.
    // Decisions are cached per (prefab, field) for the run.
    // ---------------------------------------------------------------------
    static readonly Dictionary<int, List<(int root, AbilityRules.AbilityEntry entry)>> _requests = new();
    static readonly Dictionary<(int prefab, string field, int root), bool> _decisions = new();
    static readonly Dictionary<(int prefab, string field), string> _skipped = new();

    /// <summary>Skipped writes of the last apply for one ability's chain (for ability-inspect).</summary>
    public static IEnumerable<string> SkippedFor(AbilityChainGraph.Chain chain)
    {
        foreach (var kv in _skipped)
            if (chain.NodeSet.Contains(kv.Key.prefab))
                yield return $"{kv.Key.field} on {AbilityChainGraph.Name(kv.Key.prefab)}: {kv.Value}";
    }

    /// <summary>Field value an entry requests (null = not requested). Bools map to 0/1.</summary>
    static float? RequestedValue(AbilityRules.AbilityEntry e, string field) => field switch
    {
        "cooldown" => e.CooldownSeconds,
        "range" => e.MaxRangeOverride,
        "charges" => e.ChargesMax,
        "chargetime" => e.ChargeTimeSeconds,
        "aoe" => e.AoeRadius,
        "projspeed" => e.ProjectileSpeed,
        "leapheight" => e.LeapHeight,
        "duration" => e.EffectDurationSeconds,
        "healing" => e.HealingMultiplier,
        "interruptible" => e.Interruptible.HasValue ? (e.Interruptible.Value ? 1f : 0f) : null,
        "interruptonhit" => e.InterruptOnHit.HasValue ? (e.InterruptOnHit.Value ? 1f : 0f) : null,
        "freemove" => e.FreeMoveAfterCast ? 1f : null,
        "freelymove" => e.FreeMoveAfterSeconds,
        "castspeed" => e.CastMovementSpeed,
        "maxstacks" => e.MaxStacks,
        "projcount" => e.ProjectileCount,
        "knockback" => e.KnockbackScale,
        "lifetime" => e.LifetimeSeconds,
        "casttime" => e.CastTimeScale,
        _ => null,
    };

    /// <summary>May <paramref name="root"/>'s entry write <paramref name="field"/> onto prefab <paramref name="guid"/>?
    /// Also rejects a value outside the field table's range (hand-edited JSON).</summary>
    static bool Allowed(int guid, string field, int root, AbilityRules.AbilityEntry entry, string prefabName, StringBuilder log)
    {
        var v = RequestedValue(entry, field);
        if (v.HasValue && !AbilityFieldTable.IsValidValue(field, v.Value))
        {
            log.Append($" {field}=IGNORED(out of range {v.Value})");
            return false;
        }
        if (root == 0) return true;   // global floor pass (not a curated chain)
        if (!_decisions.TryGetValue((guid, field, root), out bool ok))
        {
            ok = Decide(guid, field, root, prefabName, out string reason);
            _decisions[(guid, field, root)] = ok;
            if (!ok)
            {
                _skipped[(guid, field)] = reason;
                if (Beelzebub.Config.Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz TUNE] skip {field} on {prefabName}: {reason}");
            }
        }
        if (!ok) log.Append($" {field}=SKIP(shared)");
        return ok;
    }

    static bool Decide(int guid, string field, int root, string prefabName, out string reason)
    {
        reason = null;
        if (AbilityChainGraph.IsUnitGuid(guid)) { reason = "unit prefab"; return false; }
        if (TryPrefab(guid, out Entity pe) && AbilityChainGraph.IsInUnsafeClass(pe, out string unsafeWhy)) { reason = unsafeWhy + " — no override"; return false; }
        bool complete = AbilityChainGraph.IsOwnershipComplete(guid) && AbilityChainGraph.GetOrWalk(root).Complete;
        if (!complete) { reason = "chain not fully decoded — shared edits disabled (no override)"; return false; }
        var others = AbilityChainGraph.OtherOwners(guid, root);
        if (others.Count == 0) return true;   // exclusive to this ability

        var requesters = new Dictionary<int, float>();
        bool optIn = false;
        if (_requests.TryGetValue(guid, out var reqs))
            foreach (var (r, e) in reqs)
            {
                var v = RequestedValue(e, field);
                if (!v.HasValue) continue;
                requesters[r] = v.Value;
                if (e.AllowGlobalSharedEdit) optIn = true;
            }
        if (new HashSet<float>(requesters.Values).Count > 1)
        {
            var names = new List<string>();
            foreach (var r in requesters.Keys) names.Add(AbilityChainGraph.Name(r));
            names.Sort(StringComparer.Ordinal);
            reason = "CONFLICT between " + string.Join(", ", names);
            Core.Log.LogWarning($"[Beelz TUNE] {field} on shared prefab {prefabName} rejected for ALL requesters — conflicting values from {string.Join(", ", names)}.");
            return false;
        }
        bool allOwnersAgree = true;
        foreach (int g in AbilityChainGraph.Owners(guid)) if (!requesters.ContainsKey(g)) { allOwnersAgree = false; break; }
        if (allOwnersAgree) return true;
        if (optIn)
        {
            var affected = new List<string>();
            foreach (int g in others) if (!requesters.ContainsKey(g)) affected.Add(AbilityChainGraph.Name(g));
            Core.Log.LogWarning($"[Beelz TUNE] allowglobalsharededit: {field} on {prefabName} ALSO changes {affected.Count} other ability group(s): {string.Join(", ", affected)}");
            return true;
        }
        reason = $"shared with {others.Count} other group(s), e.g. {AbilityChainGraph.Name(others[0])}";
        return false;
    }

    /// <summary>Apply one entry's baked fields across its chain. Returns the number of prefabs changed.</summary>
    static int ApplyChain(string key, int root, AbilityRules.AbilityEntry entry, AbilityChainGraph.Chain chain)
    {
        int n = 0;
        // Near set = the group, its casts (StartAbilities) and the casts' direct spawns (on cast AND on start-cast) — where cast-level
        // and "near" components live. Deeper chain nodes get only the ability-specific-component (deep-safe)
        // fields, so duration/free-move never reach a generic buff further down (docs/ADMIN_COMMANDS_AUDIT.md §A).
        var casts = new HashSet<int>();
        foreach (var node in chain.Nodes)
            if (node.Via == AbilityChainGraph.EdgeKind.StartAbility && node.Parent == root) casts.Add(node.Guid);
        var near = new HashSet<int>(casts) { root };
        foreach (var node in chain.Nodes)
            if ((node.Via == AbilityChainGraph.EdgeKind.SpawnOnCast || node.Via == AbilityChainGraph.EdgeKind.SpawnOnStartCast)
                && casts.Contains(node.Parent)) near.Add(node.Guid);

        foreach (var node in chain.Nodes)
        {
            if (node.IsUnit || !TryPrefab(node.Guid, out Entity prefab)) continue;
            string prefabName = AbilityChainGraph.Name(node.Guid);
            var log = new StringBuilder();
            bool changed = false;
            if (node.Guid == root || casts.Contains(node.Guid))
                changed |= ApplyCastLevel(prefab, node.Guid, prefabName, root, entry, log);
            if (node.Guid != root)
                WriteSpawnedFields(prefab, node.Guid, prefabName, root, entry, log, ref changed, deepSafe: !near.Contains(node.Guid));
            if (changed) n++;
            if (log.Length > 0 && Beelzebub.Config.Settings.VerboseLogging.Value) Core.Log.LogInfo($"[Beelz TUNE] {key} → {prefabName}:{log}");
        }
        return n;
    }

    static bool TryPrefab(int guid, out Entity e)
    {
        e = Entity.Null;
        return guid != 0 && Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(new PrefabGUID(guid), out e) && e.Exists();
    }

    /// <summary>Group/cast-level knobs: max range, charges, interrupt flags, cast movement (cooldown is runtime since v0.134.0).</summary>
    static bool ApplyCastLevel(Entity prefab, int prefabGuid, string prefabName, int root, AbilityRules.AbilityEntry entry, StringBuilder log)
    {
        bool changed = false;

        // v0.134.0 (EXPERIMENTAL): casttime scales MaxCastTime + PostCastTime together, from the captured original.
        // Channels / hold-to-cast / charge abilities are refused (their timing is driven elsewhere).
        if (entry.CastTimeScale.HasValue && prefab.Has<AbilityCastTimeData>())
        {
            string why = CastTimeRefusal(root);
            if (why != null) log.Append($" casttime=REFUSED({why})");
            else if (Allowed(prefabGuid, "casttime", root, entry, prefabName, log))
            {
                try
                {
                    var o = CaptureOriginal(prefabGuid);
                    if (o.CastTime == null && prefab.TryGetComponent<AbilityCastTimeData>(out var ctCur))
                        o.CastTime = (ctCur.MaxCastTime._Value, ctCur.PostCastTime._Value);
                    if (o.CastTime is (float baseMax, float basePost))
                    {
                        float m = entry.CastTimeScale.Value;
                        prefab.With((ref AbilityCastTimeData d) =>
                        {
                            d.MaxCastTime._Value = baseMax * m;
                            d.PostCastTime._Value = basePost * m;
                        });
                        log.Append($" casttime x{m:F2}({baseMax * m:F2}+{basePost * m:F2}s)");
                        changed = true;
                    }
                }
                catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] cast-time write failed on {prefabName}: {ex.Message}"); }
            }
        }

        // v0.65.0 (J1): max-range override on the GROUP prefab's AbilityGroupInfo.MaxRange (plain float).
        if (entry.MaxRangeOverride != null && prefab.Has<AbilityGroupInfo>() && Allowed(prefabGuid, "range", root, entry, prefabName, log))
        {
            try
            {
                if (prefab.TryGetComponent<AbilityGroupInfo>(out var gi)) CaptureOriginal(prefabGuid).MaxRange ??= gi.MaxRange;
                float r = entry.MaxRangeOverride.Value;
                prefab.With((ref AbilityGroupInfo d) =>
                    System.Runtime.CompilerServices.Unsafe.AsRef(in d.MaxRange) = r);
                log.Append($" maxrange={r:F1}");
                changed = true;
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] range write failed on {prefabName}: {ex.Message}"); }
        }

        // v0.67.0 (Stage 2): charges (AbilityChargesData on the GROUP prefab). MaxCharges is an int;
        // ChargeUpTime is a ModifiableFloat (write ._Value).
        if (prefab.Has<AbilityChargesData>())
        {
            bool mcOk = entry.ChargesMax.HasValue && Allowed(prefabGuid, "charges", root, entry, prefabName, log);
            bool ctOk = entry.ChargeTimeSeconds.HasValue && Allowed(prefabGuid, "chargetime", root, entry, prefabName, log);
            if (mcOk || ctOk)
            {
                try
                {
                    if (prefab.TryGetComponent<AbilityChargesData>(out var chCur))
                    {
                        var o = CaptureOriginal(prefabGuid);
                        o.ChargesMax ??= chCur.MaxCharges;
                        o.ChargeUpTime ??= chCur.ChargeUpTime._Value;
                    }
                    int? mc = mcOk ? entry.ChargesMax : null; float? ct2 = ctOk ? entry.ChargeTimeSeconds : null;
                    prefab.With((ref AbilityChargesData d) =>
                    {
                        if (mc.HasValue) System.Runtime.CompilerServices.Unsafe.AsRef(in d.MaxCharges) = mc.Value;
                        if (ct2.HasValue) System.Runtime.CompilerServices.Unsafe.AsRef(in d.ChargeUpTime)._Value = ct2.Value;
                    });
                    if (mc.HasValue) log.Append($" charges={mc.Value}");
                    if (ct2.HasValue) log.Append($" chargetime={ct2.Value:F2}s");
                    changed = true;
                }
                catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] charges write failed on {prefabName}: {ex.Message}"); }
            }
        }

        // #3 — cast interruption. AbilityInterruptData.InterruptTypes is a [Flags] enum (readonly in the
        // ref assembly → written via Unsafe.AsRef). Two independent knobs map to two bits so they compose:
        //   Interruptible  → ManualInterrupt (1):  the player can self-cancel the cast (dash / raise shield).
        //   InterruptOnHit → OnDamageTaken  (4):  the cast is cancelled when the CASTER takes damage.
        if (prefab.Has<AbilityInterruptData>())
        {
            bool? manual = entry.Interruptible.HasValue && Allowed(prefabGuid, "interruptible", root, entry, prefabName, log) ? entry.Interruptible : null;
            bool? onHit = entry.InterruptOnHit.HasValue && Allowed(prefabGuid, "interruptonhit", root, entry, prefabName, log) ? entry.InterruptOnHit : null;
            if (manual.HasValue || onHit.HasValue)
            {
                try
                {
                    if (prefab.TryGetComponent<AbilityInterruptData>(out var aiCur)) CaptureOriginal(prefabGuid).Interrupt ??= aiCur.InterruptTypes;
                    prefab.With((ref AbilityInterruptData d) =>
                    {
                        var v = d.InterruptTypes;
                        if (manual.HasValue) v = manual.Value ? (v | InterruptTypes.ManualInterrupt) : (v & ~InterruptTypes.ManualInterrupt);
                        if (onHit.HasValue) v = onHit.Value ? (v | InterruptTypes.OnDamageTaken) : (v & ~InterruptTypes.OnDamageTaken);
                        System.Runtime.CompilerServices.Unsafe.AsRef(in d.InterruptTypes) = v;
                    });
                    if (manual.HasValue) log.Append(manual.Value ? " interrupt=manual" : " interrupt=no-manual");
                    if (onHit.HasValue) log.Append(onHit.Value ? " interrupt=onhit" : " interrupt=no-onhit");
                    changed = true;
                }
                catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] interrupt write failed on {prefabName}: {ex.Message}"); }
            }
        }

        // #4 — post-cast movement unlock + cast-movement-speed override.
        // ModifyMovementDuringCastData.{MovementSpeedMultiplier,Duration} are ModifiableFloat fields. Clamping
        // Duration to the cast window (MaxCastTime+PostCastTime) frees the player the moment the cast finishes.
        // v0.87.0: FreeMoveAfterSeconds sets Duration to an EXPLICIT N (free to move N seconds into the cast),
        // which takes precedence over FreeMoveAfterCast's clamp-to-cast-window.
        if (prefab.Has<ModifyMovementDuringCastData>())
        {
            float? explicitDur = entry.FreeMoveAfterSeconds.HasValue && Allowed(prefabGuid, "freelymove", root, entry, prefabName, log) ? entry.FreeMoveAfterSeconds : null;
            bool freeMove = entry.FreeMoveAfterCast && Allowed(prefabGuid, "freemove", root, entry, prefabName, log);
            float? speed = entry.CastMovementSpeed.HasValue && Allowed(prefabGuid, "castspeed", root, entry, prefabName, log) ? entry.CastMovementSpeed : null;
            if (explicitDur.HasValue || freeMove || speed.HasValue)
            {
                float castWindow = 0f;
                if (freeMove && prefab.TryGetComponent<AbilityCastTimeData>(out var ct))
                    castWindow = ct.MaxCastTime._Value + ct.PostCastTime._Value;
                try
                {
                    if (prefab.TryGetComponent<ModifyMovementDuringCastData>(out var mCur))
                    {
                        var o = CaptureOriginal(prefabGuid);
                        o.MoveDuration ??= mCur.Duration._Value;
                        o.MoveSpeed ??= mCur.MovementSpeedMultiplier._Value;
                        o.MoveUseCastDuration ??= mCur.UseCastDuration;
                    }
                    bool clampWindow = !explicitDur.HasValue && freeMove && castWindow > 0f;
                    float window = castWindow;
                    // v0.89.0 FIX: when we set an explicit Duration, ALSO force UseCastDuration=false — otherwise
                    // the engine ignores Duration and locks movement for the WHOLE cast/channel.
                    bool setExplicitDuration = explicitDur.HasValue || clampWindow;
                    prefab.With((ref ModifyMovementDuringCastData m) =>
                    {
                        if (speed.HasValue) m.MovementSpeedMultiplier._Value = speed.Value;
                        if (explicitDur.HasValue) m.Duration._Value = explicitDur.Value;
                        else if (clampWindow) m.Duration._Value = window;
                        if (setExplicitDuration) System.Runtime.CompilerServices.Unsafe.AsRef(in m.UseCastDuration) = false;
                    });
                    if (explicitDur.HasValue) { log.Append($" freelymove={explicitDur.Value:F2}s"); changed = true; }
                    else if (clampWindow) { log.Append($" freemove=1(window={window:F2}s)"); changed = true; }
                    else if (freeMove) log.Append(" freemove=skip(no-cast-time)");
                    if (speed.HasValue) { log.Append($" castspeed={speed.Value:F2}"); changed = true; }
                }
                catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] movement write failed on {prefabName}: {ex.Message}"); }
            }
        }
        return changed;
    }

    /// <summary>
    /// v0.72.0: per-prefab cache of the SHIPPED baseline value of every baked field we mutate, keyed by
    /// prefab GUID. Two jobs: (1) HealingMultiplier is always computed from the original so reloads don't
    /// compound; (2) `.beelz admin ability &lt;id|all&gt; defaults` live-restores these without a server
    /// restart. Captured on first write (prefabs are fresh at server init, so the first capture is the
    /// true baseline); the static dict re-inits on a server restart, when prefabs are fresh again.
    /// </summary>
    sealed class Original
    {
        public float? MaxRange, AoeRange, ProjSpeed, ChargeUpTime;
        public float? ProjRange;   // v0.73.0: Projectile.Range (projectile travel distance)
        public float? TravelHeight; // v0.125.0: TravelBuff.Height (leap/travel apex on the phase buff)
        public float? LifeTime;    // v0.73.0: LifeTime.Duration on a spawned Buff (over-time effect length)
        public int? ChargesMax;
        public Il2CppSystem.Nullable_Unboxed<float>[] Durations;   // per ApplyBuffOnGameplayEvent element
        public (float h, float pct, float perSp)[] Heals;          // per HealOnGameplayEvent element
        public float? HealPerSec;  // v0.120.0: HealingBuff.HealingPerSecond (periodic/AoE/channel heal rate)
        // v0.87.0: cast modifiers — cache so `defaults` can restore them.
        public InterruptTypes? Interrupt;   // AbilityInterruptData.InterruptTypes
        public float? MoveDuration;         // ModifyMovementDuringCastData.Duration._Value
        public float? MoveSpeed;            // ModifyMovementDuringCastData.MovementSpeedMultiplier._Value
        public bool? MoveUseCastDuration;   // ModifyMovementDuringCastData.UseCastDuration (v0.89.0)
        public long? BuffModFlags;
        // v0.134.0 knobs
        public byte? MaxStacks;                       // Buff.MaxStacks
        public Dictionary<string, int> ProjCounts;    // Count per spread component type on this prefab
        public (float range, float dur)[] Knockbacks; // per ApplyKnockbackOnGameplayEvent element
        public (float max, float post)? CastTime;     // AbilityCastTimeData
        public float? SpawnLifeTime;                  // LifeTime.Duration on a NON-buff spawn (projectile/area)   // BuffModificationFlagData.ModificationTypes (exposed as raw long) on a spawned buff (v0.89.1)
    }

    // v0.89.1: BuffModificationTypes.MovementImpair bit (the channel/lock "can't move" flag). The interop
    // field is a raw long, so we mask the numeric bit directly.
    const long MovementImpairFlag = 16L;
    static readonly Dictionary<int, Original> _originals = new();
    static Original CaptureOriginal(int prefabGuid)
    {
        if (!_originals.TryGetValue(prefabGuid, out var o)) { o = new Original(); _originals[prefabGuid] = o; }
        return o;
    }

    /// <summary>
    /// Write the downstream shaping fields onto one chain prefab (cast or spawned) if it carries them.
    /// <paramref name="deepSafe"/>=true (nodes beyond the near set) runs ONLY the ability-specific-component
    /// fields (aoe / projspeed / range / leapheight / healing) — duration, LifeTime and free-move live on
    /// generic buffs and stay near-only. Every write passes the shared-prefab guard.
    /// </summary>
    static void WriteSpawnedFields(Entity sp, int spGuid, string prefabName, int root, AbilityRules.AbilityEntry entry, StringBuilder log, ref bool changed, bool deepSafe = false)
    {
        if (entry.AoeRadius.HasValue && sp.Has<TargetAoE>() && Allowed(spGuid, "aoe", root, entry, prefabName, log))
        {
            try
            {
                if (sp.TryGetComponent<TargetAoE>(out var taCur)) CaptureOriginal(spGuid).AoeRange ??= taCur.MaxRange;
                float r = entry.AoeRadius.Value;
                sp.With((ref TargetAoE a) => System.Runtime.CompilerServices.Unsafe.AsRef(in a.MaxRange) = r);
                log.Append($" aoe={r:F1}");
                changed = true;
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] aoe write failed on {prefabName}: {ex.Message}"); }
        }
        if (entry.ProjectileSpeed.HasValue && sp.Has<Projectile>() && Allowed(spGuid, "projspeed", root, entry, prefabName, log))
        {
            try
            {
                if (sp.TryGetComponent<Projectile>(out var pCur)) CaptureOriginal(spGuid).ProjSpeed ??= pCur.Speed;
                float s = entry.ProjectileSpeed.Value;
                sp.With((ref Projectile p) => System.Runtime.CompilerServices.Unsafe.AsRef(in p.Speed) = s);
                log.Append($" projspeed={s:F1}");
                changed = true;
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] projectile-speed write failed on {prefabName}: {ex.Message}"); }
        }

        // v0.73.0: range → projectile travel distance (Projectile.Range). Pairs with the GROUP's
        // AbilityGroupInfo.MaxRange so a shorter `range` clamps both the aim and the projectile's reach.
        if (entry.MaxRangeOverride.HasValue && sp.Has<Projectile>() && Allowed(spGuid, "range", root, entry, prefabName, log))
        {
            try
            {
                if (sp.TryGetComponent<Projectile>(out var pCur)) CaptureOriginal(spGuid).ProjRange ??= pCur.Range;
                float r = entry.MaxRangeOverride.Value;
                sp.With((ref Projectile p) => System.Runtime.CompilerServices.Unsafe.AsRef(in p.Range) = r);
                log.Append($" projrange={r:F1}");
                changed = true;
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] projectile-range write failed on {prefabName}: {ex.Message}"); }
        }

        // v0.125.0: leapheight → TravelBuff.Height on a leap/travel ability's phase buff.
        if (entry.LeapHeight.HasValue && sp.Has<TravelBuff>() && Allowed(spGuid, "leapheight", root, entry, prefabName, log))
        {
            try
            {
                if (sp.TryGetComponent<TravelBuff>(out var tbCur)) CaptureOriginal(spGuid).TravelHeight ??= tbCur.Height;
                float h = entry.LeapHeight.Value;
                sp.With((ref TravelBuff t) => System.Runtime.CompilerServices.Unsafe.AsRef(in t.Height) = h);
                log.Append($" leapheight={h:F1}");
                changed = true;
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] leap-height write failed on {prefabName}: {ex.Message}"); }
        }

        // v0.68.0 (Stage 2b): ABSOLUTE override of applied buff/debuff durations. ApplyBuffOnGameplayEvent
        // is a BUFFER; OverrideDuration is a Nullable<float>. Set, not multiplied → idempotent.
        if (!deepSafe && entry.EffectDurationSeconds.HasValue && Core.EntityManager.HasBuffer<ApplyBuffOnGameplayEvent>(sp)
            && Allowed(spGuid, "duration", root, entry, prefabName, log))
        {
            try
            {
                float dur = entry.EffectDurationSeconds.Value;
                var durNullable = new Il2CppSystem.Nullable_Unboxed<float>(dur);
                var buf = Core.EntityManager.GetBuffer<ApplyBuffOnGameplayEvent>(sp);
                var o = CaptureOriginal(spGuid);
                if (o.Durations == null)
                {
                    o.Durations = new Il2CppSystem.Nullable_Unboxed<float>[buf.Length];
                    for (int k = 0; k < buf.Length; k++) o.Durations[k] = buf[k].OverrideDuration;
                }
                int n = 0;
                for (int k = 0; k < buf.Length; k++)
                {
                    var e = buf[k];
                    System.Runtime.CompilerServices.Unsafe.AsRef(in e.OverrideDuration) = durNullable;
                    buf[k] = e;
                    n++;
                }
                if (n > 0) { log.Append($" duration={dur:F1}s(x{n})"); changed = true; }
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] duration write failed on {prefabName}: {ex.Message}"); }
        }

        // v0.73.0: duration → over-time/channel effect length on a directly-spawned BUFF's LifeTime.Duration.
        // Guarded to Buff prefabs so a projectile's flight LifeTime is never touched. Value-only edit (the
        // component already exists) — never adds or removes a component.
        if (!deepSafe && entry.EffectDurationSeconds.HasValue && sp.Has<Buff>() && sp.Has<LifeTime>()
            && Allowed(spGuid, "duration", root, entry, prefabName, log))
        {
            try
            {
                if (sp.TryGetComponent<LifeTime>(out var ltCur)) CaptureOriginal(spGuid).LifeTime ??= ltCur.Duration;
                float dur = entry.EffectDurationSeconds.Value;
                sp.With((ref LifeTime lt) => System.Runtime.CompilerServices.Unsafe.AsRef(in lt.Duration) = dur);
                log.Append($" lifetime={dur:F1}s");
                changed = true;
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] lifetime write failed on {prefabName}: {ex.Message}"); }
        }

        // ---- v0.134.0 knobs (near set only; value-only; from captured originals) ----------------------------
        if (!deepSafe && entry.MaxStacks.HasValue && sp.Has<Buff>() && Allowed(spGuid, "maxstacks", root, entry, prefabName, log))
        {
            try
            {
                if (sp.TryGetComponent<Buff>(out var bCur)) CaptureOriginal(spGuid).MaxStacks ??= bCur.MaxStacks;
                byte ms = (byte)Math.Clamp(entry.MaxStacks.Value, 1, 255);
                sp.With((ref Buff b) => System.Runtime.CompilerServices.Unsafe.AsRef(in b.MaxStacks) = ms);
                log.Append($" maxstacks={ms}");
                changed = true;
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] max-stacks write failed on {prefabName}: {ex.Message}"); }
        }

        if (!deepSafe && entry.ProjectileCount.HasValue && HasSpread(sp) && Allowed(spGuid, "projcount", root, entry, prefabName, log))
        {
            int want = entry.ProjectileCount.Value;
            try
            {
                changed |= WriteCount<AbilityProjectileFanOnGameplayEvent_DataServer>(sp, spGuid, "fan", d => d.Count,
                    (ref AbilityProjectileFanOnGameplayEvent_DataServer d, int v) => System.Runtime.CompilerServices.Unsafe.AsRef(in d.Count) = v, want, log);
                changed |= WriteCount<AbilityProjectileFanOnTick_DataServer>(sp, spGuid, "fantick", d => d.Count,
                    (ref AbilityProjectileFanOnTick_DataServer d, int v) => System.Runtime.CompilerServices.Unsafe.AsRef(in d.Count) = v, want, log);
                changed |= WriteCount<Script_MultiShot_Cast_DataServer>(sp, spGuid, "multishot", d => d.Count,
                    (ref Script_MultiShot_Cast_DataServer d, int v) => System.Runtime.CompilerServices.Unsafe.AsRef(in d.Count) = v, want, log);
                changed |= WriteCount<EvenSpreadCluster_DataServer>(sp, spGuid, "cluster", d => d.Count,
                    (ref EvenSpreadCluster_DataServer d, int v) => System.Runtime.CompilerServices.Unsafe.AsRef(in d.Count) = v, want, log);
                changed |= WriteCount<EvenSpreadCluster_Tick_DataServer>(sp, spGuid, "clustertick", d => d.Count,
                    (ref EvenSpreadCluster_Tick_DataServer d, int v) => System.Runtime.CompilerServices.Unsafe.AsRef(in d.Count) = v, want, log);
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] projectile-count write failed on {prefabName}: {ex.Message}"); }
        }

        if (!deepSafe && entry.KnockbackScale.HasValue && Core.EntityManager.HasBuffer<ApplyKnockbackOnGameplayEvent>(sp)
            && Allowed(spGuid, "knockback", root, entry, prefabName, log))
        {
            try
            {
                float m = entry.KnockbackScale.Value;
                var buf = Core.EntityManager.GetBuffer<ApplyKnockbackOnGameplayEvent>(sp);
                var o = CaptureOriginal(spGuid);
                if (o.Knockbacks == null)
                {
                    o.Knockbacks = new (float, float)[buf.Length];
                    for (int k = 0; k < buf.Length; k++) o.Knockbacks[k] = (buf[k].Range, buf[k].Duration);
                }
                int n = Math.Min(buf.Length, o.Knockbacks.Length);
                for (int k = 0; k < n; k++)
                {
                    var e = buf[k];
                    System.Runtime.CompilerServices.Unsafe.AsRef(in e.Range) = o.Knockbacks[k].range * m;
                    System.Runtime.CompilerServices.Unsafe.AsRef(in e.Duration) = o.Knockbacks[k].dur * m;
                    buf[k] = e;
                }
                if (n > 0) { log.Append($" knockback x{m:F2}(x{n})"); changed = true; }
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] knockback write failed on {prefabName}: {ex.Message}"); }
        }

        // lifetime: projectiles / areas only — a Buff's length is `duration`.
        if (!deepSafe && entry.LifetimeSeconds.HasValue && sp.Has<LifeTime>() && !sp.Has<Buff>()
            && (sp.Has<Projectile>() || Core.EntityManager.HasBuffer<HitColliderCast>(sp))   // projectile or hit area only
            && Allowed(spGuid, "lifetime", root, entry, prefabName, log))
        {
            try
            {
                if (sp.TryGetComponent<LifeTime>(out var ltCur)) CaptureOriginal(spGuid).SpawnLifeTime ??= ltCur.Duration;
                float lt2 = entry.LifetimeSeconds.Value;
                sp.With((ref LifeTime lt) => System.Runtime.CompilerServices.Unsafe.AsRef(in lt.Duration) = lt2);
                log.Append($" spawnlifetime={lt2:F1}s");
                changed = true;
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] spawn-lifetime write failed on {prefabName}: {ex.Message}"); }
        }

        // v0.89.1 FIX: freelymove on a CHANNELED / locked ability — the channel roots the caster via a
        // MovementImpair flag on its SPAWNED buff, so also clear that bit on spawned buffs in the chain.
        if (!deepSafe && entry.FreeMoveAfterSeconds.HasValue && sp.Has<BuffModificationFlagData>())
        {
            try
            {
                if (sp.TryGetComponent<BuffModificationFlagData>(out var bm)
                    && (bm.ModificationTypes & MovementImpairFlag) == MovementImpairFlag
                    && Allowed(spGuid, "freelymove", root, entry, prefabName, log))
                {
                    CaptureOriginal(spGuid).BuffModFlags ??= bm.ModificationTypes;
                    sp.With((ref BuffModificationFlagData m) =>
                        System.Runtime.CompilerServices.Unsafe.AsRef(in m.ModificationTypes) &= ~MovementImpairFlag);
                    log.Append(" freelymove=unimpair");
                    changed = true;
                }
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] movement-impair clear failed on {prefabName}: {ex.Message}"); }
        }

        // v0.68.0 / v0.72.0 FIX: healing multiplier from cached ORIGINAL values (no compounding).
        // HealOnGameplayEvent is a BUFFER (one element per heal event) — iterate it.
        if (entry.HealingMultiplier.HasValue && Core.EntityManager.HasBuffer<HealOnGameplayEvent>(sp)
            && Allowed(spGuid, "healing", root, entry, prefabName, log))
        {
            try
            {
                float m = entry.HealingMultiplier.Value;
                var buf = Core.EntityManager.GetBuffer<HealOnGameplayEvent>(sp);
                var o = CaptureOriginal(spGuid);
                if (o.Heals == null)
                {
                    o.Heals = new (float, float, float)[buf.Length];
                    for (int k = 0; k < buf.Length; k++)
                        o.Heals[k] = (buf[k].Health, buf[k].HealthPercent, buf[k].HealthPerSpellPower);
                }
                int n = Math.Min(buf.Length, o.Heals.Length);
                for (int k = 0; k < n; k++)
                {
                    var e = buf[k];
                    System.Runtime.CompilerServices.Unsafe.AsRef(in e.Health) = o.Heals[k].h * m;
                    System.Runtime.CompilerServices.Unsafe.AsRef(in e.HealthPercent) = o.Heals[k].pct * m;
                    System.Runtime.CompilerServices.Unsafe.AsRef(in e.HealthPerSpellPower) = o.Heals[k].perSp * m;
                    buf[k] = e;
                }
                if (n > 0) { log.Append($" heal x{m:F2}(x{n})"); changed = true; }
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] healing write failed on {prefabName}: {ex.Message}"); }
        }

        // v0.120.0: periodic / AoE / channel heals deliver via HealingBuff.HealingPerSecond — scale from the
        // cached ORIGINAL rate so reloads/`defaults` don't compound.
        if (entry.HealingMultiplier.HasValue && sp.Has<HealingBuff>() && Allowed(spGuid, "healing", root, entry, prefabName, log))
        {
            try
            {
                float m = entry.HealingMultiplier.Value;
                if (sp.TryGetComponent<HealingBuff>(out var hbCur))
                {
                    var o = CaptureOriginal(spGuid);
                    o.HealPerSec ??= hbCur.HealingPerSecond;
                    float baseRate = o.HealPerSec.Value;
                    sp.With((ref HealingBuff h) => System.Runtime.CompilerServices.Unsafe.AsRef(in h.HealingPerSecond) = baseRate * m);
                    log.Append($" healps x{m:F2}");
                    changed = true;
                }
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] healingbuff write failed on {prefabName}: {ex.Message}"); }
        }
    }

    delegate void CountSetter<T>(ref T d, int v);

    static bool HasSpread(Entity sp)
        => sp.Has<AbilityProjectileFanOnGameplayEvent_DataServer>() || sp.Has<AbilityProjectileFanOnTick_DataServer>()
           || sp.Has<Script_MultiShot_Cast_DataServer>() || sp.Has<EvenSpreadCluster_DataServer>()
           || sp.Has<EvenSpreadCluster_Tick_DataServer>();

    /// <summary>v0.134.0 projcount on one spread component type: clamp to [1, min(orig×3, 16)], from the captured original.</summary>
    static bool WriteCount<T>(Entity sp, int spGuid, string typeKey, Func<T, int> get, CountSetter<T> set, int requested, StringBuilder log)
        where T : unmanaged
    {
        if (!sp.TryGetComponent<T>(out var cur)) return false;
        var o = CaptureOriginal(spGuid);
        o.ProjCounts ??= new Dictionary<string, int>();
        if (!o.ProjCounts.TryGetValue(typeKey, out int orig)) o.ProjCounts[typeKey] = orig = get(cur);
        if (orig <= 0) return false;   // a zero-count pattern isn't a projectile spread we can scale
        int cap = ProjCountCap(orig);
        int v = Math.Clamp(requested, 1, cap);
        sp.With((ref T d) => set(ref d, v));
        log.Append($" projcount[{typeKey}]={v}" + (v < requested ? $"(capped at {cap})" : ""));
        return true;
    }

    /// <summary>Upper bound for projcount on a pattern whose shipped count is <paramref name="orig"/>.</summary>
    public static int ProjCountCap(int orig) => Math.Max(1, Math.Min(orig * 3, 16));

    /// <summary>v0.134.0: why `casttime` can't apply to this ability (null = OK). Checked at set time and at apply.</summary>
    public static string CastTimeRefusal(int rootGuid)
    {
        if (rootGuid == 0) return "unknown ability";
        if ((AbilityChainGraph.Name(rootGuid) ?? "").IndexOf("Channel", StringComparison.OrdinalIgnoreCase) >= 0) return "channel";
        try
        {
            foreach (var n in AbilityChainGraph.GetOrWalk(rootGuid).Nodes)
            {
                if (n.Guid != rootGuid && !(n.Via == AbilityChainGraph.EdgeKind.StartAbility && n.Parent == rootGuid)) continue;
                if (!TryPrefab(n.Guid, out Entity p)) continue;
                if (p.Has<AbilityHoldToCastData>()) return "hold-to-cast";
                if (p.Has<AbilityChargesData>()) return "charges";
                if ((AbilityChainGraph.Name(n.Guid) ?? "").IndexOf("Channel", StringComparison.OrdinalIgnoreCase) >= 0) return "channel";
            }
        }
        catch (Exception ex) { return "check failed: " + ex.Message; }
        return null;
    }

    // ---------------------------------------------------------------------
    // v0.72.0 — RESET TO SHIPPED DEFAULTS. The baked edits above are GLOBAL prefab mutations that live
    // in memory until a server restart, so clearing a rule field alone does NOT undo an already-applied
    // edit. RestoreAll puts the cached baseline back live; callers then ApplyAll so the result is computed
    // from ALL owning rules (a shared prefab another entry still tunes keeps that entry's value).
    // ---------------------------------------------------------------------

    /// <summary>Restore every prefab we have a cached baseline for.</summary>
    public static int RestoreAll()
    {
        int n = 0;
        foreach (var kv in _originals)
            if (TryPrefab(kv.Key, out Entity p))
            { RestorePrefab(p, kv.Value); n++; }
        Core.Log.LogInfo($"[Beelz TUNE] restored {n} prefab(s) to shipped baseline.");
        return n;
    }

    /// <summary>
    /// v0.73.0: does any prefab in this ability's chain carry an AbilityChargesData? `charges`/`chargetime`
    /// only apply to abilities that ALREADY have a charge system — we can't add one — so the command warns
    /// when this is false instead of silently no-op'ing.
    /// </summary>
    public static bool AbilityChainHasCharges(string nameOrGuid)
    {
        int guid = AbilityRules.ResolveAbilityGuid(AbilityRules.ResolveAbilityKey(nameOrGuid) ?? nameOrGuid);
        if (guid == 0) return false;
        try
        {
            foreach (var n in AbilityChainGraph.GetOrWalk(guid).Nodes)
                if (TryPrefab(n.Guid, out Entity p) && p.Has<AbilityChargesData>())
                    return true;
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] charge-capability check failed for '{nameOrGuid}': {ex.Message}"); }
        return false;
    }

    /// <summary>Write a prefab's cached baseline back onto whichever components it carries. Value-only;
    /// never adds or removes a component. Each field guarded.</summary>
    static void RestorePrefab(Entity prefab, Original o)
    {
        try
        {
            if (o.MaxRange.HasValue && prefab.Has<AbilityGroupInfo>())
                prefab.With((ref AbilityGroupInfo d) => System.Runtime.CompilerServices.Unsafe.AsRef(in d.MaxRange) = o.MaxRange.Value);
            if ((o.ChargesMax.HasValue || o.ChargeUpTime.HasValue) && prefab.Has<AbilityChargesData>())
                prefab.With((ref AbilityChargesData d) =>
                {
                    if (o.ChargesMax.HasValue) System.Runtime.CompilerServices.Unsafe.AsRef(in d.MaxCharges) = o.ChargesMax.Value;
                    if (o.ChargeUpTime.HasValue) System.Runtime.CompilerServices.Unsafe.AsRef(in d.ChargeUpTime)._Value = o.ChargeUpTime.Value;
                });
            if (o.AoeRange.HasValue && prefab.Has<TargetAoE>())
                prefab.With((ref TargetAoE a) => System.Runtime.CompilerServices.Unsafe.AsRef(in a.MaxRange) = o.AoeRange.Value);
            if (o.ProjSpeed.HasValue && prefab.Has<Projectile>())
                prefab.With((ref Projectile p) => System.Runtime.CompilerServices.Unsafe.AsRef(in p.Speed) = o.ProjSpeed.Value);
            if (o.TravelHeight.HasValue && prefab.Has<TravelBuff>())
                prefab.With((ref TravelBuff t) => System.Runtime.CompilerServices.Unsafe.AsRef(in t.Height) = o.TravelHeight.Value);
            if (o.ProjRange.HasValue && prefab.Has<Projectile>())
                prefab.With((ref Projectile p) => System.Runtime.CompilerServices.Unsafe.AsRef(in p.Range) = o.ProjRange.Value);
            // v0.132.0: the v0.85 `forcetimeout` RemoveComponent<LifeTime> undo is gone — forcetimeout no longer
            // adds components (runtime instance timer). Prefabs are rebuilt at server start, so the deploy
            // restart clears any LifeTime an older build added.
            if (o.LifeTime.HasValue && prefab.Has<LifeTime>())
                prefab.With((ref LifeTime lt) => System.Runtime.CompilerServices.Unsafe.AsRef(in lt.Duration) = o.LifeTime.Value);
            if (o.Durations != null && Core.EntityManager.HasBuffer<ApplyBuffOnGameplayEvent>(prefab))
            {
                var buf = Core.EntityManager.GetBuffer<ApplyBuffOnGameplayEvent>(prefab);
                int n = Math.Min(buf.Length, o.Durations.Length);
                for (int k = 0; k < n; k++) { var e = buf[k]; System.Runtime.CompilerServices.Unsafe.AsRef(in e.OverrideDuration) = o.Durations[k]; buf[k] = e; }
            }
            if (o.Heals != null && Core.EntityManager.HasBuffer<HealOnGameplayEvent>(prefab))
            {
                var buf = Core.EntityManager.GetBuffer<HealOnGameplayEvent>(prefab);
                int n = Math.Min(buf.Length, o.Heals.Length);
                for (int k = 0; k < n; k++)
                {
                    var e = buf[k];
                    System.Runtime.CompilerServices.Unsafe.AsRef(in e.Health) = o.Heals[k].h;
                    System.Runtime.CompilerServices.Unsafe.AsRef(in e.HealthPercent) = o.Heals[k].pct;
                    System.Runtime.CompilerServices.Unsafe.AsRef(in e.HealthPerSpellPower) = o.Heals[k].perSp;
                    buf[k] = e;
                }
            }
            if (o.HealPerSec.HasValue && prefab.Has<HealingBuff>())   // v0.120.0: restore HealingBuff aura rate
                prefab.With((ref HealingBuff h) => System.Runtime.CompilerServices.Unsafe.AsRef(in h.HealingPerSecond) = o.HealPerSec.Value);
            if (o.Interrupt.HasValue && prefab.Has<AbilityInterruptData>())
                prefab.With((ref AbilityInterruptData d) => System.Runtime.CompilerServices.Unsafe.AsRef(in d.InterruptTypes) = o.Interrupt.Value);
            if ((o.MoveDuration.HasValue || o.MoveSpeed.HasValue || o.MoveUseCastDuration.HasValue) && prefab.Has<ModifyMovementDuringCastData>())
                prefab.With((ref ModifyMovementDuringCastData m) =>
                {
                    if (o.MoveDuration.HasValue) m.Duration._Value = o.MoveDuration.Value;
                    if (o.MoveSpeed.HasValue) m.MovementSpeedMultiplier._Value = o.MoveSpeed.Value;
                    if (o.MoveUseCastDuration.HasValue) System.Runtime.CompilerServices.Unsafe.AsRef(in m.UseCastDuration) = o.MoveUseCastDuration.Value;
                });
            // v0.134.0 knobs
            if (o.MaxStacks.HasValue && prefab.Has<Buff>())
                prefab.With((ref Buff b) => System.Runtime.CompilerServices.Unsafe.AsRef(in b.MaxStacks) = o.MaxStacks.Value);
            if (o.ProjCounts != null)
                foreach (var (k, v) in o.ProjCounts)
                    switch (k)
                    {
                        case "fan": if (prefab.Has<AbilityProjectileFanOnGameplayEvent_DataServer>()) prefab.With((ref AbilityProjectileFanOnGameplayEvent_DataServer d) => System.Runtime.CompilerServices.Unsafe.AsRef(in d.Count) = v); break;
                        case "fantick": if (prefab.Has<AbilityProjectileFanOnTick_DataServer>()) prefab.With((ref AbilityProjectileFanOnTick_DataServer d) => System.Runtime.CompilerServices.Unsafe.AsRef(in d.Count) = v); break;
                        case "multishot": if (prefab.Has<Script_MultiShot_Cast_DataServer>()) prefab.With((ref Script_MultiShot_Cast_DataServer d) => System.Runtime.CompilerServices.Unsafe.AsRef(in d.Count) = v); break;
                        case "cluster": if (prefab.Has<EvenSpreadCluster_DataServer>()) prefab.With((ref EvenSpreadCluster_DataServer d) => System.Runtime.CompilerServices.Unsafe.AsRef(in d.Count) = v); break;
                        case "clustertick": if (prefab.Has<EvenSpreadCluster_Tick_DataServer>()) prefab.With((ref EvenSpreadCluster_Tick_DataServer d) => System.Runtime.CompilerServices.Unsafe.AsRef(in d.Count) = v); break;
                    }
            if (o.Knockbacks != null && Core.EntityManager.HasBuffer<ApplyKnockbackOnGameplayEvent>(prefab))
            {
                var buf = Core.EntityManager.GetBuffer<ApplyKnockbackOnGameplayEvent>(prefab);
                int n = Math.Min(buf.Length, o.Knockbacks.Length);
                for (int k = 0; k < n; k++)
                {
                    var e = buf[k];
                    System.Runtime.CompilerServices.Unsafe.AsRef(in e.Range) = o.Knockbacks[k].range;
                    System.Runtime.CompilerServices.Unsafe.AsRef(in e.Duration) = o.Knockbacks[k].dur;
                    buf[k] = e;
                }
            }
            if (o.CastTime is (float cm, float cp) && prefab.Has<AbilityCastTimeData>())
                prefab.With((ref AbilityCastTimeData d) => { d.MaxCastTime._Value = cm; d.PostCastTime._Value = cp; });
            if (o.SpawnLifeTime.HasValue && prefab.Has<LifeTime>())
                prefab.With((ref LifeTime lt) => System.Runtime.CompilerServices.Unsafe.AsRef(in lt.Duration) = o.SpawnLifeTime.Value);
            if (o.BuffModFlags.HasValue && prefab.Has<BuffModificationFlagData>())   // v0.89.1: restore channel movement-impair
                prefab.With((ref BuffModificationFlagData m) => System.Runtime.CompilerServices.Unsafe.AsRef(in m.ModificationTypes) = o.BuffModFlags.Value);
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] restore failed: {ex.Message}"); }
    }

    /// <summary>v0.132.0: the shipped (pre-tune) values cached for a prefab, for ability-inspect ("orig").</summary>
    public static string DescribeOriginal(int prefabGuid)
    {
        if (!_originals.TryGetValue(prefabGuid, out var o)) return null;
        var p = new List<string>();
        if (o.MaxRange.HasValue) p.Add($"maxrange={o.MaxRange.Value:0.##}");
        if (o.AoeRange.HasValue) p.Add($"aoe={o.AoeRange.Value:0.##}");
        if (o.ProjSpeed.HasValue) p.Add($"projspeed={o.ProjSpeed.Value:0.##}");
        if (o.ProjRange.HasValue) p.Add($"projrange={o.ProjRange.Value:0.##}");
        if (o.TravelHeight.HasValue) p.Add($"leapheight={o.TravelHeight.Value:0.##}");
        if (o.LifeTime.HasValue) p.Add($"lifetime={o.LifeTime.Value:0.##}");
        if (o.ChargesMax.HasValue) p.Add($"charges={o.ChargesMax.Value}");
        if (o.ChargeUpTime.HasValue) p.Add($"chargetime={o.ChargeUpTime.Value:0.##}");
        if (o.MaxStacks.HasValue) p.Add($"maxstacks={o.MaxStacks.Value}");
        if (o.ProjCounts != null) foreach (var (k, v) in o.ProjCounts) p.Add($"projcount[{k}]={v}");
        if (o.Knockbacks is { Length: > 0 }) p.Add($"knockback={o.Knockbacks[0].range:0.##}m/{o.Knockbacks[0].dur:0.##}s");
        if (o.CastTime is (float m0, float p0)) p.Add($"casttime={m0:0.##}+{p0:0.##}");
        if (o.SpawnLifeTime.HasValue) p.Add($"spawnlifetime={o.SpawnLifeTime.Value:0.##}");
        return p.Count == 0 ? null : string.Join(" ", p);
    }
}
