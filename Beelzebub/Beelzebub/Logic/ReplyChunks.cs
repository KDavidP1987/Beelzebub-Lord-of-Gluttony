using System.Collections.Generic;
using System.Text;

namespace Beelzebub.Logic;

// v0.137.0 — PURE packing of a long "[BEELZ:<tag>]" API line into chat replies. VCF's ctx.Reply copies into a
// FixedString512Bytes and THROWS on overflow, so the budget is UTF-8 BYTES, never characters: the v0.136 tester
// notes carry multi-byte punctuation (an em dash is 3 bytes), and a 420-character chunk overflowed and aborted the
// whole `api catalog abilities` stream. Wire shape unchanged: `[BEELZ:<tag>] <id> part=k/n <tokens…>`.
public static class ReplyChunks
{
    /// <summary>Every emitted line stays at or under this many UTF-8 bytes (VCF's cap is 512).</summary>
    public const int MaxLineBytes = 500;

    /// <summary>The lines to send: the body's space-separated tokens packed in order, never split across lines. A
    /// single token too long for a line on its own is cut at a character boundary (the tail is dropped) rather than
    /// letting the reply throw.</summary>
    public static List<string> Pack(string tag, string idField, string body)
    {
        // the header grows with n; size it for the widest marker this line could need (part=999/999)
        int header = Bytes($"[BEELZ:{tag}] {idField} part=999/999 ");
        int budget = MaxLineBytes - header;
        var chunks = new List<string>();
        var sb = new StringBuilder();
        int used = 0;
        foreach (var raw in (body ?? "").Split(' '))
        {
            string t = Fit(raw, budget);
            int tb = Bytes(t);
            if (used > 0 && used + 1 + tb > budget) { chunks.Add(sb.ToString()); sb.Clear(); used = 0; }
            if (used > 0) { sb.Append(' '); used++; }
            sb.Append(t);
            used += tb;
        }
        if (sb.Length > 0 || chunks.Count == 0) chunks.Add(sb.ToString());
        int n = chunks.Count;
        var lines = new List<string>(n);
        for (int k = 0; k < n; k++) lines.Add($"[BEELZ:{tag}] {idField} part={k + 1}/{n} {chunks[k]}");
        return lines;
    }

    static int Bytes(string s) => Encoding.UTF8.GetByteCount(s);

    /// <summary><paramref name="t"/> cut to at most <paramref name="maxBytes"/> UTF-8 bytes, never splitting a
    /// surrogate pair.</summary>
    static string Fit(string t, int maxBytes)
    {
        if (Bytes(t) <= maxBytes) return t;
        int bytes = 0, i = 0;
        while (i < t.Length)
        {
            int len = char.IsSurrogatePair(t, i) ? 2 : 1;
            int b = Encoding.UTF8.GetByteCount(t.Substring(i, len));
            if (bytes + b > maxBytes) break;
            bytes += b;
            i += len;
        }
        return t.Substring(0, i);
    }
}
