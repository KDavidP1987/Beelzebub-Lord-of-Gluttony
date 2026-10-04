using System.Collections.Generic;

namespace Beelzebub.Logic;

/// <summary>
/// v0.137.5 (transform-chain-guard D20): decides when a <c>[Beelz TXGUARD]</c> refusal is worth a log line. The Auto-HP
/// tick asks the gate on every pass, so a pending form that never spawns would otherwise write a line per tick. One
/// line per (player, route, reason) until that player's next allowed route (<see cref="Allowed"/>) or revert
/// (<see cref="Forget"/>). Bounded: at <see cref="Cap"/> entries the ledger is cleared, so a player who refuses once
/// and never comes back costs at most one entry until then, and the worst case after a clear is one repeated line.
/// </summary>
public sealed class TransformGuardLog
{
    public const int Cap = 512;
    readonly HashSet<(ulong, TransformRoute, TransformGateVerdict)> _logged = new();

    public int Count => _logged.Count;

    /// <summary>True the first time this refusal is seen since the player's last allow/forget (Allow never logs).</summary>
    public bool ShouldLog(ulong steamId, TransformRoute route, TransformGateVerdict verdict)
    {
        if (verdict == TransformGateVerdict.Allow) return false;
        var key = (steamId, route, verdict);
        if (_logged.Contains(key)) return false;
        if (_logged.Count >= Cap) _logged.Clear();
        _logged.Add(key);
        return true;
    }

    /// <summary>The gate allowed a route for this player: their next refusal logs again.</summary>
    public void Allowed(ulong steamId) => _logged.RemoveWhere(k => k.Item1 == steamId);

    /// <summary>The player's transform ended (revert, logout reconcile): drop their entries.</summary>
    public void Forget(ulong steamId) => _logged.RemoveWhere(k => k.Item1 == steamId);
}
