using BepInEx.Configuration;

namespace Beelzebub.Config;

internal static class Settings
{
    // Capture loop
    public static ConfigEntry<bool> CaptureOnKill { get; private set; }

    // Drop chances (Phase 4)
    public static ConfigEntry<float> DropChance_Ability_Regular { get; private set; }
    public static ConfigEntry<float> DropChance_Ability_VBlood { get; private set; }
    public static ConfigEntry<float> DropChance_Transform_Regular { get; private set; }
    public static ConfigEntry<float> DropChance_Transform_VBlood { get; private set; }

    // Notifications (Phase 3)
    public static ConfigEntry<string> DefaultVerbosity { get; private set; }

    // Diagnostics
    public static ConfigEntry<bool> VerboseLogging { get; private set; }

    public static void Initialize(ConfigFile config)
    {
        CaptureOnKill = config.Bind(
            "Capture", nameof(CaptureOnKill), true,
            "Master switch. When false, no abilities are captured from kills.");

        DropChance_Ability_Regular = config.Bind(
            "Capture.DropChance", nameof(DropChance_Ability_Regular), 0.05f,
            "Per-ability chance (0.0-1.0) to capture each eligible ability from a regular mob kill. 1.0 = always (legacy behavior).");

        DropChance_Ability_VBlood = config.Bind(
            "Capture.DropChance", nameof(DropChance_Ability_VBlood), 0.05f,
            "Per-ability chance (0.0-1.0) to capture each eligible ability from a V-Blood kill.");

        DropChance_Transform_Regular = config.Bind(
            "Capture.DropChance", nameof(DropChance_Transform_Regular), 0.01f,
            "Per-kill chance (0.0-1.0) to unlock the transform-into-unit form from a regular mob (Phase 5).");

        DropChance_Transform_VBlood = config.Bind(
            "Capture.DropChance", nameof(DropChance_Transform_VBlood), 0.01f,
            "Per-kill chance (0.0-1.0) to unlock the transform-into-unit form from a V-Blood (Phase 5).");

        DefaultVerbosity = config.Bind(
            "Notifications", nameof(DefaultVerbosity), "Summary",
            "Default chat verbosity for new players: Silent | Summary | Verbose. Each player can override with .beelz verbosity <level>.");

        VerboseLogging = config.Bind(
            "Diagnostics", nameof(VerboseLogging), false,
            "Log every captured ability and filter decision to BepInEx\\LogOutput.log. Independent of in-chat verbosity.");
    }
}
