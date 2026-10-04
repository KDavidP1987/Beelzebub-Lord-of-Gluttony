using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Beelzebub.Logic;

// v0.137.0 (bar-reset D16) — PURE, fixed-schema log lines for the reset and the bar diagnostic. Only the
// enumerated fields are printed (no secrets, no free text beyond a LogSafe'd name), and every line is ONE line:
// a crafted character name cannot forge a second log line. tools/check_bar_reset.py `session` parses these.

public static class LogSafe
{
    public const int MaxNameLength = 32;

    /// <summary>Removes control characters and square brackets, trims, caps at 32 characters; "?" when empty.</summary>
    public static string Field(string value)
    {
        if (string.IsNullOrEmpty(value)) return "?";
        var sb = new StringBuilder(Math.Min(value.Length, MaxNameLength));
        foreach (char c in value)
        {
            if (char.IsControl(c) || c == '[' || c == ']' || c == '\u2028' || c == '\u2029') continue;
            sb.Append(c);
            if (sb.Length >= MaxNameLength) break;
        }
        string s = sb.ToString().Trim();
        return s.Length == 0 ? "?" : s;
    }
}

public static class BarResetLog
{
    /// <summary>`[Beelz FORM] target=&lt;steamId&gt; form=&lt;name&gt; source=&lt;form|universal|captures|native&gt;` — logged
    /// AFTER the lock filter, so a bar the locks emptied reports `native`.</summary>
    public static string FormatForm(ulong steamId, string form, FormBarSource source, int barCount) =>
        $"[Beelz FORM] target={steamId} form={LogSafe.Field(form)} source={(barCount == 0 ? FormBarSource.Native : source).ToString().ToLowerInvariant()}";

    public const int SlowMs = 250;
    public const int MaxBarLineChars = 400;

    /// <summary>`[Beelz RESET] run=&lt;n&gt; scope=&lt;s&gt; target=&lt;name&gt; (&lt;steamId&gt;) ms=&lt;n&gt; steps=&lt;Step:count|Step:ERR&gt; ... survivors=&lt;slots|none|unreadable&gt; clean=&lt;0|1&gt;[ slow=1]`</summary>
    public static string Format(BarResetResult result, BarResetScope scope, string targetName, ulong steamId, long elapsedMs, int runId)
    {
        var sb = new StringBuilder("[Beelz RESET] ");
        sb.Append("run=").Append(runId)
          .Append(" scope=").Append(scope);
        if (scope == BarResetScope.ClearSet && result?.ClearSet != null) sb.Append(" set=").Append(LogSafe.Field(result.ClearSet.Label));
        sb
          .Append(" target=").Append(LogSafe.Field(targetName)).Append(" (").Append(steamId).Append(')')
          .Append(" ms=").Append(Math.Max(0, elapsedMs))
          .Append(" steps=");
        var steps = result?.Steps ?? new List<BarResetStepResult>();
        sb.Append(steps.Count == 0
            ? "none"
            : string.Join(",", steps.Select(s => s.Failed ? $"{s.Step}:ERR" : $"{s.Step}:{s.Count}")));
        sb.Append(" survivors=").Append(Survivors(result));
        // clean=1 only for a Clean result: survivors=none alone does not prove a failed save, a thrown step or an
        // offline (saved-state only) run were clean.
        sb.Append(" clean=").Append(result?.Clean == true ? 1 : 0);
        if (elapsedMs > SlowMs) sb.Append(" slow=1");
        return sb.ToString();
    }

    static string Survivors(BarResetResult result)
    {
        if (result == null || result.Unreadable) return "unreadable";
        var s = result.Survivors;
        return s.Count == 0 ? "none" : string.Join(",", s);
    }

    /// <summary>One `[Beelz BAR]` line per readback; above 400 characters the slots list continues on further lines
    /// ending ` part=k/n`, each with the same target prefix.</summary>
    public static List<string> FormatBar(BarReadback readback, string targetName, ulong steamId)
    {
        readback ??= new BarReadback();
        string other = readback.Unreadable ? "unreadable" : readback.Other.ToString();
        string head = $"[Beelz BAR] target={LogSafe.Field(targetName)} ({steamId}) binds={readback.Binds} rows={readback.Rows} gear={readback.Gear} other={other} slots=";

        var cells = readback.Slots
            .Where(s => s.Overridden || s.Gear > 0)
            .Select(s => $"{s.Slot}:{(s.Bind == "none" ? "none" : "bind")}:{s.Gear}:{(s.Unreadable ? "unreadable" : s.Other.ToString())}")
            .ToList();
        if (cells.Count == 0) return new List<string> { head + "none" };

        var chunks = new List<string>();
        var cur = new StringBuilder();
        int budget = Math.Max(40, MaxBarLineChars - head.Length - 12);
        foreach (var c in cells)
        {
            if (cur.Length > 0 && cur.Length + 1 + c.Length > budget) { chunks.Add(cur.ToString()); cur.Clear(); }
            if (cur.Length > 0) cur.Append(',');
            cur.Append(c);
        }
        chunks.Add(cur.ToString());
        if (chunks.Count == 1) return new List<string> { head + chunks[0] };
        return chunks.Select((c, i) => $"{head}{c} part={i + 1}/{chunks.Count}").ToList();
    }

    /// <summary>`[Beelz RESET] late-survivor target=&lt;name&gt; (&lt;steamId&gt;) run=&lt;runId&gt; slot=&lt;n&gt;` — a slot that became
    /// overridden one tick after the reset's readback (a cast already in flight).</summary>
    public static string FormatLate(string targetName, ulong steamId, int runId, int slot) =>
        $"[Beelz RESET] late-survivor target={LogSafe.Field(targetName)} ({steamId}) run={runId} slot={slot}";
}
