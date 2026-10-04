using System;

namespace Beelzebub.Logic;

// v0.137.4 (clearbar-fullreset D2) — the ONE saved set `.beelz clearbar` clears. The bar readback names each slot's
// bind origin as "universal", "weapon:<Family>", "form:<Form>" or "none" (BarResetService.BindOrigins); a set KEEPS
// every origin except its own, so the reset can tell a bind the player still wants from a leftover.

public enum BarSetKind { All, Universal, Weapon, Form }

public sealed class BarSet
{
    public BarSetKind Kind { get; }
    /// <summary>The weapon family or form name for Weapon / Form sets; "" otherwise.</summary>
    public string Name { get; }

    BarSet(BarSetKind kind, string name)
    {
        Kind = kind;
        Name = name ?? "";
    }

    public static readonly BarSet All = new(BarSetKind.All, "");
    public static readonly BarSet Universal = new(BarSetKind.Universal, "");
    public static BarSet Weapon(string family) => new(BarSetKind.Weapon, family);
    public static BarSet Form(string form) => new(BarSetKind.Form, form);

    /// <summary>`all`, `universal`, `weapon:<Name>` or `form:<Name>` — the `set=` field of the reset log line.</summary>
    public string Label => Kind switch
    {
        BarSetKind.Universal => "universal",
        BarSetKind.Weapon => $"weapon:{Name}",
        BarSetKind.Form => $"form:{Name}",
        _ => "all",
    };

    /// <summary>True when a slot whose saved bind comes from <paramref name="bindOrigin"/> survives this clear on
    /// purpose. `all` keeps nothing; `none` (no bind) is never kept; names compare case-insensitively.</summary>
    public bool Keeps(string bindOrigin)
    {
        if (Kind == BarSetKind.All) return false;
        if (string.IsNullOrEmpty(bindOrigin) || string.Equals(bindOrigin, "none", StringComparison.OrdinalIgnoreCase)) return false;
        return !string.Equals(bindOrigin, Label, StringComparison.OrdinalIgnoreCase);
    }
}
