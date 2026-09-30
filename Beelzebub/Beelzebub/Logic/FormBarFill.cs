using System;
using System.Collections.Generic;

namespace Beelzebub.Logic;

// v0.137.0 (bar-reset D4) — PURE form-bar precedence. Form binds > universal binds > (only when
// Forms_AutoFillFromCaptures) the first six usable captures packed from slot 1. The capture fallback used to be
// unconditional, so a reset that kept captures had the form bar fill itself right back ("it came back").

public enum FormBarSource
{
    /// <summary>Nothing injected — the form keeps its own kit.</summary>
    Native,
    Form,
    Universal,
    Captures,
}

public readonly record struct FormBarFillResult(Dictionary<int, int> Bar, FormBarSource Source);

public static class FormBarFill
{
    public const int MaxCaptureFill = 6;

    /// <param name="formBinds">(slot, ability) of the per-form set; empty when not in a known form.</param>
    /// <param name="universalBinds">(slot, ability) of the universal set.</param>
    /// <param name="captures">ability GUIDs of the player's captures, in list order.</param>
    /// <param name="usable">kill-switch + form-lock filter, applied on every path.</param>
    public static FormBarFillResult Build(
        IEnumerable<(int Slot, int Ability)> formBinds,
        IEnumerable<(int Slot, int Ability)> universalBinds,
        IEnumerable<int> captures,
        Func<int, bool> usable,
        bool autoFillFromCaptures)
    {
        usable ??= _ => true;
        var bar = new Dictionary<int, int>();

        foreach (var (slot, ability) in formBinds ?? Array.Empty<(int, int)>())
            if (ability != 0 && usable(ability)) bar[slot] = ability;
        if (bar.Count > 0) return new FormBarFillResult(bar, FormBarSource.Form);

        foreach (var (slot, ability) in universalBinds ?? Array.Empty<(int, int)>())
            if (ability != 0 && usable(ability)) bar[slot] = ability;
        if (bar.Count > 0) return new FormBarFillResult(bar, FormBarSource.Universal);

        if (autoFillFromCaptures)
        {
            int s = 1;
            foreach (int ability in captures ?? Array.Empty<int>())
            {
                if (ability == 0 || !usable(ability)) continue;
                bar[s++] = ability;
                if (bar.Count >= MaxCaptureFill) break;
            }
            if (bar.Count > 0) return new FormBarFillResult(bar, FormBarSource.Captures);
        }
        return new FormBarFillResult(bar, FormBarSource.Native);
    }
}
