using System;
using System.Collections.Generic;
using System.Linq;
using Beelzebub.Services;
using Unity.Entities;
using VampireCommandFramework;

namespace Beelzebub.Commands;

// v0.135.0 — incompatibility locks + reseed (admin). Split from AdminCommands.cs for size; same command group.
internal static partial class AdminCommands
{
    const string LockHelp = "Members are ability names / IDs (from .beelz admin ability-inspect or the catalog) or cat:<Category> "
        + "(Travel, Aoe, Projectile, Summon, Buff, WeaponSpell, Spell, Melee, Other). Quote a list: \"Gargoyle WingShield, Rat Vanguard\".";

    static string DescribeMember(string m)
    {
        if (!ExclusionService.TryParseMember(m, out int guid, out string cat)) return $"{m} (UNRESOLVED — ignored)";
        return guid != 0 ? $"{AbilityDisplay(guid)} [{guid}]" : $"cat:{cat}";
    }

    static void AfterLockEdit(ChatCommandContext ctx, string what)
    {
        if (!Core.AbilityRules.Save()) { ctx.Reply("⚠ Couldn't write ability_rules.json — the change is live until restart only. Check the server log."); }
        ExclusionService.Invalidate();
        int n = ExclusionService.ReapplyAllOnline();
        ctx.Reply($"{what} Re-checked {n} online player(s).");
    }

    [Command("lock add", description: "Create/extend an incompatibility lock group: at most MAX of its members may be on one player's active bar + hotkeys at once (new groups start at max 1). Usage: .beelz admin lock add <group> \"<member, member, ...>\"", adminOnly: true)]
    public static void LockAdd(ChatCommandContext ctx, string group, string members)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        group = (group ?? "").Trim();
        if (group.Length == 0) { ctx.Reply("Give the group a name, e.g. .beelz admin lock add nocombo \"Gargoyle WingShield, Rat Vanguard\""); return; }
        var list = (members ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (list.Count == 0) { ctx.Reply("No members given. " + LockHelp); return; }

        var groups = Core.AbilityRules.Current.ExclusionGroups;
        if (!groups.TryGetValue(group, out var g)) { g = new AbilityRules.ExclusionGroupDto(); groups[group] = g; }
        var added = new List<string>();
        var bad = new List<string>();
        foreach (var m in list)
        {
            if (!ExclusionService.TryParseMember(m, out int guid, out string cat)) { bad.Add(m); continue; }
            string stored = guid != 0 ? (AbilityRules.ResolveAbilityKey(m) ?? m) : "cat:" + cat;
            if (g.Members.Any(x => string.Equals(x, stored, StringComparison.OrdinalIgnoreCase))) continue;
            g.Members.Add(stored);
            added.Add(DescribeMember(stored));
        }
        if (bad.Count > 0) ctx.Reply($"Not recognised (skipped): {string.Join(", ", bad)}. {LockHelp}");
        if (added.Count == 0 && bad.Count > 0 && g.Members.Count == 0) { groups.Remove(group); ctx.Reply("Nothing added."); return; }
        Audit(ctx, "lock-add", 0, "-", $"group={group} max={g.Max} added=[{string.Join("; ", added)}]");
        AfterLockEdit(ctx, $"Lock '{group}' (max {g.Max}): added {added.Count} — now {g.Members.Count} member(s).");
    }

    [Command("lock max", description: "Set how many members of a lock group may be active at once. Usage: .beelz admin lock max <group> <n>", adminOnly: true)]
    public static void LockMax(ChatCommandContext ctx, string group, int max)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (max < 1) { ctx.Reply("Max must be 1 or more (remove the group to drop the lock)."); return; }
        if (!Core.AbilityRules.Current.ExclusionGroups.TryGetValue(group ?? "", out var g)) { ctx.Reply($"No lock group '{group}'. See .beelz admin lock list"); return; }
        g.Max = max;
        Audit(ctx, "lock-max", 0, "-", $"group={group} max={max}");
        AfterLockEdit(ctx, $"Lock '{group}' now allows {max} at once.");
    }

    [Command("lock remove", description: "Remove a whole lock group, or one member from it. Usage: .beelz admin lock remove <group> [member]", adminOnly: true)]
    public static void LockRemove(ChatCommandContext ctx, string group, string member = "")
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var groups = Core.AbilityRules.Current.ExclusionGroups;
        if (!groups.TryGetValue(group ?? "", out var g)) { ctx.Reply($"No lock group '{group}'. See .beelz admin lock list"); return; }
        if (string.IsNullOrWhiteSpace(member))
        {
            groups.Remove(group);
            Audit(ctx, "lock-remove", 0, "-", $"group={group}");
            AfterLockEdit(ctx, $"Lock group '{group}' removed.");
            return;
        }
        string key = member.Trim();
        // Match the stored token, or anything that resolves to the same ability.
        int wantGuid = ExclusionService.TryParseMember(key, out int gg, out _) ? gg : 0;
        int removed = g.Members.RemoveAll(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase)
            || (wantGuid != 0 && ExclusionService.TryParseMember(x, out int xg, out _) && xg == wantGuid));
        if (removed == 0) { ctx.Reply($"'{member}' isn't in lock '{group}'."); return; }
        if (g.Members.Count == 0) groups.Remove(group);
        Audit(ctx, "lock-remove", 0, "-", $"group={group} member={member}");
        AfterLockEdit(ctx, g.Members.Count == 0 ? $"Removed '{member}'; lock '{group}' was empty and is gone." : $"Removed '{member}' from lock '{group}'.");
    }

    [Command("lock list", description: "Show every incompatibility lock group and its members.", adminOnly: true)]
    public static void LockList(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var groups = Core.AbilityRules.Current.ExclusionGroups;
        if (groups.Count == 0) { ctx.Reply("No lock groups (none ship by default). Add one: .beelz admin lock add <group> \"<a, b>\". " + LockHelp); return; }
        foreach (var (name, g) in groups.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            ctx.Reply($"🔒 {name} — max {g.Max} of: {string.Join(", ", g.Members.Select(DescribeMember))}{(string.IsNullOrEmpty(g.Notes) ? "" : $" ({g.Notes})")}");
    }

    [Command("lock check", description: "Show which of a player's active abilities are locked right now, and why. Usage: .beelz admin lock check <player>", adminOnly: true)]
    public static void LockCheck(ChatCommandContext ctx, string player)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        var character = EntityExtensions.FindCharacterByName(player, out ulong _, out string fullName);
        if (character == Entity.Null) { ctx.Reply($"No (or ambiguous) player match for '{player}'."); return; }
        ctx.Reply($"{fullName}:");
        foreach (var line in ExclusionService.Describe(character)) ctx.Reply(line);
    }

    [Command("reseed", description: "Bring this server's ability_rules.json up to this build's shipped defaults. preview = show what would change; merge = keep your edits, take shipped changes you never touched (backup kept); replace CONFIRM = overwrite with the shipped default (your curation is lost; backup kept). Usage: .beelz admin reseed <preview|merge|replace> [CONFIRM]", adminOnly: true)]
    public static void Reseed(ChatCommandContext ctx, string mode, string confirm = "")
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        switch ((mode ?? "").Trim().ToLowerInvariant())
        {
            case "preview":
            {
                var o = ReseedService.Plan();
                if (!o.Ok) { ctx.Reply("Reseed preview: " + o.Message); return; }
                ctx.Reply("Reseed preview (nothing written):");
                foreach (var l in ReseedService.Describe(o.Report)) ctx.Reply("  " + l);
                ctx.Reply("Apply with .beelz admin reseed merge  (or replace CONFIRM to take the shipped file wholesale).");
                return;
            }
            case "merge":
            {
                var o = ReseedService.Merge();
                if (o.Report != null && o.Ok) foreach (var l in ReseedService.Describe(o.Report)) ctx.Reply("  " + l);
                ctx.Reply("Reseed merge: " + o.Message);
                Audit(ctx, "reseed-merge", 0, "-", $"ok={o.Ok} {o.Report?.Summary()} backup={o.BackupPath}");
                return;
            }
            case "replace":
            {
                if (!string.Equals(confirm, "CONFIRM", StringComparison.Ordinal))
                { ctx.Reply("replace overwrites ALL your ability-rule curation with the shipped default (a backup is kept). Run .beelz admin reseed preview first; to go ahead: .beelz admin reseed replace CONFIRM"); return; }
                var o = ReseedService.Replace();
                ctx.Reply("Reseed replace: " + o.Message);
                Audit(ctx, "reseed-replace", 0, "-", $"ok={o.Ok} backup={o.BackupPath}");
                return;
            }
            default:
                ctx.Reply("Usage: .beelz admin reseed <preview|merge|replace> [CONFIRM]");
                return;
        }
    }
}
