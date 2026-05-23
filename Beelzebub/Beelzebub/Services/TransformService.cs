using System;
using System.Collections.Generic;
using System.Linq;
using Beelzebub.Config;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

internal enum TransformMode : byte
{
    Toggle = 0,
    Timed = 1,
    Disabled = 2,
}

internal sealed class TransformService
{
    public TransformMode ModeFor(CaptureSource source)
    {
        string raw = (source == CaptureSource.VBlood
            ? Settings.Transform_Mode_VBlood.Value
            : Settings.Transform_Mode_Regular.Value) ?? "Toggle";
        return raw.Trim().ToLowerInvariant() switch
        {
            "disabled" => TransformMode.Disabled,
            "timed" => TransformMode.Timed,
            _ => TransformMode.Toggle,
        };
    }

    public float DurationSecondsFor(CaptureSource source) =>
        source == CaptureSource.VBlood
            ? Settings.Transform_DurationSeconds_VBlood.Value
            : Settings.Transform_DurationSeconds_Regular.Value;

    public float CooldownSecondsFor(CaptureSource source) =>
        source == CaptureSource.VBlood
            ? Settings.Transform_CooldownSeconds_VBlood.Value
            : Settings.Transform_CooldownSeconds_Regular.Value;

    /// <summary>
    /// Activate a transformation. Returns (success, message). Caller is responsible
    /// for the chat reply. v0.14.0 Z1: spell-bar overrides live on a dedicated
    /// carrier buff (TransformBuffService) — no more weapon-swap apply caveat,
    /// no more stash-and-restore on revert, no more AbilityGroupSlot warnings.
    /// </summary>
    public (bool ok, string message) TryActivate(ulong steamId, int unitPrefabGuid)
    {
        if (!Core.AbilityRegistry.HasTransformUnlock(steamId, unitPrefabGuid))
        {
            return (false, "You haven't unlocked transformation for that unit.");
        }

        // TX1: per-unit kill-switch from TransformMap.
        if (!Core.AbilityRules.IsTransformUnitEnabled(unitPrefabGuid))
        {
            var pg = new PrefabGUID(unitPrefabGuid);
            return (false, $"Transformation into {pg.GetPrefabName()} is currently disabled by the server admin.");
        }

        // TX4: Brutal-only transformations can't be activated on a Basic server.
        string txDifficulty = Core.AbilityRules.GetTransformDifficulty(unitPrefabGuid);
        string serverMode = AbilityRules.GetServerDifficulty();
        if (!AbilityRules.IsDifficultyAllowed(txDifficulty, serverMode))
        {
            var pg = new PrefabGUID(unitPrefabGuid);
            return (false, $"{pg.GetPrefabName()} is a {txDifficulty}-tier transformation. Your server is {serverMode} mode.");
        }

        // Look up the source classification from the unlock record.
        var unlocks = Core.AbilityRegistry.ListTransforms(steamId);
        CaptureSource source = CaptureSource.Regular;
        foreach (var u in unlocks)
        {
            if (u.UnitPrefabGuid == unitPrefabGuid) { source = u.Source; break; }
        }

        TransformMode mode = ModeFor(source);
        if (mode == TransformMode.Disabled)
        {
            return (false, $"Transformations are disabled for {(source == CaptureSource.VBlood ? "V-Bloods" : "regular mobs")} by the server admin.");
        }

        // Cooldown check.
        var now = DateTime.UtcNow;
        var cooldownUntil = Core.AbilityRegistry.CooldownUntil(steamId, source);
        if (cooldownUntil > now)
        {
            var remaining = (cooldownUntil - now).TotalSeconds;
            return (false, $"On cooldown. {remaining:F0}s remaining.");
        }

        // If already transformed, revert first (cleanly destroys the carrier buff).
        var current = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (current is not null)
        {
            Revert(steamId, "Switching transformation.");
        }

        // Verify we can read the unit's ability list.
        var pgUnit = new PrefabGUID(unitPrefabGuid);
        var abilityList = GetTransformAbilities(pgUnit);
        if (abilityList.Count == 0)
        {
            return (false, $"Could not load abilities for {pgUnit.GetPrefabName()}.");
        }

        var active = new ActiveTransform
        {
            UnitPrefabGuid = unitPrefabGuid,
            Source = source,
            ActivatedAtUtc = now,
            Duration = mode == TransformMode.Timed ? TimeSpan.FromSeconds(DurationSecondsFor(source)) : null,
        };

        // Z1: apply spell bar via the dedicated carrier buff. Lifetime mirrors
        // the transform mode so the buff auto-destroys for Timed transforms.
        // Toggle transforms get an infinite buff; we destroy it on Revert.
        Entity character = EntityExtensions.FindCharacterBySteamId(steamId);
        bool appliedNow = false;
        PrefabGUID shapeshiftForm = PrefabGUID.Empty;
        if (character.Exists())
        {
            float? duration = active.Duration.HasValue ? (float)active.Duration.Value.TotalSeconds : (float?)null;
            // TX6: pass the unit GUID so TransformBuffService can attach the
            // TransformMap's stat scales (damage/cooldown/health/movement) to the
            // carrier buff. Zero unitGuid signals "no scaling" (legacy callsites).
            appliedNow = TransformBuffService.Apply(character, abilityList, duration, unitPrefabGuid);
            // Z2 / TX3: visual shapeshift fires when EITHER the global
            // `Transform_NativeShapeshift_Enabled` config is on (Z2 opt-in),
            // OR this specific transform is marked FullReplace in the
            // TransformMap (TX3 per-unit opt-in). FullReplace lets admins
            // turn on the cinematic visual for select units without
            // enabling it for every transform on the server.
            bool fullReplace = Core.AbilityRules.IsTransformFullReplace(unitPrefabGuid);
            shapeshiftForm = ShapeshiftService.Apply(character, pgUnit, forceEnabled: fullReplace);
            active.AppliedShapeshiftForm = shapeshiftForm._Value;
        }
        Core.AbilityRegistry.SetActiveTransform(steamId, active);

        string applyHint = appliedNow
            ? "Your spell bar now wields the unit's abilities."
            : "Spell bar will apply this frame.";
        string visualHint;
        if (shapeshiftForm._Value != 0)
        {
            visualHint = $" Visual form: {shapeshiftForm.GetPrefabName().Humanize()} (note: form breaks on first cast).";
        }
        else
        {
            // v0.21.1: make the no-visual case explicit so players don't keep
            // expecting a model swap that V Rising can't deliver. Only 5 native
            // shapeshift forms exist (Wolf/Bear/Rat/Spider/Toad); humanoid +
            // undead + construct units have no matching form.
            visualHint = " (No visual model swap — V Rising only provides Wolf/Bear/Rat/Spider/Toad shapeshift forms; this unit doesn't map. Spell bar + stats only.)";
        }
        // v0.24.1: push ability metadata to chat so the player sees what they
        // just got. V Rising's action-bar tooltip is client-rendered and
        // CAN'T be modified from a server-only mod; for V-Blood / NPC
        // abilities most lack client-side localization → tooltips show
        // "No Name". The chat dump is the server-side stopgap.
        if (character.Exists())
        {
            try { BroadcastTransformLoadout(character, pgUnit, abilityList); }
            catch (System.Exception ex) { Core.Log.LogWarning($"[Beelz] BroadcastTransformLoadout failed: {ex.Message}"); }
        }

        return (true, $"Transformed into {pgUnit.GetPrefabName()} ({abilityList.Count} ability slots).{visualHint} {applyHint} Use .beelz revert to end.");
    }

    /// <summary>
    /// v0.24.1: send a multi-line chat summary of the new spell bar's contents
    /// using <see cref="AbilityMetadataService"/>. Compensates for V Rising's
    /// client tooltips not rendering NPC ability text. One short line per slot.
    /// </summary>
    static void BroadcastTransformLoadout(Entity playerCharacter, PrefabGUID unitGuid, System.Collections.Generic.List<int> abilityList)
    {
        if (Core.AbilityMetadata is null || abilityList is null || abilityList.Count == 0) return;

        Core.Chat.Send(playerCharacter, Verbosity.Summary,
            $"--- {unitGuid.GetPrefabName().Humanize()} spell bar ---");

        // v0.24.5: actual V Rising player keybind labels (1-indexed, idx 0-5
        // correspond to player slots 1-6). User-verified 2026-05-23. Slots 0,
        // 7, 8 are invalid via ReplaceAbilityOnSlotBuff so we only have 6 labels.
        string[] slotLabels = { "Primary (Q)", "Travel (Space)", "Shift", "Heavy (E)", "Spell 1 (R)", "Spell 2 (C)" };

        for (int i = 0; i < abilityList.Count; i++)
        {
            int ag = abilityList[i];
            if (ag == 0) continue;
            var info = Core.AbilityMetadata.Resolve(ag);
            string slotName = i < slotLabels.Length ? slotLabels[i] : $"Slot {i}";
            string title = string.IsNullOrEmpty(info.Name)
                ? new PrefabGUID(ag).GetPrefabName()
                : info.Name;
            string school = !string.IsNullOrEmpty(info.School) ? $" [{info.School}]" : "";
            string cd = info.CooldownSeconds.HasValue ? $" · cd {info.CooldownSeconds.Value:F1}s" : "";
            string warning = info.Incompatible ? " ⚠" : "";

            Core.Chat.Send(playerCharacter, Verbosity.Summary,
                $"  {slotName}: {title}{school}{cd}{warning}");

            // v0.24.2: per-slot incompatibility reason on its own line so the
            // player understands which specific spells won't fire correctly.
            if (info.Incompatible)
            {
                string reason = !string.IsNullOrEmpty(info.IncompatibleReason)
                    ? $" — {info.IncompatibleReason}"
                    : "";
                Core.Chat.Send(playerCharacter, Verbosity.Summary,
                    $"    ⚠ may misbehave when you cast it{reason}");
            }

            // Description on its own line if we have one. Keep terse.
            if (!string.IsNullOrEmpty(info.Description))
            {
                string desc = info.Description.Replace("\\n", " ").Replace("\n", " ").Trim();
                if (info.Parameters != null)
                {
                    foreach (var (k, v) in info.Parameters) desc = desc.Replace("%" + k + "%", v);
                }
                // Cap length to a single chat line per slot — long descriptions
                // will look truncated; user can `.beelz info <name>` for full text.
                if (desc.Length > 300) desc = desc.Substring(0, 297) + "…";
                Core.Chat.Send(playerCharacter, Verbosity.Verbose, $"    {desc}");
            }
        }

        Core.Chat.Send(playerCharacter, Verbosity.Summary,
            "Use .beelz active to re-display this anytime. .beelz info <name> for full details.");
    }

    /// <summary>
    /// End a player's active transformation. Returns (wasReverted, appliedNow).
    /// `appliedNow` is true when the carrier buff was destroyed cleanly this frame.
    /// </summary>
    public (bool reverted, bool appliedNow) Revert(ulong steamId, string reason = null)
    {
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active is null) return (false, false);

        Core.AbilityRegistry.ClearActiveTransform(steamId);

        // Z1: destroying the carrier buff lets V Rising re-resolve slot bindings
        // naturally — player spell-book selections return to slots 5/6, weapon
        // naturals return to slots 1/2/3/4, no orphan AbilityGroupSlot warnings.
        bool appliedNow = false;
        Entity character = EntityExtensions.FindCharacterBySteamId(steamId);
        if (character.Exists())
        {
            appliedNow = TransformBuffService.Remove(character);
            // Z2: drop the visual shapeshift if one was applied (off by default).
            if (active.AppliedShapeshiftForm != 0)
            {
                ShapeshiftService.Remove(character, new PrefabGUID(active.AppliedShapeshiftForm));
            }
        }

        // v0.23.7: switch from staged Tick-driven drain to immediate-drain.
        // The Tick-driven path only fires when DeathEventListenerSystem runs
        // (gated on actual deaths), which isn't reliable enough — players see
        // summons persist across revert/transform-switch. Immediate drain uses
        // the same proven multi-path destroy as `.beelz admin desummon-all`.
        if (active.SummonedMinions is { Count: > 0 })
        {
            int queued = 0;
            foreach (Entity minion in active.SummonedMinions)
            {
                if (minion.Exists()) { SummonAllyService.EnqueueAdminDespawn(minion); queued++; }
            }
            active.SummonedMinions.Clear();
            active.SummonStacks?.Clear();
            int processed = SummonAllyService.DrainAdminQueueImmediate();
            Core.Log.LogInfo($"[Beelz] revert {steamId}: queued {queued}, processed {processed} summoned minion(s) via immediate drain.");
        }

        TransformMode mode = ModeFor(active.Source);
        float cooldownSec = CooldownSecondsFor(active.Source);
        if (mode == TransformMode.Timed && cooldownSec > 0f)
        {
            Core.AbilityRegistry.SetCooldownUntil(steamId, active.Source, DateTime.UtcNow.AddSeconds(cooldownSec));
        }
        return (true, appliedNow);
    }

    /// <summary>
    /// Tick called per frame to auto-revert Timed transforms whose duration has elapsed.
    /// Z1: the carrier buff has its own LifeTime; this tick is a safety net in case
    /// the buff outlived its timer (e.g. Toggle mode never expires through here at all,
    /// and Timed mode could miss the auto-destroy if the entity was queued for cleanup).
    /// </summary>
    // v0.23.0: SyncAggro throttle — heavy per-frame buffer mutations would be wasteful.
    // Run aggro injection ~3x/sec; players' enemies don't change that fast.
    DateTime _lastAggroSync = DateTime.MinValue;
    static readonly TimeSpan AggroSyncInterval = TimeSpan.FromMilliseconds(333);

    public void Tick()
    {
        var now = DateTime.UtcNow;

        // v0.23.0: drain staged-despawn queues. Budget is per-active-transform AND
        // per-pending-despawn record, so multiple players' reverts proceed in parallel
        // without compounding into a single-frame batch destroy.
        int budget = Beelzebub.Config.Settings.Transform_DespawnBudgetPerFrame.Value;
        if (budget > 0) SummonAllyService.DrainDespawnQueues(budget);
        Core.AbilityRegistry.ClearDrainedPendingDespawns();

        // v0.23.0: aggro injection — push player's enemies into each summon's
        // AggroBuffer so they engage targets instead of standing around. Throttled.
        if ((now - _lastAggroSync) >= AggroSyncInterval)
        {
            _lastAggroSync = now;
            try { SummonAllyService.SyncAggroAll(); }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz SUMMON] SyncAggroAll failed: {ex.Message}"); }

            // v0.23.1: leash check — same throttle. Teleport summons back to player
            // if they've wandered out of leash radius, or were stuck in Return/Idle
            // state. Bloodcraft's BehaviourStateChangedSystem + TryReturnFamiliar
            // patterns rolled into one tick.
            try
            {
                float leash = Beelzebub.Config.Settings.Transform_SummonLeashRadius.Value;
                if (leash > 0f) SummonAllyService.LeashCheckAll(leash);
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz SUMMON] LeashCheckAll failed: {ex.Message}"); }
        }

        foreach (var (steamId, active) in Core.AbilityRegistry.AllActiveTransforms())
        {
            if (!active.Duration.HasValue) continue;
            if (now - active.ActivatedAtUtc < active.Duration.Value) continue;

            Core.AbilityRegistry.ClearActiveTransform(steamId);
            float cooldownSec = CooldownSecondsFor(active.Source);
            if (cooldownSec > 0f)
            {
                Core.AbilityRegistry.SetCooldownUntil(steamId, active.Source, now.AddSeconds(cooldownSec));
            }

            string unitName = new PrefabGUID(active.UnitPrefabGuid).GetPrefabName();
            Core.Log.LogInfo($"[Beelz] auto-revert {steamId} from {unitName} after {active.Duration.Value.TotalSeconds:F0}s.");

            Entity character = EntityExtensions.FindCharacterBySteamId(steamId);
            bool appliedNow = false;
            if (character.Exists())
            {
                appliedNow = TransformBuffService.Remove(character);
                if (active.AppliedShapeshiftForm != 0)
                {
                    ShapeshiftService.Remove(character, new PrefabGUID(active.AppliedShapeshiftForm));
                }
            }
            // v0.23.7: immediate-drain on auto-revert (same as manual revert).
            if (active.SummonedMinions is { Count: > 0 })
            {
                foreach (Entity minion in active.SummonedMinions)
                {
                    if (minion.Exists()) SummonAllyService.EnqueueAdminDespawn(minion);
                }
                active.SummonedMinions.Clear();
                active.SummonStacks?.Clear();
                SummonAllyService.DrainAdminQueueImmediate();
            }
            if (character.Exists())
            {
                string cooldownNote = cooldownSec > 0f ? $" Cooldown {cooldownSec:F0}s." : "";
                string restoreHint = appliedNow ? "Your spell bar is restored." : "Spell bar restored.";
                Core.Chat.Send(character, Verbosity.Summary,
                    $"Transformation ended ({unitName}). {restoreHint}{cooldownNote}");
                Core.Chat.SendEvent(character,
                    $"[BEELZ:event] type=transform-ended u={active.UnitPrefabGuid} un={unitName} reason=auto");
            }
        }
    }

    /// <summary>
    /// Returns the up-to-6 ability GUIDs to grant slots 1..6 when the player is transformed
    /// into the given unit. Walks the unit prefab's AbilityGroupSlotBuffer, filters via
    /// AbilityFilter, matches the requested phase (TX5), and applies the Z3 Hard-only
    /// fallback (see <see cref="ResolveTransformAbilityNames"/>).
    /// </summary>
    public List<int> GetTransformAbilities(PrefabGUID unitGuid, int phase = 1)
    {
        var result = new List<int>(6);
        if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(unitGuid, out Entity prefabEntity)) return result;
        if (!Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(prefabEntity)) return result;

        var slots = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(prefabEntity);

        // Z3 (v0.19.0): pre-pass — collect Basic-tier ability names, NORMALIZED so
        // sibling-matching survives V-Blood naming asymmetry. V Rising V-Blood
        // abilities have `_VBlood_` in the name but their Hard variants don't,
        // e.g. `AB_X_Y_VBlood_RockSmash_AbilityGroup` (basic) vs
        // `AB_X_Y_RockSmash_Hard_AbilityGroup` (hard). The pre-0.19.0 logic
        // stripped only `_Hard_` from the hard name and looked for a literal
        // sibling — which never matched because the basic carries `_VBlood_`.
        // Result: every Hard variant for a V-Blood was treated as "Hard-only"
        // and got through Z3's carve-out, taking slot space and pushing real
        // ultimate-tier abilities off the bar.
        // Normalization: strip both `_Hard_` and `_VBlood_` tokens, then compare.
        var basicNormPresent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < slots.Length; i++)
        {
            PrefabGUID ab = slots[i].BaseAbilityGroupOnSlot;
            if (ab._Value == 0) continue;
            string n = ab.GetPrefabName();
            if (string.IsNullOrEmpty(n)) continue;
            if (n.IndexOf("_Hard_", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            basicNormPresent.Add(NormalizeForHardCompare(n));
        }

        for (int i = 0; i < slots.Length && result.Count < 6; i++)
        {
            PrefabGUID ability = slots[i].BaseAbilityGroupOnSlot;
            if (ability._Value == 0) continue;
            string name = ability.GetPrefabName();
            if (string.IsNullOrEmpty(name)) continue;

            // Z3: only let a Hard variant through if its normalized form has no
            // matching Basic sibling on this prefab. With v0.19.0 normalization,
            // V-Blood Hard variants now correctly match their `_VBlood_`-tagged
            // basic counterparts and get filtered, freeing up slot space for
            // the unit's real signature abilities.
            bool hardOnlyFallback = false;
            if (name.IndexOf("_Hard_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string norm = NormalizeForHardCompare(name);
                if (!basicNormPresent.Contains(norm)) hardOnlyFallback = true;
            }

            if (!hardOnlyFallback && !Core.AbilityFilter.ShouldCapture(name, ability._Value, out _)) continue;
            // TX5: phase filter. Skip abilities whose matrix entry says they belong to a different phase.
            if (Core.AbilityRules.GetAbilityPhase(name) != phase) continue;

            result.Add(ability._Value);
        }

        // v0.24.5: reorder by V Rising's actual player keybind layout
        // (1=Q, 2=Space, 3=Shift, 4=E, 5=R, 6=C — user-verified). Admin
        // slot template overrides take precedence over heuristic if set
        // on the TransformMap entry. See ReorderForPlayerSlots for full
        // mapping rules.
        return ReorderForPlayerSlots(result, unitGuid._Value);
    }

    /// <summary>
    /// v0.24.5 (CORRECTED): classify abilities by intent and place them in
    /// the player-UI-convention slots. v0.24.4 used wrong slot indices; the
    /// ACTUAL V Rising player keybinds (verified by user testing 2026-05-23)
    /// are:
    /// <list type="bullet">
    ///   <item><b>Slot 1 (idx 0) = Q</b> — Primary weapon attack</item>
    ///   <item><b>Slot 2 (idx 1) = Space bar</b> — Travel / teleport</item>
    ///   <item><b>Slot 3 (idx 2) = Shift</b> — usually unused (BCH can override)</item>
    ///   <item><b>Slot 4 (idx 3) = E</b> — Secondary weapon attack</item>
    ///   <item><b>Slot 5 (idx 4) = R</b> — First standard spell slot</item>
    ///   <item><b>Slot 6 (idx 5) = C</b> — Second standard spell slot</item>
    /// </list>
    /// Slots 0, 7, 8 are invalid via <see cref="ReplaceAbilityOnSlotBuff.Slot"/>
    /// (user tested). The ultimate slot (R-key in vanilla minus the spell
    /// override) appears to use a different mechanism; for now the boss's
    /// Hard-tier ultimate equivalent lands on slot 6 (C — second spell).
    ///
    /// Classification rules (substring on prefab name, case-insensitive):
    /// <list type="bullet">
    ///   <item><b>Primary</b> → slot 1: <c>_Projectile_Group</c>, <c>_MeleeAttack_</c>, <c>_Auto_</c></item>
    ///   <item><b>Travel</b> → slot 2: <c>_Teleport_</c>, <c>_Travel_</c>, <c>_Dash_</c>, <c>_Leap_</c>, <c>_PhaseShift_</c></item>
    ///   <item><b>Heavy/Q-Weapon</b> → slot 4 (E): higher-cooldown attack patterns</item>
    ///   <item><b>Ultimate</b> → slot 6: <c>_Hard_</c>, <c>_Ultimate_</c></item>
    ///   <item><b>Spell</b> → fills slots 3, 5 in original order</item>
    /// </list>
    /// </summary>
    public static List<int> ReorderForPlayerSlots(List<int> abilities, int unitPrefabGuid = 0)
    {
        if (abilities == null || abilities.Count == 0) return abilities ?? new List<int>();

        // v0.24.5: admin slot template wins over heuristic when present.
        // Look up the admin's per-V-Blood SlotTemplate map. If set, build the
        // slot array from it first; then fill unspecified slots with the
        // heuristic from remaining abilities.
        int[] slot = new int[6];
        var consumed = new HashSet<int>();

        Dictionary<string, string> adminTemplate = null;
        if (unitPrefabGuid != 0)
        {
            try { adminTemplate = Core.AbilityRules.GetTransformSlotTemplate(unitPrefabGuid); }
            catch { /* AbilityRules may not be ready in test contexts */ }
        }
        if (adminTemplate != null)
        {
            foreach (var (slotKey, abilityName) in adminTemplate)
            {
                if (!int.TryParse(slotKey, out int s) || s < 1 || s > 6) continue;
                if (string.IsNullOrWhiteSpace(abilityName)) continue;
                // Find the matching ability GUID in the supplied list (case-insensitive name match).
                int match = 0;
                foreach (int ag in abilities)
                {
                    string n = new PrefabGUID(ag).GetPrefabName();
                    if (string.IsNullOrEmpty(n)) continue;
                    if (string.Equals(n, abilityName, StringComparison.OrdinalIgnoreCase))
                    {
                        match = ag;
                        break;
                    }
                }
                if (match != 0)
                {
                    slot[s - 1] = match;
                    consumed.Add(match);
                }
            }
        }

        // Heuristic pass on the remaining abilities. Slot indices match the
        // V Rising keybind layout in the docstring above.
        int? primary = null, travel = null, heavy = null, ultimate = null;
        var spells = new List<int>();

        foreach (int ag in abilities)
        {
            if (consumed.Contains(ag)) continue;
            string name = new PrefabGUID(ag).GetPrefabName() ?? "";

            // Order matters — check Ultimate before Heavy so Hard-tier
            // attacks go to slot 6 rather than slot 4.
            if (primary == null && slot[0] == 0 && IsPrimaryAttack(name)) { primary = ag; continue; }
            if (travel == null && slot[1] == 0 && IsTravelAbility(name)) { travel = ag; continue; }
            if (ultimate == null && slot[5] == 0 && IsUltimate(name)) { ultimate = ag; continue; }
            if (heavy == null && slot[3] == 0 && IsHeavyAttack(name)) { heavy = ag; continue; }
            spells.Add(ag);
        }

        if (primary.HasValue) slot[0] = primary.Value;     // Slot 1 (Q)
        if (travel.HasValue) slot[1] = travel.Value;       // Slot 2 (Space)
        if (heavy.HasValue) slot[3] = heavy.Value;         // Slot 4 (E)
        if (ultimate.HasValue) slot[5] = ultimate.Value;   // Slot 6 (C)

        // Remaining slots (3 = Shift, 5 = R, 2 if no travel, 4 if no heavy)
        // fill from spells in their original order.
        int spellIdx = 0;
        for (int i = 0; i < 6; i++)
        {
            if (slot[i] != 0) continue;
            if (spellIdx >= spells.Count) break;
            slot[i] = spells[spellIdx++];
        }

        // Compact and return.
        var result = new List<int>(6);
        for (int i = 0; i < 6; i++)
        {
            if (slot[i] != 0) result.Add(slot[i]);
        }
        // Append any spell overflow (very rare; boss has > 6 valid abilities).
        while (spellIdx < spells.Count && result.Count < 6) result.Add(spells[spellIdx++]);
        return result;
    }

    /// <summary>
    /// v0.24.5: heavy/secondary attack pattern — boss's "Q-special" equivalent.
    /// Distinct from <see cref="IsPrimaryAttack"/> (basic auto) and
    /// <see cref="IsUltimate"/> (Hard tier). Used for slot 4 (E key).
    /// </summary>
    static bool IsHeavyAttack(string name)
    {
        return name.IndexOf("_Charge_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Smash_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Slam_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Heavy_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Cleave_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Strike_", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool IsPrimaryAttack(string name)
    {
        return name.IndexOf("_Projectile_Group", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_MeleeAttack_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Auto_", StringComparison.OrdinalIgnoreCase) >= 0
            // Plain "_Projectile_" without _Nova_ / _Hard_ — boss basic attacks.
            || (name.IndexOf("_Projectile_", StringComparison.OrdinalIgnoreCase) >= 0
                && name.IndexOf("_Nova_", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("_Hard_", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("_Charge_", StringComparison.OrdinalIgnoreCase) < 0);
    }

    static bool IsTravelAbility(string name)
    {
        return name.IndexOf("_Teleport_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Travel_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Dash_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Leap_", StringComparison.OrdinalIgnoreCase) >= 0
            // _Phase_ alone is too broad (matches phase entities); require Phase as suffix-token only.
            || name.IndexOf("_PhaseShift_", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool IsUltimate(string name)
    {
        // _Hard_ variants are the boss's "Brutal-tier" ultimate-equivalent.
        // _Ultimate_ pattern doesn't appear in V Rising NPC naming but we
        // include it as a future-safe match.
        return name.IndexOf("_Hard_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Ultimate_", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Z3 (v0.19.0): normalize an ability name for Hard/Basic sibling comparison.
    /// Strips both `_Hard_` and `_VBlood_` tokens — V Rising's V-Blood abilities
    /// have `_VBlood_` in their basic name but not in their Hard duplicate, so
    /// a literal sibling-name match fails without normalization.
    /// </summary>
    static string NormalizeForHardCompare(string abilityName)
    {
        if (string.IsNullOrEmpty(abilityName)) return abilityName;
        string s = ReplaceFirst(abilityName, "_Hard_", "_");
        s = ReplaceFirst(s, "_VBlood_", "_");
        return s;
    }

    /// <summary>
    /// TX5: returns the set of distinct phase numbers defined for this unit's abilities
    /// in the AbilityMap. Always includes Phase 1. If the matrix has phase-2 or phase-3
    /// entries among the unit's natural abilities, those phases are included.
    /// </summary>
    public List<int> GetAvailablePhases(PrefabGUID unitGuid)
    {
        var phases = new HashSet<int> { 1 };
        if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(unitGuid, out Entity prefabEntity)) return new List<int> { 1 };
        if (!Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(prefabEntity)) return new List<int> { 1 };

        var slots = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(prefabEntity);
        for (int i = 0; i < slots.Length; i++)
        {
            PrefabGUID ability = slots[i].BaseAbilityGroupOnSlot;
            if (ability._Value == 0) continue;
            phases.Add(Core.AbilityRules.GetAbilityPhase(ability.GetPrefabName()));
        }
        return phases.OrderBy(p => p).ToList();
    }

    static string ReplaceFirst(string source, string search, string replacement)
    {
        int idx = source.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return source;
        return source.Substring(0, idx) + replacement + source.Substring(idx + search.Length);
    }
}
