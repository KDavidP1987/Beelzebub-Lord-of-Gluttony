using System;
using System.Linq;
using System.Text;
using Beelzebub.Services;
using Stunlock.Core;
using Unity.Entities;
using VampireCommandFramework;

namespace Beelzebub.Commands;

[CommandGroup("beelz admin")]
internal static class AdminCommands
{
    /// <summary>
    /// Audit-log line for every admin grant/revoke/force action. Goes to BepInEx\LogOutput.log.
    /// Format is grep-friendly: "[Beelz AUDIT] admin=<sid> action=<name> target=<sid> ..."
    /// </summary>
    static void Audit(ChatCommandContext ctx, string action, ulong targetSteamId, string targetName, string detail)
    {
        ulong adminSteamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Core.Log.LogInfo($"[Beelz AUDIT] admin={adminSteamId} action={action} target={targetSteamId} ({targetName}) {detail}");
    }

    /// <summary>v0.38.0: friendly in-game unit name for admin replies (falls back to the prefab name).</summary>
    static string UnitDisplay(int guid) => Core.AbilityMetadata?.ResolveUnitName(guid) ?? new Stunlock.Core.PrefabGUID(guid).GetPrefabName();

    /// <summary>v0.38.0: friendly ability name for admin replies (falls back to the prefab name).</summary>
    static string AbilityDisplay(int guid)
    {
        var i = Core.AbilityMetadata?.Resolve(guid);
        return (i != null && !string.IsNullOrEmpty(i.Name)) ? i.Name : new Stunlock.Core.PrefabGUID(guid).GetPrefabName();
    }

    [Command("rules", description: "Show the currently loaded ability filter rules.", adminOnly: true)]
    public static void Rules(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var r = Core.AbilityRules.Current;
        var sb = new StringBuilder();
        sb.Append("Ability rules (v").Append(r.Version).Append("):").AppendLine();
        sb.Append("  Deny patterns: ").AppendLine(r.DenyPatterns.Count == 0 ? "(none)" : string.Join(", ", r.DenyPatterns));
        sb.Append("  Allow patterns: ").AppendLine(r.AllowPatterns.Count == 0 ? "(none — all allowed unless denied)" : string.Join(", ", r.AllowPatterns));
        sb.Append("  Deny GUIDs: ").Append(r.DenyGuids.Count).Append(" entries").AppendLine();
        sb.Append("  Allow GUIDs: ").Append(r.AllowGuids.Count).Append(" entries").AppendLine();
        sb.Append("File: ").Append(Core.AbilityRules.RulesFilePath);
        ctx.Reply(sb.ToString());
    }

    [Command("deny", description: "Add a substring pattern to the deny list. Usage: .beelz admin deny <pattern>", adminOnly: true)]
    public static void Deny(ChatCommandContext ctx, string pattern)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (Core.AbilityRules.AddDenyPattern(pattern))
            ctx.Reply($"Added deny pattern '{pattern}'. Existing captures retain (not retroactive); future captures are filtered.");
        else
            ctx.Reply($"Pattern '{pattern}' already on the deny list (or empty).");
    }

    [Command("undeny", description: "Remove a substring pattern from the deny list. Usage: .beelz admin undeny <pattern>", adminOnly: true)]
    public static void Undeny(ChatCommandContext ctx, string pattern)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ctx.Reply(Core.AbilityRules.RemoveDenyPattern(pattern)
            ? $"Removed '{pattern}' from deny list."
            : $"'{pattern}' not found on deny list.");
    }

    [Command("allow", description: "Add a substring pattern to the allow list (when non-empty, only matching abilities are captured). Usage: .beelz admin allow <pattern>", adminOnly: true)]
    public static void Allow(ChatCommandContext ctx, string pattern)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (Core.AbilityRules.AddAllowPattern(pattern))
            ctx.Reply($"Added allow pattern '{pattern}'. When the allow list is non-empty, ONLY abilities whose names contain one of these patterns are captured.");
        else
            ctx.Reply($"Pattern '{pattern}' already on the allow list (or empty).");
    }

    [Command("unallow", description: "Remove a substring pattern from the allow list. Usage: .beelz admin unallow <pattern>", adminOnly: true)]
    public static void Unallow(ChatCommandContext ctx, string pattern)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ctx.Reply(Core.AbilityRules.RemoveAllowPattern(pattern)
            ? $"Removed '{pattern}' from allow list."
            : $"'{pattern}' not found on allow list.");
    }

    [Command("reload", description: "Re-read ability rules JSON from disk (for hand-edited changes).", adminOnly: true)]
    public static void Reload(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        Core.AbilityRules.Load();
        ctx.Reply($"Rules reloaded from {Core.AbilityRules.RulesFilePath}.");
    }

    [Command("transform mode", description: "Set transform mode. Usage: .beelz admin transform mode <regular|vblood> <toggle|timed|disabled>", adminOnly: true)]
    public static void TransformMode(ChatCommandContext ctx, string source, string mode)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var s = source.Trim().ToLowerInvariant();
        var m = mode.Trim().ToLowerInvariant();
        if (m != "toggle" && m != "timed" && m != "disabled")
        {
            ctx.Reply("Mode must be one of: toggle, timed, disabled.");
            return;
        }
        var canonical = m == "toggle" ? "Toggle" : m == "timed" ? "Timed" : "Disabled";
        if (s == "regular") { Beelzebub.Config.Settings.Transform_Mode_Regular.Value = canonical; ctx.Reply($"Regular transform mode set to {canonical}."); }
        else if (s == "vblood") { Beelzebub.Config.Settings.Transform_Mode_VBlood.Value = canonical; ctx.Reply($"V-Blood transform mode set to {canonical}."); }
        else ctx.Reply("Source must be 'regular' or 'vblood'.");
    }

    [Command("transform duration", description: "Set Timed-mode duration. Usage: .beelz admin transform duration <regular|vblood> <seconds>", adminOnly: true)]
    public static void TransformDuration(ChatCommandContext ctx, string source, float seconds)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (seconds < 0) { ctx.Reply("Duration must be non-negative."); return; }
        var s = source.Trim().ToLowerInvariant();
        if (s == "regular") { Beelzebub.Config.Settings.Transform_DurationSeconds_Regular.Value = seconds; ctx.Reply($"Regular transform duration: {seconds}s."); }
        else if (s == "vblood") { Beelzebub.Config.Settings.Transform_DurationSeconds_VBlood.Value = seconds; ctx.Reply($"V-Blood transform duration: {seconds}s."); }
        else ctx.Reply("Source must be 'regular' or 'vblood'.");
    }

    [Command("transform cooldown", description: "Set post-revert cooldown. Usage: .beelz admin transform cooldown <regular|vblood> <seconds>", adminOnly: true)]
    public static void TransformCooldown(ChatCommandContext ctx, string source, float seconds)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (seconds < 0) { ctx.Reply("Cooldown must be non-negative."); return; }
        var s = source.Trim().ToLowerInvariant();
        if (s == "regular") { Beelzebub.Config.Settings.Transform_CooldownSeconds_Regular.Value = seconds; ctx.Reply($"Regular transform cooldown: {seconds}s."); }
        else if (s == "vblood") { Beelzebub.Config.Settings.Transform_CooldownSeconds_VBlood.Value = seconds; ctx.Reply($"V-Blood transform cooldown: {seconds}s."); }
        else ctx.Reply("Source must be 'regular' or 'vblood'.");
    }

    [Command("give", description: "Grant a captured ability to a player. Usage: .beelz admin give <player> <unitGuid> <abilityGuid>", adminOnly: true)]
    public static void Give(ChatCommandContext ctx, string player, int unitGuid, int abilityGuid)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Unity.Entities.Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        var source = new Stunlock.Core.PrefabGUID(unitGuid).IsVBloodUnit()
            ? Beelzebub.Services.CaptureSource.VBlood
            : Beelzebub.Services.CaptureSource.Regular;
        bool added = Core.AbilityRegistry.Add(steamId, unitGuid, abilityGuid, source);
        Core.Persistence.RequestSave();

        string abilityName = AbilityDisplay(abilityGuid);
        string unitName = UnitDisplay(unitGuid);
        ctx.Reply(added
            ? $"Granted {abilityName} (from {unitName}, source={source}) to {fullName}."
            : $"{fullName} already has that capture — no change.");
    }

    [Command("give-transform", description: "Grant a transform unlock to a player. Usage: .beelz admin give-transform <player> <unitGuid>", adminOnly: true)]
    public static void GiveTransform(ChatCommandContext ctx, string player, int unitGuid)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Unity.Entities.Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        var source = new Stunlock.Core.PrefabGUID(unitGuid).IsVBloodUnit()
            ? Beelzebub.Services.CaptureSource.VBlood
            : Beelzebub.Services.CaptureSource.Regular;
        bool added = Core.AbilityRegistry.AddTransformUnlock(steamId, unitGuid, source);
        Core.Persistence.RequestSave();

        string unitName = UnitDisplay(unitGuid);
        ctx.Reply(added
            ? $"Granted transform unlock for {unitName} (source={source}) to {fullName}."
            : $"{fullName} already has that transform unlock — no change.");
    }

    [Command("difficulty", description: "Set or show the server's difficulty mode for TX4 gating. Usage: .beelz admin difficulty [basic|brutal]", adminOnly: true)]
    public static void Difficulty(ChatCommandContext ctx, string mode = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (string.IsNullOrWhiteSpace(mode))
        {
            string current = AbilityRules.GetServerDifficulty();
            ctx.Reply($"Server difficulty mode: {current}. Brutal-tagged abilities + transforms are " +
                      (current == "Brutal" ? "AVAILABLE." : "BLOCKED."));
            return;
        }
        var canonical = mode.Trim();
        if (string.Equals(canonical, "Brutal", StringComparison.OrdinalIgnoreCase)) canonical = "Brutal";
        else if (string.Equals(canonical, "Basic", StringComparison.OrdinalIgnoreCase)) canonical = "Basic";
        else { ctx.Reply("Mode must be Basic or Brutal."); return; }

        Beelzebub.Config.Settings.Server_DifficultyMode.Value = canonical;
        ctx.Reply($"Server difficulty mode set to {canonical}. Future captures + transform unlocks gate accordingly.");
    }

    [Command("transform show", description: "Show current transform settings.", adminOnly: true)]
    public static void TransformShow(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var s = new System.Text.StringBuilder();
        s.AppendLine("Transform settings:");
        s.Append("  Regular: mode=").Append(Beelzebub.Config.Settings.Transform_Mode_Regular.Value)
            .Append(", duration=").Append(Beelzebub.Config.Settings.Transform_DurationSeconds_Regular.Value).Append("s")
            .Append(", cooldown=").Append(Beelzebub.Config.Settings.Transform_CooldownSeconds_Regular.Value).Append("s").AppendLine();
        s.Append("  V-Blood: mode=").Append(Beelzebub.Config.Settings.Transform_Mode_VBlood.Value)
            .Append(", duration=").Append(Beelzebub.Config.Settings.Transform_DurationSeconds_VBlood.Value).Append("s")
            .Append(", cooldown=").Append(Beelzebub.Config.Settings.Transform_CooldownSeconds_VBlood.Value).Append("s");
        ctx.Reply(s.ToString());
    }

    // --- AT1: inspect another player's full state ---

    [Command("inspect", description: "View another player's Beelzebub state. Usage: .beelz admin inspect <player>", adminOnly: true)]
    public static void Inspect(ChatCommandContext ctx, string player)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        var sb = new StringBuilder();
        sb.Append("=== ").Append(fullName).Append(" (SteamID ").Append(steamId).Append(") ===").AppendLine();

        var captured = Core.AbilityRegistry.ListFor(steamId);
        int vbloodCount = captured.Count(c => c.Source == CaptureSource.VBlood);
        sb.Append("Captures: ").Append(captured.Count).Append(" total")
            .Append(" (").Append(vbloodCount).Append(" V-Blood, ").Append(captured.Count - vbloodCount).Append(" regular)").AppendLine();

        var universal = Core.AbilityRegistry.GetSlots(steamId);
        sb.Append("Universal slots bound: ").Append(universal.Count).AppendLine();
        var weaponSlots = Core.AbilityRegistry.AllWeaponSlots(steamId);
        if (weaponSlots.Count > 0)
        {
            foreach (var (weapon, slots) in weaponSlots.OrderBy(kv => kv.Key.ToString()))
            {
                sb.Append("  ").Append(weapon).Append(" slots: ").Append(slots.Count).AppendLine();
            }
        }

        var transforms = Core.AbilityRegistry.ListTransforms(steamId);
        int vbloodTxCount = transforms.Count(t => t.Source == CaptureSource.VBlood);
        sb.Append("Transform unlocks: ").Append(transforms.Count)
            .Append(" (").Append(vbloodTxCount).Append(" V-Blood, ").Append(transforms.Count - vbloodTxCount).Append(" regular)").AppendLine();

        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active != null)
        {
            sb.Append("  ACTIVE: ").AppendLine(UnitDisplay(active.UnitPrefabGuid));
        }

        int hotkeyCount = Core.AbilityRegistry.HotkeyCount(steamId);
        if (hotkeyCount > 0)
        {
            sb.Append("Hotkeys bound: ").Append(hotkeyCount).AppendLine();
        }

        var verbosity = Core.AbilityRegistry.GetVerbosity(steamId, Verbosity.Summary);
        bool bchOn = Core.AbilityRegistry.GetEmitApiEvents(steamId);
        sb.Append("Verbosity: ").Append(verbosity).Append(", BCH events: ").Append(bchOn ? "on" : "off");

        ctx.Reply(sb.ToString());
        Audit(ctx, "inspect", steamId, fullName, "");
    }

    // --- AT2: revoke an ability ---

    [Command("revoke", description: "Remove a captured ability from a player. Usage: .beelz admin revoke <player> <unitGuid> <abilityGuid> [reason]", adminOnly: true)]
    public static void Revoke(ChatCommandContext ctx, string player, int unitGuid, int abilityGuid, string reason = "admin")
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        bool removed = Core.AbilityRegistry.Forget(steamId, unitGuid, abilityGuid);
        Core.Persistence.RequestSave();

        string abilityName = AbilityDisplay(abilityGuid);
        string unitName = UnitDisplay(unitGuid);
        if (removed)
        {
            ctx.Reply($"Revoked {abilityName} (from {unitName}) from {fullName}. Reason: {reason}.");
            Audit(ctx, "revoke-ability", steamId, fullName, $"ability={abilityGuid} ({abilityName}) unit={unitGuid} ({unitName}) reason={reason}");
        }
        else
        {
            ctx.Reply($"{fullName} doesn't have that capture — no change.");
        }
    }

    // --- AT3: revoke a transform unlock ---

    [Command("revoke-transform", description: "Remove a transform unlock from a player. Usage: .beelz admin revoke-transform <player> <unitGuid> [reason]", adminOnly: true)]
    public static void RevokeTransform(ChatCommandContext ctx, string player, int unitGuid, string reason = "admin")
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        // If they're currently transformed into this unit, revert first.
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active != null && active.UnitPrefabGuid == unitGuid)
        {
            Core.Transforms.Revert(steamId, "admin revoke");
        }

        bool removed = Core.AbilityRegistry.ForgetTransform(steamId, unitGuid);
        Core.Persistence.RequestSave();

        string unitName = UnitDisplay(unitGuid);
        if (removed)
        {
            ctx.Reply($"Revoked transform unlock for {unitName} from {fullName}. Reason: {reason}.");
            Audit(ctx, "revoke-transform", steamId, fullName, $"unit={unitGuid} ({unitName}) reason={reason}");
        }
        else
        {
            ctx.Reply($"{fullName} doesn't have that transform unlock — no change.");
        }
    }

    // --- AT4: force / clear transform on another player ---

    [Command("force-transform", description: "Force a transformation on another player (bypasses unlock + cooldown). Usage: .beelz admin force-transform <player> <unitGuid>", adminOnly: true)]
    public static void ForceTransform(ChatCommandContext ctx, string player, int unitGuid)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        // Ensure unlock exists and clear cooldown so TryActivate sails through.
        var source = new PrefabGUID(unitGuid).IsVBloodUnit() ? CaptureSource.VBlood : CaptureSource.Regular;
        Core.AbilityRegistry.AddTransformUnlock(steamId, unitGuid, source);
        Core.AbilityRegistry.SetCooldownUntil(steamId, source, DateTime.MinValue);

        var (ok, message) = Core.Transforms.TryActivate(steamId, unitGuid);
        Core.Persistence.RequestSave();

        string unitName = UnitDisplay(unitGuid);
        if (ok)
        {
            ctx.Reply($"Forced {fullName} into {unitName}. ({message})");
            Audit(ctx, "force-transform", steamId, fullName, $"unit={unitGuid} ({unitName})");
        }
        else
        {
            ctx.Reply($"Force-transform failed: {message}");
        }
    }

    [Command("clear-transform", description: "End another player's active transformation. Usage: .beelz admin clear-transform <player>", adminOnly: true)]
    public static void ClearTransform(ChatCommandContext ctx, string player)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active == null) { ctx.Reply($"{fullName} is not currently transformed."); return; }

        var (reverted, _) = Core.Transforms.Revert(steamId, "admin clear");
        Core.Persistence.RequestSave();

        string unitName = UnitDisplay(active.UnitPrefabGuid);
        if (reverted)
        {
            ctx.Reply($"Cleared {fullName}'s transformation ({unitName}).");
            Audit(ctx, "clear-transform", steamId, fullName, $"unit={active.UnitPrefabGuid} ({unitName})");
        }
        else
        {
            ctx.Reply($"Could not clear {fullName}'s transformation.");
        }
    }

    [Command("desummon", description: "v0.23.2: clean up all Beelzebub-tagged ally summons following a specific player. Includes orphans from previous sessions. Usage: .beelz admin desummon <player>", adminOnly: true)]
    public static void Desummon(ChatCommandContext ctx, string player)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        int tracked = 0;
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active != null && active.SummonedMinions is { Count: > 0 })
        {
            foreach (var m in active.SummonedMinions)
            {
                if (m.Exists()) { SummonAllyService.EnqueueAdminDespawn(m); tracked++; }
            }
            active.SummonedMinions.Clear();
            active.SummonStacks?.Clear();
        }

        int orphans = SummonAllyService.SweepOrphans(character);
        // v0.23.5: immediate drain for admin cleanup. Tries DestroyUtility, then
        // DestroyEntity, then <Disabled> component as fallback. No staged budget —
        // admin commands run in safe state (chat input), no combat-frame pressure.
        int processed = SummonAllyService.DrainAdminQueueImmediate();
        ctx.Reply($"Queued {tracked} tracked + {orphans} orphan summon(s) for {fullName}. Processed {processed} via immediate drain (DestroyUtility / fallback).");
        Audit(ctx, "desummon", steamId, fullName, $"tracked={tracked} orphans={orphans} processed={processed}");
    }

    [Command("desummon-all", description: "v0.23.2: global sweep — destroy every Beelzebub-tagged ally summon on the map regardless of owner. Use to recover from stale state. Usage: .beelz admin desummon-all", adminOnly: true)]
    public static void DesummonAll(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        // Also clear all live tracked SummonedMinions across active transforms.
        int tracked = 0;
        foreach (var (_, active) in Core.AbilityRegistry.AllActiveTransforms())
        {
            if (active.SummonedMinions is { Count: > 0 })
            {
                foreach (var m in active.SummonedMinions)
                {
                    if (m.Exists()) { SummonAllyService.EnqueueAdminDespawn(m); tracked++; }
                }
                active.SummonedMinions.Clear();
                active.SummonStacks?.Clear();
            }
        }

        // Then sweep every BlockFeedBuff+Faction_Players+Follower entity in the world.
        int orphans = SummonAllyService.SweepOrphans(Entity.Null);
        // v0.23.5: immediate drain — multiple destroy paths with <Disabled> fallback.
        int processed = SummonAllyService.DrainAdminQueueImmediate();
        ctx.Reply($"Queued {tracked} tracked + {orphans} orphan ally summon(s) globally. Processed {processed} via immediate drain.");
        ulong adminSteamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Core.Log.LogInfo($"[Beelz AUDIT] admin={adminSteamId} action=desummon-all tracked={tracked} orphans={orphans} processed={processed}");
    }

    // --- R1.4: ability metadata discovery tool ---

    [Command("scan-abilities", description: "Dump per-ability metadata (shipped + ECS-derived + admin policy) to discovered_abilities.json for the admin to audit. Surfaces abilities lacking curated descriptions so they can be filled in. Usage: .beelz admin scan-abilities", adminOnly: true)]
    public static void ScanAbilities(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        // Collect every ability GUID we know about: from every player's captures,
        // from ability_rules.json, and from the global PrefabNames map (anything
        // starting with AB_ that's an AbilityGroup).
        var guids = new System.Collections.Generic.HashSet<int>();

        // Captured abilities across all players.
        foreach (var (_, list) in Core.AbilityRegistry.Snapshot())
        {
            foreach (var c in list) guids.Add(c.AbilityPrefabGuid);
        }

        // Every AB_*_AbilityGroup / AB_*_Group in the global prefab map.
        foreach (var (guid, name) in Core.PrefabNames)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (!name.StartsWith("AB_", StringComparison.OrdinalIgnoreCase)) continue;
            // Only group-level prefabs — casts/buffs would be too noisy.
            if (!name.EndsWith("_AbilityGroup", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith("_Group", StringComparison.OrdinalIgnoreCase)) continue;
            guids.Add(guid);
        }

        var report = new System.Collections.Generic.List<object>();
        int curated = 0, fallbackOnly = 0, incompatible = 0;
        foreach (int guid in guids.OrderBy(g => g))
        {
            var info = Core.AbilityMetadata.Resolve(guid);
            bool isCurated = info.HasShippedEntry || info.HasOverrideEntry;
            if (isCurated) curated++; else fallbackOnly++;
            if (info.Incompatible) incompatible++;

            report.Add(new
            {
                guid,
                prefabName = new PrefabGUID(guid).GetPrefabName(),
                info.Name,
                info.School,
                info.Type,
                info.Description,
                cooldown = info.CooldownSeconds,
                castTime = info.CastTimeSeconds,
                range = info.MaxRange,
                info.BehaviorType,
                info.Incompatible,
                info.IncompatibleReason,
                source = info.HasOverrideEntry ? "override" : info.HasShippedEntry ? "shipped" : "fallback",
            });
        }

        // Write to data folder alongside ability_rules.json.
        var dir = System.IO.Path.GetDirectoryName(Core.AbilityRules.RulesFilePath);
        var path = System.IO.Path.Combine(dir, "discovered_abilities.json");
        try
        {
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(report, opts));
            ctx.Reply($"Scanned {guids.Count} abilities: {curated} curated, {fallbackOnly} fallback-only, {incompatible} marked incompatible.");
            ctx.Reply($"Report: {path}");
            ulong adminSteamId = ctx.Event.SenderCharacterEntity.GetSteamId();
            Core.Log.LogInfo($"[Beelz AUDIT] admin={adminSteamId} action=scan-abilities total={guids.Count} curated={curated} fallback={fallbackOnly} incompatible={incompatible} path={path}");
        }
        catch (Exception ex)
        {
            ctx.Reply($"Failed to write discovered_abilities.json: {ex.Message}");
        }
    }

    // --- AT5: progress overview (admin can inspect any player) ---

    [Command("progress", description: "Show another player's collection progress. Usage: .beelz admin progress <player>", adminOnly: true)]
    public static void Progress(ChatCommandContext ctx, string player)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        BeelzCommands.ShowProgress(ctx, steamId, fullName + "'s");
    }

    // --- AUDIT-4 (v0.20.1): admin remote slot binding ---
    //
    // Bridges the gap identified in the v0.20.0 audit: admins could give-ability and
    // give-transform to a player, but couldn't actually put the ability on the
    // player's spell bar. For "fix my broken loadout" support tickets, the admin
    // previously had to walk the player through `.beelz grant` themselves.

    [Command("set-slot", description: "Bind a player's universal-bucket slot to an ability. Usage: .beelz admin set-slot <player> <slot 1-6> <abilityGuid>", adminOnly: true)]
    public static void SetSlot(ChatCommandContext ctx, string player, int slot, int abilityGuid)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (slot < 1 || slot > 6) { ctx.Reply("Slot must be 1-6."); return; }

        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        var ability = new PrefabGUID(abilityGuid);
        string abilityName = ability.GetPrefabName();
        // Admin can override the TransformOnly + Enabled guards — they're explicitly
        // forcing this, so we don't refuse the bind. Log it clearly for audit trail.
        bool transformOnly = Core.AbilityRules.IsTransformOnly(abilityName, abilityGuid);
        bool enabled = Core.AbilityRules.IsEnabled(abilityName, abilityGuid);

        Core.AbilityRegistry.SetSlot(steamId, slot, abilityGuid);
        Core.Persistence.RequestSave();

        // If the player is currently online + not transformed, try to apply live.
        bool appliedNow = false;
        if (character.Exists())
        {
            appliedNow = SlotApply.ApplyGrant(character, slot, ability);
        }

        string warnings = "";
        if (transformOnly) warnings += " [WARNING: ability is TransformOnly — fires only during transform]";
        if (!enabled) warnings += " [WARNING: ability has Enabled=false — admin override]";
        string applyHint = appliedNow ? "Applied to spell bar live." : "Will activate next time the player wields a compatible weapon.";

        ctx.Reply($"Bound slot {slot} on {fullName} to {abilityName}. {applyHint}{warnings}");
        Audit(ctx, "set-slot", steamId, fullName, $"slot={slot} ability={abilityGuid} ({abilityName}) appliedNow={appliedNow} transformOnly={transformOnly} enabled={enabled}");
    }

    [Command("set-weapon-slot", description: "Bind a player's weapon-specific slot to an ability. Usage: .beelz admin set-weapon-slot <player> <weapon> <slot 1-6> <abilityGuid>", adminOnly: true)]
    public static void SetWeaponSlot(ChatCommandContext ctx, string player, string weaponStr, int slot, int abilityGuid)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (slot < 1 || slot > 6) { ctx.Reply("Slot must be 1-6."); return; }

        if (!Enum.TryParse<WeaponFamily>(weaponStr, ignoreCase: true, out var weapon)
            || weapon == WeaponFamily.None
            || weapon == WeaponFamily.Magic)
        {
            ctx.Reply($"Unknown weapon family '{weaponStr}'. Valid: Sword, GreatSword, Axe, Mace, DualHammers, Spear, Daggers, Crossbow, Longbow, Pistols, Reaper, Whip, Claws, Pollaxe, Slashers, TwinBlades, Unarmed, FishingPole.");
            return;
        }

        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        var ability = new PrefabGUID(abilityGuid);
        string abilityName = ability.GetPrefabName();
        bool transformOnly = Core.AbilityRules.IsTransformOnly(abilityName, abilityGuid);
        bool enabled = Core.AbilityRules.IsEnabled(abilityName, abilityGuid);

        Core.AbilityRegistry.SetSlot(steamId, weapon, slot, abilityGuid);
        Core.Persistence.RequestSave();

        // Apply live only if player currently wields the matching weapon family.
        bool appliedNow = false;
        if (character.Exists() && SlotApply.GetCurrentWeapon(character) == weapon)
        {
            appliedNow = SlotApply.ApplyGrant(character, slot, ability);
        }

        string warnings = "";
        if (transformOnly) warnings += " [WARNING: ability is TransformOnly]";
        if (!enabled) warnings += " [WARNING: ability has Enabled=false — admin override]";
        string applyHint = appliedNow ? "Applied live." : $"Activates next time the player wields {weapon}.";

        ctx.Reply($"Bound {weapon} slot {slot} on {fullName} to {abilityName}. {applyHint}{warnings}");
        Audit(ctx, "set-weapon-slot", steamId, fullName, $"weapon={weapon} slot={slot} ability={abilityGuid} ({abilityName}) appliedNow={appliedNow}");
    }

    [Command("clear-slot", description: "Clear a player's universal-bucket slot binding. Usage: .beelz admin clear-slot <player> <slot 1-6>", adminOnly: true)]
    public static void ClearSlot(ChatCommandContext ctx, string player, int slot)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (slot < 1 || slot > 6) { ctx.Reply("Slot must be 1-6."); return; }

        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        Core.AbilityRegistry.ClearSlot(steamId, slot);
        Core.Persistence.RequestSave();

        bool clearedNow = false;
        if (character.Exists()) clearedNow = SlotApply.ClearGrant(character, slot);

        ctx.Reply($"Cleared {fullName}'s universal slot {slot}. {(clearedNow ? "Removed from spell bar live." : "Saved bind removed; spell bar refreshes on next weapon swap.")}");
        Audit(ctx, "clear-slot", steamId, fullName, $"slot={slot} clearedNow={clearedNow}");
    }

    // --- AUDIT-6 (v0.20.1): bulk admin operations ---
    //
    // For server-wide events / hot-patches / season resets: ops that need to
    // touch every player at once. Each is audit-logged. The destructive ones
    // (wipe-all) require an explicit confirmation token.

    [Command("revert-all", description: "Force every active transformation on the server to end immediately. Usage: .beelz admin revert-all", adminOnly: true)]
    public static void RevertAll(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        // Snapshot the steamIds before mutating — Core.Transforms.Revert removes
        // entries from _activeTransforms, which would otherwise invalidate the
        // enumeration mid-loop.
        var activeSteamIds = Core.AbilityRegistry.AllActiveTransforms()
            .Select(kv => kv.Key)
            .ToList();
        if (activeSteamIds.Count == 0)
        {
            ctx.Reply("No active transformations on the server.");
            return;
        }

        int reverted = 0;
        foreach (ulong sid in activeSteamIds)
        {
            try
            {
                var (ok, _) = Core.Transforms.Revert(sid, "admin revert-all");
                if (ok) reverted++;
            }
            catch (Exception ex)
            {
                Core.Log.LogWarning($"[Beelz AUDIT] revert-all: failed reverting {sid}: {ex}");
            }
        }
        Core.Persistence.RequestSave();

        ctx.Reply($"Reverted {reverted}/{activeSteamIds.Count} active transformations.");
        ulong adminSteamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Core.Log.LogInfo($"[Beelz AUDIT] admin={adminSteamId} action=revert-all reverted={reverted}/{activeSteamIds.Count}");
    }

    [Command("freeze-captures", description: "Toggle the CaptureOnKill master switch at runtime. Usage: .beelz admin freeze-captures <on|off|status>", adminOnly: true)]
    public static void FreezeCaptures(ChatCommandContext ctx, string mode = "status")
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        string verb = mode?.Trim().ToLowerInvariant() ?? "status";

        switch (verb)
        {
            case "status":
                ctx.Reply($"CaptureOnKill = {(Beelzebub.Config.Settings.CaptureOnKill.Value ? "ON (capturing)" : "OFF (frozen)")}");
                return;
            case "on":
            case "unfreeze":
            case "resume":
                Beelzebub.Config.Settings.CaptureOnKill.Value = true;
                ctx.Reply("Captures RESUMED.");
                break;
            case "off":
            case "freeze":
            case "pause":
                Beelzebub.Config.Settings.CaptureOnKill.Value = false;
                ctx.Reply("Captures FROZEN. Kills no longer roll for ability/transform unlocks.");
                break;
            default:
                ctx.Reply("Usage: .beelz admin freeze-captures <on|off|status>");
                return;
        }
        ulong adminSteamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Core.Log.LogInfo($"[Beelz AUDIT] admin={adminSteamId} action=freeze-captures verb={verb} value={Beelzebub.Config.Settings.CaptureOnKill.Value}");
    }

    [Command("snapshot", description: "Dump high-level Beelzebub state to chat (player counts, active transforms, etc.). Usage: .beelz admin snapshot", adminOnly: true)]
    public static void Snapshot(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        int players = Core.AbilityRegistry.PlayerCount;
        int activeTransforms = Core.AbilityRegistry.AllActiveTransforms().Count();
        int abilityMapEntries = Core.AbilityRules?.Current?.AbilityMap?.Count ?? 0;
        int transformMapEntries = Core.AbilityRules?.Current?.TransformMap?.Count ?? 0;
        int denyPatterns = Core.AbilityRules?.Current?.DenyPatterns?.Count ?? 0;
        bool capturesOn = Beelzebub.Config.Settings.CaptureOnKill.Value;
        string serverMode = AbilityRules.GetServerDifficulty();
        string scalingMode = Beelzebub.Config.Settings.Transform_PowerScalingMode?.Value ?? "CuratedScales";

        ctx.Reply($"[SNAPSHOT] players={players} active_transforms={activeTransforms} captures={(capturesOn ? "ON" : "OFF")}");
        ctx.Reply($"  ability_rules: deny_patterns={denyPatterns} curated_abilities={abilityMapEntries} curated_units={transformMapEntries}");
        ctx.Reply($"  server: difficulty={serverMode} scaling_mode={scalingMode}");
        ctx.Reply($"  build: {MyPluginInfo.PLUGIN_NAME} v{MyPluginInfo.PLUGIN_VERSION}");
        ulong adminSteamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Core.Log.LogInfo($"[Beelz AUDIT] admin={adminSteamId} action=snapshot");
    }

    [Command("wipe-all", description: "DESTRUCTIVE: wipe ALL player data (captures, transforms, slots, hotkeys, presets, cooldowns). Requires literal confirmation token. Usage: .beelz admin wipe-all CONFIRM-WIPE", adminOnly: true)]
    public static void WipeAll(ChatCommandContext ctx, string confirmToken)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (confirmToken != "CONFIRM-WIPE")
        {
            ctx.Reply("Wipe NOT executed. To confirm, run: .beelz admin wipe-all CONFIRM-WIPE");
            return;
        }

        // Revert any active transforms FIRST so the cleanup is graceful (carrier
        // buffs destroyed, summoned minions despawned). Without this, the buff
        // entities would linger past the registry wipe and we'd have orphaned
        // transforms.
        var activeSteamIds = Core.AbilityRegistry.AllActiveTransforms().Select(kv => kv.Key).ToList();
        foreach (ulong sid in activeSteamIds)
        {
            try { Core.Transforms.Revert(sid, "admin wipe-all"); } catch { /* swallow */ }
        }

        var (players, abilities, transforms) = Core.AbilityRegistry.WipeAll();
        Core.Persistence.RequestSave();

        ctx.Reply($"WIPED ALL DATA: {players} player(s), {abilities} captured abilit(ies), {transforms} transform unlock(s). Reverted {activeSteamIds.Count} active transforms first.");
        ulong adminSteamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Core.Log.LogWarning($"[Beelz AUDIT] admin={adminSteamId} action=wipe-all players={players} abilities={abilities} transforms={transforms} reverted_first={activeSteamIds.Count}");
    }

    [Command("clear-weapon-slot", description: "Clear a player's weapon-specific slot binding. Usage: .beelz admin clear-weapon-slot <player> <weapon> <slot 1-6>", adminOnly: true)]
    public static void ClearWeaponSlot(ChatCommandContext ctx, string player, string weaponStr, int slot)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (slot < 1 || slot > 6) { ctx.Reply("Slot must be 1-6."); return; }

        if (!Enum.TryParse<WeaponFamily>(weaponStr, ignoreCase: true, out var weapon)
            || weapon == WeaponFamily.None
            || weapon == WeaponFamily.Magic)
        {
            ctx.Reply($"Unknown weapon family '{weaponStr}'.");
            return;
        }

        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        Core.AbilityRegistry.ClearSlot(steamId, weapon, slot);
        Core.Persistence.RequestSave();

        bool clearedNow = false;
        if (character.Exists() && SlotApply.GetCurrentWeapon(character) == weapon)
        {
            clearedNow = SlotApply.ClearGrant(character, slot);
        }

        ctx.Reply($"Cleared {fullName}'s {weapon} slot {slot}. {(clearedNow ? "Removed live." : "Saved bind removed.")}");
        Audit(ctx, "clear-weapon-slot", steamId, fullName, $"weapon={weapon} slot={slot} clearedNow={clearedNow}");
    }
}
