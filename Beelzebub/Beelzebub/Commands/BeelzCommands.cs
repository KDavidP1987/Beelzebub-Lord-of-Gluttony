using System.Collections.Generic;
using System.Text;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using VampireCommandFramework;

namespace Beelzebub.Commands;

[CommandGroup("beelz")]
internal static class BeelzCommands
{
    [Command("list", description: "List the abilities you've captured from kills.")]
    public static void List(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized — try again after the server finishes loading."); return; }

        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        if (steamId == 0) { ctx.Reply("Could not resolve your Steam ID."); return; }

        var captured = Core.AbilityRegistry.ListFor(steamId);
        if (captured.Count == 0) { ctx.Reply("You haven't captured any abilities yet. Go kill something."); return; }

        var sb = new StringBuilder();
        sb.AppendLine($"Captured abilities ({captured.Count}):");
        for (int i = 0; i < captured.Count; i++)
        {
            var c = captured[i];
            sb.Append(i).Append(": ");
            sb.Append(new PrefabGUID(c.AbilityPrefabGuid).GetPrefabName());
            sb.Append("  (from ").Append(new PrefabGUID(c.UnitPrefabGuid).GetPrefabName()).Append(')');
            sb.AppendLine();
        }
        ctx.Reply(sb.ToString());
    }

    [Command("grant", description: "Grant a captured ability to one of your spell slots. Usage: .beelz grant <slot 1-6> <index from .beelz list>")]
    public static void Grant(ChatCommandContext ctx, int slot, int index)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (slot < 1 || slot > 6) { ctx.Reply("Slot must be 1-6 (1=primary, 3=Q/shift, 5=spell1, 6=spell2)."); return; }

        Entity character = ctx.Event.SenderCharacterEntity;
        ulong steamId = character.GetSteamId();
        var captured = Core.AbilityRegistry.ListFor(steamId);
        if (index < 0 || index >= captured.Count) { ctx.Reply($"Index {index} out of range (0-{captured.Count - 1})."); return; }

        PrefabGUID ability = new(captured[index].AbilityPrefabGuid);

        if (!Core.EntityManager.HasBuffer<ReplaceAbilityOnSlotBuff>(character))
        {
            Core.EntityManager.AddBuffer<ReplaceAbilityOnSlotBuff>(character);
        }
        var buffer = Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(character);
        buffer.Add(new ReplaceAbilityOnSlotBuff
        {
            Slot = slot,
            NewGroupId = ability,
            CopyCooldown = true,
            Priority = 0,
        });

        ctx.Reply($"Wrote {ability.GetPrefabName()} to slot {slot}. If it doesn't appear in your spell bar, try swapping weapons to re-trigger the game's slot system.");
        Core.Log.LogInfo($"[Beelz] {steamId} grant slot={slot} ability={ability._Value} ({ability.GetPrefabName()})");
    }

    [Command("clear", description: "Forget all your captured abilities. Cannot be undone.")]
    public static void Clear(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        if (Core.AbilityRegistry.Clear(steamId))
        {
            Core.Persistence.SaveSync();
            ctx.Reply("Captured abilities cleared.");
        }
        else
        {
            ctx.Reply("You had no captured abilities.");
        }
    }
}
