using System;
using System.Collections.Generic;
using HarmonyLib;
using ProjectM;
using ProjectM.Gameplay.Systems;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace Beelzebub.Patches;

/// <summary>
/// W5: damage events. v0.133.0: the prefix now also drives per-hit damage scaling for captured casts
/// (<see cref="Services.DamageScaler"/>, Damage_Mode) — the "readonly fields" note below is historical: the
/// event is scaled by writing a modified COPY with SetComponentData.
///
/// **Current state:** observe only. The DealDamageEvent struct's fields
/// (SpellSource, Target, MainType, MainFactor, ResourceModifier, Modifier,
/// MaterialModifiers) are marked readonly in the IL2CPP interop wrapper, so we
/// can't mutate them via the standard <c>entity.With((ref T t) => ...)</c>
/// pattern. Bloodcraft's commented-out DealDamageSystemPatch (in their
/// StatChangeSystemPatch.cs) used `EntityManager.DestroyEntity(entity)` to
/// cancel damage events but never modified values — confirming the constraint.
///
/// **Path forward for per-ability scaling:** apply a temporary
/// `ModifyUnitStatBuff_DOTS` (SpellPower / PhysicalPower modifier) to the
/// player while a Beelzebub-granted ability cast resolves, then remove. This
/// is timing-sensitive and is the next-session W5 work. See task #48 notes.
///
/// **Cooldown scaling** is similarly deferred — the cast cooldown pipeline
/// needs hook research before we touch it.
///
/// Until full W5 ships, this Prefix is purely a diagnostic that logs (verbose
/// only) any damage event whose source ability is one of the player's
/// Beelzebub-granted slots. Useful for admins curating DamageScale values to
/// verify which abilities are actually firing.
/// </summary>
[HarmonyPatch(typeof(DealDamageSystem), nameof(DealDamageSystem.OnUpdate))]
internal static class DealDamageSystemPatch
{
    // v0.133.0: explicit priority — scale before other mods' prefixes read the event (Plugin logs any co-patchers).
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    public static void OnUpdatePrefix(DealDamageSystem __instance)
    {
        if (!Core.IsReady) return;
        Services.Heartbeat.Pulse();   // v0.81.0: drive periodic ticks during combat (throttled)

        // v0.23.1: this patch now serves two purposes:
        //   (a) telemetry (existing W5 observe path — verbose only)
        //   (b) offensive aggro injection — push player's damage targets into
        //       their summons' AggroBuffer. Always on when SummonsAreAllies.
        bool aggroEnabled = Beelzebub.Config.Settings.Transform_SummonsAreAllies.Value;
        bool telemetryEnabled = Beelzebub.Config.Settings.VerboseLogging.Value;
        // v0.133.0 (P2): per-hit damage attribution/scaling for captured casts (Damage_Mode != Off).
        var damageMode = Services.DamageScaler.EffectiveMode;
        bool damageEnabled = damageMode != Logic.DamageMode.Off;
        if (!aggroEnabled && !telemetryEnabled && !damageEnabled) return;

        NativeArray<Entity> entities;
        try
        {
            entities = __instance._Query.ToEntityArray(Allocator.Temp);
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] DealDamageSystemPatch: failed to read query: {ex}");
            return;
        }

        try
        {
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                // Scale BEFORE anything else reads the event, so every consumer this frame sees one value.
                if (damageEnabled)
                {
                    try { Services.DamageScaler.Process(entity, damageMode); }
                    catch (Exception ex) { Core.Log.LogError($"[Beelz DMG] damage scaling on {entity}: {ex}"); }
                }
                if (aggroEnabled)
                {
                    try { RouteAggro(entity); }
                    catch (Exception ex) { Core.Log.LogError($"[Beelz SUMMON] aggro-routing on {entity}: {ex}"); }
                }
                if (telemetryEnabled)
                {
                    try { Observe(entity); }
                    catch (Exception ex) { Core.Log.LogError($"[Beelz] DealDamageSystemPatch entity {entity}: {ex}"); }
                }
            }
        }
        finally
        {
            entities.Dispose();
        }
    }

    /// <summary>
    /// v0.23.1: when a player deals damage, push the target into all the
    /// player's live summons' AggroBuffer so they engage. The piece v0.23.0
    /// was missing (SyncAggro only handles defensive — what's attacking the
    /// player, not what the player's attacking).
    /// </summary>
    static void RouteAggro(Entity eventEntity)
    {
        if (!eventEntity.TryGetComponent<DealDamageEvent>(out var evt)) return;
        Entity source = evt.SpellSource;
        Entity target = evt.Target;
        if (!source.Exists() || !target.Exists()) return;
        if (source == target) return;
        if (target.IsPlayer()) return;

        Entity ownerPlayer = ResolveOwningPlayer(source);
        if (!ownerPlayer.Exists() || !ownerPlayer.IsPlayer()) return;

        ulong steamId = ownerPlayer.GetSteamId();
        if (steamId == 0) return;

        // v0.23.18 diagnostic: log every offensive aggro push from a transformed
        // player so we can verify this path fires when the user initiates combat.
        // If it doesn't, then the DealDamageEvent _Query field name has changed
        // or the patch isn't being invoked.
        if (Beelzebub.Config.Settings.VerboseLogging.Value)
        {
            var activeLog = Core.AbilityRegistry.GetSummonOwner(steamId, createIfMissing: false);
            if (activeLog != null)
            {
                string targetName = target.GetPrefabGuid().GetPrefabName() ?? "?";
                int liveSummonCount = activeLog.SummonedMinions?.Count ?? 0;
                Core.Log.LogInfo($"[Beelz SUMMON][offensive] player {steamId} damaged {targetName} → broadcasting to {liveSummonCount} summon(s)");
            }
        }

        Services.SummonAllyService.PushTargetToAllSummons(steamId, target);

        // v0.23.19: also re-prime the horde for combat (mode=1, leash-teleport,
        // SyncAggro, AggroConsumer anchor). Without this, the AggroBuffer entry
        // is added but units in leash mode (or beyond combat range) don't react.
        // Throttled to once per second per player inside HandleHordeEnteringCombat,
        // so sustained damage doesn't spam expensive mutations.
        Services.SummonAllyService.HandleHordeEnteringCombat(steamId, ownerPlayer);
    }

    /// <summary>The owning player via EntityOwner (v0.133.0: one shared cycle-safe ≤ 8-hop walk for damage,
    /// forcetimeout and aggro — rev 6.1 #15).</summary>
    internal static Entity ResolveOwningPlayer(Entity start) => Services.DamageScaler.ResolveOwningPlayer(start, out _);

    /// <summary>v0.133.0: report the per-target HP change of traced hits (observational truth table).</summary>
    /// <summary>Startup: name any OTHER mod patching DealDamageSystem.OnUpdate (ordering matters for Damage_Mode=Scale).</summary>
    internal static void LogCoPatchers(string ownId)
    {
        try
        {
            var m = AccessTools.Method(typeof(DealDamageSystem), nameof(DealDamageSystem.OnUpdate));
            var info = m == null ? null : Harmony.GetPatchInfo(m);
            if (info == null) return;
            var others = new List<string>();
            foreach (var p in info.Prefixes) if (p.owner != ownId) others.Add($"{p.owner} (prefix, priority {p.priority})");
            foreach (var p in info.Postfixes) if (p.owner != ownId) others.Add($"{p.owner} (postfix)");
            if (others.Count > 0)
                Core.Log.LogInfo($"[Beelz DMG] other patches on DealDamageSystem.OnUpdate: {string.Join(", ", others)}. Beelzebub's prefix runs at Priority.First.");
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz DMG] co-patcher scan failed: {ex.Message}"); }
    }

    [HarmonyPostfix]
    public static void OnUpdatePostfix()
    {
        if (!Core.IsReady) return;   // FlushProbes is a no-op unless Telemetry/trace recorded probes
        try { Services.DamageScaler.FlushProbes(); }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz DMG] probe flush failed: {ex.Message}"); }
    }

    static void Observe(Entity eventEntity)
    {
        if (!eventEntity.TryGetComponent<DealDamageEvent>(out var evt)) return;
        Entity spellSource = evt.SpellSource;
        if (!spellSource.Exists()) return;

        if (!spellSource.TryGetComponent<EntityOwner>(out var entityOwner)) return;
        Entity owner = entityOwner.Owner;
        if (!owner.IsPlayer()) return;

        ulong steamId = owner.GetSteamId();
        if (steamId == 0) return;

        PrefabGUID sourcePrefab = spellSource.GetPrefabGuid();
        string sourceName = sourcePrefab.GetPrefabName();
        if (string.IsNullOrEmpty(sourceName)) return;

        string grantName = FindMatchingGrant(steamId, sourceName);
        if (grantName == null) return;

        float scale = Core.AbilityRules.GetDamageScale(grantName);
        Core.Log.LogInfo($"[Beelz] dmg-event player={steamId} src={sourceName} matched-grant={grantName} damage-scale={scale:F2} (telemetry only — scaling not yet applied)");
    }

    static string FindMatchingGrant(ulong steamId, string sourceName)
    {
        var bound = new HashSet<int>();
        foreach (var (_, abilityGuid) in Core.AbilityRegistry.GetSlots(steamId)) bound.Add(abilityGuid);
        foreach (var (_, weaponSlots) in Core.AbilityRegistry.AllWeaponSlots(steamId))
            foreach (var (_, abilityGuid) in weaponSlots) bound.Add(abilityGuid);
        if (bound.Count == 0) return null;

        foreach (int grantGuid in bound)
        {
            string grantName = new PrefabGUID(grantGuid).GetPrefabName();
            if (string.IsNullOrEmpty(grantName)) continue;
            string stem = StripAbilityGroupSuffix(grantName);
            if (string.IsNullOrEmpty(stem)) continue;
            if (sourceName.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
            {
                return grantName;
            }
        }
        return null;
    }

    static string StripAbilityGroupSuffix(string name)
    {
        foreach (var s in new[] { "_AbilityGroup", "_Group" })
        {
            if (name.EndsWith(s, StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - s.Length);
        }
        return name;
    }
}
