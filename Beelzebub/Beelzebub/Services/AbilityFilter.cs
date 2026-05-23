using System;

namespace Beelzebub.Services;

internal sealed class AbilityFilter
{
    public bool ShouldCapture(string abilityName, out string reason) => ShouldCapture(abilityName, 0, out reason);

    public bool ShouldCapture(string abilityName, int abilityGuid, out string reason)
    {
        if (string.IsNullOrEmpty(abilityName))
        {
            reason = "empty name";
            return false;
        }

        var rules = Core.AbilityRules.Current;

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

        // Per-ability admin kill-switch from AbilityMap.
        if (!Core.AbilityRules.IsEnabled(abilityName, abilityGuid))
        {
            reason = "AbilityMap entry has Enabled=false";
            return false;
        }

        // TX4: Brutal-only abilities can't be captured on a Basic server.
        string abilityDifficulty = Core.AbilityRules.GetAbilityDifficulty(abilityName);
        string serverMode = AbilityRules.GetServerDifficulty();
        if (!AbilityRules.IsDifficultyAllowed(abilityDifficulty, serverMode))
        {
            reason = $"ability difficulty {abilityDifficulty} not allowed on {serverMode} server";
            return false;
        }

        reason = null;
        return true;
    }
}
