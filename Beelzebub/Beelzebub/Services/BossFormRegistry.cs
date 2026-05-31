using System.Collections.Generic;
using Stunlock.Core;

namespace Beelzebub.Services;

/// <summary>
/// v0.31.0 — boss "form" registry for the ExoForm-style transform.
///
/// Some bosses have a real in-game FORM/shapeshift buff (the model + rig change
/// the boss itself uses). Transforming a player by applying that form buff —
/// instead of only swapping spell slots on the player's vampire body — gives the
/// player the correct rig, which is what makes the boss's animation-bound and
/// chained abilities actually fire (the lesson from Bloodcraft's ExoForm:
/// EvolvedVampire = Dracula, CorruptedSerpent = Morgana).
///
/// Each entry pairs a transform unit (CHAR_ PrefabGUID) with its form buff and a
/// CURATED, hand-picked ability set (slots 0-7) proven to work under that form.
/// The sets here are adopted from Bloodcraft's ShapeshiftInterface — abilities
/// that misfire on a player (e.g. Dracula's WolfLeap) are deliberately excluded;
/// the form-compatible ones (VeilOfBats, EtherialSword, RingOfBlood, …) are kept.
///
/// Units NOT in this registry fall back to the ability-only transform
/// (<see cref="TransformBuffService"/> carrier buff + slot heuristic).
/// </summary>
internal static class BossFormRegistry
{
    internal sealed class BossForm
    {
        /// <summary>The form/shapeshift buff PrefabGUID applied to the player.</summary>
        public int FormBuffGuid;
        /// <summary>Friendly label for logs/messages.</summary>
        public string Label;
        /// <summary>
        /// v0.32.0: ordered ability sets ("forms"/phases). FormSets[0] is the
        /// default; the player switches between them via `.beelz phase &lt;n&gt;`
        /// (or auto-on-HP in Auto phase mode). Each set is the 8-slot form bar
        /// (slots 0-7). A unit with one set is single-form. (V Rising exposes only
        /// one player-applicable MODEL per boss, so all sets share the form buff /
        /// visual — the "shift" changes the kit, not the 3D model.)
        /// </summary>
        public int[][] FormSets;
        /// <summary>Optional per-set names (parallel to FormSets) for messages.</summary>
        public string[] FormNames;

        // NOTE (v0.99.1): a per-phase form-buff array was trialled (v0.98 Dracula "Wolf" phase) to let one
        // unit swap MODELS across phases, but swapping the model mid-transform routes through the async
        // form re-apply that breaks on native forms — so it was removed. A second model for an existing
        // transform should be its OWN standalone transform/unlock instead. All phases of a unit share the
        // one FormBuffGuid model; `.beelz phase` only swaps the ability set.

        public int FormCount => FormSets?.Length ?? 0;
        public int[] SetForPhase(int phase) // phase is 1-based
        {
            if (FormSets == null || FormSets.Length == 0) return System.Array.Empty<int>();
            int idx = phase - 1;
            if (idx < 0) idx = 0; else if (idx >= FormSets.Length) idx = FormSets.Length - 1;
            return FormSets[idx];
        }
    }

    // Keyed by transform unit CHAR_ PrefabGUID.
    static readonly Dictionary<int, BossForm> _forms = new()
    {
        // Dracula → EvolvedVampire form buff (Buff_Vampire_Dracula_SpellPhase).
        // TWO switchable kits on the same form (V Rising has no 2nd Dracula model):
        //   Form 1 "Warrior" — melee/sword. (Replaced Bloodcraft's combo-only
        //     SideStepLong_Followup, which was a dead R-key, with real melee moves.)
        //   Form 2 "Bloodmage" — spell kit (incl. SummonBats + BloodStorm ult).
        { -327335305, new BossForm {
            Label = "Dracula",
            FormBuffGuid = -31099041,
            // v0.99.1: the v0.98 experimental "Wolf" phase 3 was REMOVED. It swapped to a different MODEL
            // mid-transform (a per-phase form buff), which routes through the async destroy+respawn form
            // apply — the same path that breaks mid-transform on native forms — so it didn't work in-game.
            // Dracula's wolf is really a standalone shapeshift (his WolfLeap ability), so a "Dracula's Wolf"
            // would need to be its OWN transform/unlock, not a phase of the spell form. Deferred (see notes).
            FormNames = new[] { "Warrior", "Bloodmage" },
            FormSets = new[]
            {
                // Form 1 Warrior: ShockwaveSlash, DownSwing, SwordThrow, SliceNDice,
                // WolfAttack, QuickTeleport, ShockwaveFastSlash, DownSwingDetonating.
                new[] { 364141768, -459642635, 532210332, -847327302, 1888098383, -1940289109, -1473399128, 841757706 },
                // Form 2 Bloodmage: BloodBoltSwarm, RingOfBlood, BloodShower, VeilOfBats,
                // EtherialSword, SummonBats, BloodStorm (ult), QuickTeleport.
                new[] { 797450963, -7407393, -1765846328, 1270706044, -1161896955, -1406000418, -1284243288, -1940289109 },
            },
        }},
        // Morgana → CorruptedSerpent form buff (SnakePhase), TWO switchable kits like Dracula
        // (v0.43.2, after testing: the humanoid-stage attempt was dropped — V Rising has no
        // player-renderable humanoid-Morgana model AND her humanoid casts are rig-bound so
        // they wouldn't fire on the player's own body; see [[project_model_swap_hard_limit]]).
        // Both phases wear the serpent form (so the model renders + abilities fire); `.beelz
        // phase 1/2` (or Auto-on-HP) swaps the KIT, not the model.
        //   Phase 1 "Spectral" — her ranged spectral-barrage kit (caster playstyle).
        //   Phase 2 "Serpent"  — the proven Bloodcraft CorruptedSerpent melee+spectral set.
        { 591725925, new BossForm {
            Label = "Morgana",
            FormBuffGuid = -1859425781,             // serpent form — used for BOTH phases
            FormNames = new[] { "Spectral", "Serpent" },
            FormSets = new[]
            {
                // Phase 1 Spectral (8 form slots): SpectralBeam, QuickTeleport, SpectralSwarm,
                // CorruptionFountain, SpectralOrbBarrage, RingsOfTerror, SpectralHell,
                // TravelingOrbBarrage (ult). (Heavily scripted boss casts — curate per testing.)
                new[] { 2099754785, -1940289109, 1485893437, 1298623256, 1990869093, -616120746, 1185642044, 1242557903 },
                // Phase 2 Serpent (8 form slots): MeleeAttack, GroundPiercer, QuickTeleport,
                // MistSpinners, CrossWindSlash, SpectralBlast, SpectralBeam, EyeOfTheCorruption.
                new[] { 2134120100, -668068170, -1940289109, 1278045964, 846291757, 1173842428, 2099754785, 734658196 },
            },
        }},
        // v0.96.0 — Werewolf Chieftain (Willfred) → the real cursed-forest WEREWOLF form
        // (Buff_General_Shapeshift_Werewolf_Standard). The third player-renderable transform after
        // Dracula/Morgana, and the first that ISN'T a boss spell-phase buff — it's the werewolf-curse
        // shapeshift, so the player takes the werewolf MODEL. The Werewolf Chieftain VBlood ships ONLY as
        // a `_GateBoss_Major` variant, so VBloodSystemPatch was changed to let registered Tier-1 units
        // unlock despite the gate-boss exclusion.
        //
        // ⚠️ EXPERIMENTAL/TEST: this form's ability bar is normally defined by a CastOptions prefab
        // (CO_Werewolf), NOT ReplaceAbilityOnSlotBuff. EnrichFormBuff still injects the curated kit on
        // slots 0-7 exactly like Dracula/Morgana — IN-GAME TEST whether the injected abilities render +
        // fire, or the CastOptions werewolf bar wins. If the latter, the form is still a valid cosmetic
        // "become a werewolf" with its native kit. See docs/WEREWOLF_FORM_TRANSFORM_DESIGN.md.
        { 2079933370, new BossForm {
            Label = "Werewolf",
            FormBuffGuid = -622259665,              // v0.98.0: the V-BLOOD werewolf form (the boss variant);
                                                    // the basic NPC werewolf below uses the _Standard buff so the two are distinct.
            // v0.99.0: two combat phases (same werewolf model) — `.beelz phase` swaps the kit.
            FormNames = new[] { "Feral", "Alpha" },
            FormSets = new[]
            {
                // Phase 1 "Feral" — agile bleed kit (the core AB_Werewolf_* moves that match the rig).
                new[]
                {
                    -831562637,   // 0 AB_Werewolf_MeleeAttack_Group (primary claw)
                    -1789525825,  // 1 AB_Werewolf_Bite_AbilityGroup (bite → bleed)
                    1063690361,   // 2 AB_Werewolf_Dash_AbilityGroup (travel/leap)
                    797495975,    // 3 AB_Werewolf_Howl_Group
                },
                // Phase 2 "Alpha" — the Chieftain's heavy crowd-control kit.
                new[]
                {
                    -831562637,   // 0 AB_Werewolf_MeleeAttack_Group (primary claw)
                    -174926399,   // 1 AB_WerewolfChieftain_MultiBite_AbilityGroup
                    1445822330,   // 2 AB_WerewolfChieftain_Knockdown_AbilityGroup
                    -566065717,   // 3 AB_WerewolfChieftain_ShadowDash_AbilityGroup
                    -192549213,   // 4 AB_WerewolfChieftain_Stealth_AbilityGroup
                },
            },
        }},
        // v0.97.0 — Geomancer (Terah) → GOLEM form (AB_Shapeshift_Golem_T02_Buff). The iron-golem
        // model-swap shapeshift V Rising ships (historically flagged "not reliably player-usable" — this
        // is the in-game test of whether it renders + holds for a player). Kit = the Geomancer's earth
        // abilities (adopted from Bloodcraft's commented-out AncientGuardian set).
        // ⚠️ EXPERIMENTAL/TEST — report which slots render + fire.
        { -1065970933, new BossForm {        // CHAR_Geomancer_Human_VBlood
            Label = "Golem",
            FormBuffGuid = 914043867,          // AB_Shapeshift_Golem_T02_Buff
            // v0.99.0: two combat phases (same golem model) mirroring the Geomancer's calm→enraged fight.
            FormNames = new[] { "Earthshaper", "Enraged" },
            FormSets = new[]
            {
                // Phase 1 "Earthshaper" — controlled earth kit.
                new[]
                {
                    1500843923,   // 0 AB_Geomancer_MeleeAttack_Group (primary slam)
                    2106422510,   // 1 AB_Geomancer_GroundSlam_Group
                    -1940289109,  // 2 AB_Vampire_Dracula_QuickTeleport_AbilityGroup (mobility — player-safe)
                    -221719333,   // 3 AB_Geomancer_RockSlam_AbilityGroup
                },
                // Phase 2 "Enraged" — the boss's enrage kit (summon guardians + heavy smashes).
                new[]
                {
                    1500843923,   // 0 AB_Geomancer_MeleeAttack_Group (primary slam)
                    1079488801,   // 1 AB_Geomancer_EnragedSmash_AbilityGroup
                    -598112885,   // 2 AB_Geomancer_Golem_RaiseGuardians_AbilityGroup (summon)
                    -1204505053,  // 3 AB_Geomancer_Enrage_AbilityGroup
                    -1148606177,  // 4 AB_Geomancer_UndergroundTremmors_AbilityGroup
                },
            },
        }},
        // v0.97.0 — The Tailor → GARGOYLE form (AB_Tailor_Shapeshift_Gargoyle_Buff). The Tailor's phase-2
        // gargoyle is a model-swap shapeshift V Rising ships. The gargoyle's native ability pool is THIN
        // (it's primarily a flying/defensive form — fly + wing-shield), so this kit is sparse + leans on
        // those gargoyle moves plus a generic teleport. ⚠️ EXPERIMENTAL/TEST — the fly/shield abilities are
        // scripted travel/defense sequences that may not behave as normal slot casts; report what works.
        { -1942352521, new BossForm {        // CHAR_Villager_Tailor_VBlood
            Label = "Gargoyle",
            FormBuffGuid = -395216184,         // AB_Tailor_Shapeshift_Gargoyle_Buff
            // v0.99.0: two phases (same gargoyle model) — grounded defense vs flight. The gargoyle's native
            // pool is thin (fly + wing-shield), so the two kits overlap more than the other forms.
            FormNames = new[] { "Sentinel", "Skyterror" },
            FormSets = new[]
            {
                // Phase 1 "Sentinel" — grounded wing-shield defense.
                new[]
                {
                    88850785,     // 0 AB_Gargoyle_WingShield_Emerge_AbilityGroup (wing slam)
                    1460741503,   // 1 AB_Gargoyle_WingShield_AbilityGroup        (wing shield)
                    -1940289109,  // 2 AB_Vampire_Dracula_QuickTeleport_AbilityGroup (mobility — player-safe)
                    1529659981,   // 3 AB_Gargoyle_WingShield_EmergeSpawn_AbilityGroup
                },
                // Phase 2 "Skyterror" — flight kit.
                new[]
                {
                    88850785,     // 0 AB_Gargoyle_WingShield_Emerge_AbilityGroup (wing slam)
                    -382913708,   // 1 AB_Gargoyle_FlyStart_AbilityGroup (take flight / travel)
                    1563014858,   // 2 AB_Gargoyle_FlyEnd_AbilityGroup   (dive / land)
                    -1940289109,  // 3 AB_Vampire_Dracula_QuickTeleport_AbilityGroup (mobility)
                },
            },
        }},
        // v0.98.0 — BASIC WEREWOLF: the common (non-boss) werewolf NPC's curse form. Uses the _Standard
        // werewolf shapeshift (distinct from the Chieftain's _VBlood form above). Unlocked from the regular
        // werewolf NPC via the regular-kill transform roll (DropChance_TransformUnlock_Regular) — more
        // accessible than the boss form. Core AB_Werewolf_* kit (no Chieftain specials).
        { -951976780, new BossForm {        // CHAR_Farmlands_HostileVillager_Werewolf
            Label = "Basic Werewolf",
            FormBuffGuid = -1598161201,         // Buff_General_Shapeshift_Werewolf_Standard
            FormNames = new[] { "Basic Werewolf" },
            FormSets = new[]
            {
                new[]
                {
                    -831562637,   // 0 AB_Werewolf_MeleeAttack_Group (primary claw)
                    -1789525825,  // 1 AB_Werewolf_Bite_AbilityGroup
                    1063690361,   // 2 AB_Werewolf_Dash_AbilityGroup (travel)
                    797495975,    // 3 AB_Werewolf_Howl_Group
                },
            },
        }},
    };

    public static bool TryGet(int unitGuid, out BossForm form) => _forms.TryGetValue(unitGuid, out form);
    public static bool Has(int unitGuid) => _forms.ContainsKey(unitGuid);

    /// <summary>v0.44.0: number of units that can actually be transformed into (Dracula + Morgana).
    /// Used as the honest denominator for transform progress now that the per-unit transform
    /// system is retired in favour of per-ability Devour.</summary>
    public static int Count => _forms.Count;

    // ---------------------------------------------------------------------
    // v0.33.0 (#17): native shapeshift forms reused as REAL (persistent)
    // ExoForms. V Rising ships these form buffs and the client knows how to
    // render them, so applying one via the ApplyForm recipe gives the player a
    // real model+rig that survives ability casts (unlike the legacy cosmetic
    // ShapeshiftService.Apply, which auto-exits on first cast). A unit that maps
    // to one of these (by name, via ShapeshiftService.PickFormFor) gets a
    // single-set form built from its OWN curated abilities.
    // ---------------------------------------------------------------------
    static readonly int[] _nativeFormGuids =
    {
        -351718282,   // AB_Shapeshift_Wolf_Buff
        -1569370346,  // AB_Shapeshift_Bear_Buff
        902394170,    // AB_Shapeshift_Rat_Buff
        124832551,    // AB_Shapeshift_Spider_Buff
        -1038422434,  // AB_Shapeshift_Toad_Buff
        -1158884666,  // AB_Shapeshift_Wolf_Skin01_Buff (Werewolf)
        -395216184,   // AB_Tailor_Shapeshift_Gargoyle_Buff
    };

    /// <summary>
    /// v0.41.1: is this buff GUID one of the NATIVE shapeshift forms (wolf/bear/rat/
    /// spider/toad/werewolf/gargoyle)? Used to detect a combat-form EXIT (the form buff
    /// being destroyed) so the active transform's bar can be re-applied. Excludes the
    /// boss form buffs (Dracula/Morgana) on purpose — those are torn down only on revert.
    /// </summary>
    public static bool IsNativeShapeshiftBuff(int guid)
    {
        foreach (int g in _nativeFormGuids) if (g == guid) return true;
        return false;
    }

    /// <summary>
    /// The native form-buff GUID a unit maps to (Wolf/Bear/…), or 0 if none.
    /// Gated by the <c>Transform_RealFormWhenAvailable</c> kill-switch. Curated
    /// boss forms (Dracula/Morgana) are NOT routed here — they live in _forms.
    /// </summary>
    public static int PickNativeForm(int unitGuid)
    {
        if (!Beelzebub.Config.Settings.Transform_RealFormWhenAvailable.Value) return 0;
        if (_forms.ContainsKey(unitGuid)) return 0; // curated boss form wins
        return ShapeshiftService.PickFormFor(new PrefabGUID(unitGuid))._Value;
    }

    /// <summary>True if this unit transforms via a real form buff (curated boss OR native).</summary>
    public static bool IsFormUnit(int unitGuid) => _forms.ContainsKey(unitGuid) || PickNativeForm(unitGuid) != 0;

    /// <summary>Form/phase count for a unit: curated set count, or 1 for a native form, else 0.</summary>
    public static int FormCountFor(int unitGuid)
    {
        if (_forms.TryGetValue(unitGuid, out var f)) return f.FormCount;
        return PickNativeForm(unitGuid) != 0 ? 1 : 0;
    }

    /// <summary>
    /// Resolve the form to apply for a unit. Curated boss units return their
    /// hand-authored multi-set form (ignores <paramref name="unitAbilities"/>);
    /// native-matched units return a single-set form built from their own
    /// abilities. Returns false for units with no form (caller uses ability-only).
    /// </summary>
    public static bool TryResolve(int unitGuid, IReadOnlyList<int> unitAbilities, out BossForm form)
    {
        if (_forms.TryGetValue(unitGuid, out form)) return true;

        int native = PickNativeForm(unitGuid);
        if (native != 0 && unitAbilities != null && unitAbilities.Count > 0)
        {
            var set = new int[unitAbilities.Count];
            for (int i = 0; i < set.Length; i++) set[i] = unitAbilities[i];
            form = new BossForm
            {
                Label = "Native form",
                FormBuffGuid = native,
                FormNames = new[] { "Form" },
                FormSets = new[] { set },
            };
            return true;
        }

        form = null;
        return false;
    }

    /// <summary>
    /// All form-buff GUIDs revert must be able to destroy: curated boss forms +
    /// every native shapeshift form we may have applied.
    /// </summary>
    public static IEnumerable<int> FormBuffGuids
    {
        get
        {
            foreach (var f in _forms.Values) yield return f.FormBuffGuid;
            foreach (var g in _nativeFormGuids) yield return g;
        }
    }
}
