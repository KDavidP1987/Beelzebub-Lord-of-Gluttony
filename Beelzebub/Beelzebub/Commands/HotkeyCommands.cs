using System.Linq;
using System.Text;
using Beelzebub.Services;
using Stunlock.Core;
using Unity.Entities;
using VampireCommandFramework;

namespace Beelzebub.Commands;

/// <summary>
/// W4: named hotkey bindings beyond V Rising's 6 ability slots. These are
/// BCH-facing: the server stores name → ability bindings; the BCH client
/// renders buttons in its UI and is responsible for triggering casts.
///
/// Until the BCH cast-trigger mechanism is finalized, the binding is
/// observable via `.beelz api hotkeys` but won't fire automatically. Players
/// can still inspect and manage their bindings here, and admins can gate the
/// feature entirely via the Hotkeys_Enabled config switch.
/// </summary>
[CommandGroup("beelz hotkey")]
internal static class HotkeyCommands
{
    [Command("set", description: "Bind a hotkey name to a captured ability by index. Usage: .beelz hotkey set <name> <index>")]
    public static void Set(ChatCommandContext ctx, string name, int index)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (!Beelzebub.Config.Settings.Hotkeys_Enabled.Value)
        {
            ctx.Reply("Hotkey bindings are disabled by the server admin.");
            return;
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            ctx.Reply("Hotkey name must not be empty.");
            return;
        }

        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var captured = Core.AbilityRegistry.ListFor(steamId);
        if (index < 0 || index >= captured.Count)
        {
            ctx.Reply($"Index {index} out of range (valid: 0-{captured.Count - 1}). Use .beelz list to see indices.");
            return;
        }

        int max = Beelzebub.Config.Settings.Hotkeys_MaxPerPlayer.Value;
        int existing = Core.AbilityRegistry.HotkeyCount(steamId);
        // Allow overwriting an existing name without bumping the count.
        bool isOverwrite = Core.AbilityRegistry.GetHotkey(steamId, name) != 0;
        if (!isOverwrite && existing >= max)
        {
            ctx.Reply($"Hotkey limit reached ({existing}/{max}). Clear one with .beelz hotkey clear <name>, or ask the server admin to raise Hotkeys_MaxPerPlayer.");
            return;
        }

        PrefabGUID ability = new(captured[index].AbilityPrefabGuid);
        string abilityName = ability.GetPrefabName();
        if (!Core.AbilityRules.IsEnabled(abilityName, ability._Value))
        {
            ctx.Reply($"'{abilityName}' is currently disabled by the server admin.");
            return;
        }
        if (Core.AbilityRules.IsTransformOnly(abilityName, ability._Value))
        {
            ctx.Reply($"'{abilityName}' is reserved for .beelz transform — cannot be bound to a hotkey.");
            return;
        }

        Core.AbilityRegistry.SetHotkey(steamId, name, ability._Value);
        Core.Persistence.RequestSave();
        ctx.Reply($"Hotkey '{name}' bound to {abilityName}. (BCH UI integration pending — server stores the binding for now.)");
        Core.Chat.SendEvent(ctx.Event.SenderCharacterEntity,
            $"[BEELZ:event] type=hotkey-set name={name} a={ability._Value} an={abilityName}");
    }

    [Command("clear", description: "Clear a hotkey binding by name. Usage: .beelz hotkey clear <name>")]
    public static void Clear(ChatCommandContext ctx, string name)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        bool removed = Core.AbilityRegistry.ClearHotkey(steamId, name);
        if (removed)
        {
            Core.Persistence.RequestSave();
            ctx.Reply($"Hotkey '{name}' cleared.");
            Core.Chat.SendEvent(ctx.Event.SenderCharacterEntity,
                $"[BEELZ:event] type=hotkey-cleared name={name}");
        }
        else
        {
            ctx.Reply($"No hotkey named '{name}'.");
        }
    }

    [Command("list", description: "List your hotkey bindings.")]
    public static void List(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var bindings = Core.AbilityRegistry.ListHotkeys(steamId);
        if (bindings.Count == 0)
        {
            ctx.Reply("No hotkey bindings. Use .beelz hotkey set <name> <index> to add one.");
            return;
        }

        int max = Beelzebub.Config.Settings.Hotkeys_MaxPerPlayer.Value;
        var sb = new StringBuilder();
        sb.Append("Hotkey bindings (").Append(bindings.Count).Append('/').Append(max).AppendLine("):");
        foreach (var (name, abilityGuid) in bindings.OrderBy(kv => kv.Key))
        {
            sb.Append("  ").Append(name).Append(" → ").AppendLine(new PrefabGUID(abilityGuid).GetPrefabName());
        }
        ctx.Reply(sb.ToString());
    }
}
