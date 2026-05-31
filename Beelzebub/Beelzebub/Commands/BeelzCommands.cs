using System.Collections.Generic;
using System.Linq;
using System.Text;
using Beelzebub.Services;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using VampireCommandFramework;

namespace Beelzebub.Commands;

[CommandGroup("beelz")]
internal static class BeelzCommands
{
    [Command("help", description: "Show a short walkthrough of how Beelzebub works.")]
    public static void Help(ChatCommandContext ctx)
    {
        ctx.Reply("Beelzebub — kill units, devour their abilities.");
        ctx.Reply("1. Kill any unit → chance to capture its abilities. V-Bloods are tagged separately.");
        ctx.Reply("2. .beelz list — see what you've collected.");
        ctx.Reply("3. .beelz grant <slot 1-6> <index> — universal slot (any weapon).");
        ctx.Reply("   For a weapon-specific loadout: .beelz weapon-grant <weapon|auto> <slot> <index>");
        ctx.Reply("   Wielding that weapon overrides your universal slots with the weapon-specific ones.");
        ctx.Reply("4. RARE jackpot = DEVOUR: learn ALL of a unit's abilities in one kill (vs one at a time).");
        ctx.Reply("   Some bosses unlock a true TRANSFORM (.beelz transforms / transform / revert) — Dracula, Morgana, Werewolf, Golem, Gargoyle. Customize each form's kit with .beelz tform.");
        ctx.Reply("5. .beelz verbosity <silent|summary|verbose> — tune chat noise.");
        ctx.Reply("Full command list: .beelz commands. Group detail: .beelz admin help · .beelz api help · .beelz hotkey help.");
    }

    [Command("commands", description: "List all Beelzebub player commands. For a specific group's detail: .beelz admin help · .beelz api help · .beelz hotkey help.")]
    public static void Commands(ChatCommandContext ctx)
    {
        ctx.Reply("=== Beelzebub player commands === (group detail: .beelz admin help · .beelz api help · .beelz hotkey help)");
        ctx.Reply("-- COLLECTION --");
        ctx.Reply(".beelz list [vblood|shard|regular] [page] — your captured abilities + slot binds");
        ctx.Reply(".beelz transforms [filter] — your transform unlocks");
        ctx.Reply(".beelz bestiary [page] / .beelz bestiary unit <name> — collection book, per unit");
        ctx.Reply(".beelz catalog [page] — curated boss-kit reference (only player-renderable forms transform; collect other units as abilities)");
        ctx.Reply(".beelz search <term> — search your captures · .beelz info <index|name> — full ability detail");
        ctx.Reply(".beelz progress — your collection-completion %");
        ctx.Reply("-- SLOTS / LOADOUT --");
        ctx.Reply(".beelz grant <slot 1-6> <index> / .beelz unslot <slot> — universal slot binds");
        ctx.Reply(".beelz weapon-grant <weapon|auto> <slot> <index> / weapon-unslot <weapon> <slot> — per-weapon binds (unarmed = its own family; swapping weapons auto-switches the set)");
        ctx.Reply(".beelz form-grant <form|auto> <slot> <index> / form-unslot <form> <slot> — per-FORM binds (Wolf/Bear/Rat/Spider/Toad/Werewolf/Gargoyle; applies in the shapeshift form — requires Forms_CustomAbilities_Enabled)");
        ctx.Reply(".beelz loadouts — view your universal 'basic' set + each per-weapon set, and which is active");
        ctx.Reply(".beelz preset save|load|list|delete <name> — slot loadout presets");
        ctx.Reply(".beelz cast <hotkey|index> — cast a capture on demand (extra hotkeys: .beelz hotkey help)");
        ctx.Reply(".beelz active / .beelz current — what's effectively on your bar right now");
        ctx.Reply(".beelz clearbar [all|universal|<weapon>|<form>] — clear bound slots (no confirm) · .beelz resetbar CONFIRM — full reset to vanilla · .beelz refresh — re-apply your bar");
        ctx.Reply("-- TRANSFORM (player-renderable forms: Dracula, Morgana, Werewolf, Golem, Gargoyle; other units' kits are learned as abilities / Devoured) --");
        ctx.Reply(".beelz transforms / .beelz transform <name> / .beelz revert — your unlocked transformations");
        ctx.Reply(".beelz preview <name> — a transform's abilities · .beelz phase [n] — switch a form's phase kit");
        ctx.Reply(".beelz tform <unit> abilities|set <phase> <slot> <index>|clear|defaults — customize a transform's ability loadout");
        ctx.Reply(".beelz summon [n] / .beelz detonate — fire your transform's signature summon / AoE");
        ctx.Reply(".beelz summons [stash|restore|clear|status] / .beelz tp — manage summons (works for captured summon abilities too, not just transforms; waygate-safe)");
        ctx.Reply("-- MANAGE --");
        ctx.Reply(".beelz forget <i> / .beelz forget-transform <i> — delete one entry");
        ctx.Reply(".beelz clear CONFIRM — wipe ALL your data (warns first; use .beelz resetbar to keep captures)");
        ctx.Reply(".beelz verbosity <silent|summary|verbose> — chat detail level");
        ctx.Reply(".beelz help — walkthrough · .beelz commands — this list");
        ctx.Reply("Group help: .beelz admin help (admins) · .beelz api help (BCH/UI) · .beelz hotkey help");
    }

    [Command("loadouts", description: "Show your slot loadouts: the universal 'basic' set + each per-weapon set, and which is active for your equipped weapon. Usage: .beelz loadouts")]
    public static void Loadouts(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        Entity character = ctx.Event.SenderCharacterEntity;
        ulong steamId = character.GetSteamId();
        if (steamId == 0) { ctx.Reply("Could not resolve your Steam ID."); return; }

        var weapon = SlotApply.GetCurrentWeapon(character);
        ctx.Reply($"=== Your loadouts === (wielding: {weapon}). A per-weapon set overrides the universal set on its slots; swapping weapons switches sets automatically. Unarmed/spellcasting is its own weapon family.");

        EmitLoadoutBucket(ctx, "UNIVERSAL (basic / fallback — fires on any weapon)", Core.AbilityRegistry.GetSlots(steamId));

        var byWeapon = Core.AbilityRegistry.AllWeaponSlots(steamId);
        if (byWeapon.Count == 0)
            ctx.Reply("No per-weapon loadouts yet. Build one: .beelz weapon-grant <weapon|auto> <slot 1-6> <index>");
        else
            foreach (var (fam, slots) in byWeapon.OrderBy(kv => kv.Key.ToString()))
            {
                bool active = fam == weapon;
                EmitLoadoutBucket(ctx, active ? $"{fam} (ACTIVE — currently wielded)" : fam.ToString(), slots);
            }

        // v0.100.0: per-FORM loadouts (apply inside a shapeshift form; requires Forms_CustomAbilities_Enabled).
        var byForm = Core.AbilityRegistry.AllFormSlots(steamId);
        if (byForm.Count > 0)
        {
            var activeForm = ShapeshiftAbilityService.GetCurrentForm(character);
            foreach (var (form, slots) in byForm.OrderBy(kv => kv.Key.ToString()))
                EmitLoadoutBucket(ctx, form == activeForm ? $"FORM {form} (ACTIVE)" : $"FORM {form}", slots);
        }
    }

    static void EmitLoadoutBucket(ChatCommandContext ctx, string label, IReadOnlyDictionary<int, int> slots)
    {
        if (slots == null || slots.Count == 0) { ctx.Reply($"-- {label}: (empty)"); return; }
        ctx.Reply($"-- {label}:");
        foreach (int slot in slots.Keys.OrderBy(s => s))
        {
            int guid = slots[slot];
            string name = Core.AbilityMetadata?.Resolve(guid).Name;
            if (string.IsNullOrEmpty(name)) name = new PrefabGUID(guid).GetPrefabName();
            ctx.Reply($"   slot {slot}: {name}");
        }
    }

    [Command("list", description: "List your captured abilities + slot assignments. Optional filter: vblood | shard | regular. Usage: .beelz list [filter] [page]. Paginated 15/page.")]
    public static void List(ChatCommandContext ctx, string arg = null, int page = 1)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        if (steamId == 0) { ctx.Reply("Could not resolve your Steam ID."); return; }

        // v0.39.0: first arg is a page number (".beelz list 2") OR a filter keyword
        // (".beelz list vblood [page]").
        string filter = null;
        if (!string.IsNullOrWhiteSpace(arg))
        {
            if (int.TryParse(arg.Trim(), out int p)) page = p;
            else filter = arg.Trim().ToLowerInvariant();
        }
        bool fVBlood = filter is "vblood" or "vbloods" or "v";
        bool fShard = filter is "shard" or "shards" or "shardboss";
        bool fRegular = filter is "regular" or "r";

        var captured = Core.AbilityRegistry.ListFor(steamId);
        var slots = Core.AbilityRegistry.GetSlots(steamId);

        if (captured.Count == 0 && slots.Count == 0)
        {
            ctx.Reply("You haven't captured any abilities yet. Go kill something.");
            return;
        }

        // Index over the FULL list (idx stays valid for .beelz grant), then filter.
        var entries = captured.Select((c, idx) => (idx, ability: c)).ToList();
        if (fVBlood) entries = entries.Where(t => t.ability.Source == CaptureSource.VBlood).ToList();
        else if (fShard) entries = entries.Where(t => Core.Transforms.IsShardBoss(t.ability.UnitPrefabGuid)).ToList();
        else if (fRegular) entries = entries.Where(t => t.ability.Source == CaptureSource.Regular).ToList();

        // v0.21.0: one chat reply per block (VCF 510-byte cap). Page 1 adds the slot header.
        const int pageSize = 15;
        int total = entries.Count;
        int pages = total == 0 ? 1 : (total + pageSize - 1) / pageSize;
        if (page < 1) page = 1;
        if (page > pages) page = pages;

        if (page == 1) EmitSlotHeader(ctx, steamId, slots);

        string scope = fVBlood ? " (V-Bloods)" : fShard ? " (shard bosses)" : fRegular ? " (regular)" : "";
        string count = total != captured.Count ? $"{total} of {captured.Count}" : $"{total}";
        ctx.Reply($"Captured {count} ability(ies){scope}. Page {page}/{pages}:");
        if (total == 0) { ctx.Reply("  (none match that filter — try: vblood | shard | regular)"); return; }

        // Sort: VBlood first, then by unit name, preserving original index.
        var ordered = entries
            .OrderByDescending(t => t.ability.Source == CaptureSource.VBlood)
            .ThenBy(t => Core.AbilityMetadata.ResolveUnitName(t.ability.UnitPrefabGuid), System.StringComparer.OrdinalIgnoreCase)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        foreach (var (idx, ability) in ordered)
        {
            string src = ability.Source == CaptureSource.VBlood ? "V" : "R";
            string unitName = Core.AbilityMetadata.ResolveUnitName(ability.UnitPrefabGuid);
            string abilityName = Core.AbilityMetadata.Resolve(ability.AbilityPrefabGuid).Name;
            ctx.Reply($"  {idx,3}: [{src}] {unitName} → {abilityName}");
        }

        if (pages > 1)
        {
            int nextPage = page < pages ? page + 1 : 1;
            string nextCmd = filter != null ? $".beelz list {filter} {nextPage}" : $".beelz list {nextPage}";
            ctx.Reply($"More? {nextCmd}   |   Search: .beelz search <term>");
        }
    }

    // v0.40.0: per-(player, ability) cooldown for on-demand .beelz cast (a force-cast
    // isn't gated by a bar slot, so we enforce the ability's own cooldown ourselves).
    static readonly System.Collections.Generic.Dictionary<(ulong, int), System.DateTime> _castCooldowns = new();

    [Command("cast", description: "Cast a captured ability on demand — beyond your 6 slots. Usage: .beelz cast <hotkey name | list index>. BloodCraftHub buttons invoke this.")]
    public static void Cast(ChatCommandContext ctx, string nameOrIndex)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (!Beelzebub.Config.Settings.Hotkeys_Enabled.Value)
        {
            ctx.Reply("On-demand casting is disabled by the server admin (Hotkeys_Enabled).");
            return;
        }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();

        // Resolve a hotkey name first, then fall back to a captured-list index.
        int abilityGuid = Core.AbilityRegistry.GetHotkey(steamId, nameOrIndex);
        if (abilityGuid == 0 && int.TryParse(nameOrIndex?.Trim(), out int idx))
        {
            var captured = Core.AbilityRegistry.ListFor(steamId);
            if (idx >= 0 && idx < captured.Count) abilityGuid = captured[idx].AbilityPrefabGuid;
        }
        if (abilityGuid == 0)
        {
            ctx.Reply($"No hotkey or captured ability matches '{nameOrIndex}'. Bind one with .beelz hotkey set <name> <index>, or pass a .beelz list index.");
            return;
        }

        var ability = new PrefabGUID(abilityGuid);
        string abilityName = ability.GetPrefabName();
        if (!Core.AbilityRules.IsEnabled(abilityName, abilityGuid)) { ctx.Reply($"'{abilityName}' is currently disabled by the server admin."); return; }
        if (Core.AbilityRules.IsTransformOnlyEnforced(abilityName, abilityGuid)) { ctx.Reply($"'{abilityName}' is reserved for .beelz transform."); return; }

        // Per-ability cooldown — the ability's own cooldown (min 1s anti-spam).
        var info = Core.AbilityMetadata?.Resolve(abilityGuid);
        double cd = info?.CooldownSeconds ?? 0;
        // v0.44.0: per-ability CooldownScale (ability_rules.json) is now LIVE for force-casts —
        // admins can lengthen/shorten an on-demand ability's cooldown. Floor applied after scaling.
        cd *= Core.AbilityRules.GetCooldownScale(abilityName);
        if (cd < 1.0) cd = 1.0;
        string label = (info != null && !string.IsNullOrEmpty(info.Name)) ? info.Name : abilityName;
        var key = (steamId, abilityGuid);
        var now = System.DateTime.UtcNow;
        if (_castCooldowns.TryGetValue(key, out var until) && until > now)
        {
            ctx.Reply($"{label} on cooldown ({(until - now).TotalSeconds:F0}s).");
            return;
        }

        if (!Beelzebub.Services.ForceCastService.Cast(ctx.Event.SenderCharacterEntity, ability))
        {
            ctx.Reply("Cast failed (see server log).");
            return;
        }
        _castCooldowns[key] = now.AddSeconds(cd);
        Core.Chat.Send(ctx.Event.SenderCharacterEntity, Verbosity.Verbose, $"Cast {label}.");
        Core.Chat.SendEvent(ctx.Event.SenderCharacterEntity, $"[BEELZ:event] type=cast a={abilityGuid} an={abilityName}");
    }

    /// <summary>
    /// v0.21.0: shared slot-binding header used by .beelz list page 1 and .beelz search.
    /// Emits one reply per bound bucket (universal + per-weapon) to stay under VCF cap.
    /// </summary>
    static void EmitSlotHeader(ChatCommandContext ctx, ulong steamId, IReadOnlyDictionary<int, int> slots)
    {
        if (slots.Count > 0)
        {
            var sb = new StringBuilder("Universal slots: ");
            bool first = true;
            foreach (var (slot, abilityGuid) in slots.OrderBy(kv => kv.Key))
            {
                if (!first) sb.Append(", ");
                sb.Append('[').Append(slot).Append("] ").Append(Core.AbilityMetadata.Resolve(abilityGuid).Name);
                first = false;
            }
            ctx.Reply(sb.ToString());
        }

        var weaponBuckets = Core.AbilityRegistry.AllWeaponSlots(steamId);
        foreach (var (weapon, weaponMap) in weaponBuckets.OrderBy(kv => kv.Key.ToString()))
        {
            if (weaponMap.Count == 0) continue;
            var sb = new StringBuilder();
            sb.Append(weapon).Append(" slots: ");
            bool first = true;
            foreach (var (slot, abilityGuid) in weaponMap.OrderBy(kv => kv.Key))
            {
                if (!first) sb.Append(", ");
                sb.Append('[').Append(slot).Append("] ").Append(Core.AbilityMetadata.Resolve(abilityGuid).Name);
                first = false;
            }
            ctx.Reply(sb.ToString());
        }

        var currentWeapon = Beelzebub.Services.SlotApply.GetCurrentWeapon(ctx.Event.SenderCharacterEntity);
        if (currentWeapon != Beelzebub.Services.WeaponFamily.None)
        {
            ctx.Reply($"Currently wielding: {currentWeapon}");
        }
    }

    [Command("search", description: "Search your captured abilities. Substring match against ability + unit names. Usage: .beelz search <term>. Returns up to 25 matches.")]
    public static void Search(ChatCommandContext ctx, string term)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (string.IsNullOrWhiteSpace(term)) { ctx.Reply("Search term required. Usage: .beelz search <term>"); return; }

        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var captured = Core.AbilityRegistry.ListFor(steamId);
        if (captured.Count == 0) { ctx.Reply("You haven't captured any abilities yet."); return; }

        string needle = term.Trim();
        const int maxResults = 25;

        // Match against ability name OR unit name. Preserve original index so the
        // result IDs are still valid for .beelz grant <slot> <index>.
        // v0.38.0: display the resolved in-game names, but still match against BOTH the
        // friendly name AND the raw prefab name so either works as a search term.
        var matches = captured
            .Select((c, idx) =>
            {
                string abRaw = new PrefabGUID(c.AbilityPrefabGuid).GetPrefabName();
                string unitRaw = new PrefabGUID(c.UnitPrefabGuid).GetPrefabName();
                var abInfo = Core.AbilityMetadata?.Resolve(c.AbilityPrefabGuid);
                string abName = (abInfo != null && !string.IsNullOrEmpty(abInfo.Name)) ? abInfo.Name : abRaw;
                string unitName = Core.AbilityMetadata?.ResolveUnitName(c.UnitPrefabGuid) ?? unitRaw;
                return (idx, ability: c, abName, unitName, abRaw, unitRaw);
            })
            .Where(t => (t.abName?.Contains(needle, System.StringComparison.OrdinalIgnoreCase) ?? false)
                     || (t.unitName?.Contains(needle, System.StringComparison.OrdinalIgnoreCase) ?? false)
                     || (t.abRaw?.Contains(needle, System.StringComparison.OrdinalIgnoreCase) ?? false)
                     || (t.unitRaw?.Contains(needle, System.StringComparison.OrdinalIgnoreCase) ?? false))
            .Take(maxResults + 1) // +1 to detect "more results available"
            .ToList();

        if (matches.Count == 0)
        {
            ctx.Reply($"No matches for '{needle}'.");
            return;
        }

        bool truncated = matches.Count > maxResults;
        if (truncated) matches = matches.Take(maxResults).ToList();

        ctx.Reply($"Search '{needle}': {matches.Count}{(truncated ? "+ (truncated)" : "")} match(es).");
        foreach (var m in matches)
        {
            string src = m.ability.Source == CaptureSource.VBlood ? "V" : "R";
            ctx.Reply($"  {m.idx,3}: [{src}] {m.unitName} → {m.abName}");
        }
        if (truncated)
        {
            ctx.Reply($"…more than {maxResults} results. Narrow your search term.");
        }
    }

    [Command("info", description: "Show full info for an ability: title, description, school, type, cooldown, cast time, source NPCs. Usage: .beelz info <index|name>. Index = your .beelz list entry; name = substring search across all known abilities.")]
    public static void Info(ChatCommandContext ctx, string arg)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (string.IsNullOrWhiteSpace(arg)) { ctx.Reply("Usage: .beelz info <index|name>"); return; }

        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();

        // Try numeric index against the player's captured list first.
        if (int.TryParse(arg.Trim(), out int idx))
        {
            var captured = Core.AbilityRegistry.ListFor(steamId);
            if (idx >= 0 && idx < captured.Count)
            {
                int guid = captured[idx].AbilityPrefabGuid;
                RenderAbilityInfo(ctx, guid);
                return;
            }
            // numeric but out of range — fall through to name search using the digits as a query
        }

        // Substring name search.
        var hits = Core.AbilityMetadata.SearchByName(arg);
        if (hits.Count == 0)
        {
            // Fallback: maybe the arg matches a prefab name we have in the global map.
            // Iterate Core.PrefabNames (built at init) and humanize-match.
            string needle = arg.Trim();
            foreach (var (rawGuid, rawName) in Core.PrefabNames)
            {
                if (!rawName.StartsWith("AB_", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (rawName.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                hits.Add(Core.AbilityMetadata.Resolve(rawGuid));
                if (hits.Count >= 10) break;
            }
        }

        if (hits.Count == 0)
        {
            ctx.Reply($"No ability found matching '{arg}'. Try .beelz list (then .beelz info <index>) or search a shorter substring.");
            return;
        }

        if (hits.Count > 1)
        {
            ctx.Reply($"{hits.Count} match(es) for '{arg}' — showing top result. Refine search for others:");
            for (int i = 0; i < System.Math.Min(5, hits.Count); i++)
            {
                ctx.Reply($"  • {hits[i].Name} [{hits[i].School ?? "?"}]");
            }
            ctx.Reply("---");
        }
        RenderAbilityInfo(ctx, hits[0].AbilityGroupGuid);
    }

    /// <summary>Format an AbilityInfo into a multi-line chat report.</summary>
    static void RenderAbilityInfo(ChatCommandContext ctx, int abilityGroupGuid)
    {
        var info = Core.AbilityMetadata.Resolve(abilityGroupGuid);

        // Header line with title + school/type tags.
        var tags = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(info.School)) tags.Add(info.School);
        if (!string.IsNullOrEmpty(info.Type)) tags.Add(info.Type);
        if (info.Incompatible) tags.Add("⚠ INCOMPATIBLE");
        string tagStr = tags.Count > 0 ? $" [{string.Join(" / ", tags)}]" : "";
        ctx.Reply($"{info.Name}{tagStr}");

        // v0.24.2: incompatibility warning on its own line so the player
        // understands why the ability "doesn't work" when they cast it.
        if (info.Incompatible)
        {
            string reason = !string.IsNullOrEmpty(info.IncompatibleReason)
                ? $" ({info.IncompatibleReason})"
                : "";
            ctx.Reply($"⚠ This ability is known to misbehave when cast by a player{reason}. The cast animates but the effect doesn't complete. See the V-Blood audit doc for the chain-failure class.");
        }

        // Description (multi-line — split into chat-sized chunks).
        if (!string.IsNullOrEmpty(info.Description))
        {
            string desc = info.Description.Replace("\\n", " ").Replace("\n", " ").Trim();
            // Substitute parameters into %placeholder% values where present.
            if (info.Parameters != null)
            {
                foreach (var (k, v) in info.Parameters)
                {
                    desc = desc.Replace("%" + k + "%", v);
                }
            }
            ctx.Reply(desc);
        }
        else
        {
            ctx.Reply("(no description curated yet — admin can override via ability_metadata_overrides.json)");
        }

        // Stats line — combines curated + ECS-derived.
        var stats = new System.Collections.Generic.List<string>();
        if (info.CooldownSeconds.HasValue) stats.Add($"cd {info.CooldownSeconds.Value:F1}s");
        if (info.CastTimeSeconds.HasValue) stats.Add($"cast {info.CastTimeSeconds.Value:F2}s");
        if (info.MaxRange.HasValue && info.MaxRange.Value > 0) stats.Add($"range {info.MinRange ?? 0:F0}-{info.MaxRange.Value:F0}");
        if (!string.IsNullOrEmpty(info.BehaviorType)) stats.Add(info.BehaviorType);
        if (stats.Count > 0) ctx.Reply("Stats: " + string.Join(" · ", stats));

        // Source NPCs.
        if (info.SourceNpcs != null && info.SourceNpcs.Count > 0)
        {
            var names = info.SourceNpcs.Select(n => n.Name).Where(s => !string.IsNullOrEmpty(s));
            ctx.Reply("Source: " + string.Join(", ", names));
        }

        // Admin rules context (policy from ability_rules.json, distinct from metadata).
        try
        {
            string abName = new PrefabGUID(abilityGroupGuid).GetPrefabName() ?? "";
            bool rateOverridden = Core.AbilityRules.TryGetRateOverride(abName, out float rateR, out float rateV);
            float damageScale = Core.AbilityRules.GetDamageScale(abName);
            if (rateOverridden || System.Math.Abs(damageScale - 1f) > 0.001f)
            {
                var policy = new System.Collections.Generic.List<string>();
                if (rateOverridden) policy.Add($"drop R={rateR:P0} V={rateV:P0}");
                if (System.Math.Abs(damageScale - 1f) > 0.001f) policy.Add($"damage×{damageScale:F2}");
                ctx.Reply("Admin policy: " + string.Join(" · ", policy));
            }
        }
        catch { /* policy lookup is best-effort */ }

        // Footer with provenance.
        var sources = new System.Collections.Generic.List<string>();
        if (info.HasOverrideEntry) sources.Add("override");
        else if (info.HasShippedEntry) sources.Add("shipped");
        else sources.Add("fallback");
        sources.Add($"id {abilityGroupGuid}");
        ctx.Reply($"({string.Join(", ", sources)})");
    }

    /// <summary>
    /// v0.76.0: resolve a `.beelz grant`-style selector to a captured-list index. An in-range value
    /// (0..count-1) is treated as the list index (backward compatible); any other value is matched
    /// against the captured abilities' PrefabGUIDs (so a player can pass the stable ability ID instead
    /// of a shifting index). Returns the index, or -1 with a message in <paramref name="error"/>.
    /// </summary>
    private static int ResolveCapturedSelector(System.Collections.Generic.IReadOnlyList<Beelzebub.Services.CapturedAbility> captured, int indexOrId, out string error)
    {
        error = null;
        if (captured.Count == 0) { error = "You have no captured abilities yet. Defeat units to capture their abilities, then .beelz list."; return -1; }
        if (indexOrId >= 0 && indexOrId < captured.Count) return indexOrId;   // list index (backward compatible)
        for (int i = 0; i < captured.Count; i++)                              // otherwise: match the ability PrefabGUID
            if (captured[i].AbilityPrefabGuid == indexOrId) return i;
        error = $"'{indexOrId}' is neither a valid list index (0-{captured.Count - 1}) nor an ability ID you've captured. Use .beelz list to see your indices + IDs.";
        return -1;
    }

    /// <summary>v0.91.0: parse a slot token — a number in the bindable range, or the named
    /// 'primary' (left-click attack, slot 0) / 'ultimate' (R, slot 7) slots.</summary>
    private static bool TryParseSlotToken(string token, out int slot, out string error)
    {
        slot = -1; error = null;
        string t = (token ?? "").Trim().ToLowerInvariant();
        if (t is "primary" or "p" or "left" or "leftclick" or "attack") { slot = Beelzebub.Services.AbilityRegistry.PrimarySlot; return true; }
        if (t is "ultimate" or "ult" or "ulti" or "t" or "super") { slot = Beelzebub.Services.AbilityRegistry.UltimateSlot; return true; }
        if (int.TryParse(t, out int n) && Beelzebub.Services.AbilityRegistry.IsValidSlot(n)) { slot = n; return true; }
        error = "Slot must be 1-6, or 'primary' (left-click attack) / 'ultimate' (T key).";
        return false;
    }

    [Command("grant", description: "Assign a captured ability to a slot. Usage: .beelz grant <slot> <index OR ability ID>. Slot is 1-6, or 'primary' (left-click attack) / 'ultimate' (T key). The number is read as a list index (0-based, from .beelz list) when in range, otherwise as the ability's PrefabGUID. Universal abilities apply on any weapon; weapon-tagged ones only on their weapon family.")]
    public static void Grant(ChatCommandContext ctx, string slotToken, int indexOrId)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (!TryParseSlotToken(slotToken, out int slot, out string slotErr)) { ctx.Reply(slotErr); return; }

        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var captured = Core.AbilityRegistry.ListFor(steamId);
        // v0.76.0: accept an index OR the ability's PrefabGUID. An in-range value is a list index
        // (backward compatible); anything else is matched against captured ability GUIDs (the stable
        // way to address a specific ability — its index can shift when you capture new ones).
        int selIdx = ResolveCapturedSelector(captured, indexOrId, out string selErr);
        if (selIdx < 0) { ctx.Reply(selErr); return; }
        var sel = captured[selIdx];

        PrefabGUID ability = new(sel.AbilityPrefabGuid);

        // Admin kill-switch + transform-only gates.
        string abilityName = ability.GetPrefabName();
        if (!Core.AbilityRules.IsEnabled(abilityName, ability._Value))
        {
            ctx.Reply($"'{abilityName}' is currently disabled by the server admin.");
            return;
        }
        if (Core.AbilityRules.IsTransformOnlyEnforced(abilityName, ability._Value))
        {
            ctx.Reply($"'{abilityName}' is reserved for .beelz transform — cannot be granted to a slot.");
            return;
        }

        // #4 (v0.34.0) animation fidelity: a weapon-animation-bound ability's cast
        // animation only reads correctly while wielding its weapon family (V Rising
        // bakes the animation into the ability). When Grant_EnforceWeaponMatch is on,
        // refuse a UNIVERSAL bind and steer the player to weapon-grant; the
        // weapon-to-wield guidance is shown below either way.
        var animWeapon = Core.AbilityRules.GetAnimationWeapon(abilityName);
        if (animWeapon != Beelzebub.Services.WeaponFamily.None
            && Beelzebub.Config.Settings.Grant_EnforceWeaponMatch.Value)
        {
            ctx.Reply($"'{abilityName}' is a {animWeapon} ability — its cast animation only reads right with that weapon. " +
                      $"Bind it to the {animWeapon} loadout instead: .beelz weapon-grant {animWeapon} {slot} {sel.AbilityPrefabGuid}");
            return;
        }

        Core.AbilityRegistry.SetSlot(steamId, slot, ability._Value);
        Core.Persistence.RequestSave();

        // W2: apply immediately if the ability is compatible with the player's
        // current weapon (universal/Magic always compatible; weapon-family-tagged
        // abilities compatible only when wielding that family). Otherwise the saved
        // assignment activates next time the player swaps to a compatible weapon.
        bool appliedNow = SlotApply.ApplyGrant(ctx.Event.SenderCharacterEntity, slot, ability);
        var families = Core.AbilityRules.ClassifyWeaponFamilies(abilityName);
        string famHint = (families.Count == 1 && (families[0] == Beelzebub.Services.WeaponFamily.Magic || families[0] == Beelzebub.Services.WeaponFamily.None))
            ? "universal (any weapon)"
            : string.Join("/", families);
        string applyHint = appliedNow
            ? "Applied to your spell bar."
            : $"Activates while wielding: {famHint}.";
        // #4: animation-fidelity nudge — tell them which weapon makes the cast read right.
        string animNote = animWeapon != Beelzebub.Services.WeaponFamily.None
            ? $" ✋ Wield {animWeapon} for the correct animation."
            : "";
        ctx.Reply($"Slot {slot} assigned to {ability.GetPrefabName()}. {applyHint}{animNote} (Not all abilities are usable in every slot — e.g. _MeleeAttack_ won't appear in spell slots 5/6.)");
        Core.Log.LogInfo($"[Beelz] {steamId} assign slot={slot} ability={ability._Value} ({ability.GetPrefabName()}) appliedNow={appliedNow}");
        Core.Chat.SendEvent(ctx.Event.SenderCharacterEntity,
            $"[BEELZ:event] type=slot-granted slot={slot} a={ability._Value} an={ability.GetPrefabName()}");
    }

    [Command("odds", description: "Show your current ability/devour drop chances and your accumulated pity (bad-luck protection) bonus.")]
    public static void Odds(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        float abR = Beelzebub.Config.Settings.DropChance_Ability_Regular.Value, abV = Beelzebub.Config.Settings.DropChance_Ability_VBlood.Value;
        float dvR = Beelzebub.Config.Settings.DropChance_Devour_Regular.Value, dvV = Beelzebub.Config.Settings.DropChance_Devour_VBlood.Value;
        float pAbR = Core.AbilityRegistry.GetPityBonus(steamId, Beelzebub.Services.CaptureSource.Regular, Beelzebub.Services.PityKind.Ability);
        float pAbV = Core.AbilityRegistry.GetPityBonus(steamId, Beelzebub.Services.CaptureSource.VBlood, Beelzebub.Services.PityKind.Ability);
        float pDvR = Core.AbilityRegistry.GetPityBonus(steamId, Beelzebub.Services.CaptureSource.Regular, Beelzebub.Services.PityKind.Transform);
        float pDvV = Core.AbilityRegistry.GetPityBonus(steamId, Beelzebub.Services.CaptureSource.VBlood, Beelzebub.Services.PityKind.Transform);
        ctx.Reply("=== Your capture odds (per eligible ability / kill) ===");
        ctx.Reply($"Ability — Regular {abR * 100:F1}% + pity {pAbR * 100:F1}% = {(abR + pAbR) * 100:F1}%  ·  V-Blood {abV * 100:F1}% + pity {pAbV * 100:F1}% = {(abV + pAbV) * 100:F1}%");
        ctx.Reply($"Devour (full kit) — Regular {dvR * 100:F1}% + pity {pDvR * 100:F1}% = {(dvR + pDvR) * 100:F1}%  ·  V-Blood {dvV * 100:F1}% + pity {pDvV * 100:F1}% = {(dvV + pDvV) * 100:F1}%");
        ctx.Reply("Chance is also multiplied by the unit's tier. Pity rises on each dry kill and resets when you capture/devour.");
    }

    [Command("silent", description: "Toggle whether you see the 'you already knew all of its abilities' devour message. Usage: .beelz silent <on|off> (on = hide it).")]
    public static void Silent(ChatCommandContext ctx, string state = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        string v = (state ?? "").Trim().ToLowerInvariant();
        if (v is "on" or "true" or "1" or "yes") { Core.AbilityRegistry.SetSilenceOwned(steamId, true); ctx.Reply("Silenced: you'll no longer see the 'already knew all of its abilities' message on a Devour of a unit you've fully collected."); }
        else if (v is "off" or "false" or "0" or "no") { Core.AbilityRegistry.SetSilenceOwned(steamId, false); ctx.Reply("Un-silenced: the 'already knew all' Devour message is back on."); }
        else ctx.Reply($"Usage: .beelz silent <on|off>. Currently: {(Core.AbilityRegistry.GetSilenceOwned(steamId) ? "ON (hidden)" : "OFF (shown)")}.");
    }

    [Command("top", description: "Show the server leaderboard: the top players by abilities collected (count + %). Admins are excluded. Usage: .beelz top")]
    public static void Top(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        // v0.86.0 (Bug B): denominator = the full capturable universe (~1400), not the curated
        // AbilityMap.Count (~453) which captures can exceed → the old >100% bug. Fall back to the
        // curated count only if the universe isn't computed yet.
        int total = Beelzebub.Services.BestiaryService.TotalCapturableAbilities();
        if (total <= 0) total = Core.AbilityRules?.Current?.AbilityMap?.Count ?? 0;
        var board = new System.Collections.Generic.List<(string name, int count)>();
        foreach (var (sid, name, isAdmin) in EntityExtensions.AllUsers())
        {
            if (isAdmin) continue;   // exclude admin IDs
            int c = Core.AbilityRegistry.CapturedCount(sid);
            if (c > 0) board.Add((name, c));
        }
        if (board.Count == 0) { ctx.Reply("No abilities collected yet — be the first! Defeat units to steal their abilities."); return; }
        board.Sort((a, b) => b.count.CompareTo(a.count));
        ctx.Reply("=== 🏆 Ability Collection Leaderboard ===");
        int n = System.Math.Min(5, board.Count);
        for (int i = 0; i < n; i++)
        {
            var (name, count) = board[i];
            string pct = total > 0 ? $" ({System.Math.Min(100f, count * 100f / total):F1}%)" : "";
            string medal = i == 0 ? "🥇" : i == 1 ? "🥈" : i == 2 ? "🥉" : $"#{i + 1}";
            ctx.Reply($"{medal} {name} — {count} abilities{pct}");
        }
    }

    [Command("unslot", description: "Remove your universal-bucket assignment from a spell slot. Usage: .beelz unslot <slot> (1-6, or 'primary' / 'ultimate').")]
    public static void Unslot(ChatCommandContext ctx, string slotToken)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (!TryParseSlotToken(slotToken, out int slot, out string slotErr)) { ctx.Reply(slotErr); return; }   // v0.100.0: accept primary/ultimate
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Core.AbilityRegistry.ClearSlot(steamId, slot);
        Core.Persistence.RequestSave();
        bool clearedNow = SlotApply.ClearGrant(ctx.Event.SenderCharacterEntity, slot);
        ctx.Reply(clearedNow
            ? $"Universal slot {slot} cleared from your spell bar."
            : $"Universal slot {slot} cleared.");
        Core.Chat.SendEvent(ctx.Event.SenderCharacterEntity,
            $"[BEELZ:event] type=slot-cleared slot={slot}");
    }

    [Command("resetbar", description: "Reset your action bar to its vanilla in-game state: ends any active transformation and removes ALL Beelzebub slot bindings (universal + weapon-specific), so your normal spells and weapon skills return. Keeps your captured abilities and transform unlocks — re-grant anytime with .beelz grant. Requires confirmation: .beelz resetbar CONFIRM")]
    public static void ResetBar(ChatCommandContext ctx, string confirm = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        // v0.76.0: guard against an accidental wipe of every slot bind — require the CONFIRM token.
        if (!string.Equals(confirm, "CONFIRM", System.StringComparison.Ordinal))
        {
            ctx.Reply("This removes ALL your Beelzebub slot bindings (universal + weapon-specific) and returns your bar to vanilla. Your captured abilities + unlocks are KEPT. Re-run as: .beelz resetbar CONFIRM");
            return;
        }
        Entity character = ctx.Event.SenderCharacterEntity;
        ulong steamId = character.GetSteamId();

        // 1) End any active transformation first (destroys its carrier/form buff, despawns
        //    its summons, clears the record). restoreBar:false — we re-resolve the bar below.
        var (reverted, _) = Core.Transforms.Revert(steamId, "resetbar", restoreBar: false);

        // 2) Snapshot which universal slots were bound (for the BCH events), then drop EVERY
        //    binding (universal + weapon-specific) from the saved loadout.
        var boundSlots = new System.Collections.Generic.List<int>(Core.AbilityRegistry.GetSlots(steamId).Keys);
        int cleared = Core.AbilityRegistry.ClearAllSlots(steamId);

        // 3) Wipe the injected overrides off the LIVE bar so it returns to vanilla right now
        //    (mirrors `.beelz unslot`, which only the player can otherwise do one slot at a time).
        //    v0.94.0: 0-7 so the primary (0) and ultimate (7) slots are cleared too (v0.91 made them bindable).
        for (int slot = Beelzebub.Services.AbilityRegistry.PrimarySlot; slot <= Beelzebub.Services.AbilityRegistry.UltimateSlot; slot++)
            Beelzebub.Services.SlotApply.ClearGrant(character, slot);

        // 4) Hardened teardown: walk the live buff buffer and destroy any lingering
        //    carrier/form buff OR stuck shapeshift/transformation buff driving the bar
        //    (catches buffs that TryGetBuff-based removal misses). EquipBuff is left alone.
        int buffsKilled = Beelzebub.Services.TransformBuffService.RemoveAllFormsAndShapeshifts(character);

        // 5) DEEP FIX: destroy any player-owned ability-slot OVERRIDE SOURCE that isn't gear/
        //    jewels — including an orphaned carrier/form source that left the BuffBuffer but is
        //    still injecting abilities and freezing the bar (invisible to the BuffBuffer sweep).
        int orphanSources = Beelzebub.Services.TransformBuffService.DestroyOwnedAbilitySlotOrphans(character);

        // Re-resolve the bar to vanilla now that grants + override buffs/sources are gone.
        Beelzebub.Services.SlotApply.RestoreResolvedGrants(character);

        Core.Persistence.RequestSave();

        string buffNote = (buffsKilled + orphanSources) > 0 ? $", removed {buffsKilled + orphanSources} override source(s)" : "";
        ctx.Reply(reverted
            ? $"Transformation ended and your action bar is reset to vanilla ({cleared} binding(s){buffNote}). Your captures + unlocks are intact — re-grant with .beelz grant. (If your bar is stuck on a creature kit from a previous shapeshift, an admin can run .beelz admin respawn to fully rebuild it.)"
            : $"Action bar reset to vanilla ({cleared} binding(s) removed{buffNote}). Your captures + unlocks are intact — re-grant with .beelz grant. (If your bar is stuck on a creature kit from a previous shapeshift, an admin can run .beelz admin respawn to fully rebuild it.)");

        // Reuse the existing slot-cleared event per previously-bound slot so BCH refreshes
        // its loadout view (no new event type → no wire-API change).
        foreach (int slot in boundSlots)
            Core.Chat.SendEvent(character, $"[BEELZ:event] type=slot-cleared slot={slot}");
    }

    [Command("clearbar", description: "Clear your captured-ability bindings (no confirmation needed) — choose WHICH set. Usage: .beelz clearbar [all|universal|<weapon>|<form>]. No arg or 'all' = every set (universal + all weapons + all forms). 'universal'/'basic' = the any-weapon set. A weapon (sword/spear/unarmed/crossbow/…) = that weapon's set. A form (wolf/bear/rat/spider/toad/…) = that form's set. Your captured abilities are KEPT — re-grant anytime with .beelz grant / weapon-grant / form-grant.")]
    public static void ClearBar(ChatCommandContext ctx, string bucket = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        Entity character = ctx.Event.SenderCharacterEntity;
        ulong steamId = character.GetSteamId();
        string b = (bucket ?? "all").Trim().ToLowerInvariant();

        int cleared;
        string what;
        if (b is "all" or "")
        {
            Core.Transforms.Revert(steamId, "clearbar", restoreBar: false);   // end any active transform first
            cleared = Core.AbilityRegistry.ClearAllSlots(steamId);
            what = "ALL loadouts (universal + every weapon + every form)";
        }
        else if (b is "universal" or "basic" or "any")
        {
            cleared = Core.AbilityRegistry.ClearUniversalBucket(steamId);
            what = "your universal loadout";
        }
        else if (System.Enum.TryParse<Beelzebub.Services.WeaponFamily>(b, ignoreCase: true, out var weapon)
                 && weapon != Beelzebub.Services.WeaponFamily.None && weapon != Beelzebub.Services.WeaponFamily.Magic)
        {
            cleared = Core.AbilityRegistry.ClearWeaponBucket(steamId, weapon);
            what = $"your {weapon} loadout";
        }
        else if (System.Enum.TryParse<Beelzebub.Services.ShapeshiftForm>(b, ignoreCase: true, out var form)
                 && form != Beelzebub.Services.ShapeshiftForm.None)
        {
            cleared = Core.AbilityRegistry.ClearFormBucket(steamId, form);
            what = $"your {form} form loadout";
        }
        else
        {
            ctx.Reply("Usage: .beelz clearbar [all | universal | <weapon: sword/spear/unarmed/crossbow/…> | <form: wolf/bear/rat/spider/toad/…>].");
            return;
        }

        // Re-resolve the LIVE bar now: wipe our overrides (0-7) then re-apply whatever's left for the
        // active weapon. The cleared set is gone, so its binds don't come back.
        for (int slot = Beelzebub.Services.AbilityRegistry.PrimarySlot; slot <= Beelzebub.Services.AbilityRegistry.UltimateSlot; slot++)
            Beelzebub.Services.SlotApply.ClearGrant(character, slot);
        try { Beelzebub.Services.SlotApply.RestoreResolvedGrants(character); } catch { /* re-resolve best-effort */ }
        Core.Persistence.RequestSave();

        ctx.Reply(cleared > 0
            ? $"Cleared {what} — {cleared} binding(s) removed. Captured abilities kept; re-grant anytime."
            : $"Nothing was bound in {what}.");
        Core.Chat.SendEvent(character, $"[BEELZ:event] type=slot-cleared");
    }

    /// <summary>
    /// W3: weapon-family-specific slot bind. Lets the player keep a sword loadout
    /// AND a crossbow loadout AND a universal loadout simultaneously; switching
    /// weapons swaps loadouts automatically.
    /// </summary>
    [Command("weapon-grant", description: "Bind a captured ability to a slot for a specific weapon family. Usage: .beelz weapon-grant <weapon|auto> <slot> <index OR ability ID>. Slot is 1-6, or 'primary' / 'ultimate'. Use 'auto' for the weapon you're currently wielding; pass the stable ability ID (from .beelz list) instead of an index if you prefer.")]
    public static void WeaponGrant(ChatCommandContext ctx, string weaponStr, string slotToken, int index)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (!TryParseSlotToken(slotToken, out int slot, out string slotErr)) { ctx.Reply(slotErr); return; }

        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Entity character = ctx.Event.SenderCharacterEntity;

        Beelzebub.Services.WeaponFamily weapon;
        if (string.Equals(weaponStr, "auto", System.StringComparison.OrdinalIgnoreCase))
        {
            weapon = Beelzebub.Services.SlotApply.GetCurrentWeapon(character);
            if (weapon == Beelzebub.Services.WeaponFamily.None)
            {
                ctx.Reply("Couldn't detect your current weapon. Try naming it explicitly: .beelz weapon-grant <sword|crossbow|...> <slot> <index>.");
                return;
            }
        }
        else if (!System.Enum.TryParse<Beelzebub.Services.WeaponFamily>(weaponStr, ignoreCase: true, out weapon)
                 || weapon == Beelzebub.Services.WeaponFamily.None
                 || weapon == Beelzebub.Services.WeaponFamily.Magic)
        {
            ctx.Reply($"Unknown weapon family '{weaponStr}'. Valid: Sword, GreatSword, Axe, Mace, Spear, Daggers, Crossbow, Longbow, Pistols, Reaper, Whip, Claws, Pollaxe, Slashers, TwinBlades, Unarmed, FishingPole.");
            return;
        }

        var captured = Core.AbilityRegistry.ListFor(steamId);
        int selIdx = ResolveCapturedSelector(captured, index, out string selErr);   // v0.76.0: index OR ability ID
        if (selIdx < 0) { ctx.Reply(selErr); return; }

        PrefabGUID ability = new(captured[selIdx].AbilityPrefabGuid);
        string abilityName = ability.GetPrefabName();
        if (!Core.AbilityRules.IsEnabled(abilityName, ability._Value))
        {
            ctx.Reply($"'{abilityName}' is currently disabled by the server admin.");
            return;
        }
        if (Core.AbilityRules.IsTransformOnlyEnforced(abilityName, ability._Value))
        {
            ctx.Reply($"'{abilityName}' is reserved for .beelz transform — cannot be granted to a slot.");
            return;
        }

        Core.AbilityRegistry.SetSlot(steamId, weapon, slot, ability._Value);
        Core.Persistence.RequestSave();

        // Apply in-place only if the player is currently wielding the matching family.
        var currentWeapon = Beelzebub.Services.SlotApply.GetCurrentWeapon(character);
        bool appliedNow = false;
        if (currentWeapon == weapon)
        {
            appliedNow = Beelzebub.Services.SlotApply.ApplyGrant(character, slot, ability, explicitWeaponBucket: true);
        }

        string applyHint = appliedNow
            ? $"Applied to your spell bar (currently wielding {weapon})."
            : $"Activates next time you wield {weapon}.";
        ctx.Reply($"{weapon} slot {slot} = {ability.GetPrefabName()}. {applyHint}");
        Core.Log.LogInfo($"[Beelz] {steamId} weapon-grant weapon={weapon} slot={slot} ability={ability._Value} ({ability.GetPrefabName()}) appliedNow={appliedNow}");
        Core.Chat.SendEvent(character,
            $"[BEELZ:event] type=weapon-slot-granted weapon={weapon} slot={slot} a={ability._Value} an={ability.GetPrefabName()}");
    }

    [Command("weapon-unslot", description: "Clear a weapon-family-specific slot bind. Usage: .beelz weapon-unslot <weapon|auto> <slot> (1-6, or 'primary' / 'ultimate').")]
    public static void WeaponUnslot(ChatCommandContext ctx, string weaponStr, string slotToken)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (!TryParseSlotToken(slotToken, out int slot, out string slotErr)) { ctx.Reply(slotErr); return; }   // v0.100.0: accept primary/ultimate
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Entity character = ctx.Event.SenderCharacterEntity;

        Beelzebub.Services.WeaponFamily weapon;
        if (string.Equals(weaponStr, "auto", System.StringComparison.OrdinalIgnoreCase))
        {
            weapon = Beelzebub.Services.SlotApply.GetCurrentWeapon(character);
            if (weapon == Beelzebub.Services.WeaponFamily.None)
            {
                ctx.Reply("Couldn't detect your current weapon. Name it explicitly.");
                return;
            }
        }
        else if (!System.Enum.TryParse<Beelzebub.Services.WeaponFamily>(weaponStr, ignoreCase: true, out weapon)
                 || weapon == Beelzebub.Services.WeaponFamily.None
                 || weapon == Beelzebub.Services.WeaponFamily.Magic)
        {
            ctx.Reply($"Unknown weapon family '{weaponStr}'.");
            return;
        }

        Core.AbilityRegistry.ClearSlot(steamId, weapon, slot);
        Core.Persistence.RequestSave();

        // If currently wielding the matching family, clear the live slot too.
        bool clearedNow = false;
        var currentWeapon = Beelzebub.Services.SlotApply.GetCurrentWeapon(character);
        if (currentWeapon == weapon)
        {
            clearedNow = Beelzebub.Services.SlotApply.ClearGrant(character, slot);
        }
        ctx.Reply(clearedNow
            ? $"{weapon} slot {slot} cleared from your spell bar."
            : $"{weapon} slot {slot} cleared.");
        Core.Chat.SendEvent(character,
            $"[BEELZ:event] type=weapon-slot-cleared weapon={weapon} slot={slot}");
    }

    // --- v0.59.0: per-FORM ability sets (parallel to the per-weapon buckets) ---

    static bool TryParseForm(ChatCommandContext ctx, string formStr, Entity character, out Beelzebub.Services.ShapeshiftForm form)
    {
        form = Beelzebub.Services.ShapeshiftForm.None;
        if (string.Equals(formStr, "auto", System.StringComparison.OrdinalIgnoreCase))
        {
            form = Beelzebub.Services.ShapeshiftAbilityService.GetCurrentForm(character);
            if (form == Beelzebub.Services.ShapeshiftForm.None)
            {
                ctx.Reply("You're not in a shapeshift form right now. Name it explicitly: .beelz form-grant <wolf|bear|rat|spider|toad|werewolf|gargoyle> <slot> <index>.");
                return false;
            }
            return true;
        }
        if (!System.Enum.TryParse<Beelzebub.Services.ShapeshiftForm>(formStr, ignoreCase: true, out form)
            || form == Beelzebub.Services.ShapeshiftForm.None)
        {
            ctx.Reply($"Unknown form '{formStr}'. Valid: Wolf, Bear, Rat, Spider, Toad, Werewolf, Gargoyle.");
            return false;
        }
        return true;
    }

    [Command("form-grant", description: "Bind a captured ability to a shapeshift FORM (Wolf/Bear/Rat/Spider/Toad/Werewolf/Gargoyle), like a weapon loadout: it auto-loads when you enter that form and reverts to your weapon bar on exit. Usage: .beelz form-grant <form|auto> <slot 1-6> <index OR ability ID>. NOTE: a form has only a FEW ability slots, so your form abilities fill them in slot order (your lowest-numbered grant takes the form's first slot, etc.); extras beyond the form's slot count won't appear. Takes effect next time you enter that form.")]
    public static void FormGrant(ChatCommandContext ctx, string formStr, string slotToken, int index)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (!TryParseSlotToken(slotToken, out int slot, out string slotErr)) { ctx.Reply(slotErr); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Entity character = ctx.Event.SenderCharacterEntity;

        if (!TryParseForm(ctx, formStr, character, out var form)) return;

        var captured = Core.AbilityRegistry.ListFor(steamId);
        int selIdx = ResolveCapturedSelector(captured, index, out string selErr);   // v0.76.0: index OR ability ID
        if (selIdx < 0) { ctx.Reply(selErr); return; }
        PrefabGUID ability = new(captured[selIdx].AbilityPrefabGuid);
        string abilityName = ability.GetPrefabName();
        // A form IS a transform context, so transform-only abilities are allowed here (unlike the
        // normal-bar grant). Only the admin Enabled kill-switch blocks.
        if (!Core.AbilityRules.IsEnabled(abilityName, ability._Value))
        {
            ctx.Reply($"'{abilityName}' is currently disabled by the server admin.");
            return;
        }

        Core.AbilityRegistry.SetFormSlot(steamId, form, slot, ability._Value);
        Core.Persistence.RequestSave();

        string gateHint = Beelzebub.Config.Settings.Forms_CustomAbilities_Enabled.Value
            ? $"Enter {form} form (shapeshift wheel) to use it; re-enter to apply changes."
            : "NOTE: custom form abilities are OFF on this server — an admin must enable Forms_CustomAbilities_Enabled for this to take effect in-form.";
        ctx.Reply($"{form} slot {slot} = {abilityName}. {gateHint}");
        Core.Log.LogInfo($"[Beelz] {steamId} form-grant form={form} slot={slot} ability={ability._Value} ({abilityName})");
        Core.Chat.SendEvent(character,
            $"[BEELZ:event] type=form-slot-granted form={form} slot={slot} a={ability._Value} an={abilityName}");
    }

    [Command("form-unslot", description: "Clear a form-specific slot bind. Usage: .beelz form-unslot <form|auto> <slot> (1-6, or 'primary' / 'ultimate').")]
    public static void FormUnslot(ChatCommandContext ctx, string formStr, string slotToken)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (!TryParseSlotToken(slotToken, out int slot, out string slotErr)) { ctx.Reply(slotErr); return; }   // v0.100.0: accept primary/ultimate
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Entity character = ctx.Event.SenderCharacterEntity;

        if (!TryParseForm(ctx, formStr, character, out var form)) return;

        Core.AbilityRegistry.ClearFormSlot(steamId, form, slot);
        Core.Persistence.RequestSave();
        ctx.Reply($"{form} slot {slot} cleared. Re-enter the form to apply.");
        Core.Chat.SendEvent(character,
            $"[BEELZ:event] type=form-slot-cleared form={form} slot={slot}");
    }

    [Command("verbosity", description: "Set your in-chat notification level: silent | summary | verbose.")]
    public static void SetVerbosity(ChatCommandContext ctx, string level)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var v = ChatNotifier.ParseDefault(level);
        if (!string.Equals(level?.Trim(), v.ToString(), System.StringComparison.OrdinalIgnoreCase) &&
            !int.TryParse(level, out _))
        {
            ctx.Reply($"Unknown level '{level}'. Valid: silent | summary | verbose. Defaulted to '{v}'.");
        }
        Core.AbilityRegistry.SetVerbosity(steamId, v);
        Core.Persistence.RequestSave();
        ctx.Reply($"Chat verbosity set to {v}.");
    }

    [Command("forget", description: "Delete one captured ability by index. Usage: .beelz forget <index>")]
    public static void Forget(ChatCommandContext ctx, int index)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var captured = Core.AbilityRegistry.ListFor(steamId);
        if (index < 0 || index >= captured.Count)
        {
            ctx.Reply($"Index {index} out of range (valid: 0-{captured.Count - 1}). Use .beelz list to see indices.");
            return;
        }
        var entry = captured[index];
        bool ok = Core.AbilityRegistry.Forget(steamId, entry.UnitPrefabGuid, entry.AbilityPrefabGuid);
        if (ok)
        {
            Core.Persistence.RequestSave();
            var fInfo = Core.AbilityMetadata?.Resolve(entry.AbilityPrefabGuid);
            string fAbility = (fInfo != null && !string.IsNullOrEmpty(fInfo.Name)) ? fInfo.Name : new Stunlock.Core.PrefabGUID(entry.AbilityPrefabGuid).GetPrefabName();
            string fUnit = Core.AbilityMetadata?.ResolveUnitName(entry.UnitPrefabGuid) ?? new Stunlock.Core.PrefabGUID(entry.UnitPrefabGuid).GetPrefabName();
            ctx.Reply($"Forgot {fAbility} (from {fUnit}).");
            // v0.35.0: BCH event so the client refreshes its collection view.
            Core.Chat.SendEvent(ctx.Event.SenderCharacterEntity,
                $"[BEELZ:event] type=forget a={entry.AbilityPrefabGuid} u={entry.UnitPrefabGuid}");
        }
        else
        {
            ctx.Reply("Could not forget that entry (already removed?).");
        }
    }

    [Command("forget-transform", description: "Delete one transform unlock by index. Usage: .beelz forget-transform <index>")]
    public static void ForgetTransform(ChatCommandContext ctx, int index)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var unlocks = Core.AbilityRegistry.ListTransforms(steamId);
        if (index < 0 || index >= unlocks.Count)
        {
            ctx.Reply($"Index {index} out of range (valid: 0-{unlocks.Count - 1}). Use .beelz transforms to see indices.");
            return;
        }
        var entry = unlocks[index];
        // v0.100.0: if the player is CURRENTLY transformed into the unit they're forgetting, revert first —
        // otherwise they're left in an orphaned form whose unlock no longer exists (relies on logout to clear).
        var activeNow = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (activeNow != null && activeNow.UnitPrefabGuid == entry.UnitPrefabGuid)
            Core.Transforms.Revert(steamId, "forget-transform");
        bool ok = Core.AbilityRegistry.ForgetTransform(steamId, entry.UnitPrefabGuid);
        if (ok)
        {
            Core.Persistence.RequestSave();
            string ftUnit = Core.AbilityMetadata?.ResolveUnitName(entry.UnitPrefabGuid) ?? new Stunlock.Core.PrefabGUID(entry.UnitPrefabGuid).GetPrefabName();
            ctx.Reply($"Forgot transform unlock: {ftUnit}.");
            // v0.35.0: BCH event so the client refreshes its transform list.
            Core.Chat.SendEvent(ctx.Event.SenderCharacterEntity,
                $"[BEELZ:event] type=forget-transform u={entry.UnitPrefabGuid}");
        }
        else
        {
            ctx.Reply("Could not forget that transform unlock.");
        }
    }

    [Command("preset save", description: "Save your current slot assignments as a named preset. Usage: .beelz preset save <name>")]
    public static void PresetSave(ChatCommandContext ctx, string name)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (string.IsNullOrWhiteSpace(name)) { ctx.Reply("Preset name required."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        Core.AbilityRegistry.SavePreset(steamId, name);
        Core.Persistence.RequestSave();
        int slotCount = Core.AbilityRegistry.GetSlots(steamId).Count;
        ctx.Reply($"Saved preset '{name}' ({slotCount} slot(s)).");
    }

    [Command("preset load", description: "Load a named preset into your current slot assignments. Usage: .beelz preset load <name>")]
    public static void PresetLoad(ChatCommandContext ctx, string name)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        if (Core.AbilityRegistry.LoadPreset(steamId, name))
        {
            Core.Persistence.RequestSave();
            // A2: apply each slot in-place if unarmed.
            int appliedCount = 0;
            foreach (var (slot, abilityGuid) in Core.AbilityRegistry.GetSlots(steamId))
            {
                if (SlotApply.ApplyGrant(ctx.Event.SenderCharacterEntity, slot, new PrefabGUID(abilityGuid))) appliedCount++;
            }
            ctx.Reply(appliedCount > 0
                ? $"Loaded preset '{name}'. {appliedCount} slot(s) applied."
                : $"Loaded preset '{name}'. (Applies while UNARMED.)");
        }
        else
        {
            ctx.Reply($"No preset named '{name}'. Use .beelz preset list to see saved presets.");
        }
    }

    [Command("preset list", description: "List your saved slot-loadout presets.")]
    public static void PresetList(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var presets = Core.AbilityRegistry.ListPresets(steamId);
        if (presets.Count == 0) { ctx.Reply("No saved presets. Use .beelz preset save <name>."); return; }
        var sb = new StringBuilder();
        sb.Append("Saved presets (").Append(presets.Count).AppendLine("):");
        foreach (var (name, slots) in presets)
        {
            sb.Append("  ").Append(name).Append(" — ").Append(slots.Count).Append(" slot(s)");
            if (slots.Count > 0)
            {
                sb.Append(" [");
                bool first = true;
                foreach (var (slot, abilityGuid) in slots)
                {
                    if (!first) sb.Append(", ");
                    sb.Append(slot).Append('=').Append(new Stunlock.Core.PrefabGUID(abilityGuid).GetPrefabName());
                    first = false;
                }
                sb.Append(']');
            }
            sb.AppendLine();
        }
        ctx.Reply(sb.ToString());
    }

    [Command("preset delete", description: "Delete a saved preset. Usage: .beelz preset delete <name>")]
    public static void PresetDelete(ChatCommandContext ctx, string name)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        if (Core.AbilityRegistry.DeletePreset(steamId, name))
        {
            Core.Persistence.RequestSave();
            ctx.Reply($"Deleted preset '{name}'.");
        }
        else
        {
            ctx.Reply($"No preset named '{name}'.");
        }
    }

    [Command("clear", description: "Wipe ALL your Beelzebub data — captured abilities, transform unlocks, every slot bind, hotkey, and preset. Cannot be undone. Requires confirmation: .beelz clear CONFIRM. (To only reset your action bar while KEEPING your collection, use .beelz resetbar.)")]
    public static void Clear(ChatCommandContext ctx, string confirm = null)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();

        // v0.43.23: guard this destructive wipe behind a confirmation token so a player
        // can't nuke their whole collection with a single mistyped/auto-complete command.
        // Typing `.beelz clear` (no token) shows what's at stake + the safer alternative.
        if (!string.Equals(confirm?.Trim(), "CONFIRM", System.StringComparison.OrdinalIgnoreCase))
        {
            int abilities = Core.AbilityRegistry.ListFor(steamId).Count;
            int transforms = Core.AbilityRegistry.ListTransforms(steamId).Count;
            if (abilities == 0 && transforms == 0)
            {
                ctx.Reply("You have no Beelzebub data to clear.");
                return;
            }
            ctx.Reply($"⚠ WARNING: this permanently wipes ALL your Beelzebub progress — {abilities} captured abilities, {transforms} transform unlock(s), plus every slot bind, hotkey, and preset. This CANNOT be undone.");
            ctx.Reply("If you only want to reset your action bar to vanilla while KEEPING your collection, use .beelz resetbar instead.");
            ctx.Reply("To confirm the full wipe, type:  .beelz clear CONFIRM");
            return;
        }

        if (Core.AbilityRegistry.Clear(steamId))
        {
            Core.Persistence.RequestSave();
            ctx.Reply("Confirmed — all your captured abilities, transform unlocks, and slot assignments have been wiped.");
            // v0.35.0: BCH event — wipe the client's cached collection + slots.
            Core.Chat.SendEvent(ctx.Event.SenderCharacterEntity, "[BEELZ:event] type=cleared");
        }
        else
        {
            ctx.Reply("You had no captured abilities.");
        }
    }

    [Command("progress", description: "Show your collection progress: % of curated abilities captured and transforms unlocked.")]
    public static void Progress(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        ShowProgress(ctx, steamId, "Your");
    }

    /// <summary>
    /// Shared progress-summary writer used by both `.beelz progress` (self) and
    /// `.beelz admin progress &lt;player&gt;` (admin variant in AdminCommands).
    /// "Totals" are derived from the curated AbilityMap entry count (for abilities)
    /// and the V-Blood-prefab count (for transforms) — admins can adjust the
    /// completion bar by trimming/extending the curated matrix.
    /// </summary>
    internal static void ShowProgress(VampireCommandFramework.ChatCommandContext ctx, ulong steamId, string subjectLabel)
    {
        var captured = Core.AbilityRegistry.ListFor(steamId);
        var transforms = Core.AbilityRegistry.ListTransforms(steamId);

        // v0.86.0 (Bug B): total = the full capturable universe (~1400 distinct abilities), so the %
        // reflects "of every ability in the game" and can never exceed 100% (the curated AbilityMap.Count
        // was smaller than what a full-devour player holds → the old >100% bug). Fall back to the curated
        // count only before the universe is computed.
        int totalAbilities = Beelzebub.Services.BestiaryService.TotalCapturableAbilities();
        if (totalAbilities <= 0) totalAbilities = Core.AbilityRules?.Current?.AbilityMap?.Count ?? 0;
        // v0.44.0: transformation is Dracula/Morgana-only now, so the honest denominator is
        // the number of real transform forms (BossFormRegistry.Count), not the full curated
        // TransformMap. Other units' kits surface in the Abilities line (via capture / Devour).
        int totalTransforms = Beelzebub.Services.BossFormRegistry.Count;

        int vbloodCaptures = captured.Count(c => c.Source == Beelzebub.Services.CaptureSource.VBlood);

        float abilityPct = totalAbilities > 0
            ? System.Math.Min(100f, captured.Count * 100f / totalAbilities)
            : 0f;

        var sb = new System.Text.StringBuilder();
        sb.Append(subjectLabel).AppendLine(" progress:");
        sb.Append("  Abilities: ").Append(captured.Count).Append(" / ~").Append(totalAbilities)
          .Append(" (").Append(abilityPct.ToString("F1")).Append("%)")
          .Append("   V-Blood ").Append(vbloodCaptures).Append(" • Regular ").Append(captured.Count - vbloodCaptures).AppendLine();
        sb.Append("  Transformations: ").Append(transforms.Count).Append(" / ").Append(totalTransforms)
          .Append(" (Dracula/Morgana)").AppendLine();
        sb.Append("  Slots bound: ").Append(Core.AbilityRegistry.GetSlots(steamId).Count)
          .Append(" universal");
        int weaponBindings = Core.AbilityRegistry.AllWeaponSlots(steamId).Sum(kv => kv.Value.Count);
        if (weaponBindings > 0) sb.Append(", ").Append(weaponBindings).Append(" weapon-specific");
        int hotkeys = Core.AbilityRegistry.HotkeyCount(steamId);
        if (hotkeys > 0) sb.Append(", ").Append(hotkeys).Append(" hotkey(s)");
        ctx.Reply(sb.ToString());
    }

    // ---- v0.37.0: collection book (bestiary) -------------------------------

    [Command("bestiary", description: "Your collection book: per-unit ability progress (X/Y) + transform status. Usage: .beelz bestiary [page]")]
    public static void Bestiary(ChatCommandContext ctx, int page = 0)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var entries = Beelzebub.Services.BestiaryService.Build(steamId);
        if (entries.Count == 0)
        {
            ctx.Reply("Your bestiary is empty — defeat units to collect their abilities. See .beelz help.");
            return;
        }

        const int pageSize = 10;
        int pages = (entries.Count + pageSize - 1) / pageSize;
        if (page < 0) page = 0;
        if (page >= pages) page = pages - 1;

        int complete = entries.Count(e => e.Complete);
        ctx.Reply($"--- Bestiary: {entries.Count} units, {complete} fully collected — page {page + 1}/{pages} ---");
        foreach (var e in entries.Skip(page * pageSize).Take(pageSize))
        {
            string src = e.Source == Beelzebub.Services.CaptureSource.VBlood ? " [V]" : "";
            string done = e.Complete ? " (complete)" : "";
            // v0.44.0: only Dracula/Morgana can transform — show the marker only for them.
            string tx = Beelzebub.Services.BossFormRegistry.Has(e.UnitPrefabGuid)
                ? (e.TransformUnlocked ? " · transform ✓" : " · transform ·") : "";
            ctx.Reply($"  {e.UnitName}{src} — abilities {e.CapturedCount}/{e.TotalCount}{tx}{done}");
        }
        ctx.Reply($"(.beelz bestiary {page + 2} for next page · .beelz bestiary unit <name> for detail)");
    }

    [Command("bestiary unit", description: "Detail for one collected unit: which of its abilities you have. Usage: .beelz bestiary unit <name>")]
    public static void BestiaryUnit(ChatCommandContext ctx, string name)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();

        int unitGuid = ResolveBestiaryUnit(steamId, name);
        if (unitGuid == 0)
        {
            ctx.Reply($"No collected unit matches '{name}'. Use .beelz bestiary to see your units.");
            return;
        }

        var e = Beelzebub.Services.BestiaryService.BuildForUnit(steamId, unitGuid);
        string src = e.Source == Beelzebub.Services.CaptureSource.VBlood ? "[V-Blood]" : "[Regular]";
        // v0.44.0: transform status is only meaningful for the two renderable bosses.
        string tx = Beelzebub.Services.BossFormRegistry.Has(e.UnitPrefabGuid)
            ? (e.TransformUnlocked ? " · transform unlocked ✓" : " · transform locked ·") : "";
        ctx.Reply($"--- {e.UnitName} {src} — abilities {e.CapturedCount}/{e.TotalCount}{tx} ---");

        var heldSet = new System.Collections.Generic.HashSet<int>(e.CapturedAbilityGuids);
        if (e.AllAbilityGuids.Count == 0)
        {
            ctx.Reply("  (No capturable abilities found for this unit — it may have none, or its prefab isn't loaded.)");
            return;
        }
        foreach (int ag in e.AllAbilityGuids)
        {
            var info = Core.AbilityMetadata?.Resolve(ag);
            string title = (info != null && !string.IsNullOrEmpty(info.Name)) ? info.Name : new PrefabGUID(ag).GetPrefabName();
            string school = (info != null && !string.IsNullOrEmpty(info.School)) ? $" [{info.School}]" : "";
            ctx.Reply($"  {(heldSet.Contains(ag) ? "✓" : "·")} {title}{school}");
        }
    }

    /// <summary>Match a query (exact name → V-Blood substring → any substring) against the player's collected units.</summary>
    static int ResolveBestiaryUnit(ulong steamId, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0;
        var entries = Beelzebub.Services.BestiaryService.Build(steamId);
        foreach (var e in entries)
            if (string.Equals(e.UnitName, query, System.StringComparison.OrdinalIgnoreCase)) return e.UnitPrefabGuid;
        foreach (var e in entries)
            if (e.Source == Beelzebub.Services.CaptureSource.VBlood
                && e.UnitName.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0) return e.UnitPrefabGuid;
        foreach (var e in entries)
            if (e.UnitName.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0) return e.UnitPrefabGuid;
        return 0;
    }
}
