using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Beelzebub.Services;

/// <summary>
/// v0.132.0 (P1 step 3) — the ONE table of per-ability config fields. It drives:
///   * alias resolution for <c>.beelz admin ability</c> / <c>ability-set</c> / <c>tune</c> (one parser),
///   * <see cref="IsBaked"/> — which fields need a prefab re-tune after a change (the old hand-kept
///     switch missed <c>leapheight</c> and every alias, so those edits only applied after a restart),
///   * numeric validation (finite + range) shared by the command path, the rule loader and the tuner,
///   * the help/field lists printed by the commands and the field table in docs/ABILITY_CONFIG.md.
/// Add a field HERE first; <see cref="AbilityRules.SetAbilityField"/> switches on the canonical name.
/// </summary>
internal static class AbilityFieldTable
{
    public enum Kind { OnOff, OnOffClear, Float, Int, Choice, Text, List }

    /// <summary>Where the field takes effect.</summary>
    public enum Scope
    {
        Rule,      // capture / availability / curation — no re-tune
        Baked,     // global prefab edit, re-applied by AbilityTuningService (changes the source NPC too)
        Runtime,   // player-only runtime behaviour (never touches prefabs)
    }

    public sealed class Field
    {
        public string Name;
        public string[] Aliases = Array.Empty<string>();
        public Kind Kind;
        public Scope Scope;
        public float Min, Max;
        public bool Tunable;   // offered by `.beelz admin tune` (shaping knobs)
        public string Help;
    }

    static Field F(string name, Kind kind, Scope scope, string help, string[] aliases = null,
        float min = 0f, float max = 0f, bool tunable = false)
        => new() { Name = name, Kind = kind, Scope = scope, Help = help, Aliases = aliases ?? Array.Empty<string>(), Min = min, Max = max, Tunable = tunable };

    public static readonly Field[] All =
    {
        // --- availability / capture rules ---
        F("enabled", Kind.OnOff, Scope.Rule, "on|off — admin kill-switch (capture + use)"),
        F("weapons", Kind.List, Scope.Rule, "weapon allow-list (sword,axe) and/or !blocks (!sword); 'any' clears"),
        F("forms", Kind.List, Scope.Rule, "form allow-list (wolf) and/or !blocks (!mounted); 'any' clears"),
        F("transformonly", Kind.OnOff, Scope.Rule, "on|off — only usable while transformed"),
        F("difficulty", Kind.Choice, Scope.Rule, "Basic|Brutal capture gate"),
        F("phase", Kind.Int, Scope.Rule, "boss phase this ability belongs to (>=1)", min: 1, max: 20),
        F("allowdenied", Kind.OnOff, Scope.Rule, "on|off — force-allow past deny patterns"),
        F("category", Kind.Choice, Scope.Rule, "Travel|Aoe|Projectile|Melee|Summon|Buff|WeaponSpell|Spell|Other|clear"),
        F("reviewstatus", Kind.Choice, Scope.Rule, "Unreviewed|Reviewed|Approved|Blocked|Hidden", new[] { "review", "status" }),
        F("reviewtag", Kind.Text, Scope.Rule, "free-text audit tag", new[] { "tag", "audittag" }),
        F("notes", Kind.Text, Scope.Rule, "free-text note"),

        // --- runtime (player casts) ---
        F("damagescale", Kind.Float, Scope.Runtime, "damage multiplier on captured casts (1.0 = no change)", min: 0.01f, max: 100f),
        F("cooldownscale", Kind.Float, Scope.Runtime, "cooldown multiplier on captured casts (1.0 = no change)", min: 0.01f, max: 100f),
        F("forcetimeout", Kind.Float, Scope.Runtime, "seconds — expire this ability's otherwise-indefinite buffs cast by a PLAYER (no prefab edit)", new[] { "effecttimeout", "bufftimeout" }, 0f, 3600f, tunable: true),
        F("powerwindow", Kind.Float, Scope.Runtime, "seconds the granted-cast power buff lasts (0 = default 1.5s)", new[] { "powerwindowseconds", "dmgwindow" }, 0f, 600f, tunable: true),
        F("summoncap", Kind.Int, Scope.Runtime, "max simultaneous uses of this summon (0 = unlimited)", new[] { "summonlimit", "maxsummons" }, 0f, 100f, tunable: true),
        F("summontimeout", Kind.Float, Scope.Runtime, "seconds before this ability's summons despawn (0 = never)", new[] { "summonlifetime", "summonduration" }, 0f, 36000f, tunable: true),
        F("summonpower", Kind.Float, Scope.Runtime, "summon power multiplier (Summon_PowerMode=OwnerRelative: minion power = owner power × Transform_SummonPowerFactor × this)", new[] { "summonpowerscale" }, 0.01f, 100f, tunable: true),
        F("cooldown", Kind.Float, Scope.Runtime, "absolute cooldown seconds on captured bar casts (wins over cooldownscale; still floored by Grant_MinimumCooldownSeconds)", new[] { "cooldownseconds", "cd" }, 0f, 3600f, tunable: true),
        F("summonunits", Kind.Int, Scope.Runtime, "max units one cast summons (0 = natural count)", new[] { "summonunitspercast", "unitspercast" }, 0f, 100f, tunable: true),

        // --- baked shaping (GLOBAL prefab edits; also change the source NPC) ---
        F("range", Kind.Float, Scope.Baked, "max cast range + projectile travel distance", new[] { "maxrange" }, 0f, 500f, tunable: true),
        F("charges", Kind.Int, Scope.Baked, "max charges (abilities that already use charges)", new[] { "maxcharges" }, 0f, 100f, tunable: true),
        F("chargetime", Kind.Float, Scope.Baked, "recharge seconds per charge", new[] { "chargeuptime" }, 0f, 3600f, tunable: true),
        F("aoe", Kind.Float, Scope.Baked, "area-of-effect max radius", new[] { "aoeradius", "radius" }, 0f, 100f, tunable: true),
        F("projspeed", Kind.Float, Scope.Baked, "projectile speed", new[] { "projectilespeed" }, 0f, 500f, tunable: true),
        F("leapheight", Kind.Float, Scope.Baked, "leap/travel apex height (vanilla boss leaps ~250)", new[] { "travelheight" }, 0f, 1000f, tunable: true),
        F("duration", Kind.Float, Scope.Baked, "applied buff/debuff duration seconds", new[] { "effectduration" }, 0f, 3600f, tunable: true),
        F("healing", Kind.Float, Scope.Baked, "healing multiplier (1.0 = no change)", new[] { "healmult", "healingmultiplier" }, 0f, 100f, tunable: true),
        F("interruptible", Kind.OnOffClear, Scope.Baked, "on|off|clear — player can self-cancel the cast", new[] { "interrupt" }, tunable: true),
        F("interruptonhit", Kind.OnOffClear, Scope.Baked, "on|off|clear — cast cancels when the caster is hit", new[] { "interruptattack", "breakonhit" }, tunable: true),
        F("freemove", Kind.OnOff, Scope.Baked, "on|off — free movement when the cast finishes", tunable: true),
        F("freelymove", Kind.Float, Scope.Baked, "seconds into the cast before movement is freed", new[] { "freemovesecs", "freemoveafter" }, 0f, 60f, tunable: true),
        F("castspeed", Kind.Float, Scope.Baked, "move speed during the cast (0 = rooted .. 1 = full)", new[] { "castmovementspeed" }, 0f, 1f, tunable: true),
        F("maxstacks", Kind.Int, Scope.Baked, "how many times the ability's own buff can stack (1-255)", new[] { "stacks" }, 1f, 255f, tunable: true),
        F("projcount", Kind.Int, Scope.Baked, "projectiles per volley on fan/multishot/cluster abilities (capped at 3x the ability's own count, max 16)", new[] { "projectilecount", "projectiles" }, 1f, 16f, tunable: true),
        F("knockback", Kind.Float, Scope.Baked, "knockback multiplier (distance + push time; 1.0 = no change, 0 = none)", new[] { "knockbackscale" }, 0f, 10f, tunable: true),
        F("lifetime", Kind.Float, Scope.Baked, "seconds the ability's projectiles/areas last (buff length is 'duration')", new[] { "spawnlifetime" }, 0.1f, 600f, tunable: true),
        F("casttime", Kind.Float, Scope.Baked, "EXPERIMENTAL cast-time multiplier (windup + recovery; not channels/hold-to-cast/charges)", new[] { "casttimescale" }, 0.1f, 10f, tunable: true),
        F("allowglobalsharededit", Kind.OnOff, Scope.Baked, "on|off — allow baked edits on prefabs SHARED with other abilities/bosses (only when the chain is fully decoded)", new[] { "sharededit" }),
    };

    static readonly Dictionary<string, Field> _byToken = Build();

    static Dictionary<string, Field> Build()
    {
        var d = new Dictionary<string, Field>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in All)
        {
            d[f.Name] = f;
            foreach (var a in f.Aliases) d[a] = f;
        }
        return d;
    }

    /// <summary>Canonical field for a name or alias (case-insensitive); null if unknown.</summary>
    public static Field Resolve(string token)
        => token != null && _byToken.TryGetValue(token.Trim(), out var f) ? f : null;

    /// <summary>Does changing this field (name or alias) need a baked prefab re-tune?</summary>
    public static bool IsBaked(string token) => Resolve(token)?.Scope == Scope.Baked;

    public static string ValidNames() => string.Join(", ", All.Select(f => f.Name));
    public static string TunableNames() => string.Join(" | ", All.Where(f => f.Tunable).Select(f => f.Name));

    public static bool IsClearToken(string v)
        => v is not null && (v.Equals("clear", StringComparison.OrdinalIgnoreCase)
            || v.Equals("none", StringComparison.OrdinalIgnoreCase) || v.Equals("null", StringComparison.OrdinalIgnoreCase));

    /// <summary>Parse + validate a numeric value for a Float/Int field: finite and within [Min, Max].</summary>
    public static bool TryParseNumber(Field f, string raw, out float value, out string error)
    {
        value = 0f;
        error = null;
        if (f.Kind == Kind.Int)
        {
            if (!int.TryParse((raw ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
            { error = $"{f.Name} expects a whole number {Range(f)}, or clear. ({f.Help})"; return false; }
            value = i;
        }
        else if (!float.TryParse((raw ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        { error = $"{f.Name} expects a number {Range(f)}, or clear. ({f.Help})"; return false; }
        if (!InRange(f, value)) { error = $"{f.Name} must be {Range(f)} (got {raw}). ({f.Help})"; return false; }
        return true;
    }

    public static bool InRange(Field f, float v) => float.IsFinite(v) && v >= f.Min && v <= f.Max;

    /// <summary>Runtime guard used by the tuner/loader: a hand-edited JSON value outside the table's range is
    /// ignored (with a warning) instead of being written to a prefab.</summary>
    public static bool IsValidValue(string fieldName, float v)
    {
        var f = Resolve(fieldName);
        return f == null || f.Kind is not (Kind.Float or Kind.Int) || InRange(f, v);
    }

    static string Range(Field f) => f.Kind == Kind.Int
        ? $"between {f.Min:0} and {f.Max:0}"
        : $"between {f.Min.ToString("0.##", CultureInfo.InvariantCulture)} and {f.Max.ToString("0.##", CultureInfo.InvariantCulture)}";

    /// <summary>Markdown table of every field (docs/ABILITY_CONFIG.md §4 is regenerated from this).</summary>
    public static string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("| Field | Aliases | Scope | Values | Meaning |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var f in All)
        {
            string vals = f.Kind switch
            {
                Kind.Float or Kind.Int => Range(f),
                Kind.OnOff => "on / off",
                Kind.OnOffClear => "on / off / clear",
                _ => "see meaning",
            };
            sb.AppendLine($"| `{f.Name}` | {(f.Aliases.Length == 0 ? "" : string.Join(", ", f.Aliases.Select(a => $"`{a}`")))} | {f.Scope} | {vals} | {f.Help} |");
        }
        return sb.ToString();
    }
}
