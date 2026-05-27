using System;
using System.Linq;
using System.Text;
using Beelzebub.Services;
using ProjectM;
using ProjectM.Network;
using ProjectM.Shared;
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

    [Command("help", description: "List all Beelzebub admin commands, grouped by purpose.", adminOnly: true)]
    public static void Help(ChatCommandContext ctx)
    {
        ctx.Reply("=== Beelzebub ADMIN commands === (player commands: .beelz commands)");
        ctx.Reply("-- RULES / CAPTURE FILTERS --");
        ctx.Reply(".beelz admin rules / reload — show / re-read the ability rules");
        ctx.Reply(".beelz admin deny|undeny|allow|unallow <pattern> — capture-filter substring patterns");
        ctx.Reply(".beelz admin freeze-captures <on|off|status> — master CaptureOnKill toggle");
        ctx.Reply("-- TRANSFORM CONFIG --");
        ctx.Reply(".beelz admin transform mode|duration|cooldown <regular|vblood> <...> — transform tuning");
        ctx.Reply(".beelz admin transform show — current transform settings · difficulty [basic|brutal] — server gating");
        ctx.Reply("-- PLAYER GRANTS --");
        ctx.Reply(".beelz admin give|revoke <player> <unitGuid> <abilityGuid> — grant / remove a captured ability");
        ctx.Reply(".beelz admin give-transform|revoke-transform <player> <unitGuid> — grant / remove a transform unlock");
        ctx.Reply(".beelz admin force-transform|clear-transform <player> [unitGuid] — force / end a transform");
        ctx.Reply(".beelz admin set-slot|clear-slot <player> <slot> [abilityGuid] — universal slot binds");
        ctx.Reply(".beelz admin set-weapon-slot|clear-weapon-slot <player> <weapon> <slot> [abilityGuid] — per-weapon binds");
        ctx.Reply("-- INSPECT --");
        ctx.Reply(".beelz admin inspect <player> / progress <player> — view a player's state");
        ctx.Reply(".beelz admin snapshot — server-wide summary · scan-abilities — dump ability metadata to disk");
        ctx.Reply("-- SUMMONS --");
        ctx.Reply(".beelz admin desummon <player> / desummon-all — clean up ally summons · revert-all — end all transforms");
        ctx.Reply("-- RECOVERY (fix a stuck player, no server wipe) --");
        ctx.Reply(".beelz admin respawn <player> — rebuild a stuck bar by respawning in place (keeps progress)");
        ctx.Reply(".beelz admin rebuildslots / clearslotmods / rebuildbar <player> — slot/bar repair levers");
        ctx.Reply(".beelz admin buffs <player> — DIAGNOSTIC: dump buffs + slot overrides to the server log");
        ctx.Reply(".beelz admin copy-collection <player> / paste-collection <player> — backup + restore a collection");
        ctx.Reply(".beelz admin reset-character <player> CONFIRM-RESET — fresh character (collection preserved)");
        ctx.Reply("-- DANGER --");
        ctx.Reply(".beelz admin set <key> <value> — set any config live (keys via .beelz api config)");
        ctx.Reply(".beelz admin wipe-all CONFIRM-WIPE — wipe ALL player data on the server");
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

    [Command("buffs", description: "DIAGNOSTIC: dump a player's active buffs and which ones override ability slots, to the server log (with a chat summary). Use to find what's driving a stuck ability bar. Usage: .beelz admin buffs [player] (default: you)", adminOnly: true)]
    public static void Buffs(ChatCommandContext ctx, string player = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        Entity character;
        ulong steamId;
        string fullName;
        if (string.IsNullOrWhiteSpace(player))
        {
            character = ctx.Event.SenderCharacterEntity;
            steamId = character.GetSteamId();
            fullName = "you";
        }
        else
        {
            character = EntityExtensions.FindCharacterByName(player, out steamId, out fullName);
            if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        }

        if (!character.Exists() || !Core.EntityManager.HasBuffer<BuffBuffer>(character))
        {
            ctx.Reply("No buff buffer on that character.");
            return;
        }

        // v0.43.10: dump the CHARACTER ENTITY's own prefab — the decisive check for a
        // server-side shapeshift. A normal vampire reads CHAR_Vampire_*; if this shows a
        // creature/bear prefab, the player is genuinely shapeshifted server-side (and the
        // fix must operate on this entity), not just a stale client HUD.
        var charPg = character.GetPrefabGuid();
        Core.Log.LogInfo($"[Beelz BUFFS] character entity prefab = {charPg._Value} {charPg.GetPrefabName()}");

        // v0.43.11: dump the character's OWN equipped-ability slots (the persistent base
        // the cast resolves from). If a shapeshift baked creature abilities in here and the
        // form buff was removed abnormally (logout w/o revert), these stick — and normal
        // equipment/spellbook changes won't overwrite them. This is what a "frozen bar" looks like.
        if (Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(character))
        {
            var slotBuf = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(character);
            Core.Log.LogInfo($"[Beelz BUFFS] character AbilityGroupSlotBuffer ({slotBuf.Length} slot(s)):");
            for (int s = 0; s < slotBuf.Length; s++)
            {
                int ag = slotBuf[s].BaseAbilityGroupOnSlot._Value;
                Core.Log.LogInfo($"[Beelz BUFFS]   baseSlot[{s}] = {ag} {(ag == 0 ? "(empty)" : new Stunlock.Core.PrefabGUID(ag).GetPrefabName())}");
            }
        }
        else
        {
            Core.Log.LogInfo($"[Beelz BUFFS] character has NO AbilityGroupSlotBuffer.");
        }

        var buffs = Core.EntityManager.GetBuffer<BuffBuffer>(character);
        int total = buffs.Length, overriders = 0;
        bool hasBearShapeshift = false, hasCarrier = false;
        Core.Log.LogInfo($"[Beelz BUFFS] === {fullName} (SteamID {steamId}) has {total} buff(s) ===");
        for (int i = 0; i < buffs.Length; i++)
        {
            int guid = buffs[i].PrefabGuid._Value;
            if (guid == -1569370346) hasBearShapeshift = true; // AB_Shapeshift_Bear_Buff
            if (guid == 1171608023) hasCarrier = true;          // Beelzebub ability carrier
            string name = buffs[i].PrefabGuid.GetPrefabName() ?? "(unknown)";
            Entity be = buffs[i].Entity;
            string overrideInfo = "";
            if (be.Exists() && Core.EntityManager.HasBuffer<ReplaceAbilityOnSlotBuff>(be))
            {
                var ovr = Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(be);
                var sbo = new StringBuilder();
                int entries = 0;
                for (int j = 0; j < ovr.Length; j++)
                {
                    var e = ovr[j];
                    if (e.NewGroupId._Value == 0) continue;
                    entries++;
                    sbo.Append($" [slot{e.Slot}={AbilityDisplay(e.NewGroupId._Value)}#{e.NewGroupId._Value}/p{e.Priority}/{e.Target}]");
                }
                if (entries > 0) { overriders++; overrideInfo = " OVERRIDES:" + sbo; }
            }
            Core.Log.LogInfo($"[Beelz BUFFS]   #{i} guid={guid} {name}{overrideInfo}");
        }
        // v0.43.12: hunt ALL player-owned ability-slot override SOURCES — including ones NOT in
        // the BuffBuffer (the orphan that can freeze the bar). This is what actually drives the
        // resolved bar via ReplaceAbilityOnSlotSystem.
        try
        {
            EntityQuery q = Core.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ReplaceAbilityOnSlotBuff>(),
                ComponentType.ReadOnly<EntityOwner>());
            var arr = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            int ownedSrc = 0;
            Core.Log.LogInfo($"[Beelz BUFFS] --- owned ReplaceAbilityOnSlot sources ---");
            for (int i = 0; i < arr.Length; i++)
            {
                Entity e = arr[i];
                if (!e.Exists() || !e.TryGetComponent<EntityOwner>(out var o) || o.Owner != character) continue;
                ownedSrc++;
                var pg = e.GetPrefabGuid();
                var ob = Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(e);
                var sbo = new StringBuilder();
                for (int j = 0; j < ob.Length; j++)
                {
                    if (ob[j].NewGroupId._Value == 0) continue;
                    sbo.Append($" [slot{ob[j].Slot}={AbilityDisplay(ob[j].NewGroupId._Value)}#{ob[j].NewGroupId._Value}/p{ob[j].Priority}]");
                }
                Core.Log.LogInfo($"[Beelz BUFFS]   src {pg._Value} {pg.GetPrefabName()}{sbo}");
            }
            arr.Dispose();
            Core.Log.LogInfo($"[Beelz BUFFS] --- {ownedSrc} owned source(s) ---");
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz BUFFS] owned-source query failed: {ex.Message}"); }

        Core.Log.LogInfo($"[Beelz BUFFS] === end ({overriders} override slots) | charPrefab={charPg.GetPrefabName()} bearShapeshiftBuff={hasBearShapeshift} carrierBuff={hasCarrier} ===");
        ctx.Reply($"Dumped {total} buff(s) for {fullName} to the server log; {overriders} override ability slots; entity prefab = {charPg.GetPrefabName()}. See BepInEx LogOutput.log lines tagged [Beelz BUFFS].");
        Audit(ctx, "buffs", steamId, fullName, $"total={total} overriders={overriders} charPrefab={charPg._Value} bear={hasBearShapeshift} carrier={hasCarrier}");
    }

    [Command("rebuildbar", description: "Force a player's ability bar to rebuild from scratch via the engine's init-state (fixes a bar frozen at the resolved level, e.g. stuck after a shapeshift). Usage: .beelz admin rebuildbar [player] (default: you)", adminOnly: true)]
    public static void RebuildBar(ChatCommandContext ctx, string player = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        Entity character;
        ulong steamId;
        string fullName;
        if (string.IsNullOrWhiteSpace(player))
        {
            character = ctx.Event.SenderCharacterEntity;
            steamId = character.GetSteamId();
            fullName = "you";
        }
        else
        {
            character = EntityExtensions.FindCharacterByName(player, out steamId, out fullName);
            if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        }

        bool ok = TransformBuffService.ForceAbilityBarReinit(character);
        ctx.Reply(ok
            ? $"Forced an ability-bar rebuild for {fullName}. If the bar still looks wrong, try equipping a weapon to trigger the rebuild, and tell me — the server log has the details."
            : $"Could not toggle the ability-bar init-state for {fullName} (logged details). Tell me what the server log says under [Beelz] rebuildbar.");
        Audit(ctx, "rebuildbar", steamId, fullName, $"ok={ok}");
    }

    [Command("respawn", description: "Respawn a player's character AT THEIR CURRENT SPOT — V Rising rebuilds the character fresh, which fixes a stuck/frozen ability bar (the bear-form bug). Inventory, equipment, blood, and progress are preserved (same as dying + respawning). Usage: .beelz admin respawn [player] (default: you)", adminOnly: true)]
    public static void Respawn(ChatCommandContext ctx, string player = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        Entity character;
        ulong steamId;
        string fullName;
        if (string.IsNullOrWhiteSpace(player))
        {
            character = ctx.Event.SenderCharacterEntity;
            steamId = character.GetSteamId();
            fullName = "you";
        }
        else
        {
            character = EntityExtensions.FindCharacterByName(player, out steamId, out fullName);
            if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        }

        // First clear any Beelzebub state so the rebuilt character starts clean.
        try { Core.Transforms.Revert(steamId, "respawn", restoreBar: false); } catch { /* not transformed */ }
        try { TransformBuffService.RemoveAllFormsAndShapeshifts(character); } catch { /* best effort */ }

        try
        {
            if (!character.TryGetComponent<PlayerCharacter>(out var pc))
            {
                ctx.Reply("That entity isn't a player character."); return;
            }
            Entity userEntity = pc.UserEntity;
            var pos = Core.EntityManager.GetComponentData<Unity.Transforms.LocalToWorld>(character).Position;
            var spawnLoc = new Il2CppSystem.Nullable_Unboxed<Unity.Mathematics.float3> { value = pos };

            var sbs = Core.Server.GetExistingSystemManaged<ServerBootstrapSystem>();
            var bufferSystem = Core.Server.GetExistingSystemManaged<Unity.Entities.EntityCommandBufferSystem>();
            var buffer = bufferSystem.CreateCommandBuffer();
            sbs.RespawnCharacter(buffer, userEntity, customSpawnLocation: spawnLoc, previousCharacter: character);

            Core.Log.LogInfo($"[Beelz] respawn: RespawnCharacter requested for {fullName} ({steamId}) at current position to rebuild a clean character + ability bar.");
            ctx.Reply($"Respawning {fullName} on the spot to rebuild the character — this clears a stuck/frozen ability bar. Equipment + progress are preserved. Give it a moment, then check your bar.");
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] respawn failed for {fullName}: {ex}");
            ctx.Reply($"Respawn failed: {ex.Message}");
        }
        Audit(ctx, "respawn", steamId, fullName, "");
    }

    [Command("clearslotmods", description: "RECOVERY: clear orphaned ability-slot MODIFICATIONS on a player's character (the deep cause of a bar frozen on a creature kit) and force the slots to rebuild from base. Run after .beelz clear if a stuck bar survives everything. Usage: .beelz admin clearslotmods [player] (default: you)", adminOnly: true)]
    public static void ClearSlotMods(ChatCommandContext ctx, string player = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        Entity character;
        ulong steamId;
        string fullName;
        if (string.IsNullOrWhiteSpace(player))
        {
            character = ctx.Event.SenderCharacterEntity;
            steamId = character.GetSteamId();
            fullName = "you";
        }
        else
        {
            character = EntityExtensions.FindCharacterByName(player, out steamId, out fullName);
            if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        }

        if (!character.Exists() || !Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(character))
        {
            ctx.Reply("No ability-slot buffer on that character.");
            return;
        }

        int slots = 0, withMods = 0, modsCleared = 0, dirtied = 0;
        try
        {
            var dirtyTag = Unity.Entities.ComponentType.ReadWrite(Il2CppInterop.Runtime.Il2CppType.Of<AbilityGroupSlot.DirtyTag>());

            // Snapshot the slot entities first — adding components / clearing buffers below can
            // cause structural changes that invalidate the character's live buffer handle.
            var slotEntities = new System.Collections.Generic.List<Entity>();
            var slotBuffer = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(character);
            for (int i = 0; i < slotBuffer.Length; i++)
            {
                Entity se = slotBuffer[i].GroupSlotEntity._Entity;
                if (se != Entity.Null) slotEntities.Add(se);
            }

            foreach (Entity slotEntity in slotEntities)
            {
                if (!slotEntity.Exists()) continue;
                slots++;

                if (Core.EntityManager.HasBuffer<AbilityGroupSlotModificationBuffer>(slotEntity))
                {
                    var modBuf = Core.EntityManager.GetBuffer<AbilityGroupSlotModificationBuffer>(slotEntity);
                    if (modBuf.Length > 0)
                    {
                        withMods++;
                        modsCleared += modBuf.Length;
                        Core.Log.LogInfo($"[Beelz CLEARMODS] slot entity {slotEntity} had {modBuf.Length} modification(s) — clearing.");
                        modBuf.Clear();
                    }
                }

                if (!Core.EntityManager.HasComponent(slotEntity, dirtyTag))
                {
                    Core.EntityManager.AddComponent(slotEntity, dirtyTag);
                    dirtied++;
                }
            }

            if (Core.ReplaceAbilityOnSlotSystem != null) Core.ReplaceAbilityOnSlotSystem.OnUpdate();
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] clearslotmods failed for {fullName}: {ex}");
            ctx.Reply($"clearslotmods failed: {ex.Message}");
            return;
        }

        Core.Log.LogInfo($"[Beelz CLEARMODS] {fullName} ({steamId}): {slots} slot(s) scanned, {withMods} had modifications, {modsCleared} cleared, {dirtied} marked dirty for rebuild.");
        ctx.Reply($"Cleared {modsCleared} ability-slot modification(s) across {withMods}/{slots} slot(s) for {fullName} and forced a rebuild. Check your bar — equip/swap a weapon to refresh it if needed.");
        Audit(ctx, "clearslotmods", steamId, fullName, $"slots={slots} withMods={withMods} cleared={modsCleared} dirtied={dirtied}");
    }

    [Command("rebuildslots", description: "RECOVERY (safe): re-sync a player's active ability slots back to their stored base abilities via the engine's slot-setter — fixes a bar stuck on creature/shapeshift abilities. Values only; never destroys entities. Usage: .beelz admin rebuildslots [player] (default: you)", adminOnly: true)]
    public static void RebuildSlots(ChatCommandContext ctx, string player = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        Entity character;
        ulong steamId;
        string fullName;
        if (string.IsNullOrWhiteSpace(player))
        {
            character = ctx.Event.SenderCharacterEntity;
            steamId = character.GetSteamId();
            fullName = "you";
        }
        else
        {
            character = EntityExtensions.FindCharacterByName(player, out steamId, out fullName);
            if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        }

        if (!character.Exists() || !Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(character))
        {
            ctx.Reply("No ability-slot buffer on that character.");
            return;
        }

        int active = 0, resynced = 0, mismatched = 0;
        try
        {
            // Find the equipped-weapon buff — ModifyAbilityGroupOnSlot needs a modification source
            // (Bloodcraft uses the equip buff the same way).
            Entity equipBuff = Entity.Null;
            var bb = Core.EntityManager.GetBuffer<BuffBuffer>(character);
            for (int i = 0; i < bb.Length; i++)
            {
                string n = bb[i].PrefabGuid.GetPrefabName();
                if (n != null && n.StartsWith("EquipBuff_Weapon", StringComparison.OrdinalIgnoreCase)) { equipBuff = bb[i].Entity; break; }
            }
            if (!equipBuff.Exists())
            {
                ctx.Reply("Couldn't find your equipped-weapon buff to drive the re-sync.");
                return;
            }

            // Snapshot the ACTIVE-region slots (0-8): their stored base ability + slot entity.
            // (Structural changes below can invalidate the live buffer handle, so snapshot first.)
            var slotBuffer = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(character);
            int count = System.Math.Min(slotBuffer.Length, 9);
            var snap = new System.Collections.Generic.List<(int idx, Stunlock.Core.PrefabGUID baseAbility, Entity slot)>();
            for (int i = 0; i < count; i++)
                snap.Add((i, slotBuffer[i].BaseAbilityGroupOnSlot, slotBuffer[i].GroupSlotEntity._Entity));

            // DIAGNOSTIC: log active (what it casts) vs base (what it should be) per slot.
            Core.Log.LogInfo($"[Beelz REBUILDSLOTS] {fullName} ({steamId}) active vs base (pre-resync):");
            foreach (var (idx, baseAbility, slot) in snap)
            {
                string activeName = "(none)";
                if (slot.Exists() && Core.EntityManager.HasComponent<AbilityGroupSlot>(slot))
                {
                    var ags = Core.EntityManager.GetComponentData<AbilityGroupSlot>(slot);
                    Entity st = ags.StateEntity._Entity;
                    activeName = st.Exists() ? (st.GetPrefabGuid().GetPrefabName() ?? "?") : "(none)";
                }
                string baseName = baseAbility._Value == 0 ? "(empty)" : (baseAbility.GetPrefabName() ?? "?");
                if (!string.Equals(activeName, baseName)) mismatched++;
                Core.Log.LogInfo($"[Beelz REBUILDSLOTS]   slot[{idx}] active={activeName} base={baseName}");
            }

            // SAFE FIX: write each active slot back to its stored BASE ability (the vanilla value
            // already on your character) via the engine's own slot-setter. Values only — NO entity
            // destruction (that crashed the server). Mark each slot dirty so the bar re-resolves.
            var sgm = Core.ServerGameManager;
            var dirty = Unity.Entities.ComponentType.ReadWrite(Il2CppInterop.Runtime.Il2CppType.Of<AbilityGroupSlot.DirtyTag>());
            foreach (var (idx, baseAbility, slot) in snap)
            {
                active++;
                try { sgm.ModifyAbilityGroupOnSlot(equipBuff, character, idx, baseAbility); resynced++; }
                catch (Exception ex) { Core.Log.LogWarning($"[Beelz REBUILDSLOTS] resync slot {idx} failed: {ex.Message}"); }
                try { if (slot.Exists() && !Core.EntityManager.HasComponent(slot, dirty)) Core.EntityManager.AddComponent(slot, dirty); }
                catch (Exception ex) { Core.Log.LogWarning($"[Beelz REBUILDSLOTS] dirty slot {idx} failed: {ex.Message}"); }
            }
            if (Core.ReplaceAbilityOnSlotSystem != null) Core.ReplaceAbilityOnSlotSystem.OnUpdate();
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] rebuildslots failed for {fullName}: {ex}");
            ctx.Reply($"rebuildslots failed: {ex.Message}");
            return;
        }

        Core.Log.LogInfo($"[Beelz REBUILDSLOTS] {fullName} ({steamId}): re-synced {resynced}/{active} active slot(s) to base ({mismatched} were mismatched).");
        ctx.Reply($"Re-synced {resynced} ability slot(s) to your base abilities for {fullName} ({mismatched} were mismatched). No entities destroyed. Equip/swap a weapon to refresh your bar; if it's still off, relog. Details in the server log under [Beelz REBUILDSLOTS].");
        Audit(ctx, "rebuildslots", steamId, fullName, $"active={active} resynced={resynced} mismatched={mismatched}");
    }

    // --- v0.43.20: collection copy/paste (admin backup-and-restore redundancy) ---

    // In-memory admin clipboard for a player's collected captures + transform unlocks. Survives
    // for the server session (the copy -> re-roll -> paste flow happens in one session: the
    // re-roll just kicks + recreates the character, it doesn't restart the server).
    static System.Collections.Generic.List<CapturedAbility> _clipboardCaptures;
    static System.Collections.Generic.List<UnlockedTransform> _clipboardTransforms;
    static string _clipboardSource;

    [Command("copy-collection", description: "Copy a player's captured abilities + transform unlocks into the admin clipboard (to paste onto another character — e.g. as a backup before a re-roll). Usage: .beelz admin copy-collection <player>", adminOnly: true)]
    public static void CopyCollection(ChatCommandContext ctx, string player)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        _clipboardCaptures = Core.AbilityRegistry.ListFor(steamId).ToList();
        _clipboardTransforms = Core.AbilityRegistry.ListTransforms(steamId).ToList();
        _clipboardSource = fullName;

        ctx.Reply($"Copied {_clipboardCaptures.Count} captured ability(ies) + {_clipboardTransforms.Count} transform unlock(s) from {fullName} to the clipboard. Use .beelz admin paste-collection <player> to apply (clipboard lasts until the server restarts).");
        Audit(ctx, "copy-collection", steamId, fullName, $"captures={_clipboardCaptures.Count} transforms={_clipboardTransforms.Count}");
    }

    [Command("paste-collection", description: "Paste the admin clipboard's captured abilities + transform unlocks onto a player (additive — keeps anything they already have). Run .beelz admin copy-collection first. Usage: .beelz admin paste-collection <player>", adminOnly: true)]
    public static void PasteCollection(ChatCommandContext ctx, string player)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (_clipboardCaptures == null && _clipboardTransforms == null)
        {
            ctx.Reply("Clipboard is empty — run .beelz admin copy-collection <player> first.");
            return;
        }
        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }

        int abilities = 0, transforms = 0;
        if (_clipboardCaptures != null)
            foreach (var c in _clipboardCaptures)
                if (Core.AbilityRegistry.Add(steamId, c.UnitPrefabGuid, c.AbilityPrefabGuid, c.Source)) abilities++;
        if (_clipboardTransforms != null)
            foreach (var t in _clipboardTransforms)
                if (Core.AbilityRegistry.AddTransformUnlock(steamId, t.UnitPrefabGuid, t.Source)) transforms++;

        Core.Persistence.RequestSave();

        ctx.Reply($"Pasted {abilities} new ability(ies) + {transforms} new transform unlock(s) onto {fullName}" +
            (string.IsNullOrEmpty(_clipboardSource) ? "" : $" (copied from {_clipboardSource})") +
            ". Their existing collection was kept; duplicates were skipped.");
        Audit(ctx, "paste-collection", steamId, fullName, $"newAbilities={abilities} newTransforms={transforms} from={_clipboardSource}");
    }

    [Command("reset-character", description: "Reset a player's CHARACTER to a fresh start: unbinds their Steam ID + kicks them, so they create a NEW character on next login. Their Beelzebub collection (captures/transforms) is preserved (it's tied to their Steam account). The old body stays in-world but unplayable. Requires a confirm token. Usage: .beelz admin reset-character <player> CONFIRM-RESET", adminOnly: true)]
    public static void ResetCharacter(ChatCommandContext ctx, string player, string confirm = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (!string.Equals(confirm, "CONFIRM-RESET", StringComparison.Ordinal))
        {
            ctx.Reply("This UNBINDS the character: the player is kicked and creates a fresh character on next login. Their Beelzebub captures/transforms are KEPT (tied to their Steam account). The old body stays in-world but becomes unplayable. To do it, re-run: .beelz admin reset-character <player> CONFIRM-RESET");
            return;
        }

        var character = EntityExtensions.FindCharacterByName(player, out ulong steamId, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        if (!character.TryGetComponent<PlayerCharacter>(out var pc) || !pc.UserEntity.Exists())
        {
            ctx.Reply("Couldn't resolve that player's user entity.");
            return;
        }
        Entity userEntity = pc.UserEntity;

        // Kick first (best-effort) while the Steam ID is still bound, then UNBIND by zeroing
        // PlatformId on the User (mirrors KindredCommands .unbindplayer). On next login V Rising
        // sees no character bound to the Steam ID and prompts a fresh one. No entity destruction.
        try { KickUser(userEntity); }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz] reset-character: kick failed (will still unbind): {ex.Message}"); }

        try
        {
            var user = Core.EntityManager.GetComponentData<User>(userEntity);
            user.PlatformId = 0;
            Core.EntityManager.SetComponentData(userEntity, user);
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] reset-character unbind failed for {fullName}: {ex}");
            ctx.Reply($"Reset failed: {ex.Message}");
            return;
        }

        Core.Log.LogInfo($"[Beelz] reset-character: unbound {fullName} ({steamId}) — fresh character on next login (Beelzebub collection preserved).");
        ctx.Reply($"Reset {fullName}: Steam ID unbound + kicked. On their next login they'll create a brand-new character; their Beelzebub collection is preserved. (The old body remains in-world but is unplayable. Use .beelz admin paste-collection if you also backed up a copy.)");
        Audit(ctx, "reset-character", steamId, fullName, "unbound");
    }

    /// <summary>Kick a connected user (mirrors KindredCommands Helper.KickPlayer): create a
    /// KickEvent network event. Best-effort; used by reset-character before unbinding.</summary>
    static void KickUser(Entity userEntity)
    {
        var em = Core.EntityManager;
        var user = em.GetComponentData<User>(userEntity);
        if (!user.IsConnected || user.PlatformId == 0) return;

        Entity e = em.CreateEntity(
            Unity.Entities.ComponentType.ReadOnly(Il2CppInterop.Runtime.Il2CppType.Of<NetworkEventType>()),
            Unity.Entities.ComponentType.ReadOnly(Il2CppInterop.Runtime.Il2CppType.Of<SendEventToUser>()),
            Unity.Entities.ComponentType.ReadOnly(Il2CppInterop.Runtime.Il2CppType.Of<KickEvent>()));
        em.SetComponentData(e, new KickEvent { PlatformId = user.PlatformId });
        em.SetComponentData(e, new SendEventToUser { UserIndex = user.Index });
        em.SetComponentData(e, new NetworkEventType
        {
            EventId = NetworkEvents.EventId_KickEvent,
            IsAdminEvent = false,
            IsDebugEvent = false
        });
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
        Core.AbilityRegistry.SetCooldownUntil(steamId, Core.Transforms.CategoryFor(source, unitGuid), DateTime.MinValue);

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

    // --- v0.39.0: runtime config editing (BCH settings panel) ---

    [Command("set", description: "Set a config value at runtime by key (applies live, persists to the .cfg). Usage: .beelz admin set <key> <value>. See .beelz api config for keys.", adminOnly: true)]
    public static void SetConfig(ChatCommandContext ctx, string key, string value)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        var entry = FindConfigEntry(key);
        if (entry == null)
        {
            ctx.Reply($"No config key matches '{key}'. Use .beelz api config (or the .cfg) for valid keys.");
            return;
        }

        object parsed;
        try { parsed = ParseConfigValue(entry.SettingType, value); }
        catch (Exception ex)
        {
            ctx.Reply($"Couldn't set {entry.Definition.Key}: '{value}' isn't a valid {entry.SettingType.Name} ({ex.Message}).");
            return;
        }

        object old = entry.BoxedValue;
        try { entry.BoxedValue = parsed; }
        catch (Exception ex) { ctx.Reply($"Rejected: {ex.Message}"); return; }

        ctx.Reply($"Set {entry.Definition.Key} = {parsed} (was {old}). Applies live.");
        Audit(ctx, "config-set", 0, "-", $"key={entry.Definition.Key} old={old} new={parsed}");
        // BCH refresh hint (sent to the admin who changed it; broadcast-to-all-subscribers is a follow-up).
        Core.Chat.SendEvent(ctx.Event.SenderCharacterEntity,
            $"[BEELZ:event] type=config-changed key={entry.Definition.Key} value={parsed}");
    }

    static BepInEx.Configuration.ConfigEntryBase FindConfigEntry(string key)
    {
        foreach (var prop in typeof(Beelzebub.Config.Settings).GetProperties(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (prop.GetValue(null) is BepInEx.Configuration.ConfigEntryBase e
                && string.Equals(e.Definition.Key, key, StringComparison.OrdinalIgnoreCase))
                return e;
        }
        return null;
    }

    static object ParseConfigValue(Type t, string raw)
    {
        if (t == typeof(string)) return raw;
        if (t == typeof(bool)) return bool.Parse(raw);
        if (t == typeof(int)) return int.Parse(raw);
        if (t == typeof(float)) return float.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        if (t == typeof(double)) return double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        if (t.IsEnum) return Enum.Parse(t, raw, ignoreCase: true);
        return BepInEx.Configuration.TomlTypeConverter.ConvertToValue(raw, t);
    }
}
