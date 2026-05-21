using System.Collections.Generic;
using System.Linq;
using System.Text;
using Beelzebub.Services;
using Stunlock.Core;
using VampireCommandFramework;

namespace Beelzebub.Commands;

[CommandGroup("beelz")]
internal static class TransformCommands
{
    [Command("transforms", description: "List the units you've unlocked the ability to transform into.")]
    public static void Transforms(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var unlocks = Core.AbilityRegistry.ListTransforms(steamId);
        if (unlocks.Count == 0)
        {
            ctx.Reply("No transformation unlocks yet. Defeat units (each kill rolls a small chance to unlock).");
            return;
        }

        var sb = new StringBuilder();
        sb.Append("Transformation unlocks (").Append(unlocks.Count).AppendLine("):");

        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active is not null)
        {
            sb.Append("ACTIVE: ").Append(new PrefabGUID(active.UnitPrefabGuid).GetPrefabName());
            if (active.Duration.HasValue)
            {
                var elapsed = (System.DateTime.UtcNow - active.ActivatedAtUtc).TotalSeconds;
                var remaining = active.Duration.Value.TotalSeconds - elapsed;
                sb.Append(" (").Append(remaining.ToString("F0")).Append("s remaining)");
            }
            else
            {
                sb.Append(" (Toggle mode — manual revert)");
            }
            sb.AppendLine();
        }

        var byGroup = unlocks
            .Select((u, idx) => (idx, unlock: u))
            .GroupBy(t => t.unlock.Source)
            .OrderByDescending(g => g.Key == CaptureSource.VBlood);

        foreach (var grp in byGroup)
        {
            sb.Append(grp.Key == CaptureSource.VBlood ? "-- V-Bloods --" : "-- Regular mobs --").AppendLine();
            foreach (var (idx, unlock) in grp.OrderBy(t => new PrefabGUID(t.unlock.UnitPrefabGuid).GetPrefabName()))
            {
                sb.Append("  ").Append(idx).Append(": ").AppendLine(new PrefabGUID(unlock.UnitPrefabGuid).GetPrefabName());
            }
        }
        ctx.Reply(sb.ToString());
    }

    [Command("transform", description: "Activate transformation. Usage: .beelz transform <index|unitName>. Swap a weapon to apply.")]
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
            // Match by case-insensitive substring of the unit's PrefabName.
            var match = unlocks.FirstOrDefault(u =>
                new PrefabGUID(u.UnitPrefabGuid).GetPrefabName().Contains(unitOrIndex, System.StringComparison.OrdinalIgnoreCase));
            if (match.UnitPrefabGuid == 0)
            {
                ctx.Reply($"No unlocked transform matches '{unitOrIndex}'. Use .beelz transforms for the list.");
                return;
            }
            unitGuid = match.UnitPrefabGuid;
        }

        var (ok, message) = Core.Transforms.TryActivate(steamId, unitGuid);
        ctx.Reply(message);
        if (ok)
        {
            Core.Chat.SendEvent(ctx.Event.SenderCharacterEntity,
                $"[BEELZ:event] type=transform-activated u={unitGuid} un={new PrefabGUID(unitGuid).GetPrefabName()}");
        }
    }

    [Command("revert", description: "Revert your current transformation. Swap a weapon to restore normal abilities.")]
    public static void Revert(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active is null) { ctx.Reply("You are not currently transformed."); return; }

        Core.Transforms.Revert(steamId, "manual");
        string unitName = new PrefabGUID(active.UnitPrefabGuid).GetPrefabName();
        ctx.Reply($"Reverted from {unitName}. Swap a weapon to restore normal abilities.");
        Core.Chat.SendEvent(ctx.Event.SenderCharacterEntity,
            $"[BEELZ:event] type=transform-ended u={active.UnitPrefabGuid} un={unitName} reason=manual");
    }
}
