using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Beelzebub.Logic;

// v0.137.0 (bar-reset D6 D11 D24 D27) — PURE chat replies for the reset commands and `.beelz admin bar`. VCF's
// ctx.Reply caps at FixedString512Bytes and THROWS on overflow, so every line here is kept under MaxReplyBytes.
// Design › UX: a reset replies in at most two lines, slot numbers are written as the player sees them (primary,
// 1-6, ultimate), and no reply contains `<` or `>`.

public static class BarResetReply
{
    public const int MaxReplyBytes = 480;
    public const string NotReachable = "live bar not reachable — relog or respawn, then .beelz admin bar";
    public const string NotSaved = "not saved — binds may return after a restart, run it again";
    public const string CouldNotRead = "could not read slot mods — run .beelz admin bar";
    public const string OfflineBar = "offline: live bar resets on next login";
    public const string AskAdmin = "still stuck? ask an admin for .beelz admin bar";
    public const string DismountedSelf = "You were dismounted to reset your bar; remount to ride.";
    public const string TransformEndedSelf = "Your transform was ended to clear the bar.";
    const int MaxListed = 12;
    const int MaxPrefabName = 64;

    /// <summary>The reply for one reset: a headline, plus at most one line naming what went wrong and the next rung
    /// of the recovery ladder (RECOVERY_GUIDE). <paramref name="target"/> is the player's name, used in the admin
    /// commands the reply suggests (PlayerReset: the sender's own, never printed as a command).</summary>
    public static List<string> ForReset(BarResetResult r, BarResetScope scope, string target)
    {
        r ??= new BarResetResult();
        bool self = scope == BarResetScope.PlayerReset;
        string who = Name(target);
        string whose = self ? "Your" : $"{who}'s";
        int binds = r.CountOf(BarResetStep.ClearSavedBindings);
        string hot = BarResetPlanner.ClearsHotkeys(scope) ? $", {r.CountOf(BarResetStep.ClearHotkeys)} hotkey(s)" : "";

        var lines = new List<string>();
        if (!r.Online)
            lines.Add($"{whose} saved binds cleared ({binds} bind(s){hot}). Offline: the live bar resets on next login; if it is still stuck then, run this again while they are online.");
        else if (!r.LiveReady)
            lines.Add($"{whose} saved binds cleared ({binds} bind(s){hot}), but the {NotReachable}.");
        else if (r.Clean)
            lines.Add($"{whose} bar is back to vanilla: cleared {binds} bind(s){hot}, removed {r.CountOf(BarResetStep.ClearEquipEntries)} injected row(s), popped {r.CountOf(BarResetStep.PopSlotMods)} slot mod(s). Captures and unlocks kept.");
        else
            lines.Add($"{whose} bar reset ran but is NOT clean yet (cleared {binds} bind(s){hot}).");

        string ladder = scope switch
        {
            BarResetScope.PlayerReset => "ask an admin for .beelz admin bar",
            BarResetScope.Purge => $"if they stay after a relog: .beelz admin reset-character {who} CONFIRM-RESET",
            _ => $"run .beelz admin bar {who}; if they stay: .beelz admin purge {who} CONFIRM",
        };
        var problems = Problems(r, ladder);

        // mounted-bar-reset D5: the dismount is said on the headline, so a reset still replies in at most two lines
        if (r.CountOf(BarResetStep.Dismount) > 0 && lines.Count > 0)
            lines[0] += " " + (self ? DismountedSelf : $"{who} was dismounted to reset the bar.");

        if (problems.Count > 0) lines.Add(string.Join("; ", problems));
        else if (self && r.Online) lines.Add(AskAdmin);
        return lines.Select(Cap).ToList();
    }

    /// <summary>v0.137.4 (clearbar-fullreset D4) — the `.beelz clearbar` reply: the pre-0.137.4 headline (how many binds
    /// the chosen set held), a dismount / ended-transform suffix when those steps ran, and a second line only when
    /// something went wrong (the same problem texts as <see cref="ForReset"/>). <paramref name="what"/> names the set
    /// ("your Sword loadout").</summary>
    public static List<string> ForClear(BarResetResult r, string what)
    {
        r ??= new BarResetResult();
        int n = r.CountOf(BarResetStep.ClearSavedBindings);
        string head = n > 0
            ? $"Cleared {what} — {n} binding(s) removed. Captured abilities kept; re-grant anytime."
            : $"Nothing was bound in {what}.";
        if (r.CountOf(BarResetStep.Dismount) > 0) head += " " + DismountedSelf;
        if (r.CountOf(BarResetStep.RevertTransform) > 0) head += " " + TransformEndedSelf;

        var problems = Problems(r, "ask an admin for .beelz admin bar");
        if (r.Online && !r.LiveReady) problems.Insert(0, $"the {NotReachable}");
        var lines = new List<string> { head };
        if (problems.Count > 0) lines.Add(string.Join("; ", problems));
        return lines.Select(Cap).ToList();
    }

    /// <summary>Everything that kept a reset from being clean, in reply words; empty when nothing went wrong.</summary>
    static List<string> Problems(BarResetResult r, string ladder)
    {
        var problems = new List<string>();
        if (r.Steps.Any(s => s.Step == BarResetStep.SaveBindings) && !r.Saved) problems.Add(NotSaved);
        var failed = r.Steps.Where(s => s.Failed && s.Step != BarResetStep.SaveBindings)
            .Select(s => s.Step.ToString()).Distinct().ToList();
        if (failed.Count > 0) problems.Add($"failed: {string.Join(", ", failed)} (see [Beelz RESET] in the server log)");
        if (r.Online && r.LiveReady && r.Unreadable) problems.Add(CouldNotRead);
        if (r.Survivors.Count > 0)
            problems.Add($"still overridden: {Slots(r.Survivors)} — {ladder}");
        if (r.Online && r.LiveReady && r.OverrideBuffsLeft.Count > 0)
            problems.Add($"override buff still on: {string.Join(", ", r.OverrideBuffsLeft.Take(3).Select(b => Text(b, MaxPrefabName)))} — {ladder}");
        return problems;
    }

    /// <summary>`.beelz admin bar` lines: offline → the saved state; online → a header, override buffs, and one line per
    /// bar slot 0-8 (ability, saved bind, injected row, gear mods with their sources, other mods or `unreadable`).</summary>
    public static List<string> ForBar(BarReadback rb, string target)
    {
        rb ??= new BarReadback();
        var lines = new List<string>();
        string who = Name(target);
        if (rb.Offline)
        {
            lines.Add(OfflineBar);
            if (rb.SavedSets.Count == 0) lines.Add($"{who}: no saved binds");
            else lines.AddRange(rb.SavedSets.Select(set => $"{who} · {Text(set, MaxReplyBytes)}"));
            lines.Add($"transform: {Text(rb.Transform, MaxPrefabName)} · hotkeys={rb.Hotkeys}");
            return lines.Select(Cap).ToList();
        }

        lines.Add($"{who}: binds={rb.Binds} rows={rb.Rows} gear={rb.Gear} other={(rb.Unreadable ? "unreadable" : rb.Other.ToString())} · transform: {Text(rb.Transform, MaxPrefabName)}");
        lines.Add(rb.OverrideBuffs.Count == 0
            ? "override buffs: none"
            : $"override buffs: {string.Join(", ", rb.OverrideBuffs.Take(MaxListed).Select(b => Text(b, MaxPrefabName)))}");
        foreach (var s in rb.Slots.Where(s => s.Slot >= 0 && s.Slot <= 8).OrderBy(s => s.Slot))
        {
            string gear = s.Gear == 0 ? "0" : $"{s.Gear} ({string.Join(", ", s.GearSources.Take(3).Select(g => Text(g, MaxPrefabName)))})";
            string ability = string.IsNullOrEmpty(s.Ability) ? "?" : Text(s.Ability, MaxPrefabName);
            lines.Add($"{SlotLabel(s.Slot)}: {ability} · bind={Text(s.Bind, MaxPrefabName)} · row={(s.Row ? "yes" : "no")} · gear={gear} · other={(s.Unreadable ? "unreadable" : s.Other.ToString())}");
        }
        return lines.Select(Cap).ToList();
    }

    /// <summary>The slot as the player sees it: 0 = primary, 7 = ultimate, the rest by number.</summary>
    public static string SlotLabel(int slot) => slot switch
    {
        0 => "primary",
        7 => "ultimate",
        _ => $"slot {slot}",
    };

    static string Slots(List<int> slots) =>
        string.Join(", ", slots.Take(MaxListed).Select(SlotLabel)) + (slots.Count > MaxListed ? $", … (+{slots.Count - MaxListed})" : "");

    /// <summary>A player name for any reply: LogSafe (32 characters, no control characters or brackets) and no `<` `>`.</summary>
    public static string Name(string s) => LogSafe.Field(s).Replace("<", "").Replace(">", "");

    /// <summary>Free text (prefab names, set lists): control characters, `[` `]` `<` `>` removed, capped.</summary>
    static string Text(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "?";
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (char.IsControl(c) || c is '[' or ']' or '<' or '>' or '\u2028' or '\u2029') continue;
            sb.Append(c);
            if (sb.Length >= max) break;
        }
        string t = sb.ToString().Trim();
        return t.Length == 0 ? "?" : t;
    }

    /// <summary>Trims a line to MaxReplyBytes of UTF-8 (VCF throws above 512 bytes), never splitting a surrogate pair.</summary>
    public static string Cap(string line)
    {
        if (line == null) return "";
        if (Encoding.UTF8.GetByteCount(line) <= MaxReplyBytes) return line;
        int bytes = 0, i = 0;
        while (i < line.Length)
        {
            int len = char.IsSurrogatePair(line, i) ? 2 : 1;
            int n = Encoding.UTF8.GetByteCount(line.AsSpan(i, len));
            if (bytes + n > MaxReplyBytes - 3) break;
            bytes += n;
            i += len;
        }
        return line.Substring(0, i) + "...";
    }
}
