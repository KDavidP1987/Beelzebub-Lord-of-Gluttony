using System;

namespace Beelzebub.Services;

internal sealed class AbilityFilter
{
    // v0.50.0: hardcoded "never a usable ability" name fragments — animation / lifecycle /
    // debug stubs, not castable abilities. These are rejected even in Capture_InclusiveMode
    // (which otherwise bypasses the admin deny lists + difficulty gate), so the inclusive
    // capture/devour pool fills with REAL abilities instead of no-op stubs.
    static readonly string[] _alwaysJunk =
    {
        "_Idle_", "_Flee_", "_Sequence_", "_Spawn_", "_Despawn_", "_Disappear_",
        "_Death_", "_Wounded_", "_Test_", "_Internal_", "_DEBUG_",
    };

    /// <summary>
    /// v0.100.0: true if the name is a non-ability stub (idle/spawn/death/test/etc.), independent of
    /// enable/deny/difficulty/inclusive rules. Used by the admin "all abilities" catalog scope to include
    /// every REAL ability group an admin might configure — even disabled/denied ones.
    /// </summary>
    public bool IsJunkAbility(string abilityName) => MatchesAlwaysJunk(abilityName);

    static bool MatchesAlwaysJunk(string abilityName)
    {
        foreach (var j in _alwaysJunk)
            if (abilityName.Contains(j, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // v0.120.0: name fragments identifying a TRANSFORM / shapeshift TRIGGER ability — the thing that
    // turns the caster INTO a form (AB_Shapeshift_Wolf_Group, AB_Shapeshift_Bat_Group, the boss
    // exoform shifts, …). These are RESERVED for the transformation-unlock system (.beelz transform)
    // and must never be capturable/devourable as normal action-bar abilities — the form is kept
    // separate from the player's captured kit by design. A boss's normal COMBAT abilities
    // (AB_Vampire_Dracula_*, AB_Blackfang_Morgana_*, …) do NOT contain "_Shapeshift_", so they remain
    // fully capturable; only the form trigger itself is blocked. NOT folded into MatchesAlwaysJunk so
    // the admin "all abilities" catalog still lists them.
    // v0.126.0: also reserve the boss "transform-INTO-a-form" triggers that do NOT use the _Shapeshift_
    // token — the Geomancer's AB_Geomancer_Transform_ToGolem / _ToHuman group abilities. These are the
    // "become the form" action, not a usable combat ability, so they must never be captured OR offered in
    // a transform's bindable kit (FullEligibleKit routes through ShouldCapture) — otherwise a player could
    // bind "Transform To Golem" into a transform phase slot. "_Transform_To" is specific enough not to
    // catch normal combat abilities (only the form-change triggers match it).
    static readonly string[] _transformTriggers = { "_Shapeshift_", "_Transform_To" };

    static bool MatchesTransformTrigger(string abilityName)
    {
        foreach (var t in _transformTriggers)
            if (abilityName.Contains(t, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public bool ShouldCapture(string abilityName, out string reason) => ShouldCapture(abilityName, 0, out reason);

    public bool ShouldCapture(string abilityName, int abilityGuid, out string reason)
    {
        if (string.IsNullOrEmpty(abilityName))
        {
            reason = "empty name";
            return false;
        }

        var rules = Core.AbilityRules.Current;

        // An explicit admin allow-list is a deliberate whitelist — honored even in inclusive
        // mode (it's MORE restrictive on purpose, not the over-denying we're relaxing).
        if (rules.AllowGuids.Count > 0)
        {
            if (!rules.AllowGuids.Contains(abilityGuid))
            {
                reason = "not in allow-guid list";
                return false;
            }
        }
        else if (rules.AllowPatterns.Count > 0)
        {
            bool anyAllow = false;
            foreach (var pat in rules.AllowPatterns)
            {
                if (abilityName.Contains(pat, StringComparison.OrdinalIgnoreCase)) { anyAllow = true; break; }
            }
            if (!anyAllow)
            {
                reason = "not in allow-pattern list";
                return false;
            }
        }

        // v0.120.0: transform / shapeshift TRIGGER abilities are reserved for the transformation
        // system — never captured/devoured onto the action bar (the form comes from the transform-unlock
        // path, kept separate from the captured kit). Enforced in BOTH inclusive and curated modes. An
        // explicit admin allow-list (AllowGuids/AllowPatterns above, already passed if we're here) or a
        // per-ability allow-denied override still wins, for the rare admin who wants one capturable.
        if (MatchesTransformTrigger(abilityName)
            && rules.AllowGuids.Count == 0 && rules.AllowPatterns.Count == 0
            && !Core.AbilityRules.IsAllowDenied(abilityName, abilityGuid))
        {
            reason = "transformation/shapeshift trigger (reserved for the transform system)";
            return false;
        }

        // v0.50.0 INCLUSIVE TESTING MODE (Capture_InclusiveMode, default on for this alpha):
        // make abilities across all V-Bloods/NPCs broadly capturable + devourable by BYPASSING
        // the admin DenyPatterns/DenyGuids and the Basic/Brutal difficulty gate. Only the junk
        // stubs above and the per-ability Enabled kill-switch (below) still reject. Admins who
        // want curated capture turn this OFF and the full deny/difficulty pipeline returns.
        bool inclusive = Beelzebub.Config.Settings.Capture_InclusiveMode.Value;
        if (inclusive)
        {
            if (MatchesAlwaysJunk(abilityName))
            {
                reason = "non-ability stub (idle/spawn/death/etc.)";
                return false;
            }
        }
        else
        {
            // v0.27.2: per-ability force-allow lets a curated phase ability bypass the
            // deny lists when its name legitimately matches a deny-pattern (e.g. Solarus's
            // "_Hard_" fallen-angel logic-gate abilities). The Enabled kill-switch below
            // still applies, so admins can still disable a force-allowed ability.
            bool forceAllow = Core.AbilityRules.IsAllowDenied(abilityName, abilityGuid);

            if (!forceAllow && rules.DenyGuids.Contains(abilityGuid))
            {
                reason = "matches deny-guid";
                return false;
            }

            if (!forceAllow)
            {
                foreach (var pat in rules.DenyPatterns)
                {
                    if (abilityName.Contains(pat, StringComparison.OrdinalIgnoreCase))
                    {
                        reason = $"matches deny pattern '{pat}'";
                        return false;
                    }
                }
            }
        }

        // Per-ability admin kill-switch from AbilityMap — ALWAYS enforced (the hard "off" lever
        // even in inclusive mode).
        if (!Core.AbilityRules.IsEnabled(abilityName, abilityGuid))
        {
            reason = "AbilityMap entry has Enabled=false";
            return false;
        }

        // TX4: Brutal-only abilities can't be captured on a Basic server — gate skipped in
        // inclusive mode so Brutal-tier kits are testable too.
        if (!inclusive)
        {
            string abilityDifficulty = Core.AbilityRules.GetAbilityDifficulty(abilityName);
            string serverMode = AbilityRules.GetServerDifficulty();
            if (!AbilityRules.IsDifficultyAllowed(abilityDifficulty, serverMode))
            {
                reason = $"ability difficulty {abilityDifficulty} not allowed on {serverMode} server";
                return false;
            }
        }

        reason = null;
        return true;
    }
}
