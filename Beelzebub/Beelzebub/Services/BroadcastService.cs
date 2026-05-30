using System;
using System.Collections.Generic;
using Beelzebub.Config;

namespace Beelzebub.Services;

/// <summary>
/// v0.88.0 — server-wide announcements:
///   • collection-complete: when a player reaches 100% of the capturable catalog (fired from the
///     DeathEventListener milestone site), post a celebratory broadcast (default ON).
///   • periodic leaderboard: every N minutes, broadcast the top players by collection % (default OFF,
///     admin-toggleable). Driven by the heartbeat (<see cref="Tick"/>).
///
/// Both pull their text from a pipe ( | )-separated config pool and rotate through it, substituting
/// %player% / %top% / %count%. Messages go out via <see cref="ChatNotifier.Announce"/> (every online
/// player, verbosity-independent — it's a server announcement).
/// </summary>
internal static class BroadcastService
{
    static DateTime _lastLeaderboard = DateTime.MinValue;
    static int _completeRotation;
    static int _leaderboardRotation;

    /// <summary>Heartbeat hook (≤1/sec). Fires the periodic leaderboard when its interval elapses.</summary>
    public static void Tick()
    {
        if (!Core.IsReady) return;
        if (!Settings.Broadcast_Leaderboard_Enabled.Value) return;

        int mins = Math.Max(1, Settings.Broadcast_Leaderboard_IntervalMinutes.Value);
        var now = DateTime.UtcNow;
        // Don't fire on the very first tick after boot/enable — start the clock instead, so a freshly
        // started server (or a just-toggled-on broadcast) waits a full interval before announcing.
        if (_lastLeaderboard == DateTime.MinValue) { _lastLeaderboard = now; return; }
        if ((now - _lastLeaderboard).TotalMinutes < mins) return;
        _lastLeaderboard = now;

        try { BroadcastLeaderboard(); }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz] leaderboard broadcast failed: {ex.Message}"); }
    }

    /// <summary>Reset the leaderboard clock (e.g. when an admin toggles it on) so the next one is a full interval out.</summary>
    public static void ResetLeaderboardClock() => _lastLeaderboard = DateTime.UtcNow;

    /// <summary>v0.88.0: a player just hit 100% — announce it server-wide (if enabled).</summary>
    public static void OnCollectionComplete(string playerName)
    {
        if (!Settings.Broadcast_CollectionComplete_Enabled.Value) return;
        if (string.IsNullOrEmpty(playerName)) playerName = "A vampire";
        string tmpl = Pick(Settings.Broadcast_CollectionComplete_Messages.Value, ref _completeRotation,
            "🩸 %player% has devoured the entire bestiary — every ability is theirs.");
        Core.Chat.Announce(tmpl.Replace("%player%", playerName));
        Core.Log.LogInfo($"[Beelz] collection-complete broadcast for {playerName}.");
    }

    /// <summary>Build + send the leaderboard broadcast now (also used by the admin test command).</summary>
    public static void BroadcastLeaderboard()
    {
        var board = BuildBoard(out int total);
        if (board.Count == 0) return;

        int topN = Math.Clamp(Settings.Broadcast_Leaderboard_TopN.Value, 1, 5);
        int n = Math.Min(topN, board.Count);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(" · ");
            string medal = i == 0 ? "🥇" : i == 1 ? "🥈" : i == 2 ? "🥉" : $"#{i + 1}";
            float pct = total > 0 ? Math.Min(100f, board[i].count * 100f / total) : 0f;
            sb.Append($"{medal} {board[i].name} ({pct:F1}%)");
        }

        string tmpl = Pick(Settings.Broadcast_Leaderboard_Messages.Value, ref _leaderboardRotation,
            "🏆 Bestiary standings — %top%");
        string msg = tmpl.Replace("%top%", sb.ToString()).Replace("%count%", board.Count.ToString());
        Core.Chat.Announce(msg);
        Core.Log.LogInfo($"[Beelz] leaderboard broadcast ({n} of {board.Count} collectors).");
    }

    /// <summary>Top collectors (desc), excluding admins. <paramref name="total"/> = capturable universe denominator.</summary>
    static List<(string name, int count)> BuildBoard(out int total)
    {
        total = BestiaryService.TotalCapturableAbilities();
        if (total <= 0) total = Core.AbilityRules?.Current?.AbilityMap?.Count ?? 0;

        var board = new List<(string name, int count)>();
        foreach (var (sid, name, isAdmin) in EntityExtensions.AllUsers())
        {
            if (isAdmin) continue;
            int c = Core.AbilityRegistry.CapturedCount(sid);
            if (c > 0) board.Add((name, c));
        }
        board.Sort((a, b) => b.count.CompareTo(a.count));
        return board;
    }

    /// <summary>Pick the next message from a pipe-separated pool, rotating so it doesn't repeat the same one.</summary>
    static string Pick(string raw, ref int rotation, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var parts = raw.Split('|');
        var pool = new List<string>(parts.Length);
        foreach (var p in parts) { var t = p.Trim(); if (t.Length > 0) pool.Add(t); }
        if (pool.Count == 0) return fallback;
        string chosen = pool[rotation % pool.Count];
        rotation = (rotation + 1) % pool.Count;
        return chosen;
    }
}
