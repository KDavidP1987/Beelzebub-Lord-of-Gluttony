using BepInEx.Configuration;

namespace Beelzebub.Config;

internal static class Settings
{
    // Capture loop
    public static ConfigEntry<bool> CaptureOnKill { get; private set; }

    // v0.50.0: inclusive testing mode (bypass deny lists + difficulty gate) and the
    // transform-only enforcement switch. See Initialize() for full descriptions.
    public static ConfigEntry<bool> Capture_InclusiveMode { get; private set; }
    public static ConfigEntry<bool> Grant_EnforceTransformOnly { get; private set; }

    // v0.115.0: when true, an ability whose ReviewStatus is Blocked or Hidden is treated as a hard
    // curation gate — not capturable/grantable, and excluded from the player catalog/collection. See Initialize().
    public static ConfigEntry<bool> Curation_EnforceReviewStatus { get; private set; }

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
    // v0.64.0 (#3): the rare "jackpot" roll that DEVOURS a unit (grants its whole kit at once; for
    // Dracula/Morgana it unlocks transformation instead). Renamed from DropChance_Transform_* — the
    // old keys' values are migrated to these automatically on load (see Initialize).
    public static ConfigEntry<float> DropChance_Devour_Regular { get; private set; }
    public static ConfigEntry<float> DropChance_Devour_VBlood { get; private set; }
    // v0.98.0: the TRANSFORMATION-unlock roll is now SEPARATE from Devour (it was the same jackpot
    // before). Only registered transform bosses (BossFormRegistry) roll it; it grants the FORM, not
    // abilities — so transformations are their own independently-gated prize. Defaults rarer than Devour.
    public static ConfigEntry<float> DropChance_TransformUnlock_Regular { get; private set; }
    public static ConfigEntry<float> DropChance_TransformUnlock_VBlood { get; private set; }

    // v0.38.0: escalating pity / bad-luck protection. (v0.64.0: this pair governs ABILITY-capture
    // pity; the Devour jackpot roll has its own pity pair below so admins can tune them apart.)
    public static ConfigEntry<float> Capture_PityIncrementPerKill { get; private set; }
    public static ConfigEntry<float> Capture_PityMaxBonus { get; private set; }
    // v0.64.0 (#3): separate bad-luck protection for the Devour jackpot roll (defaults to the
    // ability values, so existing balance is unchanged until an admin diverges them).
    public static ConfigEntry<float> Capture_PityIncrement_Devour { get; private set; }
    public static ConfigEntry<float> Capture_PityMax_Devour { get; private set; }
    // v0.98.0: separate bad-luck protection for the TRANSFORMATION-unlock roll.
    public static ConfigEntry<float> Capture_PityIncrement_Transform { get; private set; }
    public static ConfigEntry<float> Capture_PityMax_Transform { get; private set; }
    public static ConfigEntry<bool> Capture_PitySessionBased { get; private set; }

    // Notifications (Phase 3)
    public static ConfigEntry<string> DefaultVerbosity { get; private set; }

    // v0.88.0 — server-wide announcements (collection-complete + periodic leaderboard)
    public static ConfigEntry<bool> Broadcast_CollectionComplete_Enabled { get; private set; }
    public static ConfigEntry<string> Broadcast_CollectionComplete_Messages { get; private set; }
    public static ConfigEntry<bool> Broadcast_Leaderboard_Enabled { get; private set; }
    public static ConfigEntry<int> Broadcast_Leaderboard_IntervalMinutes { get; private set; }
    public static ConfigEntry<int> Broadcast_Leaderboard_TopN { get; private set; }
    public static ConfigEntry<string> Broadcast_Leaderboard_Messages { get; private set; }

    // Transformation (Phase 5)
    public static ConfigEntry<string> Transform_Mode_Regular { get; private set; }
    public static ConfigEntry<string> Transform_Mode_VBlood { get; private set; }
    public static ConfigEntry<float> Transform_DurationSeconds_Regular { get; private set; }
    public static ConfigEntry<float> Transform_DurationSeconds_VBlood { get; private set; }
    public static ConfigEntry<float> Transform_CooldownSeconds_Regular { get; private set; }
    public static ConfigEntry<float> Transform_CooldownSeconds_VBlood { get; private set; }

    // v0.39.0: shard bosses (Dracula/Morgana/Adam/Gorecrusher/Trizon/Solarus) split out
    // from regular V-Bloods, with their own mode/duration/cooldown + cooldown bucket.
    public static ConfigEntry<string> Transform_ShardBossNames { get; private set; }
    public static ConfigEntry<string> Transform_Mode_ShardBoss { get; private set; }
    public static ConfigEntry<float> Transform_DurationSeconds_ShardBoss { get; private set; }
    public static ConfigEntry<float> Transform_CooldownSeconds_ShardBoss { get; private set; }

    // v0.100.0: master transform kill-switch + cooldown/duration SCOPE.
    public static ConfigEntry<bool> Transform_Enabled { get; private set; }
    public static ConfigEntry<string> Transform_CooldownScope { get; private set; }

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
    // v0.43.7: superseded by Transform_ReconnectGraceSeconds while the player is
    // actively transformed (the grace window governs that case).
    public static ConfigEntry<bool> Transform_DespawnSummonsOnDisconnect { get; private set; }

    // v0.43.7: reconnect grace window for a disconnected player's active transform
    // + summons. >0 = keep them and resume on a reconnect within N seconds (else
    // revert + despawn); 0 = revert immediately on disconnect; -1 = keep until a
    // manual revert. A server restart during the window ends the grace (the player
    // is safely returned to base form on reconnect via the on-connect reconciliation).
    public static ConfigEntry<float> Transform_ReconnectGraceSeconds { get; private set; }

    // v0.46.0: master switch for ability-tuning (cast interrupt + post-cast movement
    // unlock). When true (the DEFAULT — this is a master kill-switch, NOT an opt-in), Beelzebub
    // applies the per-ability config you set (cooldown, range, charges, interrupt, cast tuning, …)
    // by rewriting the curated abilities' baked prefab fields at server init / on reload. The edits
    // are SERVER-WIDE — an ability behaves the configured way for every player who casts it, and
    // because the ability prefab is shared, the source NPC/boss's copy changes too (intended: you're
    // configuring how the ability functions on the server). Set false only to disable ALL baked
    // ability-config edits. Renamed from AbilityTuning_Enabled in v0.66.0 (now default ON).
    public static ConfigEntry<bool> Abilities_ApplyConfig { get; private set; }

    // v0.48.0 (Phase-1.5 native-form abilities test): when true, entering a VANILLA shapeshift
    // form (Wolf/Bear — the test set) via the in-game shapeshift wheel injects the player's
    // loadout abilities onto the form bar AND strips the form's break-on-cast trigger
    // (RemoveBuffOnGameplayEvent) so the form HOLDS while casting them. Default false — this
    // changes vanilla form behavior; it's the feasibility probe for the per-form-loadout feature.
    public static ConfigEntry<bool> Forms_CustomAbilities_Enabled { get; private set; }

    // v0.23.1: max distance (world units) a summon can wander from its player
    // before being teleported back. 0 = no leashing.
    public static ConfigEntry<float> Transform_SummonLeashRadius { get; private set; }

    // v0.26.0: max lifetime (seconds) for a summon cast-group before it is
    // auto-despawned. 0 = infinite (default). Prevents indefinite horde
    // accumulation on long-lived servers / AFK players.
    public static ConfigEntry<float> Transform_SummonLifetimeSeconds { get; private set; }

    // v0.43.6: summon scaling — keeps summoned allies relevant as the player levels.
    // MatchPlayerLevel sets the summon's UnitLevel to the player's; PowerFactor multiplies
    // its Physical/Spell power + max health. Applies to BOTH transform summons and
    // standalone (untransformed) signature summons.
    public static ConfigEntry<bool> Transform_SummonMatchPlayerLevel { get; private set; }
    public static ConfigEntry<float> Transform_SummonPowerFactor { get; private set; }

    // v0.27.0: how multi-phase boss transforms switch combat phases.
    public enum PhaseControlMode { Manual, Auto }
    public static ConfigEntry<PhaseControlMode> Transform_PhaseMode { get; private set; }

    // v0.42.0: what happens to a transformed player's ally-summons when they MOUNT a horse.
    //   Stash  — auto-stash on mount, restore on dismount (like the bat-form/waygate stash).
    //            Safer: summons don't try to chase a galloping horse.
    //   Follow — summons persist, leash to the (mounted) player, and keep fighting (no special
    //            handling on mount; the existing leash + combat plumbing carries them along).
    public enum MountedSummonMode { Stash, Follow }
    public static ConfigEntry<MountedSummonMode> Transform_MountedSummonMode { get; private set; }

    // v0.29.0 (#4): GUID of a buff used as the on-screen summon-count indicator.
    // The buff is applied to the player and its Stacks reflect the live summon
    // count; 0 = feature off. The icon is the chosen buff's own art — pick any
    // buff whose icon you like.
    public static ConfigEntry<int> Transform_SummonCounterBuffGuid { get; private set; }

    // #3 (v0.36.0): cooldown (seconds) between `.beelz detonate` manual detonations,
    // per player. Prevents spamming a transformed boss's signature AoE. 0 = no
    // cooldown. Only applies to units that have a registered manual detonation.
    public static ConfigEntry<float> Transform_ManualDetonateCooldownSeconds { get; private set; }

    // v0.43.3: cooldown (seconds) between `.beelz summon` manual signature-summon casts,
    // per player. Force-casts a transformed unit's add-summon (e.g. the Toad King's frogs)
    // that the boss normally only triggers at a health-threshold soft phase. 0 = no
    // cooldown. Only affects units with a registered summon (see SummonRegistry).
    public static ConfigEntry<float> Transform_SummonCooldownSeconds { get; private set; }

    // v0.43.4: when true, unlocking a unit's transform also adds that unit's signature
    // add-summon(s) to the player's CAPTURED pool as standalone abilities — so they can be
    // granted to a normal spell slot or bound to the custom hotkey bar and used WITHOUT
    // transforming. Applies retroactively to already-unlocked units on load. Off = summons
    // stay transform-only (usable via `.beelz summon` while transformed).
    public static ConfigEntry<bool> Capture_GrantSignatureSummons { get; private set; }

    // v0.43.5: GRANTED-ability power scaling (the deferred W5 runtime, now live).
    // Granted abilities already scale with the caster's Physical/Spell Power + crit
    // (vanilla). These add an OPTIONAL admin multiplier on top.
    //   Mode: "PlayerScaled" (default — no extra, pure vanilla) | "Boosted" (× the factor).
    //   Factor: the multiplier used in Boosted mode.
    // Combined with the per-ability AbilityMap DamageScale (now applied). Safe-by-default:
    // PlayerScaled + factor 1.0 + per-ability 1.0 = zero change.
    public static ConfigEntry<string> Grant_PowerScalingMode { get; private set; }
    public static ConfigEntry<float> Grant_PowerScalingFactor { get; private set; }

    // v0.65.0 (J1): global minimum-cooldown floor, applied via the baked-prefab tuning path.
    public static ConfigEntry<float> Grant_MinimumCooldownSeconds { get; private set; }

    // v0.120.0: cross-mod slot-conflict priority. The Priority stamped on Beelzebub's spell-slot grant
    // overrides (ReplaceAbilityOnSlotBuff). Only matters when ANOTHER mod writes the same slot at the
    // same priority (e.g. Bloodcraft's class/"shift" spell on slot 3 + unarmed spells on slots 1/4, all
    // Priority 0) — at an equal priority the winner is load-order-dependent. Raise above 0 to make
    // Beelzebub win deterministically. See Initialize() + docs/INTEROP_BLOODCRAFT.md.
    public static ConfigEntry<int> Interop_SlotInjectionPriority { get; private set; }

    public static void Initialize(ConfigFile config)
    {
        CaptureOnKill = config.Bind(
            "Capture", nameof(CaptureOnKill), true,
            "Master switch. When false, no abilities are captured from kills.");

        Capture_InclusiveMode = config.Bind(
            "Capture", nameof(Capture_InclusiveMode), true,
            "v0.50.0 INCLUSIVE TESTING MODE (default ON). When true, ability capture AND the Devour " +
            "jackpot ignore the DenyPatterns/DenyGuids lists and the Basic/Brutal difficulty gate, so " +
            "abilities across ALL V-Bloods and NPCs are broadly capturable/devourable for testing. " +
            "A small hardcoded junk filter (idle/spawn/death/etc. stubs) and the per-ability Enabled " +
            "kill-switch (AbilityMap) still apply. Set to false for a curated server — the full " +
            "deny-list + difficulty pipeline then returns.");

        Curation_EnforceReviewStatus = config.Bind(
            "Capture", nameof(Curation_EnforceReviewStatus), true,
            "v0.115.0 CURATION GATE (default ON). When true, an ability whose ReviewStatus is 'Blocked' " +
            "(incompatible / unwanted) or 'Hidden' (junk) is NOT capturable or grantable and is excluded " +
            "from the player ability catalog + collection total — making ReviewStatus a real curation lever, " +
            "not just a tracking note. Enforced even in Capture_InclusiveMode (it's a deliberate decision, " +
            "like the per-ability Enabled kill-switch). Set to false to TEST a blocked ability without " +
            "un-blocking it (e.g. inclusive testing where you want every tagged ability still reachable). " +
            "Unreviewed/Reviewed/Approved are always collectible.");

        Grant_EnforceTransformOnly = config.Bind(
            "Capture", nameof(Grant_EnforceTransformOnly), false,
            "v0.50.0 (default OFF). When false, the per-ability 'transform-only' reservation " +
            "(AbilityMap TransformOnly / TransformOnlyPatterns / TransformOnlyGuids) is NOT enforced — " +
            "every ability can be granted/slotted/hotkeyed/devoured to the normal bar for testing. " +
            "Set to true to honor those reservations again (abilities so marked become usable only via " +
            "an actual transform). Per-ability Enabled=false is the separate hard kill-switch.");

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

        // v0.64.0 (#3): renamed Transform_* -> Devour_*. Migrate any tuned value automatically — bind
        // the legacy key (which reads the existing .cfg value if present), seed the new Devour key
        // from it, then drop the legacy orphan so only the Devour_* key persists. The config.Save()
        // at the end of Initialize purges the removed legacy key from disk.
        var legacyDevourRegular = config.Bind(
            "Capture.DropChance", "DropChance_Transform_Regular", 0.0025f,
            "(deprecated — renamed to DropChance_Devour_Regular; value migrated automatically)");
        DropChance_Devour_Regular = config.Bind(
            "Capture.DropChance", nameof(DropChance_Devour_Regular), legacyDevourRegular.Value,
            "Per-kill DEVOUR chance (0.0-1.0) on a regular mob. The Devour roll grants ALL of the unit's eligible abilities at once instead of one at a time. (Arbitrary-unit transformation is a postponed phase-two feature; only Dracula & Morgana, both V-Bloods, transform.)");
        config.Remove(legacyDevourRegular.Definition);

        var legacyDevourVBlood = config.Bind(
            "Capture.DropChance", "DropChance_Transform_VBlood", 0.0025f,
            "(deprecated — renamed to DropChance_Devour_VBlood; value migrated automatically)");
        DropChance_Devour_VBlood = config.Bind(
            "Capture.DropChance", nameof(DropChance_Devour_VBlood), legacyDevourVBlood.Value,
            "Per-kill DEVOUR chance (0.0-1.0) on a V-Blood. The Devour roll grants ALL of its eligible abilities at once. (v0.98.0: this is now INDEPENDENT of the transformation roll — devouring a transform boss grants its abilities but NOT its form; the form has its own DropChance_TransformUnlock_VBlood.)");
        config.Remove(legacyDevourVBlood.Definition);

        // v0.98.0: TRANSFORMATION-unlock roll — separate from Devour, only rolled for registered transform
        // bosses (BossFormRegistry). Grants the FORM, not abilities. Rarer than Devour by default so the
        // transformation is a prestige prize a player works toward, on top of devouring/capturing the kit.
        DropChance_TransformUnlock_VBlood = config.Bind(
            "Capture.DropChance", nameof(DropChance_TransformUnlock_VBlood), 1.0f,
            "Per-kill chance (0.0-1.0) to unlock a V-Blood transform boss's TRANSFORMATION (Dracula, Morgana, " +
            "Werewolf Chieftain, Geomancer/Golem, Tailor/Gargoyle, …). Independent of the Devour/ability rolls. " +
            "v0.128.0 TEST-FRIENDLY DEFAULT = 1.0 (every transform-boss kill grants its form) so testers reliably " +
            "get transforms — LOWER this for a balanced release (it was 0.0015, ~0.15%, before). Only fires for " +
            "units the game can render as a player form. NOTE: changing this default only affects a FRESH config; " +
            "an existing server keeps its .cfg value until you run `.beelz admin set DropChance_TransformUnlock_VBlood <v>`.");
        DropChance_TransformUnlock_Regular = config.Bind(
            "Capture.DropChance", nameof(DropChance_TransformUnlock_Regular), 1.0f,
            "Per-kill chance (0.0-1.0) to unlock a non-V-Blood transform unit's TRANSFORMATION (e.g. the basic " +
            "werewolf from the common werewolf NPC). v0.128.0 TEST-FRIENDLY DEFAULT = 1.0 (was 0.005) — LOWER for a " +
            "balanced release. Independent of Devour/ability rolls.");

        Capture_PityIncrementPerKill = config.Bind(
            "Capture.Pity", nameof(Capture_PityIncrementPerKill), 0.0025f,
            "v0.38.0 bad-luck protection for ABILITY captures: each kill whose ability roll gives nothing " +
            "raises that roll's effective chance by this amount (0.0025 = +0.25%), resetting to baseline the " +
            "moment it pays out. Tracked independently per source (Regular/V-Blood). The rare Devour jackpot " +
            "has its own pity dial (Capture_PityIncrement_Devour). 0 = disabled (pure flat chance).");

        Capture_PityMaxBonus = config.Bind(
            "Capture.Pity", nameof(Capture_PityMaxBonus), 1.0f,
            "v0.38.0: cap on the accumulated ABILITY-capture pity bonus (1.0 = +100%, i.e. the chance can " +
            "climb to a guaranteed payout over a long enough dry streak). Lower it (e.g. 0.5) to keep rare " +
            "drops rare even on long streaks. 0 = uncapped.");

        // v0.64.0 (#3): Devour-specific pity, separate from ability-capture pity. Seeded from the
        // ability values so balance is unchanged until an admin diverges them.
        Capture_PityIncrement_Devour = config.Bind(
            "Capture.Pity", nameof(Capture_PityIncrement_Devour), Capture_PityIncrementPerKill.Value,
            "Bad-luck protection for the rare DEVOUR jackpot roll (separate from ability-capture pity). Each " +
            "kill whose Devour roll fails raises the next Devour chance by this amount, reset on a Devour. " +
            "Defaults to the ability pity increment; raise it to make a long dry streak reach a Devour sooner. " +
            "0 = disabled.");

        Capture_PityMax_Devour = config.Bind(
            "Capture.Pity", nameof(Capture_PityMax_Devour), Capture_PityMaxBonus.Value,
            "Cap on the accumulated DEVOUR pity bonus. Defaults to the ability pity cap. 0 = uncapped.");

        // v0.98.0: bad-luck protection for the TRANSFORMATION-unlock roll, independent of ability + Devour pity.
        Capture_PityIncrement_Transform = config.Bind(
            "Capture.Pity", nameof(Capture_PityIncrement_Transform), Capture_PityIncrementPerKill.Value,
            "Bad-luck protection for the TRANSFORMATION-unlock roll (separate from ability + Devour pity). Each " +
            "kill of a transform boss whose transform roll fails raises the next transform chance by this amount, " +
            "reset when the transform unlocks. 0 = disabled.");
        Capture_PityMax_Transform = config.Bind(
            "Capture.Pity", nameof(Capture_PityMax_Transform), Capture_PityMaxBonus.Value,
            "Cap on the accumulated TRANSFORMATION pity bonus. Defaults to the ability pity cap. 0 = uncapped.");

        Capture_PitySessionBased = config.Bind(
            "Capture.Pity", nameof(Capture_PitySessionBased), false,
            "v0.83.0: if true, a player's accumulated pity (bad-luck protection) RESETS when they log out " +
            "(session-based). If false (default), pity is PERMANENT and carries across sessions. Session-based " +
            "makes pity a within-session catch-up only; permanent rewards long-term grinding.");

        DefaultVerbosity = config.Bind(
            "Notifications", nameof(DefaultVerbosity), "Summary",
            "Default chat verbosity for new players: Silent | Summary | Verbose. Each player can override with .beelz verbosity <level>.");

        // v0.88.0 — server-wide announcements.
        Broadcast_CollectionComplete_Enabled = config.Bind(
            "Announcements", nameof(Broadcast_CollectionComplete_Enabled), true,
            "v0.88.0: when a player collects EVERY capturable ability (100%), post a server-wide " +
            "celebratory message. Default ON.");
        Broadcast_CollectionComplete_Messages = config.Bind(
            "Announcements", nameof(Broadcast_CollectionComplete_Messages),
            "🩸 %player% has devoured the entire bestiary — every ability in the realm now bends to their will.|" +
            "👑 The Lord of Gluttony is sated: %player% has collected ALL known abilities. Bow before the complete collection.|" +
            "🦇 Legend spreads through the night — %player% has mastered every ability V Rising has to offer.",
            "v0.88.0: pipe ( | )-separated pool of collection-complete messages (one is picked per event). " +
            "Use %player% for the player's name. Edit freely — keep them short (one chat line).");

        Broadcast_Leaderboard_Enabled = config.Bind(
            "Announcements", nameof(Broadcast_Leaderboard_Enabled), false,
            "v0.88.0: periodically post a server-wide leaderboard of the top players by ability-collection %. " +
            "Default OFF. Toggle live with .beelz admin broadcast leaderboard on|off.");
        Broadcast_Leaderboard_IntervalMinutes = config.Bind(
            "Announcements", nameof(Broadcast_Leaderboard_IntervalMinutes), 60,
            "v0.88.0: minutes between leaderboard broadcasts (60 = hourly, 1440 = daily). Min 1. " +
            "Set live with .beelz admin broadcast interval <minutes>.");
        Broadcast_Leaderboard_TopN = config.Bind(
            "Announcements", nameof(Broadcast_Leaderboard_TopN), 3,
            "v0.88.0: how many top players to list in the leaderboard broadcast (1, 3, or 5). Clamped to 1..5.");
        Broadcast_Leaderboard_Messages = config.Bind(
            "Announcements", nameof(Broadcast_Leaderboard_Messages),
            "🏆 The hungriest vampires tonight: %top%|" +
            "🩸 Bestiary standings — %top%|" +
            "👑 Who devours the most? %top%",
            "v0.88.0: pipe ( | )-separated pool of leaderboard messages (one is picked per broadcast). " +
            "Use %top% for the ranked list (e.g. '🥇 Alice (62%) · 🥈 Bob (55%)') and %count% for the number of collectors.");

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

        Transform_ShardBossNames = config.Bind(
            "Transformation", nameof(Transform_ShardBossNames), "Dracula,Adam,Solarus,Talzur,Winged Horror,Megara,Morgana,Gorecrusher",
            "v0.39.0: comma-separated list of SHARD-BOSS name tokens (the six primary shard-granting end bosses: " +
            "Dracula, Adam the Firstborn, Solarus the Immaculate, The Winged Horror/Talzur, Megara the Serpent Queen, " +
            "Gorecrusher the Behemoth). A transform target is a shard boss if its in-game display name OR its prefab " +
            "name contains any token (case-insensitive). The default lists aliases for match robustness — 'Talzur'/" +
            "'Winged Horror' for the Winged Horror, 'Megara'/'Morgana' for the serpent queen (her prefab is " +
            "Blackfang_Morgana). Trim or extend this if a boss doesn't resolve in your game version. Shard " +
            "bosses use the Transform_*_ShardBoss settings below and their own cooldown bucket, separate from other V-Bloods.");

        Transform_Mode_ShardBoss = config.Bind(
            "Transformation", nameof(Transform_Mode_ShardBoss), "Toggle",
            "v0.39.0: transformation mode for shard bosses: Toggle | Timed | Disabled. Lets admins, e.g., make the " +
            "powerful shard-boss forms time-limited while leaving other V-Bloods as toggles.");

        Transform_DurationSeconds_ShardBoss = config.Bind(
            "Transformation", nameof(Transform_DurationSeconds_ShardBoss), 60f,
            "v0.39.0: auto-revert duration (seconds) for shard-boss transforms when Transform_Mode_ShardBoss = Timed.");

        Transform_CooldownSeconds_ShardBoss = config.Bind(
            "Transformation", nameof(Transform_CooldownSeconds_ShardBoss), 0f,
            "v0.39.0: cooldown (seconds) after a shard-boss transform ends before another shard-boss transform can " +
            "start. Independent of the regular/V-Blood cooldown buckets. 0 = no cooldown.");

        // v0.100.0: master kill-switch — turn ALL transformations off server-wide (captures/devour unaffected).
        Transform_Enabled = config.Bind(
            "Transformation", nameof(Transform_Enabled), true,
            "Master switch for ALL transformations. false = no player can transform (the unlock still drops, but " +
            ".beelz transform is refused). Ability capture + Devour are unaffected. Per-unit blocking is still " +
            "'.beelz admin transform-set <unit> enabled false'.");
        // v0.100.0: how the cooldown + duration BUDGET is shared.
        Transform_CooldownScope = config.Bind(
            "Transformation", nameof(Transform_CooldownScope), "PerCategory",
            "How the transform cooldown budget is shared: 'PerCategory' (default — one cooldown bucket per " +
            "Regular/V-Blood/Shard-Boss group, the legacy behavior), 'PerTransformation' (each unit has its OWN " +
            "independent cooldown — e.g. 30 min/day PER form, so you can use every form), or 'Global' (a SINGLE " +
            "cooldown across ALL transforms — e.g. 30 min/day total, one form at a time). Duration is per-unit when " +
            "an override is set (.beelz admin transform-set <unit> duration <sec>), else the category default.");

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
            "DEPRECATED (v0.44.0): inert — transformation is now Dracula/Morgana-only and non-boss " +
            "units no longer transform (their kits are collected as abilities / Devour). Native " +
            "animal-form transforms return in 'phase two'. — Z2 (v0.14.0): apply a native V Rising shapeshift form (Wolf/Bear/Rat/Spider/Toad) " +
            "as the visual when a player transforms into a matching unit. WARNING: native shapeshift " +
            "forms auto-exit when the player casts any ability NOT in the form's own moveset — which " +
            "captured abilities always are — so the visual will drop the moment any spell fires. " +
            "Default false: keep the player's vampire model and only swap the spell bar. " +
            "Set true if you'd rather see the wolf model for a couple seconds at the cost of the " +
            "visual ending on first cast.");

        Transform_RealFormWhenAvailable = config.Bind(
            "Transformation", nameof(Transform_RealFormWhenAvailable), true,
            "DEPRECATED (v0.44.0): inert for the common path — only Dracula/Morgana transform now " +
            "(they always use their form buff regardless of this flag); other units' native-form " +
            "transforms are postponed to 'phase two'. — v0.33.0 (#17): for units that map to a real V Rising form/shapeshift buff " +
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
            "GLOBAL ability dial (applies to EVERY summon ability — transform summons AND standalone " +
            "captured summons cast in normal form, since v0.45): maximum number of live ally-summons a " +
            "player can have from a single summon ability at once. Subsequent casts are refused (or " +
            "destroyed for natural-chain summons that V Rising spawns through its own pipeline). Stack " +
            "decays as minions die. 0 = no cap (legacy behavior, prone to runaway). Default 3. " +
            "v0.79.0: a PER-ABILITY override (.beelz admin ability <name> summoncap <n>) takes precedence " +
            "over this global default for that ability.");

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
            "Transformation", nameof(Transform_SummonLifetimeSeconds), 30f,
            "GLOBAL summon TIMEOUT (applies to EVERY summon ability — transform AND standalone): max " +
            "lifetime in seconds for a summon cast-group before it is automatically despawned. Each cast " +
            "of a summon ability starts its own timer. v0.79.0: default changed 0 → 30 so summons that " +
            "would otherwise persist indefinitely fade after 30s (set 0 to make summons never expire). A " +
            "PER-ABILITY override (.beelz admin ability <name> summontimeout <seconds>) takes precedence " +
            "for that ability. Uses the same crash-safe staged despawn as revert/disconnect cleanup.");

        Transform_SummonMatchPlayerLevel = config.Bind(
            "Transformation", nameof(Transform_SummonMatchPlayerLevel), true,
            "v0.43.6: when true (default), a summoned ally's UnitLevel is set to its owner's " +
            "level on spawn — so a low-level boss-add stays relevant as you out-level it (V Rising's " +
            "level-difference modifier keeps its damage/defense appropriate vs your tier of enemies). " +
            "Applies to transform summons AND standalone signature summons. False = keep the unit's " +
            "baked level.");

        Transform_SummonPowerFactor = config.Bind(
            "Transformation", nameof(Transform_SummonPowerFactor), 1.0f,
            "v0.43.6: multiplier applied to a summoned ally's PhysicalPower, SpellPower and MaxHealth " +
            "on spawn. 1.0 = no change (default), 1.5 = +50% tankier/harder-hitting summons, 0.75 = -25%. " +
            "This is the lever for summon DAMAGE output (UnitLevel mainly drives the level-difference " +
            "modifier). Combine with Transform_SummonMatchPlayerLevel.");

        Transform_PhaseMode = config.Bind(
            "Transformation", nameof(Transform_PhaseMode), PhaseControlMode.Manual,
            "v0.27.0: how multi-phase boss transforms (e.g. Dracula) switch phases. " +
            "Manual = the player chooses with `.beelz phase <n>`. Auto = phases auto-advance " +
            "as the transformed player loses health WHILE IN COMBAT (one-way; even-split " +
            "thresholds by phase count — e.g. 3 phases advance at 66% and 33% HP), then RESET " +
            "to phase 1 when combat ends, just like a boss resetting on leash. Only affects " +
            "units with curated multi-phase ability sets.");

        Transform_MountedSummonMode = config.Bind(
            "Transformation", nameof(Transform_MountedSummonMode), MountedSummonMode.Stash,
            "v0.42.0: what happens to a transformed player's ally-summons when they mount a horse. " +
            "Stash (default) = auto-stash the summons on mount and restore them on dismount (like the " +
            "bat-form/waygate stash) — they won't try to chase a galloping horse. " +
            "Follow = summons stay in-world, leash to the mounted player and keep engaging in combat " +
            "(the existing leash + combat plumbing carries them along; far ones teleport-follow). " +
            "Only affects players who have summons active from a transform; requires Transform_SummonsAreAllies.");

        Transform_SummonCounterBuffGuid = config.Bind(
            "Transformation", nameof(Transform_SummonCounterBuffGuid), 0,
            "v0.29.0: PrefabGUID (integer) of a buff to show on the player as a live summon-count " +
            "indicator while transformed — its stack count = how many summon 'uses' you have active " +
            "(toward Transform_MaxStacksPerSummonAbility). 0 = OFF (default). The indicator uses the " +
            "chosen buff's OWN icon (V Rising buffs can't have their icon swapped at runtime), so set " +
            "this to any buff whose icon you like — e.g. a consumable/blessing buff. The buff's " +
            "gameplay effects are stripped automatically; only the icon + stack number remain.");

        Transform_ManualDetonateCooldownSeconds = config.Bind(
            "Transformation", nameof(Transform_ManualDetonateCooldownSeconds), 5f,
            "#3 (v0.36.0): cooldown in seconds between `.beelz detonate` manual detonations per player " +
            "(fires a transformed boss's signature AoE — e.g. the Undead Priest's Nova — on demand). " +
            "Prevents spamming the AoE. 0 = no cooldown. Only affects units with a registered detonation.");

        Transform_SummonCooldownSeconds = config.Bind(
            "Transformation", nameof(Transform_SummonCooldownSeconds), 12f,
            "v0.43.3: cooldown in seconds between `.beelz summon` manual signature-summon casts per " +
            "player (force-casts a transformed unit's add-summon — e.g. the Toad King's frogs — that " +
            "the boss normally only triggers at a low-HP soft phase). 0 = no cooldown. Only affects " +
            "units with a registered summon.");

        Grant_PowerScalingMode = config.Bind(
            "Abilities", nameof(Grant_PowerScalingMode), "PlayerScaled",
            "v0.43.5: how GRANTED abilities (captured boss abilities on your normal bar/hotkeys) " +
            "scale in power. 'PlayerScaled' (default) = pure vanilla — the ability already scales " +
            "with YOUR Physical/Spell Power + crit, so it tracks your level/gear/prestige automatically " +
            "(no extra applied). 'Boosted' = additionally multiply granted-ability power by " +
            "Grant_PowerScalingFactor (server-wide dial for under/over-tuned boss abilities). Combines " +
            "with each ability's AbilityMap DamageScale. Does NOT affect transforms (those use " +
            "Transform_PowerScalingMode).");

        Grant_PowerScalingFactor = config.Bind(
            "Abilities", nameof(Grant_PowerScalingFactor), 1.0f,
            "v0.43.5: the multiplier applied to granted-ability power when Grant_PowerScalingMode = " +
            "'Boosted'. 1.0 = no change, 1.5 = +50%, 0.75 = -25%. Ignored in PlayerScaled mode. " +
            "Implemented as a brief Physical+Spell power buff around the cast, so other actions in a " +
            "~1.5s window are also affected (a tuning approximation, not a surgical per-hit multiply).");

        Grant_MinimumCooldownSeconds = config.Bind(
            "Abilities", nameof(Grant_MinimumCooldownSeconds), 0f,
            "v0.65.0: GLOBAL minimum cooldown (seconds) floored onto EVERY ability whose baked cooldown " +
            "is lower — stops spammy low/zero-cooldown captured abilities server-wide. 0 = off (default). " +
            "Requires Abilities_ApplyConfig (default ON; it's a baked-prefab edit applied at load + on `.beelz admin " +
            "reload`). WARNING: GLOBAL — it also raises the source NPC/boss cooldown of any ability below " +
            "the floor. A per-ability absolute cooldown (`.beelz admin ability <name> cooldown <sec>`) is " +
            "still floored by this value.");

        Interop_SlotInjectionPriority = config.Bind(
            "Interop", nameof(Interop_SlotInjectionPriority), 0,
            "v0.120.0: the Priority value Beelzebub stamps on its spell-slot grant overrides — the " +
            "ReplaceAbilityOnSlotBuff entries that put your captured abilities on slots 0-7. This ONLY " +
            "matters when ANOTHER mod also writes the same slot. Bloodcraft, for example, writes its " +
            "class/'shift' spell to slot 3 and its unarmed spells to slots 1+4 at Priority 0; at an equal " +
            "priority the winner is load-order-dependent (nondeterministic). Set this to 1 (or higher) to " +
            "make Beelzebub's bind WIN those contested slots deterministically; leave at 0 for neutral/" +
            "legacy behavior; set negative to make Beelzebub YIELD so the other mod wins. Harmless on " +
            "single-mod servers — nothing else competes there, so 0 and 1 look identical. The complementary " +
            "lever is Bloodcraft's own ShiftSlot/UnarmedSlots flags. See docs/INTEROP_BLOODCRAFT.md.");

        Capture_GrantSignatureSummons = config.Bind(
            "Capture", nameof(Capture_GrantSignatureSummons), true,
            "v0.43.4: when true, unlocking a unit's transform ALSO grants that unit's signature " +
            "add-summon(s) into the player's captured pool as standalone abilities — usable WITHOUT " +
            "transforming (grant to a spell slot or bind to a `.beelz hotkey` / `.beelz cast`). Applies " +
            "retroactively to already-unlocked units on load. False = summons stay transform-only " +
            "(via `.beelz summon`). Note: cast in normal form (no boss rig) the spawn usually still " +
            "fires, but the cast animation may look off and a few scripted summons may no-op.");

        Transform_DespawnSummonsOnDisconnect = config.Bind(
            "Transformation", nameof(Transform_DespawnSummonsOnDisconnect), true,
            "v0.23.0: when a player disconnects (logout, network drop, kick), queue their " +
            "active summons for staged despawn. Mirrors Bloodcraft's familiar pattern. " +
            "Set false to leave summons in-world during DC — useful if you want short " +
            "reconnects to resume with intact summons, but risks accumulating offline " +
            "minion clutter on long disconnects. Default true. NOTE (v0.43.7): while a " +
            "player is actively TRANSFORMED, Transform_ReconnectGraceSeconds governs their " +
            "summons instead of this setting (a kept transform keeps its summons).");

        Transform_ReconnectGraceSeconds = config.Bind(
            "Transformation", nameof(Transform_ReconnectGraceSeconds), 90f,
            "v0.43.7: grace window (seconds) for a disconnected player's ACTIVE TRANSFORM and " +
            "its summons. >0 (default 90) = on disconnect the transform is kept and its summons " +
            "are stashed; if the player reconnects within this window, both are restored intact. " +
            "If the window elapses with no reconnect, the transform is reverted and its summons " +
            "despawned. 0 = revert immediately on disconnect (no grace). -1 = keep indefinitely " +
            "until a manual revert. Independent of how they disconnected (logout, drop, kick). " +
            "On EVERY login Beelzebub also reconciles state: a transform buff left stuck from a " +
            "previous session (e.g. a server restart, which clears the in-memory transform record) " +
            "is detected and cleared so the player always returns with a working ability bar.");

        Transform_PlayerLeveled_MaxLevel = config.Bind(
            "Transformation", nameof(Transform_PlayerLeveled_MaxLevel), 90,
            "v0.19.0: max-level anchor for the PlayerLeveled scaling curve. Formula: " +
            "factor = clamp(player.UnitLevel / this, 0.0, 1.0). At level 0 the transform's " +
            "boss-stat bonus is 0; at this level it's the full boss tier. Default 90 = V Rising's " +
            "typical max gear level. Bloodcraft servers with raised level caps should bump this " +
            "to match (the curve continues to apply at this:1 ratio even above; clamp keeps it " +
            "from exploding past 100% boss-tier).");

        // v0.66.0: renamed from AbilityTuning_Enabled and flipped to DEFAULT ON. Migrate by removing
        // the old key so EVERY server (not just fresh ones) adopts the new default — the per-ability
        // config you set now applies out of the box, no opt-in. Deliberately NOT seeding from the old
        // value: the old default was off/opt-in, and the intent is that configured values just apply.
        var legacyTuningGate = config.Bind(
            "AbilityTuning", "AbilityTuning_Enabled", false,
            "(deprecated — renamed to Abilities.Abilities_ApplyConfig, now default ON)");
        config.Remove(legacyTuningGate.Definition);
        Abilities_ApplyConfig = config.Bind(
            "Abilities", nameof(Abilities_ApplyConfig), true,
            "v0.66.0 (renamed from AbilityTuning_Enabled, now DEFAULT ON — a master kill-switch, not " +
            "an opt-in): apply the per-ability config you set (cooldown, range, charges, interrupt, " +
            "free-move, cast-speed, …) by rewriting the curated abilities' baked prefab fields at " +
            "server init and on `.beelz admin reload`. Set per-ability via `.beelz admin ability " +
            "<name> <field> <value>` / `.beelz admin tune`, or hand-edit ability_rules.json. The edits " +
            "are SERVER-WIDE: the ability behaves the configured way for every player who casts it, " +
            "and because V Rising shares the ability prefab, the source NPC/boss's copy changes too " +
            "(this is intended — you're configuring how the ability functions on the server). Set " +
            "false ONLY to disable ALL baked ability-config edits (e.g. to debug a conflict).");

        Forms_CustomAbilities_Enabled = config.Bind(
            "Forms", nameof(Forms_CustomAbilities_Enabled), true,
            "v0.48.0 [EXPERIMENTAL]: when you enter a vanilla shapeshift form (currently the " +
            "Wolf/Bear test set) via the in-game shapeshift wheel, inject your current loadout's " +
            "abilities onto the form's bar AND strip the form's break-on-cast trigger so the form " +
            "HOLDS while you cast them (vanilla travel forms normally exit on the first cast). This " +
            "is the feasibility probe for a full per-form-loadout feature (assign abilities per " +
            "form, auto-applied when you shift into it, like the per-weapon loadouts). v0.75.0: now " +
            "DEFAULT TRUE so per-form loadouts work out of the box — set false to restore stock " +
            "vanilla form behavior. It changes vanilla form behavior for every player when on. The " +
            "form buff keeps its own RemoveOnDisconnect, so logging out still exits the form cleanly.");

        // v0.64.0 (#3): flush the file so the legacy DropChance_Transform_* keys we migrated +
        // removed above don't linger as orphans on disk (only the Devour_* keys persist).
        config.Save();
    }
}
