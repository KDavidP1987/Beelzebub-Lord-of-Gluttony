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
        // Morgana → CorruptedSerpent form buff (Transformation_SnakePhaseBuff).
        // v0.38.0: two switchable kits on the serpent form (like Dracula), via .beelz phase 1/2.
        //   Form 1 "Serpent" — the proven Bloodcraft CorruptedSerpent set (melee + a few spectral).
        //   Form 2 "Spectral" — her ranged spectral barrage kit. EXPERIMENTAL: several of these
        //     are heavily scripted boss casts and may not all fire for a player; curate per testing.
        { 591725925, new BossForm {
            Label = "Morgana",
            FormBuffGuid = -1859425781,
            FormNames = new[] { "Serpent", "Spectral" },
            FormSets = new[]
            {
                // Form 1 Serpent: MeleeAttack, GroundPiercer, QuickTeleport, MistSpinners,
                // CrossWindSlash, SpectralBlast, SpectralBeam, EyeOfTheCorruption.
                new[] { 2134120100, -668068170, -1940289109, 1278045964, 846291757, 1173842428, 2099754785, 734658196 },
                // Form 2 Spectral: SpectralBeam (primary), QuickTeleport, SpectralSwarm,
                // CorruptionFountain, SpectralOrbBarrage, RingsOfTerror, SpectralHell,
                // TravelingOrbBarrage (ult).
                new[] { 2099754785, -1940289109, 1485893437, 1298623256, 1990869093, -616120746, 1185642044, 1242557903 },
            },
        }},
    };

    public static bool TryGet(int unitGuid, out BossForm form) => _forms.TryGetValue(unitGuid, out form);
    public static bool Has(int unitGuid) => _forms.ContainsKey(unitGuid);

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
