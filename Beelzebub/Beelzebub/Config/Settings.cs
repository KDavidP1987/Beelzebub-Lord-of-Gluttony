using BepInEx.Configuration;

namespace Beelzebub.Config;

internal static class Settings
{
    public static ConfigEntry<bool> CaptureOnKill { get; private set; }
    public static ConfigEntry<bool> ExcludeIdleAbilities { get; private set; }
    public static ConfigEntry<bool> ExcludeFleeAbilities { get; private set; }
    public static ConfigEntry<bool> ExcludeHardVariants { get; private set; }
    public static ConfigEntry<string> ExtraDenyPatterns { get; private set; }
    public static ConfigEntry<string> ExtraAllowPatterns { get; private set; }
    public static ConfigEntry<bool> VerboseLogging { get; private set; }

    public static void Initialize(ConfigFile config)
    {
        CaptureOnKill = config.Bind(
            "Capture", nameof(CaptureOnKill), true,
            "Master switch. When false, no abilities are captured from kills.");

        ExcludeIdleAbilities = config.Bind(
            "Capture.Filter", nameof(ExcludeIdleAbilities), true,
            "Skip ability prefabs whose name contains '_Idle_' (pushups, situps, etc.).");

        ExcludeFleeAbilities = config.Bind(
            "Capture.Filter", nameof(ExcludeFleeAbilities), true,
            "Skip ability prefabs whose name contains '_Flee_'.");

        ExcludeHardVariants = config.Bind(
            "Capture.Filter", nameof(ExcludeHardVariants), true,
            "Skip V-Blood Brutal-difficulty ability duplicates whose name contains '_Hard_'.");

        ExtraDenyPatterns = config.Bind(
            "Capture.Filter", nameof(ExtraDenyPatterns), "",
            "Comma-separated case-insensitive substrings. Any ability whose name contains one is excluded.");

        ExtraAllowPatterns = config.Bind(
            "Capture.Filter", nameof(ExtraAllowPatterns), "",
            "If set, ONLY abilities whose name contains one of these comma-separated substrings are captured. Empty = allow all (subject to deny rules).");

        VerboseLogging = config.Bind(
            "Diagnostics", nameof(VerboseLogging), false,
            "Log every captured ability and filter decision.");
    }

    public static string[] SplitPatterns(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return System.Array.Empty<string>();
        var parts = raw.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        return parts;
    }
}
