using System.Collections.Generic;
using System.Linq;
using System.Text;
using Beelzebub.Services;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using VampireCommandFramework;

namespace Beelzebub.Commands;

[CommandGroup("beelz")]
internal static class BeelzCommands
{
    [Command("list", description: "List the abilities you've captured, grouped by unit, plus current slot assignments.")]
    public static void List(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        if (steamId == 0) { ctx.Reply("Could not resolve your Steam ID."); return; }

        var captured = Core.AbilityRegistry.ListFor(steamId);
        var slots = Core.AbilityRegistry.GetSlots(steamId);

        if (captured.Count == 0 && slots.Count == 0)
        {
            ctx.Reply("You haven't captured any abilities yet. Go kill something.");
            return;
        }

        var sb = new StringBuilder();
        if (slots.Count > 0)
        {
            sb.Append("Current slots: ");
            bool first = true;
            foreach (var (slot, abilityGuid) in slots)
            {
                if (!first) sb.Append(", ");
                sb.Append('[').Append(slot).Append(']').Append(' ').Append(new PrefabGUID(abilityGuid).GetPrefabName());
                first = false;
            }
            sb.AppendLine();
        }

        sb.Append("Captured ").Append(captured.Count).AppendLine(" ability(ies):");

        // Group by source first (VBlood at the top), then by unit within each source.
        var bySource = captured
            .Select((c, idx) => (idx, ability: c))
            .GroupBy(t => t.ability.Source)
            .OrderByDescending(g => g.Key == CaptureSource.VBlood);

        foreach (var sourceGroup in bySource)
        {
            sb.Append(sourceGroup.Key == CaptureSource.VBlood ? "-- V-Bloods --" : "-- Regular mobs --").AppendLine();
            var byUnit = sourceGroup
                .GroupBy(t => t.ability.UnitPrefabGuid)
                .OrderBy(g => new PrefabGUID(g.Key).GetPrefabName());
            foreach (var unitGroup in byUnit)
            {
                sb.Append(new PrefabGUID(unitGroup.Key).GetPrefabName()).AppendLine(":");
                foreach (var (idx, ability) in unitGroup)
                {
                    sb.Append("  ").Append(idx).Append(": ").AppendLine(new PrefabGUID(ability.AbilityPrefabGuid).GetPrefabName());
                }
            }
        }

        ctx.Reply(sb.ToString());
    }

    [Command("grant", description: "Assign a captured ability to a spell slot. Usage: .beelz grant <slot 1-6> <index>. Swap a weapon after to apply.")]
    public static void Grant(ChatCommandContext ctx, int slot, int index)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (slot < 1 || slot > 6) { ctx.Reply("Slot must be 1-6 (1=primary attack, 3=Q/shift, 5=spell1, 6=spell2)."); return; }

        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var captured = Core.AbilityRegistry.ListFor(steamId);
        if (index < 0 || index >= captured.Count)
        {
            ctx.Reply($"Index {index} out of range (valid: 0-{captured.Count - 1}). Use .beelz list to see indices.");
            return;
        }

        PrefabGUID ability = new(captured[index].AbilityPrefabGuid);
        Core.AbilityRegistry.SetSlot(steamId, slot, ability._Value);
        Core.Persistence.RequestSave();

        ctx.Reply($"Slot {slot} assigned to {ability.GetPrefabName()}. Swap any weapon to apply the change. (Not all abilities are usable in every slot — e.g. _MeleeAttack_ won't appear in spell slots 5/6.)");
        Core.Log.LogInfo($"[Beelz] {steamId} assign slot={slot} ability={ability._Value} ({ability.GetPrefabName()})");
        Core.Chat.SendEvent(ctx.Event.SenderCharacterEntity,
            $"[BEELZ:event] type=slot-granted slot={slot} a={ability._Value} an={ability.GetPrefabName()}");
    }

    [Command("unslot", description: "Remove your assignment from a spell slot. Usage: .beelz unslot <slot>. Swap a weapon after to apply.")]
    public static void Unslot(ChatCommandContext ctx, int slot)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Core.AbilityRegistry.ClearSlot(steamId, slot);
        Core.Persistence.RequestSave();
        ctx.Reply($"Slot {slot} cleared. Swap a weapon to apply.");
        Core.Chat.SendEvent(ctx.Event.SenderCharacterEntity,
            $"[BEELZ:event] type=slot-cleared slot={slot}");
    }

    [Command("verbosity", description: "Set your in-chat notification level: silent | summary | verbose.")]
    public static void SetVerbosity(ChatCommandContext ctx, string level)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var v = ChatNotifier.ParseDefault(level);
        if (!string.Equals(level?.Trim(), v.ToString(), System.StringComparison.OrdinalIgnoreCase) &&
            !int.TryParse(level, out _))
        {
            ctx.Reply($"Unknown level '{level}'. Valid: silent | summary | verbose. Defaulted to '{v}'.");
        }
        Core.AbilityRegistry.SetVerbosity(steamId, v);
        Core.Persistence.RequestSave();
        ctx.Reply($"Chat verbosity set to {v}.");
    }

    [Command("forget", description: "Delete one captured ability by index. Usage: .beelz forget <index>")]
    public static void Forget(ChatCommandContext ctx, int index)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var captured = Core.AbilityRegistry.ListFor(steamId);
        if (index < 0 || index >= captured.Count)
        {
            ctx.Reply($"Index {index} out of range (valid: 0-{captured.Count - 1}). Use .beelz list to see indices.");
            return;
        }
        var entry = captured[index];
        bool ok = Core.AbilityRegistry.Forget(steamId, entry.UnitPrefabGuid, entry.AbilityPrefabGuid);
        if (ok)
        {
            Core.Persistence.RequestSave();
            ctx.Reply($"Forgot {new Stunlock.Core.PrefabGUID(entry.AbilityPrefabGuid).GetPrefabName()} (from {new Stunlock.Core.PrefabGUID(entry.UnitPrefabGuid).GetPrefabName()}).");
        }
        else
        {
            ctx.Reply("Could not forget that entry (already removed?).");
        }
    }

    [Command("forget-transform", description: "Delete one transform unlock by index. Usage: .beelz forget-transform <index>")]
    public static void ForgetTransform(ChatCommandContext ctx, int index)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var unlocks = Core.AbilityRegistry.ListTransforms(steamId);
        if (index < 0 || index >= unlocks.Count)
        {
            ctx.Reply($"Index {index} out of range (valid: 0-{unlocks.Count - 1}). Use .beelz transforms to see indices.");
            return;
        }
        var entry = unlocks[index];
        bool ok = Core.AbilityRegistry.ForgetTransform(steamId, entry.UnitPrefabGuid);
        if (ok)
        {
            Core.Persistence.RequestSave();
            ctx.Reply($"Forgot transform unlock: {new Stunlock.Core.PrefabGUID(entry.UnitPrefabGuid).GetPrefabName()}.");
        }
        else
        {
            ctx.Reply("Could not forget that transform unlock.");
        }
    }

    [Command("clear", description: "Forget all captured abilities and slot assignments. Cannot be undone.")]
    public static void Clear(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        if (Core.AbilityRegistry.Clear(steamId))
        {
            Core.Persistence.RequestSave();
            ctx.Reply("Captured abilities and slot assignments cleared.");
        }
        else
        {
            ctx.Reply("You had no captured abilities.");
        }
    }
}
