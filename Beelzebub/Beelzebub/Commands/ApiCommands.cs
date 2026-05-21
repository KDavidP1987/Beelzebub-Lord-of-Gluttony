using System.Linq;
using System.Text;
using Beelzebub.Services;
using Stunlock.Core;
using VampireCommandFramework;

namespace Beelzebub.Commands;

/// <summary>
/// Structured chat output for client UI integration (BCH / BloodCraftHub).
///
/// Wire format: every line begins with a "[BEELZ:&lt;tag&gt;]" marker, followed by
/// space-separated "key=value" fields. Values are bare tokens (no quoting); prefab
/// names are guaranteed [A-Za-z0-9_] only by V Rising's naming conventions.
///
/// Lists are streamed line-by-line and terminated with "[BEELZ:end] cmd=&lt;name&gt; count=&lt;n&gt;".
/// Errors use "[BEELZ:err] cmd=&lt;name&gt; code=&lt;code&gt; msg=&lt;text&gt;".
///
/// Current API version is 1.
/// </summary>
[CommandGroup("beelz api")]
internal static class ApiCommands
{
    const int ApiVersion = 1;

    [Command("version", description: "Return the Beelzebub API version (BCH-readable).")]
    public static void Version(ChatCommandContext ctx)
    {
        ctx.Reply($"[BEELZ:version] api={ApiVersion} plugin={MyPluginInfo.PLUGIN_VERSION} ready={(Core.IsReady ? 1 : 0)}");
    }

    [Command("list", description: "Stream the caller's captured abilities (BCH-readable).")]
    public static void List(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=list code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var captured = Core.AbilityRegistry.ListFor(steamId);
        for (int i = 0; i < captured.Count; i++)
        {
            var c = captured[i];
            ctx.Reply(
                $"[BEELZ:list] i={i} s={(c.Source == CaptureSource.VBlood ? "V" : "R")}" +
                $" u={c.UnitPrefabGuid} un={new PrefabGUID(c.UnitPrefabGuid).GetPrefabName()}" +
                $" a={c.AbilityPrefabGuid} an={new PrefabGUID(c.AbilityPrefabGuid).GetPrefabName()}");
        }
        ctx.Reply($"[BEELZ:end] cmd=list count={captured.Count}");
    }

    [Command("slots", description: "Stream the caller's current slot assignments (BCH-readable).")]
    public static void Slots(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=slots code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var slots = Core.AbilityRegistry.GetSlots(steamId);
        int n = 0;
        foreach (var (slot, abilityGuid) in slots.OrderBy(kv => kv.Key))
        {
            ctx.Reply(
                $"[BEELZ:slot] slot={slot}" +
                $" a={abilityGuid} an={new PrefabGUID(abilityGuid).GetPrefabName()}");
            n++;
        }
        ctx.Reply($"[BEELZ:end] cmd=slots count={n}");
    }

    [Command("transforms", description: "Stream the caller's transform unlocks (BCH-readable).")]
    public static void Transforms(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=transforms code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var unlocks = Core.AbilityRegistry.ListTransforms(steamId);
        for (int i = 0; i < unlocks.Count; i++)
        {
            var u = unlocks[i];
            ctx.Reply(
                $"[BEELZ:tx] i={i} s={(u.Source == CaptureSource.VBlood ? "V" : "R")}" +
                $" u={u.UnitPrefabGuid} un={new PrefabGUID(u.UnitPrefabGuid).GetPrefabName()}");
        }
        ctx.Reply($"[BEELZ:end] cmd=transforms count={unlocks.Count}");
    }

    [Command("active", description: "Return the caller's active transform, if any (BCH-readable).")]
    public static void Active(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=active code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active is null) { ctx.Reply("[BEELZ:active] none=1"); return; }

        string ttl;
        if (active.Duration.HasValue)
        {
            var remaining = active.Duration.Value.TotalSeconds - (System.DateTime.UtcNow - active.ActivatedAtUtc).TotalSeconds;
            ttl = remaining > 0 ? remaining.ToString("F0") : "0";
        }
        else
        {
            ttl = "toggle";
        }
        ctx.Reply(
            $"[BEELZ:active] u={active.UnitPrefabGuid}" +
            $" un={new PrefabGUID(active.UnitPrefabGuid).GetPrefabName()}" +
            $" s={(active.Source == CaptureSource.VBlood ? "V" : "R")} ttl={ttl}");
    }

    [Command("info", description: "Return detailed info for one captured ability by index (BCH tooltip data).")]
    public static void Info(ChatCommandContext ctx, int index)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=info code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var captured = Core.AbilityRegistry.ListFor(steamId);
        if (index < 0 || index >= captured.Count)
        {
            ctx.Reply($"[BEELZ:err] cmd=info code=out_of_range msg=index_{index}_of_{captured.Count}");
            return;
        }
        var c = captured[index];
        // Description currently empty — V Rising LocalizationManager wiring is a follow-up.
        ctx.Reply(
            $"[BEELZ:info] i={index} s={(c.Source == CaptureSource.VBlood ? "V" : "R")}" +
            $" u={c.UnitPrefabGuid} un={new PrefabGUID(c.UnitPrefabGuid).GetPrefabName()}" +
            $" a={c.AbilityPrefabGuid} an={new PrefabGUID(c.AbilityPrefabGuid).GetPrefabName()}" +
            " desc=");
    }

    [Command("verbosity", description: "Return the caller's current verbosity setting (BCH-readable).")]
    public static void Verbosity(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=verbosity code=not_ready msg=plugin_not_initialized"); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var v = Core.Chat.ResolveVerbosity(steamId);
        ctx.Reply($"[BEELZ:verbosity] level={v} default={Beelzebub.Config.Settings.DefaultVerbosity.Value}");
    }

    [Command("rules", description: "Stream the loaded ability rules (BCH-readable, admin-relevant).")]
    public static void Rules(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=rules code=not_ready msg=plugin_not_initialized"); return; }
        var r = Core.AbilityRules.Current;
        var sb = new StringBuilder();
        sb.Append("[BEELZ:rules] version=").Append(r.Version);
        sb.Append(" deny_patterns=").Append(string.Join(",", r.DenyPatterns));
        sb.Append(" allow_patterns=").Append(string.Join(",", r.AllowPatterns));
        sb.Append(" deny_guids=").Append(r.DenyGuids.Count);
        sb.Append(" allow_guids=").Append(r.AllowGuids.Count);
        ctx.Reply(sb.ToString());
    }

    [Command("transform-config", description: "Return current transform mode/duration/cooldown per source (BCH-readable).")]
    public static void TransformConfig(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("[BEELZ:err] cmd=transform-config code=not_ready msg=plugin_not_initialized"); return; }
        ctx.Reply(
            $"[BEELZ:tx-config] src=R mode={Beelzebub.Config.Settings.Transform_Mode_Regular.Value}" +
            $" duration={Beelzebub.Config.Settings.Transform_DurationSeconds_Regular.Value}" +
            $" cooldown={Beelzebub.Config.Settings.Transform_CooldownSeconds_Regular.Value}");
        ctx.Reply(
            $"[BEELZ:tx-config] src=V mode={Beelzebub.Config.Settings.Transform_Mode_VBlood.Value}" +
            $" duration={Beelzebub.Config.Settings.Transform_DurationSeconds_VBlood.Value}" +
            $" cooldown={Beelzebub.Config.Settings.Transform_CooldownSeconds_VBlood.Value}");
        ctx.Reply("[BEELZ:end] cmd=transform-config count=2");
    }
}
