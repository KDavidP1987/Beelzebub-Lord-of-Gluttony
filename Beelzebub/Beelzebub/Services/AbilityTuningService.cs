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
/// <c>AbilityTuning_Enabled</c>, and re-applied on <c>.beelz admin reload</c>.
/// </summary>
internal static class AbilityTuningService
{
    /// <summary>
    /// Apply every curated tuning entry to its matching cast prefabs. No-op unless
    /// <c>AbilityTuning_Enabled</c>. Called at init (after the prefab map + rules are ready)
    /// and on <c>.beelz admin reload</c>. Fully guarded — never throws into the caller.
    /// </summary>
    public static int ApplyAll()
    {
        if (!Beelzebub.Config.Settings.AbilityTuning_Enabled.Value) return 0;

        var map = Core.AbilityRules?.Current?.AbilityMap;
        if (map == null || map.Count == 0) return 0;

        // Collect entries that actually request a change.
        var tuned = new List<(string stem, AbilityRules.AbilityEntry entry)>();
        foreach (var kv in map)
        {
            var e = kv.Value;
            if (e == null) continue;
            if (e.Interruptible == null && !e.FreeMoveAfterCast && e.CastMovementSpeed == null) continue;
            tuned.Add((Stem(kv.Key), e));
        }
        if (tuned.Count == 0)
        {
            Core.Log.LogInfo("[Beelz TUNE] AbilityTuning_Enabled is on, but no AbilityMap entries request tuning.");
            return 0;
        }

        int prefabsTuned = 0;
        try
        {
            foreach (var kv in Core.PrefabNames)
            {
                string prefabName = kv.Value;
                if (string.IsNullOrEmpty(prefabName) || !prefabName.StartsWith("AB_", StringComparison.Ordinal)) continue;

                AbilityRules.AbilityEntry match = null;
                foreach (var (stem, entry) in tuned)
                {
                    if (Matches(prefabName, stem)) { match = entry; break; }
                }
                if (match == null) continue;

                if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(new PrefabGUID(kv.Key), out Entity prefab)
                    || !prefab.Exists())
                    continue;

                if (ApplyToPrefab(prefab, prefabName, match)) prefabsTuned++;
            }
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz TUNE] scan failed: {ex.Message}");
        }

        Core.Log.LogInfo($"[Beelz TUNE] applied tuning to {prefabsTuned} cast prefab(s) from {tuned.Count} curated entry(ies).");
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

    static bool ApplyToPrefab(Entity prefab, string prefabName, AbilityRules.AbilityEntry entry)
    {
        bool changed = false;
        var log = new StringBuilder();

        // #3 — cast interruption. AbilityInterruptData.InterruptTypes is a readonly field in
        // the reference assembly, so we write it through Unsafe.AsRef (the prefab struct is
        // blittable and AllowUnsafeBlocks is on). ManualInterrupt = cancellable by player
        // action (dash / raise shield); None = uninterruptible.
        if (entry.Interruptible.HasValue && prefab.Has<AbilityInterruptData>())
        {
            try
            {
                var val = entry.Interruptible.Value ? InterruptTypes.ManualInterrupt : InterruptTypes.None;
                prefab.With((ref AbilityInterruptData d) =>
                    System.Runtime.CompilerServices.Unsafe.AsRef(in d.InterruptTypes) = val);
                log.Append(entry.Interruptible.Value ? " interrupt=manual" : " interrupt=none");
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
        bool wantMove = entry.FreeMoveAfterCast || entry.CastMovementSpeed.HasValue;
        if (wantMove && prefab.Has<ModifyMovementDuringCastData>())
        {
            float castWindow = 0f;
            if (entry.FreeMoveAfterCast && prefab.TryGetComponent<AbilityCastTimeData>(out var ct))
                castWindow = ct.MaxCastTime._Value + ct.PostCastTime._Value;

            try
            {
                bool clampDuration = entry.FreeMoveAfterCast && castWindow > 0f;
                float window = castWindow;
                float? speed = entry.CastMovementSpeed;
                prefab.With((ref ModifyMovementDuringCastData m) =>
                {
                    if (speed.HasValue) m.MovementSpeedMultiplier._Value = speed.Value;
                    if (clampDuration) m.Duration._Value = window;
                });
                if (clampDuration) { log.Append($" freemove=1(window={window:F2}s)"); changed = true; }
                else if (entry.FreeMoveAfterCast) log.Append(" freemove=skip(no-cast-time)");
                if (speed.HasValue) { log.Append($" castspeed={speed.Value:F2}"); changed = true; }
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TUNE] movement write failed on {prefabName}: {ex.Message}"); }
        }

        if (changed) Core.Log.LogInfo($"[Beelz TUNE] {prefabName}:{log}");
        return changed;
    }
}
