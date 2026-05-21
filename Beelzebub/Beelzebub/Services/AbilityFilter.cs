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

        if (rules.DenyGuids.Contains(abilityGuid))
        {
            reason = "matches deny-guid";
            return false;
        }

        foreach (var pat in rules.DenyPatterns)
        {
            if (abilityName.Contains(pat, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"matches deny pattern '{pat}'";
                return false;
            }
        }

        reason = null;
        return true;
    }
}
