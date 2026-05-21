using System.Linq;
using System.Text;
using VampireCommandFramework;

namespace Beelzebub.Commands;

[CommandGroup("beelz admin")]
internal static class AdminCommands
{
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
}
