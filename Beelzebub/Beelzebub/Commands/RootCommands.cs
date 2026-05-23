using VampireCommandFramework;

namespace Beelzebub.Commands;

/// <summary>
/// Z4 (v0.14.0): top-level `.beelz` command (no subcommand) prints a short overview
/// so players who don't know the syntax aren't dropped into a wall of help text.
/// Lives outside the [CommandGroup("beelz")] so VCF resolves bare `.beelz` to this
/// method, and `.beelz help|list|grant|...` still resolves into BeelzCommands etc.
/// </summary>
internal static class RootCommands
{
    [Command("beelz", description: "Beelzebub overview — what the mod does and how to start.")]
    public static void Beelz(ChatCommandContext ctx)
    {
        ctx.Reply("Beelzebub, Lord of Gluttony — devour your foes, wield their power.");
        ctx.Reply("Kill units to capture abilities. Rare rolls unlock TRANSFORM into the unit.");
        ctx.Reply("Top commands: .beelz list, .beelz transforms, .beelz grant <slot> <index>, .beelz transform <name>, .beelz revert");
        ctx.Reply("Walkthrough: .beelz help     Full command list: .beelz commands");
        ctx.Reply("Full reference + config docs: https://thunderstore.io/c/v-rising/p/kdpen/Beelzebub/");
    }
}
