using BepInEx.Configuration;

namespace Beelzebub.Config;

internal static class Settings
{
    // Capture loop
    public static ConfigEntry<bool> CaptureOnKill { get; private set; }

    // Shared-kill credit (B2)
    public static ConfigEntry<string> Capture_ShareCreditMode { get; private set; }
    public static ConfigEntry<float> Capture_ShareCreditRadius { get; private set; }

    // Per-unit-tier rate multipliers (C3)
    public static ConfigEntry<int> Capture_TierMidThreshold { get; private set; }
    public static ConfigEntry<int> Capture_TierHighThreshold { get; private set; }
    public static ConfigEntry<float> Capture_TierMultiplier_Low { get; private set; }
    public static ConfigEntry<float> Capture_TierMultiplier_Mid { get; private set; }
    public static ConfigEntry<float> Capture_TierMultiplier_High { get; private set; }

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

    // W4: hotkey slots (BCH-facing)
    public static ConfigEntry<bool> Hotkeys_Enabled { get; private set; }
    public static ConfigEntry<int> Hotkeys_MaxPerPlayer { get; private set; }

    // TX4: server difficulty mode (Basic | Brutal) — admin specifies their server's mode.
    public static ConfigEntry<string> Server_DifficultyMode { get; private set; }

    // Z2 (v0.14.0): opt-in native shapeshift VFX on transforms. Native V Rising
    // shapeshift forms (Wolf/Bear/Rat/Spider/Toad) auto-exit when the player casts
    // any ability NOT in the form's baked-in moveset — which our captured abilities
    // always are. Default OFF so transforms behave consistently. Admins who want
    // the cosmetic on wolf-flavored transforms can enable knowing the form will
    // drop the moment any captured ability is cast.
    public static ConfigEntry<bool> Transform_NativeShapeshift_Enabled { get; private set; }

    // v0.33.0 (#17): when a captured unit corresponds to a V Rising form/shapeshift
    // buff (the boss forms Dracula/Morgana, or a native Wolf/Bear/Rat/Spider/Toad/
    // Werewolf/Gargoyle), transform via that REAL form buff (the persistent ExoForm
    // recipe) instead of the cosmetic-only path. This gives the player the form's
    // rig AND keeps the form across casts (unlike Transform_NativeShapeshift_Enabled,
    // which drops on first cast). Default true — units without a form still fall back
    // to the ability-only transform. Set false to disable real native-form transforms
    // server-wide (per-unit disable lives in the TransformMap).
    public static ConfigEntry<bool> Transform_RealFormWhenAvailable { get; private set; }

    // #4 (v0.34.0) animation fidelity: when ON, refuse a UNIVERSAL `.beelz grant`
    // of a weapon-animation-bound ability (e.g. a greatsword slam) and steer the
    // player to `.beelz weapon-grant <weapon>` so the ability is tied to the weapon
    // whose animation it needs. Default OFF — guidance is always shown either way;
    // this only hard-blocks the universal bind.
    public static ConfigEntry<bool> Grant_EnforceWeaponMatch { get; private set; }

    // TX7 (v0.17.0): how the runtime sources power scaling for a transform.
    //   CuratedScales (default) — apply admin-curated DamageScale/HealthScale/etc.
    //                             from the TransformMap entry as ModifyUnitStatBuff_DOTS
    //                             entries on the carrier buff. (Existing TX6 behavior.)
    //   PrefabAbsolute  — read the CHAR_ prefab's UnitStats component (PhysicalPower,
    //                     SpellPower, MaxHealth, etc.) and apply matching boss-tier
    //                     stats to the player. Ignores player progression — the player
    //                     hits as hard as the boss does. Use for "true boss form" servers.
    //   PlayerScaled    — apply no overrides. V Rising's natural damage formula scales
    //                     the captured ability's base damage by the player's own
    //                     PhysicalPower/SpellPower — including any Bloodcraft expertise
    //                     / blood-quality / level buffs. Naturally tracks player
    //                     progression and Bloodcraft prestige resets.
    // Per-transformation override: `PowerScalingMode` field on each TransformMap
    // entry can be set to one of the three values to override the global default
    // for that specific unit.
    public static ConfigEntry<string> Transform_PowerScalingMode { get; private set; }

    // TX7-extended (v0.19.0): max-level anchor for PlayerLeveled scaling. The
    // factor curve is `clamp(player.UnitLevel / this, 0, 1)`. Default 90
    // matches V Rising's typical max gear level (and Bloodcraft's usual leveling
    // cap). Admins on Bloodcraft servers with raised level caps should bump
    // this to match.
    public static ConfigEntry<int> Transform_PlayerLeveled_MaxLevel { get; private set; }

    // v0.20.0: when a summon ability is cast during a Beelzebub transform, rebind
    // the spawned minions as player-allies via a LinkMinionToOwnerOnSpawnSystem
    // Prefix patch. Default true. Set false to leave V Rising's vanilla spawn
    // behavior alone (which on direct player casts usually still works via
    // SetTeamToOwner=true on the spawn event, but breaks for some chained summon
    // patterns that expect an NPC caster context).
    public static ConfigEntry<bool> Transform_SummonsAreAllies { get; private set; }

    // v0.23.0: stack-cap per (player, summon-ability) pair. Once a player has this
    // many live ally minions from a single ability, further casts of that ability
    // are refused (manual-spawn casts) or destroyed on natural-chain (LinkMinion
    // catches over-cap entities and queues for staged despawn). 0 = no cap.
    public static ConfigEntry<int> Transform_MaxStacksPerSummonAbility { get; private set; }

    // v0.23.0: per-frame budget for the staged-despawn queue drain.
    // V Rising crashes when too many entities are destroyed in one frame.
    public static ConfigEntry<int> Transform_DespawnBudgetPerFrame { get; private set; }

    // v0.23.0: when a player disconnects, dismiss their active summons.
    // Bloodcraft does this for familiars; we mirror the pattern. Setting false
    // leaves summons in-world during DC — useful for short reconnects.
    public static ConfigEntry<bool> Transform_DespawnSummonsOnDisconnect { get; private set; }

    // v0.23.1: max distance (world units) a summon can wander from its player
    // before being teleported back. 0 = no leashing.
    public static ConfigEntry<float> Transform_SummonLeashRadius { get; private set; }

    // v0.26.0: max lifetime (seconds) for a summon cast-group before it is
    // auto-despawned. 0 = infinite (default). Prevents indefinite horde
    // accumulation on long-lived servers / AFK players.
    public static ConfigEntry<float> Transform_SummonLifetimeSeconds { get; private set; }

    // v0.27.0: how multi-phase boss transforms switch combat phases.
    public enum PhaseControlMode { Manual, Auto }
    public static ConfigEntry<PhaseControlMode> Transform_PhaseMode { get; private set; }

    // v0.29.0 (#4): GUID of a buff used as the on-screen summon-count indicator.
    // The buff is applied to the player and its Stacks reflect the live summon
    // count; 0 = feature off. The icon is the chosen buff's own art — pick any
    // buff whose icon you like.
    public static ConfigEntry<int> Transform_SummonCounterBuffGuid { get; private set; }

    public static void Initialize(ConfigFile config)
    {
        CaptureOnKill = config.Bind(
            "Capture", nameof(CaptureOnKill), true,
            "Master switch. When false, no abilities are captured from kills.");

        Capture_ShareCreditMode = config.Bind(
            "Capture", nameof(Capture_ShareCreditMode), "KillerOnly",
            "Who gets capture rolls when a unit dies: KillerOnly | Proximity. " +
            "Proximity grants every online player within Capture_ShareCreditRadius of the kill site their own independent roll.");

        Capture_ShareCreditRadius = config.Bind(
            "Capture", nameof(Capture_ShareCreditRadius), 30f,
            "Radius in world units (~meters) for Proximity share mode. " +
            "Bloodcraft's analogous setting uses similar default. Ignored when ShareCreditMode = KillerOnly.");

        Capture_TierMidThreshold = config.Bind(
            "Capture.Tier", nameof(Capture_TierMidThreshold), 30,
            "UnitLevel at which a killed unit moves from Low to Mid tier (used by Capture_TierMultiplier_*).");
        Capture_TierHighThreshold = config.Bind(
            "Capture.Tier", nameof(Capture_TierHighThreshold), 60,
            "UnitLevel at which a killed unit moves from Mid to High tier.");
        Capture_TierMultiplier_Low = config.Bind(
            "Capture.Tier", nameof(Capture_TierMultiplier_Low), 1.0f,
            "Drop-chance multiplier applied to units in the Low tier (UnitLevel < MidThreshold). 1.0 = no effect.");
        Capture_TierMultiplier_Mid = config.Bind(
            "Capture.Tier", nameof(Capture_TierMultiplier_Mid), 1.0f,
            "Drop-chance multiplier applied to units in the Mid tier.");
        Capture_TierMultiplier_High = config.Bind(
            "Capture.Tier", nameof(Capture_TierMultiplier_High), 1.0f,
            "Drop-chance multiplier applied to units in the High tier (UnitLevel >= HighThreshold).");

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

        Hotkeys_Enabled = config.Bind(
            "Hotkeys", nameof(Hotkeys_Enabled), true,
            "Admin master switch for W4 named hotkey bindings (extra ability slots beyond the V Rising 6). " +
            "When false, .beelz hotkey commands are blocked and no hotkey bindings can be created. " +
            "The cast-trigger mechanism is BCH-side (server stores; BCH UI fires).");

        Hotkeys_MaxPerPlayer = config.Bind(
            "Hotkeys", nameof(Hotkeys_MaxPerPlayer), 5,
            "Maximum number of named hotkey bindings a player can create. 0 disables hotkey storage entirely.");

        Server_DifficultyMode = config.Bind(
            "Server", nameof(Server_DifficultyMode), "Basic",
            "TX4: difficulty mode this server is configured for: Basic | Brutal. " +
            "Used to gate Brutal-only ability captures and transform unlocks — abilities/transforms " +
            "tagged Difficulty:Brutal in ability_rules.json are blocked on Basic servers. " +
            "Also auto-treats any ability whose prefab name contains '_Hard_' as Brutal-only " +
            "(unless an AbilityMap entry overrides). Admins set this once to match their server's " +
            "game-difficulty preset.");

        Transform_NativeShapeshift_Enabled = config.Bind(
            "Transformation", nameof(Transform_NativeShapeshift_Enabled), false,
            "Z2 (v0.14.0): apply a native V Rising shapeshift form (Wolf/Bear/Rat/Spider/Toad) " +
            "as the visual when a player transforms into a matching unit. WARNING: native shapeshift " +
            "forms auto-exit when the player casts any ability NOT in the form's own moveset — which " +
            "captured abilities always are — so the visual will drop the moment any spell fires. " +
            "Default false: keep the player's vampire model and only swap the spell bar. " +
            "Set true if you'd rather see the wolf model for a couple seconds at the cost of the " +
            "visual ending on first cast.");

        Transform_RealFormWhenAvailable = config.Bind(
            "Transformation", nameof(Transform_RealFormWhenAvailable), true,
            "v0.33.0 (#17): for units that map to a real V Rising form/shapeshift buff " +
            "(boss forms Dracula/Morgana, or native Wolf/Bear/Rat/Spider/Toad/Werewolf/Gargoyle), " +
            "transform using that actual form buff (persistent ExoForm recipe) so the player gets " +
            "the form's model+rig AND it survives ability casts. Default true. Units with no matching " +
            "form keep the ability-only transform regardless. Set false to turn off real native-form " +
            "transforms server-wide; this does NOT affect the curated boss forms (Dracula/Morgana), " +
            "which always use their form buff.");

        Grant_EnforceWeaponMatch = config.Bind(
            "Abilities", nameof(Grant_EnforceWeaponMatch), false,
            "#4 (v0.34.0): when true, refuse a universal `.beelz grant` of a weapon-animation-bound " +
            "ability (sword/axe/spear/etc.) and tell the player to use `.beelz weapon-grant <weapon>` " +
            "so it binds to the weapon whose cast animation it needs (V Rising bakes the animation into " +
            "the ability — wielding the matching weapon is the only way it reads correctly). Default false: " +
            "the weapon-to-wield guidance is shown on every grant regardless; this only hard-blocks the " +
            "universal bind. Spells/universal abilities are never affected.");

        Transform_PowerScalingMode = config.Bind(
            "Transformation", nameof(Transform_PowerScalingMode), "CuratedScales",
            "TX7 (v0.17.0): where transform power comes from. One of: " +
            "CuratedScales (default — admin-curated *Scale fields from the TransformMap entry); " +
            "PrefabAbsolute (auto-derive from the CHAR_ prefab — boss-tier regardless of player level); " +
            "PlayerScaled (no overrides — V Rising's vanilla formula uses the player's natural " +
            "stats, including any Bloodcraft expertise/blood/level buffs); " +
            "PlayerLeveled (v0.19.0: boss-tier stats scaled by the player's current UnitLevel — " +
            "weak transforms when the player is low-level, full boss tier at max level). " +
            "Per-TransformMap-entry `PowerScalingMode` field overrides this for one unit.");

        Transform_SummonsAreAllies = config.Bind(
            "Transformation", nameof(Transform_SummonsAreAllies), true,
            "v0.20.0 (Task #74): when a summon ability fires during a Beelzebub transform " +
            "(e.g. Stonebreaker reinforcements, Bishop of Shadows shadow soldiers), rebind " +
            "the freshly-spawned minions as player-allies — they fight alongside the " +
            "player instead of attacking them, and despawn cleanly on .beelz revert. " +
            "Implementation: Harmony Prefix on LinkMinionToOwnerOnSpawnSystem.OnUpdate " +
            "rewrites EntityOwner / FactionReference / Follower / Minion components " +
            "(pattern: Bloodcraft FamiliarBindingSystem.ModifyFollowerFactionMinion). " +
            "Set false to disable the rebind and let V Rising's vanilla spawn handling " +
            "stand (most player-cast summons would still side with the player via the " +
            "engine's native SetTeamToOwner machinery, but some chained summon patterns " +
            "won't spawn anything without our intervention).");

        Transform_MaxStacksPerSummonAbility = config.Bind(
            "Transformation", nameof(Transform_MaxStacksPerSummonAbility), 3,
            "v0.23.0: maximum number of live ally-summons a player can have from a single " +
            "summon ability at once. Subsequent casts are refused (or destroyed for natural-chain " +
            "summons that V Rising spawns through its own pipeline). Stack decays as minions die. " +
            "0 = no cap (legacy behavior, prone to runaway). Default 3.");

        Transform_DespawnBudgetPerFrame = config.Bind(
            "Transformation", nameof(Transform_DespawnBudgetPerFrame), 5,
            "v0.23.0: maximum number of summon entities destroyed per frame during the " +
            "staged-despawn drain (revert, over-cap kills, etc.). V Rising's server crashes " +
            "when ~36+ entities are destroyed in a single tick; staging across frames avoids " +
            "this. Lower = safer but slower cleanup; higher = faster but risk crash. Default 5.");

        Transform_SummonLeashRadius = config.Bind(
            "Transformation", nameof(Transform_SummonLeashRadius), 30f,
            "v0.23.1: max distance (world units) summons can wander from their player " +
            "before being teleported back. Also triggers when summons enter Idle/Return " +
            "BehaviourTreeState (means they gave up on combat). Default 30. Set 0 to disable leashing.");

        Transform_SummonLifetimeSeconds = config.Bind(
            "Transformation", nameof(Transform_SummonLifetimeSeconds), 0f,
            "v0.26.0: max lifetime in seconds for a summon cast-group before it is " +
            "automatically despawned. Each cast of a summon ability starts its own timer. " +
            "0 = infinite (default). Set e.g. 120 to make summons fade 2 minutes after casting, " +
            "preventing indefinite horde accumulation. Uses the same crash-safe staged despawn " +
            "as revert/disconnect cleanup.");

        Transform_PhaseMode = config.Bind(
            "Transformation", nameof(Transform_PhaseMode), PhaseControlMode.Manual,
            "v0.27.0: how multi-phase boss transforms (e.g. Dracula) switch phases. " +
            "Manual = the player chooses with `.beelz phase <n>`. Auto = phases auto-advance " +
            "as the transformed player loses health WHILE IN COMBAT (one-way; even-split " +
            "thresholds by phase count — e.g. 3 phases advance at 66% and 33% HP), then RESET " +
            "to phase 1 when combat ends, just like a boss resetting on leash. Only affects " +
            "units with curated multi-phase ability sets.");

        Transform_SummonCounterBuffGuid = config.Bind(
            "Transformation", nameof(Transform_SummonCounterBuffGuid), 0,
            "v0.29.0: PrefabGUID (integer) of a buff to show on the player as a live summon-count " +
            "indicator while transformed — its stack count = how many summon 'uses' you have active " +
            "(toward Transform_MaxStacksPerSummonAbility). 0 = OFF (default). The indicator uses the " +
            "chosen buff's OWN icon (V Rising buffs can't have their icon swapped at runtime), so set " +
            "this to any buff whose icon you like — e.g. a consumable/blessing buff. The buff's " +
            "gameplay effects are stripped automatically; only the icon + stack number remain.");

        Transform_DespawnSummonsOnDisconnect = config.Bind(
            "Transformation", nameof(Transform_DespawnSummonsOnDisconnect), true,
            "v0.23.0: when a player disconnects (logout, network drop, kick), queue their " +
            "active summons for staged despawn. Mirrors Bloodcraft's familiar pattern. " +
            "Set false to leave summons in-world during DC — useful if you want short " +
            "reconnects to resume with intact summons, but risks accumulating offline " +
            "minion clutter on long disconnects. Default true.");

        Transform_PlayerLeveled_MaxLevel = config.Bind(
            "Transformation", nameof(Transform_PlayerLeveled_MaxLevel), 90,
            "v0.19.0: max-level anchor for the PlayerLeveled scaling curve. Formula: " +
            "factor = clamp(player.UnitLevel / this, 0.0, 1.0). At level 0 the transform's " +
            "boss-stat bonus is 0; at this level it's the full boss tier. Default 90 = V Rising's " +
            "typical max gear level. Bloodcraft servers with raised level caps should bump this " +
            "to match (the curve continues to apply at this:1 ratio even above; clamp keeps it " +
            "from exploding past 100% boss-tier).");
    }
}
