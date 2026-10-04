namespace Beelzebub.Logic;

/// <summary>
/// v0.137.5 (transform-chain-guard): the routes that apply or re-apply a transform's form/carrier. Every one of them
/// destroys the current form buff and applies a new one, so each asks <see cref="TransformGate.Decide"/> first.
/// </summary>
public enum TransformRoute
{
    /// <summary><c>TransformService.TryActivate</c> — <c>.beelz transform</c> and admin <c>force-transform</c>.</summary>
    Activate,
    /// <summary><c>TransformService.ApplyPhase</c> — <c>.beelz phase</c>, the Auto-HP tick, the combat-end reset.</summary>
    PhaseSwitch,
    /// <summary><c>TransformService.ReapplyActiveTransform</c> — refresh, travel-end, login, leaving a sub-form.</summary>
    Reapply,
    /// <summary><c>TransformService.ApplyNativeFormTest</c> — admin <c>testform</c>.</summary>
    FormTest,
}

public enum TransformGateVerdict { Allow, RefusePending, RefuseSameUnit, RefuseActive, RefuseInactive, RefuseUnknown }

/// <summary>
/// v0.137.5: the one place the transform chain rules live (pure; tested in Beelzebub.Tests/TransformGateTests.cs).
/// Chaining a transform into another without a revert — or re-applying a form whose async buff has not spawned yet —
/// is what left creature kits stuck on the bar (v0.120) and destroyed a form buff twice inside Burst (v0.49).
/// </summary>
public static class TransformGate
{
    /// <param name="pendingForm">the player's async (LifeTime-less) form buff is still waiting to spawn</param>
    /// <param name="activeUnit">the active transform's unit GUID, 0 when not transformed</param>
    /// <param name="requestedUnit">the unit an Activate asks for (ignored by the other routes)</param>
    public static TransformGateVerdict Decide(TransformRoute route, bool pendingForm, int activeUnit, int requestedUnit)
    {
        bool active = activeUnit != 0;
        switch (route)
        {
            case TransformRoute.Activate:
                if (pendingForm) return TransformGateVerdict.RefusePending;
                if (active) return activeUnit == requestedUnit ? TransformGateVerdict.RefuseSameUnit : TransformGateVerdict.RefuseActive;
                return TransformGateVerdict.Allow;
            case TransformRoute.FormTest:
                if (pendingForm) return TransformGateVerdict.RefusePending;
                return active ? TransformGateVerdict.RefuseActive : TransformGateVerdict.Allow;
            case TransformRoute.PhaseSwitch:
            case TransformRoute.Reapply:
                if (pendingForm) return TransformGateVerdict.RefusePending;
                return active ? TransformGateVerdict.Allow : TransformGateVerdict.RefuseInactive;
            default:
                return TransformGateVerdict.RefuseUnknown;
        }
    }

    public const int MaxNameChars = 64;

    /// <summary>
    /// The unit name as it goes into a reply: control characters and rich-text brackets (<c>&lt;</c>, <c>&gt;</c>)
    /// dropped so it stays one plain chat line, cut to <see cref="MaxNameChars"/>; null/blank → "another unit".
    /// </summary>
    public static string SafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "another unit";
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
            if (!char.IsControl(c) && c != '<' && c != '>') sb.Append(c);
        string s = sb.ToString().Trim();
        if (s.Length == 0) return "another unit";
        return s.Length <= MaxNameChars ? s : s.Substring(0, MaxNameChars) + "…";
    }

    /// <summary>The chat reply for a refusal; null for <see cref="TransformGateVerdict.Allow"/>.</summary>
    public static string Message(TransformGateVerdict verdict, TransformRoute route, string activeName) => verdict switch
    {
        TransformGateVerdict.Allow => null,
        TransformGateVerdict.RefusePending => "Still transforming — give it a moment, then try again.",
        TransformGateVerdict.RefuseSameUnit => "You're already transformed as that unit. Use .beelz revert to return to normal.",
        TransformGateVerdict.RefuseActive => route == TransformRoute.FormTest
            ? $"You're already transformed as {SafeName(activeName)}. Use .beelz revert first, then run testform again."
            : $"You're already transformed as {SafeName(activeName)}. Use .beelz revert first, then transform again.",
        TransformGateVerdict.RefuseInactive => "You are not currently transformed. Use .beelz transform <unit> first.",
        _ => "That transform action is not allowed.",
    };

    /// <summary>The <c>[Beelz TXGUARD]</c> server-log line for a refusal; null for Allow.</summary>
    public static string LogLine(TransformRoute route, TransformGateVerdict verdict, ulong steamId, int unit)
    {
        if (verdict == TransformGateVerdict.Allow) return null;
        string reason = verdict switch
        {
            TransformGateVerdict.RefusePending => "Pending",
            TransformGateVerdict.RefuseSameUnit => "SameUnit",
            TransformGateVerdict.RefuseActive => "Active",
            TransformGateVerdict.RefuseInactive => "Inactive",
            _ => "Unknown",
        };
        return $"[Beelz TXGUARD] refused route={route} reason={reason} steamId={steamId} unit={unit}";
    }

    /// <summary>
    /// v0.137.5 (A2): is the combat-end reset to phase 1 still owed? The reset runs once, when combat ends; the gate
    /// can refuse it while a phase's async form is spawning, so the Auto-HP tick re-tries it while this is true.
    /// A flag rather than "out of combat ⇒ phase 1": Auto mode still lets the player pick a phase by hand.
    /// </summary>
    public static bool PhaseResetDue(bool resetDue, bool inCombat, int currentPhase) =>
        resetDue && !inCombat && currentPhase > 1;
}
