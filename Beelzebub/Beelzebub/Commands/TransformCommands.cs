using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Beelzebub.Services;
using Stunlock.Core;
using Unity.Entities;
using VampireCommandFramework;

namespace Beelzebub.Commands;

[CommandGroup("beelz")]
internal static class TransformCommands
{
    [Command("transforms", description: "List your transform unlocks. Optional filter: vblood | shard | regular. Usage: .beelz transforms [filter]")]
    public static void Transforms(ChatCommandContext ctx, string filter = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var unlocks = Core.AbilityRegistry.ListTransforms(steamId);
        if (unlocks.Count == 0)
        {
            ctx.Reply("No transformation unlocks yet. Defeat units (each kill rolls a small chance to unlock).");
            return;
        }

        // v0.39.0: optional filter. Index over the FULL list first so `.beelz transform <idx>` stays valid.
        string f = filter?.Trim().ToLowerInvariant();
        bool fVBlood = f is "vblood" or "vbloods" or "v";
        bool fShard = f is "shard" or "shards" or "shardboss";
        bool fRegular = f is "regular" or "r";
        IEnumerable<(int idx, UnlockedTransform unlock)> indexed = unlocks.Select((u, idx) => (idx, unlock: u));
        if (fVBlood) indexed = indexed.Where(t => t.unlock.Source == CaptureSource.VBlood);
        else if (fShard) indexed = indexed.Where(t => Core.Transforms.IsShardBoss(t.unlock.UnitPrefabGuid));
        else if (fRegular) indexed = indexed.Where(t => t.unlock.Source == CaptureSource.Regular);
        var filtered = indexed.ToList();

        string scope = fVBlood ? " (V-Bloods)" : fShard ? " (shard bosses)" : fRegular ? " (regular)" : "";
        string count = filtered.Count != unlocks.Count ? $"{filtered.Count} of {unlocks.Count}" : $"{unlocks.Count}";
        ctx.Reply($"Transformation unlocks{scope} ({count}):");
        if (filtered.Count == 0) { ctx.Reply("  (none match that filter — try: vblood | shard | regular, or no filter for all)"); return; }

        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active is not null)
        {
            string activeName = Core.AbilityMetadata.ResolveUnitName(active.UnitPrefabGuid);
            if (active.Duration.HasValue)
            {
                var elapsed = (System.DateTime.UtcNow - active.ActivatedAtUtc).TotalSeconds;
                var remaining = active.Duration.Value.TotalSeconds - elapsed;
                ctx.Reply($"ACTIVE: {activeName} ({remaining:F0}s remaining)");
            }
            else
            {
                ctx.Reply($"ACTIVE: {activeName} (Toggle mode — manual revert)");
            }
        }

        // Group shard bosses / V-Bloods / regular mobs into their own sections (shard first).
        var byGroup = filtered
            .GroupBy(t => Core.Transforms.IsShardBoss(t.unlock.UnitPrefabGuid) ? 0
                        : t.unlock.Source == CaptureSource.VBlood ? 1 : 2)
            .OrderBy(g => g.Key);

        foreach (var grp in byGroup)
        {
            ctx.Reply(grp.Key == 0 ? "-- Shard bosses --" : grp.Key == 1 ? "-- V-Bloods --" : "-- Regular mobs --");
            foreach (var (idx, unlock) in grp.OrderBy(t => Core.AbilityMetadata.ResolveUnitName(t.unlock.UnitPrefabGuid)))
            {
                ctx.Reply($"  {idx}: {Core.AbilityMetadata.ResolveUnitName(unlock.UnitPrefabGuid)}");
            }
        }
    }

    [Command("transform", description: "Activate transformation. Usage: .beelz transform <index|unitName>. Applies immediately.")]
    public static void Transform(ChatCommandContext ctx, string unitOrIndex)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var unlocks = Core.AbilityRegistry.ListTransforms(steamId);
        if (unlocks.Count == 0) { ctx.Reply("You have no transformation unlocks."); return; }

        int unitGuid;
        if (int.TryParse(unitOrIndex, out int parsedIndex) && parsedIndex >= 0 && parsedIndex < unlocks.Count)
        {
            unitGuid = unlocks[parsedIndex].UnitPrefabGuid;
        }
        else
        {
            // v0.28.1: resolve by name against BOTH the display name and the raw
            // prefab name. Prefer an EXACT match, then a V-Blood, then any substring
            // match — so "dracula" picks the Dracula V-Blood, not a Dracula minion /
            // illusion / blood-soul that also contains the word (the old FirstOrDefault
            // substring match grabbed whichever such unit happened to be first).
            const System.StringComparison OIC = System.StringComparison.OrdinalIgnoreCase;
            string Prefab(int g) => new PrefabGUID(g).GetPrefabName() ?? "";
            string Display(int g) => Core.AbilityMetadata?.ResolveUnitName(g) ?? Prefab(g);

            var matches = unlocks.Where(u =>
                Prefab(u.UnitPrefabGuid).Contains(unitOrIndex, OIC) ||
                Display(u.UnitPrefabGuid).Contains(unitOrIndex, OIC)).ToList();
            if (matches.Count == 0)
            {
                ctx.Reply($"No unlocked transform matches '{unitOrIndex}'. Use .beelz transforms for the list.");
                return;
            }

            UnlockedTransform chosen;
            var exact = matches.Where(u =>
                Prefab(u.UnitPrefabGuid).Equals(unitOrIndex, OIC) ||
                Display(u.UnitPrefabGuid).Equals(unitOrIndex, OIC)).ToList();
            if (exact.Count > 0) chosen = exact[0];
            else
            {
                var vb = matches.FirstOrDefault(u => u.Source == CaptureSource.VBlood);
                chosen = vb.UnitPrefabGuid != 0 ? vb : matches[0];
            }

            if (matches.Count > 1)
                ctx.Reply($"'{unitOrIndex}' matched {matches.Count} unlocks — using {Display(chosen.UnitPrefabGuid)}. (Use the index from .beelz transforms to be exact.)");
            unitGuid = chosen.UnitPrefabGuid;
        }

        var (ok, message) = Core.Transforms.TryActivate(steamId, unitGuid);
        ctx.Reply(message);
        if (ok)
        {
            Core.Chat.SendEvent(ctx.Event.SenderCharacterEntity,
                $"[BEELZ:event] type=transform-activated u={unitGuid} un={new PrefabGUID(unitGuid).GetPrefabName()}");
        }
    }

    [Command("phase", description: "Switch your active transformation's spell bar between boss-phase loadouts. Usage: .beelz phase [n]. No arg = show current + available phases.")]
    public static void Phase(ChatCommandContext ctx, int n = -1)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active == null) { ctx.Reply("You are not currently transformed. Use .beelz transform <unit> first."); return; }

        var pg = new PrefabGUID(active.UnitPrefabGuid);
        var available = Core.Transforms.GetAvailablePhases(pg);

        if (n <= 0)
        {
            // Show-only mode.
            ctx.Reply($"Currently in phase {active.CurrentPhase} of {Core.AbilityMetadata.ResolveUnitName(pg._Value)}. Available phases: {string.Join(", ", available)}. " +
                      (available.Count > 1 ? "Switch via .beelz phase <n>." : "(This unit doesn't have multi-phase mechanics curated.)"));
            return;
        }

        if (!available.Contains(n))
        {
            ctx.Reply($"Phase {n} isn't defined for {Core.AbilityMetadata.ResolveUnitName(pg._Value)}. Available: {string.Join(", ", available)}.");
            return;
        }
        if (n == active.CurrentPhase)
        {
            ctx.Reply($"Already in phase {n}.");
            return;
        }

        // Validate the phase actually has abilities before swapping (nicer message).
        var abilities = Core.Transforms.GetTransformAbilities(pg, n);
        if (abilities.Count == 0)
        {
            ctx.Reply($"Phase {n} has no eligible abilities for {Core.AbilityMetadata.ResolveUnitName(pg._Value)}. Aborting.");
            return;
        }

        // v0.27.0: shared swap path (also used by the Auto-mode HP monitor).
        Entity character = ctx.Event.SenderCharacterEntity;
        bool appliedNow = Core.Transforms.ApplyPhase(steamId, active, character, n);

        string applyHint = appliedNow ? "Spell bar swapped." : "Spell bar will swap this frame.";
        ctx.Reply($"Phase {n} active: {abilities.Count} abilities now on the bar. {applyHint}");
    }

    [Command("revert", description: "Revert your current transformation. Spell bar restored immediately.")]
    public static void Revert(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active is null) { ctx.Reply("You are not currently transformed."); return; }

        string unitName = new PrefabGUID(active.UnitPrefabGuid).GetPrefabName();
        var (_, appliedNow) = Core.Transforms.Revert(steamId, "manual");
        // v0.35.0: transform-ended event now emitted centrally by Core.Transforms.Revert.
        ctx.Reply($"Reverted from {unitName}. Spell bar restored.");
    }

    [Command("refresh", description: "Re-apply your current ability bar — fixes a blank or wrong bar after reverting, leaving a travel/wolf/bat form, or a weapon quirk. Usage: .beelz refresh")]
    public static void Refresh(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var character = ctx.Event.SenderCharacterEntity;

        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active != null)
        {
            string unitName = Core.AbilityMetadata?.ResolveUnitName(active.UnitPrefabGuid)
                              ?? new PrefabGUID(active.UnitPrefabGuid).GetPrefabName();
            bool ok = Core.Transforms.ReapplyActiveTransform(steamId, active, character);
            ctx.Reply(ok
                ? $"Re-applied your {unitName} transformation bar."
                : "Couldn't re-apply the transformation bar (see server log).");
        }
        else
        {
            int n = Beelzebub.Services.SlotApply.RestoreResolvedGrants(character);
            ctx.Reply(n > 0
                ? $"Restored {n} bound abilit{(n == 1 ? "y" : "ies")} to your spell bar."
                : "Nothing to restore — assign abilities with .beelz grant / .beelz weapon-grant first.");
        }
    }

    // #3 (v0.36.0): per-player cooldown tracker for manual detonation.
    static readonly Dictionary<ulong, DateTime> _lastDetonate = new();

    [Command("detonate", description: "Manually fire your transformed unit's signature detonation AoE (e.g. the Undead Priest's Nova), if it has one. Usage: .beelz detonate")]
    public static void Detonate(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active is null) { ctx.Reply("You must be transformed to detonate."); return; }

        if (!Beelzebub.Patches.BuffSpawnServerPatch.HasManualDetonation(active.UnitPrefabGuid))
        {
            ctx.Reply($"{new PrefabGUID(active.UnitPrefabGuid).GetPrefabName()} has no manual detonation to trigger.");
            return;
        }

        // Anti-spam cooldown (config; 0 = none).
        float cd = Beelzebub.Config.Settings.Transform_ManualDetonateCooldownSeconds.Value;
        if (cd > 0f && _lastDetonate.TryGetValue(steamId, out var last))
        {
            double remaining = cd - (DateTime.UtcNow - last).TotalSeconds;
            if (remaining > 0)
            {
                ctx.Reply($"Detonation on cooldown ({remaining:F0}s).");
                return;
            }
        }

        string name = Beelzebub.Patches.BuffSpawnServerPatch.TrySpawnManualDetonation(ctx.Event.SenderCharacterEntity, active.UnitPrefabGuid);
        if (name is null) { ctx.Reply("Detonation failed (see server log)."); return; }

        _lastDetonate[steamId] = DateTime.UtcNow;
        ctx.Reply("Detonation unleashed!");
        Core.Chat.SendEvent(ctx.Event.SenderCharacterEntity,
            $"[BEELZ:event] type=detonate u={active.UnitPrefabGuid}");
    }

    [Command("active", description: "v0.24.1: show your current spell bar with full ability info per slot (name, school, cooldown, description). Useful for transforms since V Rising's action-bar tooltip can't render NPC ability text. Usage: .beelz active")]
    public static void Active(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        Entity character = ctx.Event.SenderCharacterEntity;
        ulong steamId = character.GetSteamId();
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);

        if (active is null)
        {
            // Not transformed → show universal slot bindings.
            var slots = Core.AbilityRegistry.GetSlots(steamId);
            if (slots.Count == 0)
            {
                ctx.Reply("You are not transformed and have no slot bindings. Use .beelz transform or .beelz grant first.");
                return;
            }
            ctx.Reply("--- Your universal spell bar ---");
            foreach (var (slot, abilityGuid) in slots.OrderBy(kv => kv.Key))
            {
                EmitSlotLine(ctx, slot - 1, abilityGuid);
            }
            ctx.Reply("Use .beelz info <name> for full details.");
            return;
        }

        // Transformed → list the transform's ability loadout.
        var pgUnit = new PrefabGUID(active.UnitPrefabGuid);
        var abilityList = Core.Transforms.GetTransformAbilities(pgUnit, active.CurrentPhase);
        if (abilityList.Count == 0)
        {
            ctx.Reply($"You are transformed as {Core.AbilityMetadata.ResolveUnitName(pgUnit._Value)} but no abilities are loaded for this phase ({active.CurrentPhase}).");
            return;
        }

        ctx.Reply($"--- {Core.AbilityMetadata.ResolveUnitName(pgUnit._Value)} spell bar (phase {active.CurrentPhase}) ---");
        for (int i = 0; i < abilityList.Count; i++)
        {
            if (abilityList[i] == 0) continue;
            EmitSlotLine(ctx, i, abilityList[i]);
        }
        ctx.Reply("Use .beelz info <name> for full details.");
    }

    /// <summary>Format one slot/ability pair as a chat line.</summary>
    static void EmitSlotLine(ChatCommandContext ctx, int slotIndex, int abilityGroupGuid)
    {
        // v0.24.5: actual V Rising keybind layout (1-indexed; idx 0 = slot 1 = Q).
        string[] labels = { "Primary (Q)", "Travel (Space)", "Shift", "Heavy (E)", "Spell 1 (R)", "Spell 2 (C)" };
        string slotName = slotIndex < labels.Length ? labels[slotIndex] : $"Slot {slotIndex + 1}";

        var info = Core.AbilityMetadata.Resolve(abilityGroupGuid);
        string title = !string.IsNullOrEmpty(info.Name) ? info.Name : new PrefabGUID(abilityGroupGuid).GetPrefabName();
        string school = !string.IsNullOrEmpty(info.School) ? $" [{info.School}]" : "";
        string cd = info.CooldownSeconds.HasValue ? $" · cd {info.CooldownSeconds.Value:F1}s" : "";
        string warning = info.Incompatible ? " ⚠" : "";
        ctx.Reply($"  {slotName}: {title}{school}{cd}{warning}");
    }

    [Command("tp", description: "v0.23.12: shortcut to stash summons before using a waygate or bat-form. Equivalent to `.beelz summons stash`. Summons auto-restore at your destination via PlayerTeleportSystem.")]
    public static void Tp(ChatCommandContext ctx)
    {
        Summons(ctx, "stash");
    }

    [Command("summons", description: "v0.23.8: manage summons. Usage: .beelz summons [stash|restore|off|on|status]. stash = disable + stow (lets you use waygates); restore = re-enable at your position; off/on = stash/restore aliases; status (default) = show counts.")]
    public static void Summons(ChatCommandContext ctx, string action = "status")
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        Entity character = ctx.Event.SenderCharacterEntity;
        ulong steamId = character.GetSteamId();
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active is null) { ctx.Reply("You are not currently transformed."); return; }

        string normalized = (action ?? "status").Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "stash":
            case "off":
            {
                // v0.23.8: stash uses Disabled component (Bloodcraft pattern) — entities
                // stay alive but are pulled out of the world, so V Rising's waygate
                // "subdued enemy" pre-check doesn't see them. v0.23.9 also nullifies
                // Follower.Followed (the actual subdued-check signal) — Disabled alone
                // wasn't enough. Use `.beelz summons restore` (or `on`) to bring them
                // back at your current position.
                int stashed = SummonAllyService.StashAll(active, character);
                int previouslyStashed = active.StashedSummons.Count - stashed;
                if (stashed > 0)
                {
                    ctx.Reply($"Stashed {stashed} summon(s). Total in storage: {active.StashedSummons.Count}. You can now use waygates / bat-form. Use .beelz summons restore to bring them back.");
                }
                else if (previouslyStashed > 0)
                {
                    ctx.Reply($"No live summons to stash. {previouslyStashed} already in storage. Use .beelz summons restore to bring them back.");
                }
                else
                {
                    ctx.Reply("No summons to stash.");
                }
                active.SummonsDisabled = true;
                return;
            }
            case "restore":
            case "on":
            {
                int restored = SummonAllyService.RestoreAll(active, character);
                if (restored > 0)
                {
                    ctx.Reply($"Restored {restored} summon(s) at your position. They should resume combat AI on next attack.");
                }
                else
                {
                    ctx.Reply("No summons in storage to restore.");
                }
                active.SummonsDisabled = false;
                return;
            }
            case "status":
            default:
            {
                int totalLive = 0;
                int totalStashed = 0;
                int cap = Beelzebub.Config.Settings.Transform_MaxStacksPerSummonAbility.Value;
                if (active.SummonedMinions != null)
                {
                    foreach (var e in active.SummonedMinions) if (e.Exists()) totalLive++;
                }
                if (active.StashedSummons != null)
                {
                    foreach (var e in active.StashedSummons) if (e.Exists()) totalStashed++;
                }
                string stateLabel = active.SummonsDisabled ? "stashed (use .beelz summons restore to bring back)" : "active";
                ctx.Reply($"[SUMMONS] {stateLabel} — {totalLive} live, {totalStashed} in storage. Cap-per-ability = {(cap > 0 ? cap + " uses" : "no cap")}");
                if (active.SummonStacks != null && active.SummonStacks.Count > 0)
                {
                    foreach (var kv in active.SummonStacks)
                    {
                        int liveGroups = 0;
                        int liveEntities = 0;
                        foreach (var group in kv.Value)
                        {
                            bool anyAlive = false;
                            foreach (var e in group)
                            {
                                if (e.Exists()) { anyAlive = true; liveEntities++; }
                            }
                            if (anyAlive) liveGroups++;
                        }
                        if (liveGroups == 0) continue;
                        string abName = new PrefabGUID(kv.Key).GetPrefabName();
                        ctx.Reply($"  {abName}: {liveGroups}/{(cap > 0 ? cap.ToString() : "∞")} uses ({liveEntities} entities alive)");
                    }
                }
                return;
            }
        }
    }

    [Command("current", description: "Show what's effectively on your spell bar right now (transform-aware). Helps debug 'why isn't this ability on my bar?' questions.")]
    public static void Current(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Entity character = ctx.Event.SenderCharacterEntity;
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);

        // v0.19.0: emit one chat reply per slot rather than concatenating, to
        // stay under VCF's FixedString512Bytes (~510-byte) per-reply cap.
        if (active != null)
        {
            var pg = new PrefabGUID(active.UnitPrefabGuid);
            var abilities = Core.Transforms.GetTransformAbilities(pg, active.CurrentPhase);
            ctx.Reply($"[TRANSFORM] {Core.AbilityMetadata.ResolveUnitName(pg._Value)} phase={active.CurrentPhase} — {abilities.Count} ability slot(s):");
            for (int i = 0; i < abilities.Count; i++)
            {
                string abName = new PrefabGUID(abilities[i]).GetPrefabName();
                var cat = Categorization.ClassifyAbility(abName);
                ctx.Reply($"  slot {i + 1}: {abName}  [{cat}]");
            }
            if (abilities.Count < 6)
            {
                ctx.Reply($"  slots {abilities.Count + 1}..6: (unused — unit has no more eligible abilities)");
            }
            return;
        }

        var weapon = SlotApply.GetCurrentWeapon(character);
        ctx.Reply($"[NORMAL] wielding {weapon} — saved Beelzebub grants on your bar:");
        if (weapon == WeaponFamily.None)
        {
            ctx.Reply("  (no equip-buff detected — unable to resolve slot map)");
            return;
        }
        var slots = Core.AbilityRegistry.GetSlotsResolved(steamId, weapon);
        if (slots.Count == 0)
        {
            ctx.Reply("  (no grants — your bar shows V Rising's default abilities for this weapon)");
            return;
        }
        foreach (var (slot, abilityGuid) in slots.OrderBy(kv => kv.Key))
        {
            string abName = new PrefabGUID(abilityGuid).GetPrefabName();
            var cat = Categorization.ClassifyAbility(abName);
            bool compatible = SlotApply.IsGrantCompatible(abilityGuid, weapon);
            string suffix = compatible ? "" : $" (INACTIVE — not compatible with {weapon})";
            ctx.Reply($"  slot {slot}: {abName}  [{cat}]{suffix}");
        }
    }

    [Command("catalog", description: "Hunt list — every curated transformation V-Blood with ✓ for ones you've unlocked, · for ones still to hunt. Usage: .beelz catalog [page]. Sorted by tier ascending. 10 per page.")]
    public static void Catalog(ChatCommandContext ctx, int page = 0)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();

        var transformMap = Core.AbilityRules?.Current?.TransformMap;
        if (transformMap == null || transformMap.Count == 0)
        {
            ctx.Reply("No transformation catalog curated yet. Ask the admin to install ability_rules.json.");
            return;
        }

        var unlockedGuids = new System.Collections.Generic.HashSet<int>(
            Core.AbilityRegistry.ListTransforms(steamId).Select(t => t.UnitPrefabGuid));

        // AUDIT-5 (v0.20.1): paginate 10 per page so each reply line fits comfortably
        // under VCF's 510-byte cap even with long unit names + tier + difficulty markers.
        const int pageSize = 10;
        // Sort: tier ascending (early-game first), then by display name for stability.
        var ordered = transformMap
            .OrderBy(kv => kv.Value.Tier)
            .ThenBy(kv => kv.Key, System.StringComparer.OrdinalIgnoreCase)
            .ToList();
        int total = ordered.Count;
        int pages = (total + pageSize - 1) / pageSize;
        if (page < 0) page = 0;
        if (page >= pages) page = pages - 1;

        int unlockedTotal = 0;
        foreach (var (k, _) in ordered)
        {
            if (!Core.PrefabNames.TryGetValue(0, out _)) { /* warmup */ }
            // We need the GUID for the unit; resolve from the name via the prefab map.
            if (TryResolveUnitGuid(k, out int g) && unlockedGuids.Contains(g)) unlockedTotal++;
        }

        ctx.Reply($"[CATALOG] Transformations: {unlockedTotal}/{total} unlocked. Page {page + 1}/{pages}.");
        foreach (var (name, entry) in ordered.Skip(page * pageSize).Take(pageSize))
        {
            bool unlocked = TryResolveUnitGuid(name, out int unitGuid) && unlockedGuids.Contains(unitGuid);
            string marker = unlocked ? "[X]" : "[ ]";
            string difficulty = entry.Difficulty;
            int tier = entry.Tier;
            // Strip CHAR_ prefix + _VBlood suffix for readability; lean on Humanize().
            string display = name.Humanize();
            ctx.Reply($"  {marker} T{tier} {difficulty,-6} {display}");
        }
        if (pages > 1) ctx.Reply($"More? .beelz catalog {(page + 1 < pages ? page + 2 : 1)}");
    }

    /// <summary>
    /// Resolve a CHAR_ prefab name to its GUID using Core.PrefabNames (the
    /// embedded + runtime-merged map). Case-insensitive. Returns false if not found.
    /// </summary>
    static bool TryResolveUnitGuid(string charName, out int guid)
    {
        guid = 0;
        if (string.IsNullOrEmpty(charName)) return false;
        foreach (var (g, n) in Core.PrefabNames)
        {
            if (string.Equals(n, charName, System.StringComparison.OrdinalIgnoreCase))
            {
                guid = g;
                return true;
            }
        }
        return false;
    }

    [Command("preview", description: "Show what abilities you'd get if you transformed into <unit>. Usage: .beelz preview <index|name>. Helps decide which V-Blood to transform into without committing.")]
    public static void Preview(ChatCommandContext ctx, string unitOrIndex)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var unlocks = Core.AbilityRegistry.ListTransforms(steamId);
        if (unlocks.Count == 0) { ctx.Reply("You have no transform unlocks yet."); return; }

        int unitGuid;
        if (int.TryParse(unitOrIndex, out int idx) && idx >= 0 && idx < unlocks.Count)
        {
            unitGuid = unlocks[idx].UnitPrefabGuid;
        }
        else
        {
            var match = unlocks.FirstOrDefault(u =>
                new PrefabGUID(u.UnitPrefabGuid).GetPrefabName().Contains(unitOrIndex, System.StringComparison.OrdinalIgnoreCase));
            if (match.UnitPrefabGuid == 0)
            {
                ctx.Reply($"No unlocked transform matches '{unitOrIndex}'. Use .beelz transforms for the list.");
                return;
            }
            unitGuid = match.UnitPrefabGuid;
        }

        var pg = new PrefabGUID(unitGuid);
        var phases = Core.Transforms.GetAvailablePhases(pg);

        // v0.19.0: chunked per-slot reply (was concatenated → hit VCF's
        // FixedString512Bytes limit and threw on large units like StoneBreaker).
        ctx.Reply($"[PREVIEW] {Core.AbilityMetadata.ResolveUnitName(pg._Value)}");
        foreach (int phase in phases)
        {
            var abilities = Core.Transforms.GetTransformAbilities(pg, phase);
            ctx.Reply($"  Phase {phase} — {abilities.Count} ability slot(s):");
            for (int i = 0; i < abilities.Count; i++)
            {
                string abName = new PrefabGUID(abilities[i]).GetPrefabName();
                var cat = Categorization.ClassifyAbility(abName);
                ctx.Reply($"    slot {i + 1}: {abName}  [{cat}]");
            }
        }
        ctx.Reply($"Difficulty={Core.AbilityRules.GetTransformDifficulty(unitGuid)} " +
                  $"Tier={Core.AbilityRules.GetTransformTier(unitGuid)} " +
                  $"Mode={Core.AbilityRules.GetTransformPowerScalingMode(unitGuid)} " +
                  $"Server={AbilityRules.GetServerDifficulty()}");
        string notes = Core.AbilityRules.GetTransformNotes(unitGuid);
        if (!string.IsNullOrEmpty(notes)) ctx.Reply($"Notes: {notes}");
        ctx.Reply("Note: abilities filtered by deny patterns (e.g. _Summon_, _Hard_-on-Basic) won't appear.");
    }
}
