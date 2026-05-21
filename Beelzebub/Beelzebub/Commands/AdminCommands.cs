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

        string abilityName = new Stunlock.Core.PrefabGUID(abilityGuid).GetPrefabName();
        string unitName = new Stunlock.Core.PrefabGUID(unitGuid).GetPrefabName();
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

        string unitName = new Stunlock.Core.PrefabGUID(unitGuid).GetPrefabName();
        ctx.Reply(added
            ? $"Granted transform unlock for {unitName} (source={source}) to {fullName}."
            : $"{fullName} already has that transform unlock — no change.");
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
}
