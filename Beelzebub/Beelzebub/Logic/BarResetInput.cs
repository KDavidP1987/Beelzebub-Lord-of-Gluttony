using System;

namespace Beelzebub.Logic;

// v0.137.0 (bar-reset D25) — the ONE input rule every reset command uses for its confirmation token.

public static class BarResetInput
{
    public const string ConfirmToken = "CONFIRM";

    /// <summary>Only CONFIRM (trimmed, case-insensitive) confirms a reset; missing or anything else changes nothing.</summary>
    public static bool IsConfirm(string token) =>
        token != null && string.Equals(token.Trim(), ConfirmToken, StringComparison.OrdinalIgnoreCase);
}
