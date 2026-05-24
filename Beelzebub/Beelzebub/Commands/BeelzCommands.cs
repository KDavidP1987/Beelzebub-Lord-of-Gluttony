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
        ctx.Reply("4. Rarer rolls unlock TRANSFORM into the unit. .beelz transforms / transform / revert.");
        ctx.Reply("   Transforms override your spell bar regardless of weapon.");
        ctx.Reply("5. .beelz verbosity <silent|summary|verbose> — tune chat noise.");
        ctx.Reply("Type .beelz commands for a full command list.");
    }

    [Command("commands", description: "List all Beelzebub chat commands.")]
    public static void Commands(ChatCommandContext ctx)
    {
        ctx.Reply("Beelzebub player commands:");
        ctx.Reply(".beelz list / .beelz transforms — see captures and transform unlocks");
        ctx.Reply(".beelz info <index|name> — full ability info: title, description, cooldown, source");
        ctx.Reply(".beelz grant <slot 1-6> <index> / .beelz unslot <slot> — universal slot binds");
        ctx.Reply(".beelz weapon-grant <weapon|auto> <slot> <index> / weapon-unslot <weapon> <slot> — per-weapon binds");
        ctx.Reply(".beelz transform <index|name> / .beelz revert — activate/end a transform");
        ctx.Reply(".beelz forget <i> / .beelz forget-transform <i> — delete one entry");
        ctx.Reply(".beelz clear — wipe all your data");
        ctx.Reply(".beelz preset save|load|list|delete <name> — slot loadout presets");
        ctx.Reply(".beelz hotkey set|clear|list — named hotkey bindings (extra slots, BCH-driven)");
        ctx.Reply(".beelz progress — your collection-completion %.");
        ctx.Reply(".beelz verbosity <silent|summary|verbose> — chat detail");
        ctx.Reply(".beelz help — walkthrough.   .beelz commands — this list.");
        ctx.Reply("Admin: .beelz admin rules / deny / undeny / allow / unallow / reload");
        ctx.Reply("Admin: .beelz admin transform mode|duration|cooldown|show <regular|vblood> ...");
        ctx.Reply("Admin: .beelz admin give|revoke <player> <unitGuid> <abilityGuid>");
        ctx.Reply("Admin: .beelz admin give-transform|revoke-transform|force-transform|clear-transform <player> <unitGuid?>");
        ctx.Reply("Admin: .beelz admin inspect <player> / .beelz admin progress <player>");
        ctx.Reply("BCH API: .beelz api version|list|slots|transforms|active|info|bch|hotkeys|progress|catalog ...");
    }

    [Command("list", description: "List your captured abilities + slot assignments. Usage: .beelz list [page]. Paginated 15/page.")]
    public static void List(ChatCommandContext ctx, int page = 1)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }

        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        if (steamId == 0) { ctx.Reply("Could not resolve your Steam ID."); return; }

        var captured = Core.AbilityRegistry.ListFor(steamId);
        var slots = Core.AbilityRegistry.GetSlots(steamId);

        if (captured.Count == 0 && slots.Count == 0)
        {
            ctx.Reply("You haven't captured any abilities yet. Go kill something.");
            return;
        }

        // v0.21.0: emit one chat reply per logical block to stay under VCF's 510-byte
        // per-reply cap. Page 1 shows the slot-binding header + first 15 captures;
        // subsequent pages show captures only.
        const int pageSize = 15;
        int total = captured.Count;
        int pages = total == 0 ? 1 : (total + pageSize - 1) / pageSize;
        if (page < 1) page = 1;
        if (page > pages) page = pages;

        // Page 1: header (slot bindings + current weapon).
        if (page == 1)
        {
            EmitSlotHeader(ctx, steamId, slots);
        }

        ctx.Reply($"Captured {total} ability(ies). Page {page}/{pages}:");

        // Sort: VBlood first, then by unit name, preserving original index.
        var ordered = captured
            .Select((c, idx) => (idx, ability: c))
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
            ctx.Reply($"More? .beelz list {nextPage}   |   Search: .beelz search <term>");
        }
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

    [Command("grant", description: "Assign a captured ability to a spell slot. Usage: .beelz grant <slot 1-6> <index>. Universal abilities apply on any weapon; weapon-tagged ones only on their weapon family.")]
    public static void Grant(ChatCommandContext ctx, int slot, int index)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (slot < 1 || slot > 6) { ctx.Reply("Slot must be 1-6 (1=primary attack, 3=Q/shift, 5=spell1, 6=spell2)."); return; }

        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        var captured = Core.AbilityRegistry.ListFor(steamId);
        if (index < 0 || index >= captured.Count)
        {
            ctx.Reply($"Index {index} out of range (valid: 0-{captured.Count - 1}). Use .beelz list to see indices.");
            return;
        }

        PrefabGUID ability = new(captured[index].AbilityPrefabGuid);

        // Admin kill-switch + transform-only gates.
        string abilityName = ability.GetPrefabName();
        if (!Core.AbilityRules.IsEnabled(abilityName, ability._Value))
        {
            ctx.Reply($"'{abilityName}' is currently disabled by the server admin.");
            return;
        }
        if (Core.AbilityRules.IsTransformOnly(abilityName, ability._Value))
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
                      $"Bind it to the {animWeapon} loadout instead: .beelz weapon-grant {animWeapon} {slot} {index}");
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

    [Command("unslot", description: "Remove your universal-bucket assignment from a spell slot. Usage: .beelz unslot <slot>.")]
    public static void Unslot(ChatCommandContext ctx, int slot)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
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

    /// <summary>
    /// W3: weapon-family-specific slot bind. Lets the player keep a sword loadout
    /// AND a crossbow loadout AND a universal loadout simultaneously; switching
    /// weapons swaps loadouts automatically.
    /// </summary>
    [Command("weapon-grant", description: "Bind a captured ability to a slot for a specific weapon family. Usage: .beelz weapon-grant <weapon|auto> <slot 1-6> <index>. Use 'auto' for the weapon you're currently wielding.")]
    public static void WeaponGrant(ChatCommandContext ctx, string weaponStr, int slot, int index)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        if (slot < 1 || slot > 6) { ctx.Reply("Slot must be 1-6 (1=primary attack, 3=Q/shift, 5=spell1, 6=spell2)."); return; }

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
            ctx.Reply($"Unknown weapon family '{weaponStr}'. Valid: Sword, GreatSword, Axe, Mace, DualHammers, Spear, Daggers, Crossbow, Longbow, Pistols, Reaper, Whip, Claws, Pollaxe, Slashers, TwinBlades, Unarmed, FishingPole.");
            return;
        }

        var captured = Core.AbilityRegistry.ListFor(steamId);
        if (index < 0 || index >= captured.Count)
        {
            ctx.Reply($"Index {index} out of range (valid: 0-{captured.Count - 1}). Use .beelz list to see indices.");
            return;
        }

        PrefabGUID ability = new(captured[index].AbilityPrefabGuid);
        string abilityName = ability.GetPrefabName();
        if (!Core.AbilityRules.IsEnabled(abilityName, ability._Value))
        {
            ctx.Reply($"'{abilityName}' is currently disabled by the server admin.");
            return;
        }
        if (Core.AbilityRules.IsTransformOnly(abilityName, ability._Value))
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
            appliedNow = Beelzebub.Services.SlotApply.ApplyGrant(character, slot, ability);
        }

        string applyHint = appliedNow
            ? $"Applied to your spell bar (currently wielding {weapon})."
            : $"Activates next time you wield {weapon}.";
        ctx.Reply($"{weapon} slot {slot} = {ability.GetPrefabName()}. {applyHint}");
        Core.Log.LogInfo($"[Beelz] {steamId} weapon-grant weapon={weapon} slot={slot} ability={ability._Value} ({ability.GetPrefabName()}) appliedNow={appliedNow}");
        Core.Chat.SendEvent(character,
            $"[BEELZ:event] type=weapon-slot-granted weapon={weapon} slot={slot} a={ability._Value} an={ability.GetPrefabName()}");
    }

    [Command("weapon-unslot", description: "Clear a weapon-family-specific slot bind. Usage: .beelz weapon-unslot <weapon|auto> <slot>.")]
    public static void WeaponUnslot(ChatCommandContext ctx, string weaponStr, int slot)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
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

    [Command("clear", description: "Forget all captured abilities and slot assignments. Cannot be undone.")]
    public static void Clear(ChatCommandContext ctx)
    {
        if (!Core.IsReady) { ctx.Reply("Beelzebub not yet initialized."); return; }
        ulong steamId = ctx.Event.SenderCharacterEntity.GetSteamId();
        if (Core.AbilityRegistry.Clear(steamId))
        {
            Core.Persistence.RequestSave();
            ctx.Reply("Captured abilities and slot assignments cleared.");
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

        // Total abilities = size of curated AbilityMap (admins control the denominator
        // by editing ability_rules.json). Falls back to 0 if no matrix is loaded.
        int totalAbilities = Core.AbilityRules?.Current?.AbilityMap?.Count ?? 0;
        // TX2-curated TransformMap is the source of truth for transformation totals (was a
        // hardcoded 61 placeholder pre-TX2). Fall back to the V-Blood count if no matrix loaded.
        int TotalTransforms_Approx = Core.AbilityRules?.Current?.TransformMap?.Count ?? 61;
        if (TotalTransforms_Approx == 0) TotalTransforms_Approx = 61;

        int vbloodCaptures = captured.Count(c => c.Source == Beelzebub.Services.CaptureSource.VBlood);
        int vbloodTx = transforms.Count(t => t.Source == Beelzebub.Services.CaptureSource.VBlood);

        float abilityPct = totalAbilities > 0
            ? captured.Count * 100f / totalAbilities
            : 0f;
        float transformPct = transforms.Count * 100f / TotalTransforms_Approx;

        var sb = new System.Text.StringBuilder();
        sb.Append(subjectLabel).AppendLine(" progress:");
        sb.Append("  Abilities: ").Append(captured.Count).Append(" / ~").Append(totalAbilities)
          .Append(" (").Append(abilityPct.ToString("F1")).Append("%)")
          .Append("   V-Blood ").Append(vbloodCaptures).Append(" • Regular ").Append(captured.Count - vbloodCaptures).AppendLine();
        sb.Append("  Transforms: ").Append(transforms.Count).Append(" / ").Append(TotalTransforms_Approx)
          .Append(" (").Append(transformPct.ToString("F1")).Append("%)")
          .Append("   V-Blood ").Append(vbloodTx).Append(" • Regular ").Append(transforms.Count - vbloodTx).AppendLine();
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
            string tx = e.TransformUnlocked ? "✓" : "·";
            string done = e.Complete ? " (complete)" : "";
            ctx.Reply($"  {e.UnitName}{src} — abilities {e.CapturedCount}/{e.TotalCount} · transform {tx}{done}");
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
        ctx.Reply($"--- {e.UnitName} {src} — abilities {e.CapturedCount}/{e.TotalCount} · transform {(e.TransformUnlocked ? "unlocked ✓" : "locked ·")} ---");

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
