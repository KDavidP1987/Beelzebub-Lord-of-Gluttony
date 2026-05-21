using System;
using Beelzebub.Config;

namespace Beelzebub.Services;

internal sealed class AbilityFilter
{
    public bool ShouldCapture(string abilityName, out string reason)
    {
        if (string.IsNullOrEmpty(abilityName))
        {
            reason = "empty name";
            return false;
        }

        var allow = Settings.SplitPatterns(Settings.ExtraAllowPatterns.Value);
        if (allow.Length > 0)
        {
            bool anyAllow = false;
            foreach (var pat in allow)
            {
                if (abilityName.Contains(pat, StringComparison.OrdinalIgnoreCase)) { anyAllow = true; break; }
            }
            if (!anyAllow)
            {
                reason = "not in allow-list";
                return false;
            }
        }

        if (Settings.ExcludeIdleAbilities.Value && abilityName.Contains("_Idle_", StringComparison.OrdinalIgnoreCase))
        {
            reason = "idle filler";
            return false;
        }

        if (Settings.ExcludeFleeAbilities.Value && abilityName.Contains("_Flee_", StringComparison.OrdinalIgnoreCase))
        {
            reason = "flee behavior";
            return false;
        }

        if (Settings.ExcludeHardVariants.Value && abilityName.Contains("_Hard_", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Brutal-difficulty variant";
            return false;
        }

        foreach (var pat in Settings.SplitPatterns(Settings.ExtraDenyPatterns.Value))
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
