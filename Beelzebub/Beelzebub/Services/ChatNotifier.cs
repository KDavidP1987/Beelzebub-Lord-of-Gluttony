using System;
using Beelzebub.Config;
using ProjectM;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

namespace Beelzebub.Services;

internal sealed class ChatNotifier
{
    public Verbosity ResolveVerbosity(ulong steamId)
    {
        Verbosity fallback = ParseDefault(Settings.DefaultVerbosity.Value);
        return Core.AbilityRegistry.GetVerbosity(steamId, fallback);
    }

    public void Send(Entity playerCharacter, Verbosity required, string message)
    {
        if (!playerCharacter.IsPlayer()) return;
        if (!playerCharacter.TryGetComponent<PlayerCharacter>(out var pc)) return;
        if (!pc.UserEntity.TryGetComponent<User>(out var user)) return;
        ulong steamId = user.PlatformId;

        Verbosity active = ResolveVerbosity(steamId);
        if (active < required) return;

        SendRaw(user, message);
    }

    /// <summary>
    /// Phase E4: emit a parseable [BEELZ:event] line for BCH consumption.
    /// Gated by the per-player EmitApiEvents flag — off by default.
    /// Independent of chat verbosity (a Silent player can still receive events
    /// if BCH has turned them on, and a Verbose player without BCH won't see them).
    /// </summary>
    public void SendEvent(Entity playerCharacter, string eventLine)
    {
        if (!playerCharacter.IsPlayer()) return;
        if (!playerCharacter.TryGetComponent<PlayerCharacter>(out var pc)) return;
        if (!pc.UserEntity.TryGetComponent<User>(out var user)) return;
        ulong steamId = user.PlatformId;

        if (!Core.AbilityRegistry.GetEmitApiEvents(steamId)) return;
        SendRaw(user, eventLine);
    }

    void SendRaw(User user, string message)
    {
        try
        {
            var fixedMessage = new FixedString512Bytes(SafeTruncate(message, 510));
            ServerChatUtils.SendSystemMessageToClient(Core.EntityManager, user, ref fixedMessage);
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"ChatNotifier.SendRaw failed: {ex}");
        }
    }

    static string SafeTruncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" :
        s.Length <= max ? s : s.Substring(0, max);

    public static Verbosity ParseDefault(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Verbosity.Summary;
        return raw.Trim().ToLowerInvariant() switch
        {
            "silent" or "0" => Verbosity.Silent,
            "verbose" or "2" => Verbosity.Verbose,
            _ => Verbosity.Summary,
        };
    }
}
