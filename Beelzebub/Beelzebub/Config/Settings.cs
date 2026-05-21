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

    // Transformation (Phase 5)
    public static ConfigEntry<string> Transform_Mode_Regular { get; private set; }
    public static ConfigEntry<string> Transform_Mode_VBlood { get; private set; }
    public static ConfigEntry<float> Transform_DurationSeconds_Regular { get; private set; }
    public static ConfigEntry<float> Transform_DurationSeconds_VBlood { get; private set; }
    public static ConfigEntry<float> Transform_CooldownSeconds_Regular { get; private set; }
    public static ConfigEntry<float> Transform_CooldownSeconds_VBlood { get; private set; }

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

        Transform_Mode_Regular = config.Bind(
            "Transformation", nameof(Transform_Mode_Regular), "Toggle",
            "Transformation mode for Regular-mob unlocks: Toggle | Timed | Disabled. Toggle = active until manual revert; Timed = auto-revert after duration; Disabled = transforms forbidden.");

        Transform_Mode_VBlood = config.Bind(
            "Transformation", nameof(Transform_Mode_VBlood), "Toggle",
            "Transformation mode for V-Blood unlocks: Toggle | Timed | Disabled.");

        Transform_DurationSeconds_Regular = config.Bind(
            "Transformation", nameof(Transform_DurationSeconds_Regular), 60f,
            "Auto-revert duration in seconds for Regular-mob transforms (when mode = Timed).");

        Transform_DurationSeconds_VBlood = config.Bind(
            "Transformation", nameof(Transform_DurationSeconds_VBlood), 60f,
            "Auto-revert duration in seconds for V-Blood transforms (when mode = Timed).");

        Transform_CooldownSeconds_Regular = config.Bind(
            "Transformation", nameof(Transform_CooldownSeconds_Regular), 0f,
            "Cooldown in seconds after a Regular-mob transform ends before another Regular transform can start. 0 = no cooldown.");

        Transform_CooldownSeconds_VBlood = config.Bind(
            "Transformation", nameof(Transform_CooldownSeconds_VBlood), 0f,
            "Cooldown in seconds after a V-Blood transform ends before another V-Blood transform can start. 0 = no cooldown.");

        VerboseLogging = config.Bind(
            "Diagnostics", nameof(VerboseLogging), false,
            "Log every captured ability and filter decision to BepInEx\\LogOutput.log. Independent of in-chat verbosity.");
    }
}
