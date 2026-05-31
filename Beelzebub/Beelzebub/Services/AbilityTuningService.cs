using System;
using System.Collections.Generic;
using System.Text;
using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.46.0 — per-ability CAST tuning (cast interruption + post-cast movement unlock).
///
/// Two player-facing knobs, both governed by baked fields on an ability's CAST prefab:
///   * <b>Interruptible</b> — <c>AbilityInterruptData.InterruptTypes</c>. Setting it to
///     <c>ManualInterrupt</c> lets a long cast be cancelled by player action (dashing out,
///     raising a shield); <c>None</c> makes it uninterruptible.
///   * <b>FreeMoveAfterCast</b> / <b>CastMovementSpeed</b> — <c>ModifyMovementDuringCastData</c>.
///     Some abilities root the player past the cast (Duration &gt; cast time). Clamping the
///     lock to the cast window (+ <c>UseCastDuration</c>) frees the player the moment the cast
///     finishes; the speed multiplier tunes movement DURING the cast.
///
/// These live on the CAST prefab, while players/admins curate by ability-GROUP name (what
/// <c>.beelz list</c> shows). Rather than depend on an internal group→cast buffer layout, we
/// SCAN the prefab name map and tune every prefab that (a) actually carries the tunable
/// component and (b) matches a curated entry by exact name or group-stem prefix
/// (<c>AB_X_Group</c> ⇒ <c>AB_X_Cast</c>, <c>AB_X_First_Cast</c>, …). Robust + fully logged.
///
/// The edit is GLOBAL (the prefab is shared with the original NPC/boss), config-gated behind
/// <c>Abilities_ApplyConfig</c>, and re-applied on <c>.beelz admin reload</c>.
/// </summary>
internal static class AbilityTuningService
{
    /// <summary>
    /// Apply every curated tuning entry to its matching cast prefabs. No-op unless
    /// <c>Abilities_ApplyConfig</c>. Called at init (after the prefab map + rules are ready)
    /// and on <c>.beelz admin reload</c>. Fully guarded — never throws into the caller.
    /// </summary>
    public static int ApplyAll()
    {
        if (!Beelzebub.Config.Settings.Abilities_ApplyConfig.Value) return 0;

        // v0.65.0 (J1): a global minimum-cooldown floor can apply to ANY ability prefab carrying
        // AbilityCooldownData, not just curated ones — so the scan must run even with no per-ability
        // entries when this is set.
        float globalMinCd = Math.Max(0f, Beelzebub.Config.Settings.Grant_MinimumCooldownSeconds.Value);

        // v0.69.0: heal any GUID-keyed entries (pre-fix `.beelz admin ability <guid> ...`) to their
        // prefab-name key so the matcher below can find them. Cheap no-op once migrated.
        Core.AbilityRules?.NormalizeNumericKeys();

        var map = Core.AbilityRules?.Current?.AbilityMap;

        // Collect entries that actually request a change.
        var tuned = new List<(string stem, AbilityRules.AbilityEntry entry)>();
        if (map != null)
            foreach (var kv in map)
            {
                var e = kv.Value;
                if (e == null) continue;
                if (e.Interruptible == null && !e.FreeMoveAfterCast && e.CastMovementSpeed == null
                    && e.FreeMoveAfterSeconds == null && e.InterruptOnHit == null   // v0.87.0
                    && e.CooldownSeconds == null && e.MaxRangeOverride == null
                    && e.ChargesMax == null && e.ChargeTimeSeconds == null
                    && e.AoeRadius == null && e.ProjectileSpeed == null
                    && e.EffectDurationSeconds == null && e.HealingMultiplier == null
                    && e.ForceTimeoutSeconds == null) continue;
                tuned.Add((Stem(kv.Key), e));
            }
        if (tuned.Count == 0 && globalMinCd <= 0f)
        {
            Core.Log.LogInfo("[Beelz TUNE] Abilities_ApplyConfig is on, but no AbilityMap entries request tuning and no global cooldown floor is set.");
            return 0;
        }

        int prefabsTuned = 0;
        var matchedStems = new HashSet<string>();   // v0.70.0: track which curated entries hit a prefab
        try
        {
            foreach (var kv in Core.PrefabNames)
            {
                string prefabName = kv.Value;
                if (string.IsNullOrEmpty(prefabName) || !prefabName.StartsWith("AB_", StringComparison.Ordinal)) continue;

                AbilityRules.AbilityEntry match = null;
                foreach (var (stem, entry) in tuned)
                {
                    if (Matches(prefabName, stem)) { match = entry; matchedStems.Add(stem); break; }
                }
                // Process when a curated entry matches OR a global cooldown floor is active (the
                // floor can touch any ability prefab with AbilityCooldownData).
                if (match == null && globalMinCd <= 0f) continue;

                if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(new PrefabGUID(kv.Key), out Entity prefab)
                    || !prefab.Exists())
                    continue;

                if (ApplyToPrefab(prefab, kv.Key, prefabName, match, globalMinCd)) prefabsTuned++;
            }
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz TUNE] scan failed: {ex.Message}");
        }

        // v0.70.0: flag curated entries that matched NO prefab — almost always a wrong ability
        // name/ID (the symptom of the v0.69 GUID-key bug). Helps admins self-diagnose.
        foreach (var (stem, _) in tuned)
            if (!matchedStems.Contains(stem))
                Core.Log.LogWarning($"[Beelz TUNE] tuned entry '{stem}' matched NO ability prefab — check the ability name/ID (use .beelz list / api list).");

        if (Beelzebub.Config.Settings.VerboseLogging.Value)   // v0.95.0: gate the batch summary behind verbose (BCH never reads it)
            Core.Log.LogInfo($"[Beelz TUNE] applied tuning to {prefabsTuned} prefab(s) from {tuned.Count} curated entry(ies)"
                + (globalMinCd > 0f ? $" + global min-cooldown {globalMinCd:F2}s" : "") + ".");
        return prefabsTuned;
    }

    /// <summary>Strip a trailing group suffix so a group-keyed entry matches its cast prefabs.</summary>
    static string Stem(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        foreach (var suf in new[] { "_AbilityGroup", "_Group" })
            if (key.EndsWith(suf, StringComparison.OrdinalIgnoreCase))
                return key.Substring(0, key.Length - suf.Length);
        return key;
    }

    /// <summary>
    /// A prefab matches a curated stem if its name equals the stem, or is the stem followed by
    /// '_' (so <c>AB_X</c> matches <c>AB_X_Cast</c> but NOT a sibling <c>AB_XY_Cast</c>).
    /// </summary>
    static bool Matches(string prefabName, string stem)
    {
        if (string.IsNullOrEmpty(stem)) return false;
        if (string.Equals(prefabName, stem, StringComparison.OrdinalIgnoreCase)) return true;
        return prefabName.Length > stem.Length
            && prefabName.StartsWith(stem, StringComparison.OrdinalIgnoreCase)
            && prefabName[stem.Length] == '_';
    }

    static bool ApplyToPrefab(Entity prefab, int prefabGuid, string prefabName, AbilityRules.AbilityEntry entry, float globalMinCd)
    {
        bool changed = false;
        var log = new StringBuilder();

        // v0.65.0 (J1): absolute cooldown override + global minimum-cooldown floor. Cooldown is a
        // plain float on the CAST prefab's AbilityCooldownData. Runs for a curated CooldownSeconds
        // OR whenever the global floor is active (entry may be null then).
        if (prefab.Has<AbilityCooldownData>() && (entry?.CooldownSeconds != null || globalMinCd > 0f))
        {
            try
            {
                prefab.TryGetComponent<AbilityCooldownData>(out var cd);
                float current = cd.Cooldown._Value;   // Cooldown is a ModifiableFloat
                CaptureOriginal(prefabGuid).Cooldown ??= current;   // v0.72.0: remember the shipped baseline for `defaults`
                float target = entry?.CooldownSeconds ?? current;
                if (globalMinCd > 0f && target < globalMinCd) target = globalMinCd;
                if (Math.Abs(target - current) > 0.0001f)
                {
                    prefab.With((ref AbilityCooldownData d) =>
                        System.Runtime.CompilerServices.Unsafe.AsRef(in d.Cooldown)._Value = target);
                    log.Append($" cooldown={target:F2}s");
                    changed = true;
                }
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] cooldown write failed on {prefabName}: {ex.Message}"); }
        }

        // v0.65.0 (J1): max-range override on the GROUP prefab's AbilityGroupInfo.MaxRange (plain float).
        if (entry?.MaxRangeOverride != null && prefab.Has<AbilityGroupInfo>())
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

        // The knobs below only apply to a curated entry (the global cooldown floor above is the
        // only thing that touches uncurated prefabs).
        if (entry == null)
        {
            if (changed && Beelzebub.Config.Settings.VerboseLogging.Value) Core.Log.LogInfo($"[Beelz TUNE] {prefabName}:{log}");
            return changed;
        }

        // v0.67.0 (Stage 2): charges (AbilityChargesData on the GROUP prefab). MaxCharges is an int;
        // ChargeUpTime is a ModifiableFloat (write ._Value).
        if ((entry.ChargesMax.HasValue || entry.ChargeTimeSeconds.HasValue) && prefab.Has<AbilityChargesData>())
        {
            try
            {
                if (prefab.TryGetComponent<AbilityChargesData>(out var chCur))
                {
                    var o = CaptureOriginal(prefabGuid);
                    o.ChargesMax ??= chCur.MaxCharges;
                    o.ChargeUpTime ??= chCur.ChargeUpTime._Value;
                }
                int? mc = entry.ChargesMax; float? ct2 = entry.ChargeTimeSeconds;
                prefab.With((ref AbilityChargesData d) =>
                {
                    if (mc.HasValue) System.Runtime.CompilerServices.Unsafe.AsRef(in d.MaxCharges) = mc.Value;
                    if (ct2.HasValue) System.Runtime.CompilerServices.Unsafe.AsRef(in d.ChargeUpTime)._Value = ct2.Value;
                });
                if (entry.ChargesMax.HasValue) log.Append($" charges={entry.ChargesMax.Value}");
                if (entry.ChargeTimeSeconds.HasValue) log.Append($" chargetime={entry.ChargeTimeSeconds.Value:F2}s");
                changed = true;
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] charges write failed on {prefabName}: {ex.Message}"); }
        }

        // v0.67.0 (Stage 2) / v0.68.0 (Stage 2b): AoE radius, projectile speed, buff/debuff duration,
        // and healing live on the ability's CAST + SPAWNED prefab (often NOT named after the ability),
        // so walk Group→Cast→SpawnPrefab from the GROUP prefab.
        // v0.73.0: MaxRangeOverride also walks downstream — for a projectile spell the GROUP's
        // AbilityGroupInfo.MaxRange (set above) is only the aim/cast clamp; the projectile's own travel
        // distance lives on Projectile.Range on the spawned projectile, reached by the walker.
        if ((entry.AoeRadius.HasValue || entry.ProjectileSpeed.HasValue || entry.MaxRangeOverride.HasValue
             || entry.EffectDurationSeconds.HasValue || entry.HealingMultiplier.HasValue
             || entry.ForceTimeoutSeconds.HasValue
             || entry.FreeMoveAfterSeconds.HasValue)   // v0.93.0: freelymove also clears MovementImpair on the spawned CHANNEL buff (the firing-phase root) — that's a downstream write, so the walker must run for it
            && Core.EntityManager.HasBuffer<AbilityGroupStartAbilitiesBuffer>(prefab))
        {
            ApplyDownstream(prefab, prefabName, entry, log, ref changed);
        }

        // #3 — cast interruption. AbilityInterruptData.InterruptTypes is a [Flags] enum (readonly in the
        // ref assembly → written via Unsafe.AsRef). Two independent knobs map to two bits so they compose:
        //   Interruptible  → ManualInterrupt (1):  the player can self-cancel the cast (dash / raise shield).
        //   InterruptOnHit → OnDamageTaken  (4):  the cast is cancelled when the CASTER takes damage
        //                                          (v0.87.0 — "make it end if you're attacked").
        // We OR/AND-mask only the relevant bit so each knob leaves the other (and any baked flags) intact,
        // and cache the original whole value once so `defaults` restores it.
        if ((entry.Interruptible.HasValue || entry.InterruptOnHit.HasValue) && prefab.Has<AbilityInterruptData>())
        {
            try
            {
                if (prefab.TryGetComponent<AbilityInterruptData>(out var aiCur)) CaptureOriginal(prefabGuid).Interrupt ??= aiCur.InterruptTypes;
                bool? manual = entry.Interruptible, onHit = entry.InterruptOnHit;
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

        // #4 — post-cast movement unlock + cast-movement-speed override.
        // ModifyMovementDuringCastData.{MovementSpeedMultiplier,Duration} are writable
        // ModifiableFloat fields (set ._Value). Clamping Duration to the cast window
        // (MaxCastTime+PostCastTime, both ModifiableFloat on AbilityCastTimeData) frees the
        // player the moment the cast finishes instead of for the whole effect. (UseCastDuration
        // is readonly in the ref assembly; the explicit Duration clamp achieves the same.)
        // v0.87.0: FreeMoveAfterSeconds sets Duration to an EXPLICIT N (free to move N seconds into the
        // cast — the cast keeps going), which takes precedence over FreeMoveAfterCast's clamp-to-cast-window.
        bool wantMove = entry.FreeMoveAfterCast || entry.CastMovementSpeed.HasValue || entry.FreeMoveAfterSeconds.HasValue;
        if (wantMove && prefab.Has<ModifyMovementDuringCastData>())
        {
            float castWindow = 0f;
            if (entry.FreeMoveAfterCast && prefab.TryGetComponent<AbilityCastTimeData>(out var ct))
                castWindow = ct.MaxCastTime._Value + ct.PostCastTime._Value;

            try
            {
                // Cache originals once so `defaults` can restore (the v0.46 writes never did).
                if (prefab.TryGetComponent<ModifyMovementDuringCastData>(out var mCur))
                {
                    var o = CaptureOriginal(prefabGuid);
                    o.MoveDuration ??= mCur.Duration._Value;
                    o.MoveSpeed ??= mCur.MovementSpeedMultiplier._Value;
                    o.MoveUseCastDuration ??= mCur.UseCastDuration;
                }

                float? explicitDur = entry.FreeMoveAfterSeconds;                       // v0.87.0
                bool clampWindow = !explicitDur.HasValue && entry.FreeMoveAfterCast && castWindow > 0f;
                float window = castWindow;
                float? speed = entry.CastMovementSpeed;
                // v0.89.0 FIX: when we set an explicit Duration, ALSO force UseCastDuration=false — otherwise
                // the engine ignores Duration and locks movement for the WHOLE cast/channel (this was why
                // `freelymove` did nothing on a channel: UseCastDuration was true). Without an explicit
                // Duration (speed-only override) we leave UseCastDuration untouched.
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
                else if (entry.FreeMoveAfterCast) log.Append(" freemove=skip(no-cast-time)");
                if (speed.HasValue) { log.Append($" castspeed={speed.Value:F2}"); changed = true; }
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] movement write failed on {prefabName}: {ex.Message}"); }
        }

        if (changed && Beelzebub.Config.Settings.VerboseLogging.Value) Core.Log.LogInfo($"[Beelz TUNE] {prefabName}:{log}");
        return changed;
    }

    /// <summary>
    /// v0.67.0 (Stage 2): walk the ability's spawn chain from its GROUP prefab —
    /// Group → AbilityGroupStartAbilitiesBuffer[].PrefabGUID (Cast) →
    /// AbilitySpawnPrefabOnCast[].SpawnPrefab (the projectile/AoE entity, which often has a name
    /// unrelated to the ability) — and write the downstream shaping fields (AoE radius, projectile
    /// speed) onto whichever spawned prefab carries the component. Every step is guarded; a missing
    /// hop or component is simply skipped (logged on a write fault).
    /// </summary>
    static void ApplyDownstream(Entity groupPrefab, string prefabName, AbilityRules.AbilityEntry entry, StringBuilder log, ref bool changed)
    {
        try
        {
            var starts = Core.EntityManager.GetBuffer<AbilityGroupStartAbilitiesBuffer>(groupPrefab);
            for (int i = 0; i < starts.Length; i++)
            {
                if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(starts[i].PrefabGUID, out Entity cast) || !cast.Exists()) continue;
                // Buff/debuff duration + healing can hang off the CAST itself, not only the spawned hit.
                WriteSpawnedFields(cast, starts[i].PrefabGUID._Value, prefabName, entry, log, ref changed);
                if (!Core.EntityManager.HasBuffer<AbilitySpawnPrefabOnCast>(cast)) continue;
                var spawns = Core.EntityManager.GetBuffer<AbilitySpawnPrefabOnCast>(cast);
                for (int j = 0; j < spawns.Length; j++)
                {
                    if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(spawns[j].SpawnPrefab, out Entity sp) || !sp.Exists()) continue;
                    WriteSpawnedFields(sp, spawns[j].SpawnPrefab._Value, prefabName, entry, log, ref changed);
                }
            }
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] downstream walk failed on {prefabName}: {ex.Message}"); }
        // v0.85.0: now that the spawn-prefab buffer walk is finished, apply the deferred force-timeout
        // LifeTime ADDs (structural — unsafe during the walk).
        FlushPendingForceTimeout(prefabName, log, ref changed);
    }

    /// <summary>v0.85.0: add a LifeTime+Destroy to each indefinite buff queued during the walk, so the
    /// engine despawns it after the configured force-timeout. Runs after the walk (structural change).</summary>
    static void FlushPendingForceTimeout(string prefabName, StringBuilder log, ref bool changed)
    {
        if (_pendingForceTimeoutAdd.Count == 0) return;
        foreach (var (spGuid, seconds) in _pendingForceTimeoutAdd)
        {
            try
            {
                if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(new PrefabGUID(spGuid), out Entity sp) || !sp.Exists()) continue;
                if (!sp.Has<LifeTime>()) Core.EntityManager.AddComponent<LifeTime>(sp);
                sp.With((ref LifeTime lt) =>
                {
                    System.Runtime.CompilerServices.Unsafe.AsRef(in lt.Duration) = seconds;
                    System.Runtime.CompilerServices.Unsafe.AsRef(in lt.EndAction) = LifeTimeEndAction.Destroy;
                });
                log.Append($" forcetimeout+={seconds:F1}s(added)");
                changed = true;
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] force-timeout add failed on {prefabName}: {ex.Message}"); }
        }
        _pendingForceTimeoutAdd.Clear();
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
        public float? Cooldown, MaxRange, AoeRange, ProjSpeed, ChargeUpTime;
        public float? ProjRange;   // v0.73.0: Projectile.Range (projectile travel distance)
        public float? LifeTime;    // v0.73.0: LifeTime.Duration on a spawned Buff (over-time effect length)
        public bool LifeTimeAdded; // v0.85.0: we ADDED a LifeTime to an indefinite buff (remove it on restore)
        public int? ChargesMax;
        public Il2CppSystem.Nullable_Unboxed<float>[] Durations;   // per ApplyBuffOnGameplayEvent element
        public (float h, float pct, float perSp)[] Heals;          // per HealOnGameplayEvent element
        // v0.87.0: cast modifiers — cache so `defaults` can restore them (the v0.46 interrupt/movement
        // writes never cached, so `defaults` couldn't undo them; closed here).
        public InterruptTypes? Interrupt;   // AbilityInterruptData.InterruptTypes
        public float? MoveDuration;         // ModifyMovementDuringCastData.Duration._Value
        public float? MoveSpeed;            // ModifyMovementDuringCastData.MovementSpeedMultiplier._Value
        public bool? MoveUseCastDuration;   // ModifyMovementDuringCastData.UseCastDuration (v0.89.0)
        public long? BuffModFlags;   // BuffModificationFlagData.ModificationTypes (exposed as raw long) on a spawned buff (v0.89.1)
    }

    // v0.89.1: BuffModificationTypes.MovementImpair bit (the channel/lock "can't move" flag). The interop
    // field is a raw long, so we mask the numeric bit directly.
    const long MovementImpairFlag = 16L;
    static readonly Dictionary<int, Original> _originals = new();
    // v0.85.0: force-timeout adds a LifeTime to indefinite buffs — that's a STRUCTURAL ECS change that
    // can't happen during the spawn-prefab buffer walk, so we queue (spawnGuid, seconds) and flush AFTER
    // the walk completes (FlushPendingForceTimeout).
    static readonly List<(int spGuid, float seconds)> _pendingForceTimeoutAdd = new();
    static Original CaptureOriginal(int prefabGuid)
    {
        if (!_originals.TryGetValue(prefabGuid, out var o)) { o = new Original(); _originals[prefabGuid] = o; }
        return o;
    }

    /// <summary>Write the downstream shaping fields onto one prefab (cast or spawned) if it carries them.</summary>
    static void WriteSpawnedFields(Entity sp, int spGuid, string prefabName, AbilityRules.AbilityEntry entry, StringBuilder log, ref bool changed)
    {
        if (entry.AoeRadius.HasValue && sp.Has<TargetAoE>())
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
        if (entry.ProjectileSpeed.HasValue && sp.Has<Projectile>())
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

        // v0.73.0: range → projectile travel distance. Projectile.Range is the actual flight distance
        // (plain float, same component as Speed). Pairs with the GROUP's AbilityGroupInfo.MaxRange so a
        // shorter `range` clamps both the aim and the projectile's reach.
        if (entry.MaxRangeOverride.HasValue && sp.Has<Projectile>())
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

        // v0.68.0 (Stage 2b): ABSOLUTE override of applied buff/debuff durations. ApplyBuffOnGameplayEvent
        // is a BUFFER; OverrideDuration is a Nullable<float>. Set, not multiplied → idempotent.
        if (entry.EffectDurationSeconds.HasValue && Core.EntityManager.HasBuffer<ApplyBuffOnGameplayEvent>(sp))
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

        // v0.73.0: duration → over-time/channel effect length. A directly-spawned BUFF (e.g. a heal/DoT
        // channel) lasts for its LifeTime.Duration, not an ApplyBuffOnGameplayEvent override — so also set
        // LifeTime on spawned prefabs that are BUFFS. Guarded to Buff prefabs so we never touch a
        // projectile's flight LifeTime. (This does NOT change an ability's cast/channel time — that's the
        // cast-time data, deliberately left alone so the player isn't re-rooted.)
        if (entry.EffectDurationSeconds.HasValue && sp.Has<Buff>() && sp.Has<LifeTime>())
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

        // v0.85.0: FORCE-TIMEOUT — make an otherwise-INDEFINITE buff this ability spawns expire after a
        // strict number of seconds. If the buff already has a LifeTime we just set it (idempotent); if it
        // has NONE (the indefinite case `duration` can't fix), we QUEUE adding a LifeTime+Destroy and flush
        // it after the walk (AddComponent is a structural change that would invalidate the buffer walk).
        // Buff-only, so a projectile's flight time is never touched.
        if (entry.ForceTimeoutSeconds.HasValue && entry.ForceTimeoutSeconds.Value > 0f && sp.Has<Buff>())
        {
            try
            {
                float ft = entry.ForceTimeoutSeconds.Value;
                if (sp.Has<LifeTime>())
                {
                    if (sp.TryGetComponent<LifeTime>(out var ltCur)) CaptureOriginal(spGuid).LifeTime ??= ltCur.Duration;
                    sp.With((ref LifeTime lt) => System.Runtime.CompilerServices.Unsafe.AsRef(in lt.Duration) = ft);
                    log.Append($" forcetimeout={ft:F1}s");
                    changed = true;
                }
                else
                {
                    CaptureOriginal(spGuid).LifeTimeAdded = true;   // remember we added it (for restore)
                    _pendingForceTimeoutAdd.Add((spGuid, ft));      // deferred structural add (see flush)
                    changed = true;
                }
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] force-timeout write failed on {prefabName}: {ex.Message}"); }
        }

        // v0.89.1 FIX: freelymove on a CHANNELED / locked ability. The cast's ModifyMovementDuringCastData
        // (handled in ApplyToPrefab) only governs the cast WINDUP — a channel like the Nun heal roots you
        // via a MovementImpair flag on its SPAWNED buff (BuffModificationFlagData.ModificationTypes), which
        // lasts the buff's whole lifetime (component dump v0.89.1 confirmed this is the real root, not the
        // cast component we were editing). So when freelymove is set, also clear the MovementImpair bit on
        // spawned buffs in the chain — letting the player move while the ability runs. Cached for `defaults`.
        if (entry.FreeMoveAfterSeconds.HasValue && sp.Has<BuffModificationFlagData>())
        {
            try
            {
                if (sp.TryGetComponent<BuffModificationFlagData>(out var bm)
                    && (bm.ModificationTypes & MovementImpairFlag) == MovementImpairFlag)
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

        // v0.68.0 (Stage 2b) / v0.72.0 FIX: healing multiplier, computed from cached ORIGINAL values so
        // reloads don't compound. HealOnGameplayEvent is a BUFFER (one element per heal tick/event), NOT a
        // single component — treating it as a component (pre-v0.72) read/wrote the wrong memory, which
        // inverted the heal and crashed the server on `healing 0`. Iterate the buffer like the duration
        // handler above; cache the original per element so `defaults` can restore it.
        if (entry.HealingMultiplier.HasValue && Core.EntityManager.HasBuffer<HealOnGameplayEvent>(sp))
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
    }

    // ---------------------------------------------------------------------
    // v0.72.0 — RESET TO SHIPPED DEFAULTS. The baked edits above are GLOBAL prefab mutations that live
    // in memory until a server restart, so clearing a rule field alone does NOT undo an already-applied
    // edit. These restore the cached baseline live (no restart needed) for `.beelz admin ability
    // <id|all> defaults`. Fields we never wrote (no cache entry) are already at baseline.
    // ---------------------------------------------------------------------

    /// <summary>Restore every prefab we have a cached baseline for. Used by `.beelz admin ability all defaults`.</summary>
    public static int RestoreAll()
    {
        int n = 0;
        foreach (var kv in _originals)
            if (Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(new PrefabGUID(kv.Key), out Entity p) && p.Exists())
            { RestorePrefab(p, kv.Value); n++; }
        Core.Log.LogInfo($"[Beelz TUNE] restored {n} prefab(s) to shipped baseline.");
        return n;
    }

    /// <summary>Restore just the prefab chain for one ability (group + casts + spawned). Used by `.beelz admin ability &lt;id&gt; defaults`.</summary>
    public static int RestoreAbility(string nameOrGuid)
    {
        string key = AbilityRules.ResolveAbilityKey(nameOrGuid);
        if (string.IsNullOrEmpty(key)) return 0;
        string stem = Stem(key);
        int n = 0;
        foreach (int g in CollectAbilityPrefabGuids(stem))
            if (_originals.TryGetValue(g, out var o)
                && Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(new PrefabGUID(g), out Entity p) && p.Exists())
            { RestorePrefab(p, o); n++; }
        return n;
    }

    /// <summary>
    /// v0.73.0: does any prefab in this ability's chain carry an AbilityChargesData? `charges`/`chargetime`
    /// only apply to abilities that ALREADY have a charge system — we can't add one — so the command warns
    /// when this is false instead of silently no-op'ing.
    /// </summary>
    public static bool AbilityChainHasCharges(string nameOrGuid)
    {
        string key = AbilityRules.ResolveAbilityKey(nameOrGuid);
        if (string.IsNullOrEmpty(key)) return false;
        try
        {
            foreach (int g in CollectAbilityPrefabGuids(Stem(key)))
                if (Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(new PrefabGUID(g), out Entity p)
                    && p.Exists() && p.Has<AbilityChargesData>())
                    return true;
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] charge-capability check failed for '{nameOrGuid}': {ex.Message}"); }
        return false;
    }

    /// <summary>The prefab GUIDs an ability touches: every prefab matching its stem (group + casts), plus the
    /// spawned prefabs reached by the Group→Cast→SpawnPrefab walk. Mirrors ApplyAll's matching so the cache keys line up.</summary>
    static HashSet<int> CollectAbilityPrefabGuids(string stem)
    {
        var guids = new HashSet<int>();
        try
        {
            foreach (var kv in Core.PrefabNames)
            {
                string name = kv.Value;
                if (string.IsNullOrEmpty(name) || !name.StartsWith("AB_", StringComparison.Ordinal) || !Matches(name, stem)) continue;
                guids.Add(kv.Key);
                if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(new PrefabGUID(kv.Key), out Entity prefab)
                    || !prefab.Exists() || !Core.EntityManager.HasBuffer<AbilityGroupStartAbilitiesBuffer>(prefab)) continue;
                var starts = Core.EntityManager.GetBuffer<AbilityGroupStartAbilitiesBuffer>(prefab);
                for (int i = 0; i < starts.Length; i++)
                {
                    guids.Add(starts[i].PrefabGUID._Value);
                    if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(starts[i].PrefabGUID, out Entity cast)
                        || !cast.Exists() || !Core.EntityManager.HasBuffer<AbilitySpawnPrefabOnCast>(cast)) continue;
                    var spawns = Core.EntityManager.GetBuffer<AbilitySpawnPrefabOnCast>(cast);
                    for (int j = 0; j < spawns.Length; j++) guids.Add(spawns[j].SpawnPrefab._Value);
                }
            }
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] collect-guids failed for '{stem}': {ex.Message}"); }
        return guids;
    }

    /// <summary>Write a prefab's cached baseline back onto whichever components it carries. Each field guarded.</summary>
    static void RestorePrefab(Entity prefab, Original o)
    {
        try
        {
            if (o.Cooldown.HasValue && prefab.Has<AbilityCooldownData>())
                prefab.With((ref AbilityCooldownData d) => System.Runtime.CompilerServices.Unsafe.AsRef(in d.Cooldown)._Value = o.Cooldown.Value);
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
            if (o.ProjRange.HasValue && prefab.Has<Projectile>())
                prefab.With((ref Projectile p) => System.Runtime.CompilerServices.Unsafe.AsRef(in p.Range) = o.ProjRange.Value);
            if (o.LifeTimeAdded && prefab.Has<LifeTime>())
                Core.EntityManager.RemoveComponent<LifeTime>(prefab);   // v0.85.0: undo a force-timeout we added
            else if (o.LifeTime.HasValue && prefab.Has<LifeTime>())
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
            // v0.87.0: restore cast modifiers.
            if (o.Interrupt.HasValue && prefab.Has<AbilityInterruptData>())
                prefab.With((ref AbilityInterruptData d) => System.Runtime.CompilerServices.Unsafe.AsRef(in d.InterruptTypes) = o.Interrupt.Value);
            if ((o.MoveDuration.HasValue || o.MoveSpeed.HasValue || o.MoveUseCastDuration.HasValue) && prefab.Has<ModifyMovementDuringCastData>())
                prefab.With((ref ModifyMovementDuringCastData m) =>
                {
                    if (o.MoveDuration.HasValue) m.Duration._Value = o.MoveDuration.Value;
                    if (o.MoveSpeed.HasValue) m.MovementSpeedMultiplier._Value = o.MoveSpeed.Value;
                    if (o.MoveUseCastDuration.HasValue) System.Runtime.CompilerServices.Unsafe.AsRef(in m.UseCastDuration) = o.MoveUseCastDuration.Value;
                });
            if (o.BuffModFlags.HasValue && prefab.Has<BuffModificationFlagData>())   // v0.89.1: restore channel movement-impair
                prefab.With((ref BuffModificationFlagData m) => System.Runtime.CompilerServices.Unsafe.AsRef(in m.ModificationTypes) = o.BuffModFlags.Value);
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] restore failed: {ex.Message}"); }
    }
}
