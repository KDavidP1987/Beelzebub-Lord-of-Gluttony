using System.Collections.Generic;
using System.Text;
using Beelzebub.Config;
using Beelzebub.Services;
using HarmonyLib;
using ProjectM;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace Beelzebub.Patches;

[HarmonyPatch(typeof(DeathEventListenerSystem), nameof(DeathEventListenerSystem.OnUpdate))]
internal static class DeathEventListenerSystemPatch
{
    sealed class KillAggregate
    {
        public Entity Character;
        public int Captured;
        public int Skipped;
        public bool AnySave;
        // Unit name → captures from that unit (preserves order of first appearance)
        public Dictionary<string, int> ByUnit = new();
        // v0.44.0: units "Devoured" this batch (the rare jackpot grants the whole kit).
        public List<(string unit, int learned)> Devoured = new();
    }

    // v0.84.0 (#7): players who've already been congratulated for completing the collection this session
    // (avoids re-firing the milestone every subsequent capture). Runtime-only.
    static readonly HashSet<ulong> _completionAnnounced = new();

    [HarmonyPostfix]
    public static void OnUpdatePostfix(DeathEventListenerSystem __instance)
    {
        if (!Core.IsReady) Core.TryInitialize(nameof(DeathEventListenerSystemPatch));
        if (!Core.IsReady) return;

        // v0.81.0: periodic ticks (timed-transform revert, summon-lifespan sweep, cooldown enforcer,
        // debounced save) now run via the throttled Heartbeat — also pulsed from cast/buff/damage systems
        // so they fire during play even when nothing is dying (death events alone were too sparse).
        Services.Heartbeat.Pulse();

        if (!Settings.CaptureOnKill.Value) return;

        NativeArray<DeathEvent> deathEvents;
        try
        {
            deathEvents = __instance._DeathEventQuery.ToComponentDataArray<DeathEvent>(Allocator.Temp);
        }
        catch (System.Exception ex)
        {
            Core.Log.LogError($"[Beelz] DeathEventListenerSystemPatch failed to read query: {ex}");
            return;
        }

        var aggregates = new Dictionary<ulong, KillAggregate>();
        try
        {
            for (int i = 0; i < deathEvents.Length; i++)
            {
                var evt = deathEvents[i];

                // v0.23.10: prune any tracked summon that just died. Without this,
                // SummonStacks keeps dead-but-not-yet-destroyed entities as "alive"
                // for the cap → players can't resummon for several seconds after
                // their units die in combat. Pruning on the death event fires the
                // moment V Rising marks the unit dead, before destruction completes.
                try { SummonAllyService.PruneDeadSummon(evt.Died); }
                catch (System.Exception ex) { Core.Log.LogError($"[Beelz] PruneDeadSummon failed: {ex}"); }

                try
                {
                    Process(evt, aggregates);
                }
                catch (System.Exception ex)
                {
                    Core.Log.LogError($"[Beelz] DeathEvent Process failed (killer={evt.Killer}, died={evt.Died}): {ex}");
                }
            }
        }
        finally
        {
            deathEvents.Dispose();
        }

        try { FlushAggregates(aggregates); }
        catch (System.Exception ex) { Core.Log.LogError($"[Beelz] FlushAggregates failed: {ex}"); }
    }

    static void Process(DeathEvent deathEvent, Dictionary<ulong, KillAggregate> aggregates)
    {
        Entity killer = deathEvent.Killer;
        Entity died = deathEvent.Died;

        if (killer == died) return;
        if (!killer.IsPlayer()) return;
        if (!died.Exists()) return;
        if (died.Has<VBloodConsumeSource>()) return; // V-Blood: separate hook.
        if (died.Has<Trader>()) return;
        if (died.Has<BlockFeedBuff>()) return;
        if (!died.Has<UnitLevel>()) return;

        PrefabGUID unitGuid = died.GetPrefabGuid();
        if (unitGuid._Value == 0) return;
        string unitName = unitGuid.GetPrefabName();

        // F2: only real character prefabs are eligible. Resources (TM_*), Items, etc. would
        // otherwise pass the UnitLevel check and roll for transform unlocks. Player-owned
        // summons and "Servant" units are also excluded — those aren't bosses you defeat.
        if (!unitName.StartsWith("CHAR_", StringComparison.OrdinalIgnoreCase)) return;
        if (unitName.Contains("_Summon", StringComparison.OrdinalIgnoreCase)) return;
        if (unitName.Contains("_Servant", StringComparison.OrdinalIgnoreCase)) return;

        // B2: who counts as participant?
        var participants = ResolveParticipants(killer, died);
        foreach (var participant in participants)
        {
            ProcessForParticipant(participant, died, unitGuid, unitName, aggregates);
        }
    }

    static List<Entity> ResolveParticipants(Entity killer, Entity died)
    {
        string mode = (Settings.Capture_ShareCreditMode.Value ?? "KillerOnly").Trim();
        if (string.Equals(mode, "Proximity", System.StringComparison.OrdinalIgnoreCase))
        {
            float radius = Settings.Capture_ShareCreditRadius.Value;
            if (radius > 0f && died.TryGetComponent<Unity.Transforms.LocalToWorld>(out var ltw))
            {
                var near = EntityExtensions.FindPlayersNear(ltw.Position, radius);
                // Always include the killer (they may be slightly outside the radius for ranged kills).
                if (!near.Contains(killer)) near.Add(killer);
                return near;
            }
        }
        return new List<Entity> { killer };
    }

    static void ProcessForParticipant(Entity participant, Entity died, PrefabGUID unitGuid, string unitName, Dictionary<ulong, KillAggregate> aggregates)
    {
        if (!participant.IsPlayer()) return;
        ulong steamId = participant.GetSteamId();
        if (steamId == 0) return;

        if (!aggregates.TryGetValue(steamId, out var agg))
        {
            agg = new KillAggregate { Character = participant };
            aggregates[steamId] = agg;
        }

        // v0.43.23: friendly in-game name for player-facing chat (the [BEELZ:event]
        // wire lines below keep the raw, space-free prefab name for BCH parsing).
        string unitDisplay = FriendlyUnit(unitGuid);

        if (Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(died))
        {
            // v0.38.0 pity: current accumulated ability-bonus for this source, and
            // tallies so we can reset (on a new capture) or bump (on a dry kill) after.
            float pityAbility = Core.AbilityRegistry.GetPityBonus(steamId, CaptureSource.Regular, PityKind.Ability);
            int abilityRolls = 0, abilityWins = 0;
            // v0.99.0: roll against the unit's FULL cross-phase kit (base bar ∪ metadata reverse-map ∪
            // curated transform-form sets) instead of only the dead entity's current bar — so phase-gated
            // abilities are capturable from any kill. (The HasBuffer guard above still gates real ability units.)
            foreach (int abilityVal in Services.UnitKitService.FullEligibleKit(unitGuid._Value))
            {
                PrefabGUID ability = new PrefabGUID(abilityVal);
                if (ability._Value == 0) continue;

                string abilityName = ability.GetPrefabName();
                if (!Core.AbilityFilter.ShouldCapture(abilityName, ability._Value, out string reason))
                {
                    agg.Skipped++;
                    if (Settings.VerboseLogging.Value)
                        Core.Log.LogInfo($"[Beelz] skip {abilityName}: {reason}");
                    continue;
                }

                // C2: per-ability override beats the global default.
                float chance = Core.AbilityRules.TryGetRateOverride(abilityName, out var orR, out _)
                    ? orR
                    : Settings.DropChance_Ability_Regular.Value;
                // C3: scale by the unit's tier multiplier.
                chance *= died.ResolveTierMultiplier();
                if (chance <= 0f) continue;
                // v0.38.0: this ability is eligible to roll — apply the pity bonus.
                chance += pityAbility;
                abilityRolls++;
                if (chance < 1f && System.Random.Shared.NextDouble() > chance) continue;

                if (Core.AbilityRegistry.Add(steamId, unitGuid._Value, ability._Value, CaptureSource.Regular))
                {
                    abilityWins++;
                    agg.Captured++;
                    agg.ByUnit[unitDisplay] = agg.ByUnit.TryGetValue(unitDisplay, out var n) ? n + 1 : 1;
                    agg.AnySave = true;
                    if (Settings.VerboseLogging.Value)
                        Core.Log.LogInfo($"[Beelz] capture {abilityName} from {unitName} for {steamId}");
                    // Verbose: per-ability chat line stays inline so the player sees individual unlocks.
                    Core.Chat.Send(participant, Verbosity.Verbose, $"Acquired ability: {FriendlyAbility(ability)} (from {unitDisplay}).");
                    // Phase E4: parseable event for BCH (gated by per-player EmitApiEvents flag).
                    Core.Chat.SendEvent(participant,
                        $"[BEELZ:event] type=capture s=R u={unitGuid._Value} un={unitName} a={ability._Value} an={abilityName}");
                }
            }

            // v0.38.0 pity: a new capture this kill resets the ability streak; an
            // eligible-but-dry kill bumps it so the next kill is likelier to pay out.
            if (abilityRolls > 0)
            {
                if (abilityWins > 0)
                    Core.AbilityRegistry.ResetPity(steamId, CaptureSource.Regular, PityKind.Ability);
                else
                    Core.AbilityRegistry.BumpPity(steamId, CaptureSource.Regular, PityKind.Ability,
                        Settings.Capture_PityIncrementPerKill.Value, Settings.Capture_PityMaxBonus.Value);
            }
        }

        // v0.98.0 — independent DEVOUR + TRANSFORMATION rolls (mirrors the V-Blood path). Per-ability
        // captures already happened above (Ability pity). Most regular mobs only roll Devour; a few are
        // REGISTERED transform units (e.g. the basic werewolf NPC) and ALSO roll their own transform on a
        // separate chance + pity track — so the transformation is its own hard-won prize, never bundled
        // into the Devour. Real transformation is limited to units the game can render as a player form.
        bool isTier1 = Services.BossFormRegistry.Has(unitGuid._Value);
        bool isGateBoss = unitName.IndexOf("_GateBoss_", System.StringComparison.OrdinalIgnoreCase) >= 0;
        float tierMult = died.ResolveTierMultiplier();

        // DEVOUR roll — skip gate-boss variants (their kit is a preview of the real boss; a full-kit Devour
        // off the easy copy would trivialize the main fight). Per-ability captures off them still happened.
        float devourChance = Settings.DropChance_Devour_Regular.Value * tierMult;
        if (!isGateBoss && devourChance > 0f)
        {
            devourChance += Core.AbilityRegistry.GetPityBonus(steamId, CaptureSource.Regular, PityKind.Devour);
            if (System.Random.Shared.NextDouble() <= devourChance)
            {
                Core.AbilityRegistry.ResetPity(steamId, CaptureSource.Regular, PityKind.Devour);
                int learned = Services.DevourService.Devour(steamId, unitGuid, CaptureSource.Regular);
                agg.AnySave = true;
                agg.Devoured.Add((unitDisplay, learned));
                Core.Log.LogInfo($"[Beelz] {steamId} DEVOURED {unitName} (Regular): granted {learned} new ability(ies).");
                Core.Chat.SendEvent(participant,
                    $"[BEELZ:event] type=devour s=R u={unitGuid._Value} un={unitName} count={learned}");
                // v0.43.4: some bosses' adds aren't on the slot buffer — learn their signature summon(s) too.
                Services.SummonRegistry.GrantAndNotify(participant, steamId, unitGuid, CaptureSource.Regular);
            }
            else
            {
                Core.AbilityRegistry.BumpPity(steamId, CaptureSource.Regular, PityKind.Devour,
                    Settings.Capture_PityIncrement_Devour.Value, Settings.Capture_PityMax_Devour.Value);
            }
        }
        else if (isGateBoss && Settings.VerboseLogging.Value)
        {
            Core.Log.LogInfo($"[Beelz] skip Devour jackpot for gate-boss variant {unitName} (per-ability capture still applies)");
        }

        // TRANSFORMATION roll — only registered transform units (e.g. the basic werewolf NPC). Own chance +
        // pity; gate-boss naming does NOT block it (a transform unit's form is still wanted).
        if (isTier1
            && Core.AbilityRules.IsTransformUnitEnabled(unitGuid._Value)
            && Beelzebub.Services.AbilityRules.IsDifficultyAllowed(
                Core.AbilityRules.GetTransformDifficulty(unitGuid._Value),
                Beelzebub.Services.AbilityRules.GetServerDifficulty()))
        {
            float txChance = Settings.DropChance_TransformUnlock_Regular.Value * tierMult;
            if (txChance > 0f)
            {
                txChance += Core.AbilityRegistry.GetPityBonus(steamId, CaptureSource.Regular, PityKind.Transform);
                if (System.Random.Shared.NextDouble() <= txChance)
                {
                    Core.AbilityRegistry.ResetPity(steamId, CaptureSource.Regular, PityKind.Transform);
                    if (Core.AbilityRegistry.AddTransformUnlock(steamId, unitGuid._Value, CaptureSource.Regular))
                    {
                        agg.AnySave = true;
                        Core.Log.LogInfo($"[Beelz] {steamId} unlocked TRANSFORMATION: {unitName} (Regular).");
                        Core.Chat.Send(participant, Beelzebub.Services.Verbosity.Summary,
                            $"⭐ Unlocked TRANSFORMATION: {unitDisplay}! Use .beelz transform {unitDisplay}.");
                        Core.Chat.SendEvent(participant,
                            $"[BEELZ:event] type=transform-unlock s=R u={unitGuid._Value} un={unitName}");
                        Services.SummonRegistry.GrantAndNotify(participant, steamId, unitGuid, CaptureSource.Regular);
                    }
                }
                else
                {
                    Core.AbilityRegistry.BumpPity(steamId, CaptureSource.Regular, PityKind.Transform,
                        Settings.Capture_PityIncrement_Transform.Value, Settings.Capture_PityMax_Transform.Value);
                }
            }
        }
    }

    static void FlushAggregates(Dictionary<ulong, KillAggregate> aggregates)
    {
        foreach (var (steamId, agg) in aggregates)
        {
            if (!agg.AnySave) continue;

            if (agg.Captured > 0)
            {
                Core.Log.LogInfo(
                    $"[Beelz] {steamId} kill batch: captured {agg.Captured} ability(ies), skipped {agg.Skipped}, units={agg.ByUnit.Count}.");

                string summary = agg.ByUnit.Count == 1
                    ? BuildSingleUnitSummary(agg)
                    : BuildMultiUnitSummary(agg);
                Core.Chat.Send(agg.Character, Verbosity.Summary, summary);

                // v0.84.0 (#7): collection-complete milestone — when a new capture brings the player up to
                // the full capturable catalog, fire a one-time celebratory message + BCH event (the server-wide
                // "you've devoured everything" moment). Once per session per player.
                // v0.86.0 (Bug B): total = the full capturable universe (~1400), not the curated
                // AbilityMap.Count (~453) which a full-devour player can exceed — that made the milestone
                // fire FAR too early. Now 100% genuinely means "collected everything".
                int total = Beelzebub.Services.BestiaryService.TotalCapturableAbilities();
                if (total > 0 && Core.AbilityRegistry.CapturedCount(steamId) >= total && _completionAnnounced.Add(steamId))
                {
                    Core.Chat.Send(agg.Character, Verbosity.Summary,
                        "🏆 COLLECTION COMPLETE! You've devoured the bestiary — every available ability is yours. See where you rank with .beelz top!");
                    Core.Chat.SendEvent(agg.Character,
                        $"[BEELZ:event] type=collection-complete count={Core.AbilityRegistry.CapturedCount(steamId)} total={total}");
                    Core.Log.LogInfo($"[Beelz] {steamId} COMPLETED their ability collection ({Core.AbilityRegistry.CapturedCount(steamId)}/{total}).");

                    // v0.88.0: server-wide celebratory broadcast (config-gated, default ON).
                    string completerName = "A vampire";
                    if (agg.Character.TryGetComponent<ProjectM.PlayerCharacter>(out var cpc)
                        && cpc.UserEntity.TryGetComponent<ProjectM.Network.User>(out var cu))
                    {
                        string cn = cu.CharacterName.ToString();
                        if (!string.IsNullOrEmpty(cn)) completerName = cn;
                    }
                    try { Beelzebub.Services.BroadcastService.OnCollectionComplete(completerName); }
                    catch (Exception bex) { Core.Log.LogWarning($"[Beelz] collection-complete broadcast failed: {bex.Message}"); }
                }
            }

            bool silenceOwned = Core.AbilityRegistry.GetSilenceOwned(steamId);   // v0.83.0 (#6)
            foreach (var (unit, learned) in agg.Devoured)
            {
                // v0.83.0 (#6): suppress the "you already knew all" line for players who opted into silence.
                if (learned == 0 && silenceOwned) continue;
                Core.Chat.Send(agg.Character, Verbosity.Summary,
                    learned > 0
                        ? $"⭐ DEVOURED {unit} — learned all {learned} of its abilities at once! Slot them with .beelz grant."
                        : $"⭐ DEVOURED {unit} — you already knew all of its abilities.");
            }

            Core.Persistence.RequestSave();
        }
    }

    /// <summary>v0.43.23: friendly in-game unit name for chat (falls back to the raw prefab name).</summary>
    static string FriendlyUnit(PrefabGUID unit) =>
        Core.AbilityMetadata?.ResolveUnitName(unit._Value) ?? unit.GetPrefabName();

    /// <summary>v0.43.23: friendly in-game ability name for chat (falls back to the raw prefab name).</summary>
    static string FriendlyAbility(PrefabGUID ability)
    {
        var i = Core.AbilityMetadata?.Resolve(ability._Value);
        return (i != null && !string.IsNullOrEmpty(i.Name)) ? i.Name : ability.GetPrefabName();
    }

    static string BuildSingleUnitSummary(KillAggregate agg)
    {
        string unitName = null;
        foreach (var k in agg.ByUnit.Keys) { unitName = k; break; }
        return agg.Captured == 1
            ? $"Acquired 1 new ability from {unitName}."
            : $"Acquired {agg.Captured} new abilities from {unitName}.";
    }

    static string BuildMultiUnitSummary(KillAggregate agg)
    {
        var sb = new StringBuilder();
        sb.Append("Acquired ").Append(agg.Captured).Append(" abilities across ").Append(agg.ByUnit.Count).Append(" units (");
        bool first = true;
        foreach (var (unit, n) in agg.ByUnit)
        {
            if (!first) sb.Append(", ");
            sb.Append(unit);
            if (n > 1) sb.Append('×').Append(n);
            first = false;
            if (sb.Length > 380) { sb.Append(", …"); break; } // stay under the 510-byte chat limit
        }
        sb.Append(").");
        return sb.ToString();
    }
}
