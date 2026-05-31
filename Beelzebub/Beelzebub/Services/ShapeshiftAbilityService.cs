using System;
using System.Collections.Generic;
using ProjectM;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.48.0 — custom abilities on VANILLA shapeshift forms (Wolf/Bear test set).
///
/// The player unlocks forms from V-Blood bosses (Alpha Wolf → Wolf, Ferocious Bear → Bear, …)
/// and enters them via the in-game shapeshift wheel (Alt + cursor). When the form buff spawns on
/// the player, this service:
///   1. **Strips the break-on-cast trigger.** Vanilla travel forms auto-exit the moment you cast
///      a non-form ability — that exit is driven by <c>RemoveBuffOnGameplayEvent</c> /
///      <c>RemoveBuffOnGameplayEventEntry</c> on the form buff. Removing those components makes the
///      form HOLD through casting. (Technique proven in Bloodcraft's BuffSpawnServerPatches.)
///   2. **Injects the player's loadout** onto the form bar via <c>ReplaceAbilityOnSlotBuff</c>
///      (Priority 99, Target=BuffTarget, CastBlockType=WholeCast) — the same recipe Bloodcraft's
///      <c>Shapeshifts.ModifyShapeshiftBuff</c> uses for its ExoForms.
///
/// Phase-1.5 TEST: the loadout = the player's universal slot binds (or first captures); a later
/// phase adds a dedicated per-form bucket + auto-switch. Gated by <c>Forms_CustomAbilities_Enabled</c>
/// (default off). The form buff keeps its own RemoveOnDisconnect, so logout still exits cleanly,
/// and our overrides live on the form buff entity (destroyed with it) — it cannot strand the bar.
/// </summary>
internal static class ShapeshiftAbilityService
{
    // v0.59.0: the full per-form roster (was Wolf+Bear test-only in v0.48.0). Maps each vanilla
    // shapeshift form buff GUID to its ShapeshiftForm bucket key. These are the forms a player can
    // build a distinct captured-ability loadout for (parallel to the per-weapon buckets). GUIDs
    // mirror ShapeshiftService's roster.
    static readonly Dictionary<int, ShapeshiftForm> _formByBuff = new()
    {
        { -351718282,  ShapeshiftForm.Wolf },      // AB_Shapeshift_Wolf_Buff
        { -1687924191, ShapeshiftForm.Wolf },      // AB_Shapeshift_Wolf_PMK_Skin02_Buff   (v0.80.0: skin variant)
        { -46579774,   ShapeshiftForm.Wolf },      // AB_Shapeshift_Wolf_Blackfang_Skin03_Buff (v0.80.0)
        { -1569370346, ShapeshiftForm.Bear },      // AB_Shapeshift_Bear_Buff
        { -858273386,  ShapeshiftForm.Bear },      // AB_Shapeshift_Bear_Skin01_Buff       (v0.80.0: skin variant)
        { 902394170,   ShapeshiftForm.Rat },       // AB_Shapeshift_Rat_Buff
        { 124832551,   ShapeshiftForm.Spider },    // AB_Shapeshift_Spider_Buff
        { -1038422434, ShapeshiftForm.Toad },      // AB_Shapeshift_Toad_Buff
        { -1158884666, ShapeshiftForm.Werewolf },  // AB_Shapeshift_Wolf_Skin01_Buff — NOTE: a cosmetic WOLF skin, NOT the real werewolf-curse form (Buff_General_Shapeshift_Werewolf_Standard -1598161201). See docs/WEREWOLF_FORM_TRANSFORM_DESIGN.md.
        { -395216184,  ShapeshiftForm.Gargoyle },  // AB_Tailor_Shapeshift_Gargoyle_Buff
    };

    /// <summary>
    /// v0.80.0: ability tokens that mark an AB_Shapeshift_*_Buff as an ABILITY buff (leap/bite/etc.),
    /// NOT the form-state buff — so name-based detection never mistakes a form ability for the form.
    /// </summary>
    static readonly string[] _formAbilityTokens =
        { "Leap", "Travel", "Bite", "Jump", "Bleed", "Landing", "Strike", "Attack", "Spit", "Charge", "Roar", "Slam", "Dash", "Howl", "Cast" };

    // v0.86.0 (Bug A): the vanilla shapeshift form-state buff does NOT flow through
    // BuffSystem_Spawn_Server.EntityQueries[0] (confirmed from logs: form ENTRY produced zero
    // [Beelz FORM] inject lines while the destroy/exit hook fired every time — the form buff is
    // applied through a different spawn path). So entry is now detected on ShapeshiftSystem's
    // EnterShapeshiftEvent (ShapeshiftSystemPatch → NotifyEnterShapeshift). At event time the
    // form buff hasn't materialized yet, so we record a short-lived "pending apply" and let the
    // per-frame heartbeat (TickPendingForms) inject the loadout the moment the form buff appears
    // in the player's BuffBuffer.
    static readonly Dictionary<ulong, (Entity character, int attemptsLeft)> _pendingFormApply = new();

    // ~how many heartbeat frames we keep trying to find the freshly-spawned form buff before
    // giving up (the buff normally appears within a few frames; this is just a safety bound so a
    // cancelled/failed shapeshift doesn't leave a dangling pending entry).
    const int FormApplyAttempts = 600;

    /// <summary>
    /// v0.86.0: a player just fired an EnterShapeshiftEvent. Record a pending loadout-apply for
    /// them — the actual injection happens in <see cref="TickPendingForms"/> once the form buff is
    /// present. Recording for non-supported forms (bat/travel) is harmless: TickPendingForms only
    /// injects when it finds a buff our <see cref="IsSupportedForm(int,string)"/> recognizes.
    /// </summary>
    public static void NotifyEnterShapeshift(Entity character)
    {
        if (!Beelzebub.Config.Settings.Forms_CustomAbilities_Enabled.Value) return;
        if (!character.Exists()) return;
        ulong steamId = character.GetSteamId();
        if (steamId == 0) return;
        _pendingFormApply[steamId] = (character, FormApplyAttempts);
    }

    /// <summary>
    /// v0.86.0: per-frame driver (called from the heartbeat). Cheap no-op when nothing is pending.
    /// For each player who just entered a form, scan their BuffBuffer for the now-spawned form-state
    /// buff and inject their loadout onto it; clear the pending entry once handled (or on timeout).
    /// </summary>
    public static void TickPendingForms()
    {
        if (_pendingFormApply.Count == 0) return;

        List<ulong> done = null;
        // Snapshot keys so we can mutate the dict while walking it.
        var keys = new List<ulong>(_pendingFormApply.Keys);
        foreach (ulong steamId in keys)
        {
            if (!_pendingFormApply.TryGetValue(steamId, out var pending)) continue;
            Entity character = pending.character;

            bool resolved = false;
            try
            {
                if (character.Exists() && Core.EntityManager.HasBuffer<BuffBuffer>(character))
                {
                    var buffs = Core.EntityManager.GetBuffer<BuffBuffer>(character);
                    for (int i = 0; i < buffs.Length; i++)
                    {
                        int buffGuid = buffs[i].PrefabGuid._Value;
                        if (!IsSupportedForm(buffGuid, buffs[i].PrefabGuid.GetPrefabName())) continue;
                        Entity buffEntity = buffs[i].Entity;
                        if (!buffEntity.Exists()) continue;
                        ApplyFormLoadout(buffEntity, character, buffGuid);
                        resolved = true;
                        break;
                    }
                }
                else
                {
                    // Character gone (logout/death) — stop trying.
                    resolved = true;
                }
            }
            catch (Exception ex)
            {
                Core.Log.LogWarning($"[Beelz FORM] TickPendingForms scan failed for {steamId}: {ex.Message}");
            }

            if (resolved || pending.attemptsLeft <= 1)
            {
                (done ??= new List<ulong>()).Add(steamId);
            }
            else
            {
                _pendingFormApply[steamId] = (character, pending.attemptsLeft - 1);
            }
        }

        if (done != null) foreach (ulong s in done) _pendingFormApply.Remove(s);
    }

    /// <summary>
    /// v0.80.0: derive a form from a shapeshift buff's NAME, so cosmetic SKIN variants
    /// (AB_Shapeshift_Wolf_PMK_Skin02_Buff, _Blackfang_Skin03_Buff, Bear_Skin01_Buff, …) are recognized
    /// even when their exact GUID isn't in <see cref="_formByBuff"/> (the bug where a player's skinned
    /// wolf form injected no abilities). Returns None for ability buffs and non-form names.
    /// </summary>
    static ShapeshiftForm FormFromBuffName(string name)
    {
        if (string.IsNullOrEmpty(name)) return ShapeshiftForm.None;
        // v0.94.0: prefix-agnostic detection so EVERY skin variant is recognized without enumerating each
        // GUID. The form buff can be AB_Shapeshift_*_Buff OR AB_Tailor_Shapeshift_*_Buff (Gargoyle), and
        // skins append suffixes (_PMK_Skin02, _Blackfang_Skin03, _Skin01, …). Find the "Shapeshift_" segment
        // and read the form keyword right after it.
        if (!name.EndsWith("_Buff", StringComparison.Ordinal)) return ShapeshiftForm.None;
        int idx = name.IndexOf("Shapeshift_", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return ShapeshiftForm.None;
        int start = idx + "Shapeshift_".Length;
        int len = name.Length - start - "_Buff".Length;
        if (len <= 0) return ShapeshiftForm.None;
        string body = name.Substring(start, len);
        foreach (var t in _formAbilityTokens)
            if (body.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return ShapeshiftForm.None;   // an ability buff, not the form
        if (body.StartsWith("Wolf_Skin01", StringComparison.OrdinalIgnoreCase)) return ShapeshiftForm.Werewolf;
        if (body.StartsWith("Werewolf", StringComparison.OrdinalIgnoreCase)) return ShapeshiftForm.Werewolf;
        if (body.StartsWith("Wolf", StringComparison.OrdinalIgnoreCase)) return ShapeshiftForm.Wolf;
        if (body.StartsWith("Bear", StringComparison.OrdinalIgnoreCase)) return ShapeshiftForm.Bear;
        if (body.StartsWith("Rat", StringComparison.OrdinalIgnoreCase)) return ShapeshiftForm.Rat;
        if (body.StartsWith("Spider", StringComparison.OrdinalIgnoreCase)) return ShapeshiftForm.Spider;
        if (body.StartsWith("Toad", StringComparison.OrdinalIgnoreCase) || body.StartsWith("Frog", StringComparison.OrdinalIgnoreCase)) return ShapeshiftForm.Toad;
        if (body.StartsWith("Gargoyle", StringComparison.OrdinalIgnoreCase)) return ShapeshiftForm.Gargoyle;
        return ShapeshiftForm.None;
    }

    /// <summary>Is this buff GUID one of the vanilla forms we inject abilities into?</summary>
    public static bool IsSupportedForm(int buffGuid) => _formByBuff.ContainsKey(buffGuid);

    /// <summary>v0.80.0: GUID-or-name form check — catches skin variants whose GUID isn't enumerated.</summary>
    public static bool IsSupportedForm(int buffGuid, string buffName)
        => _formByBuff.ContainsKey(buffGuid) || FormFromBuffName(buffName) != ShapeshiftForm.None;

    /// <summary>Map a form buff GUID to its <see cref="ShapeshiftForm"/> bucket (None if not a tracked form).</summary>
    public static ShapeshiftForm FormForBuff(int buffGuid) =>
        _formByBuff.TryGetValue(buffGuid, out var form) ? form : ShapeshiftForm.None;

    /// <summary>v0.80.0: GUID-or-name form mapping — falls back to the name pattern for skin variants.</summary>
    public static ShapeshiftForm FormForBuff(int buffGuid, string buffName)
        => _formByBuff.TryGetValue(buffGuid, out var form) ? form : FormFromBuffName(buffName);

    /// <summary>
    /// v0.59.0: the player's currently-active vanilla form (None if not in one), by walking their
    /// BuffBuffer for a tracked form buff — the form analogue of <see cref="SlotApply.GetCurrentWeapon"/>.
    /// Used by `.beelz form-grant auto` and the `api slots` current-form footer.
    /// </summary>
    public static ShapeshiftForm GetCurrentForm(Entity character)
    {
        if (!character.Exists() || !Core.EntityManager.HasBuffer<BuffBuffer>(character)) return ShapeshiftForm.None;
        var buffs = Core.EntityManager.GetBuffer<BuffBuffer>(character);
        for (int i = 0; i < buffs.Length; i++)
        {
            // v0.80.0: GUID-or-name so skinned forms are detected.
            var form = FormForBuff(buffs[i].PrefabGuid._Value, buffs[i].PrefabGuid.GetPrefabName());
            if (form != ShapeshiftForm.None) return form;
        }
        return ShapeshiftForm.None;
    }

    /// <summary>
    /// v0.91.0: SELECTIVE form exit-on-cast. v0.90 strips the form's global exit trigger
    /// (DestroyOnGameplayEvent) so it holds through every cast — but the player asked for it to hold ONLY
    /// for their assigned form abilities and exit when they cast something else. So on each cast, if the
    /// player is in a tracked form and the cast ability is NEITHER one of their assigned form abilities NOR
    /// a native form ability (AB_Shapeshift_* or one matching the form's name), we destroy the form buff to
    /// exit. Casting your granted form abilities (and the form's own bite/leap/etc.) keeps you in.
    /// </summary>
    public static void HandleFormCastExit(Entity character, int castAbilityGuid)
    {
        if (!Beelzebub.Config.Settings.Forms_CustomAbilities_Enabled.Value) return;
        if (!character.Exists() || castAbilityGuid == 0) return;
        var form = GetCurrentForm(character);
        if (form == ShapeshiftForm.None) return;   // not in a tracked custom form

        ulong steamId = character.GetSteamId();
        if (steamId == 0) return;

        // HOLD for the player's assigned form abilities.
        foreach (var (_, ability) in Core.AbilityRegistry.GetFormSlots(steamId, form))
            if (ability == castAbilityGuid) return;

        // HOLD for the form's OWN native abilities — never exit on the wolf's bite/leap/howl, the primary
        // attack, etc. (covers AB_Shapeshift_* and anything carrying the form's name, e.g. "Wolf"/"Bear").
        string castName = new PrefabGUID(castAbilityGuid).GetPrefabName() ?? "";
        if (castName.StartsWith("AB_Shapeshift_", StringComparison.Ordinal)) return;
        if (form != ShapeshiftForm.None && castName.IndexOf(form.ToString(), StringComparison.OrdinalIgnoreCase) >= 0) return;

        // A genuinely foreign ability → exit the form (destroy its buff; the destroy hook reverts the bar).
        if (!Core.EntityManager.HasBuffer<BuffBuffer>(character)) return;
        var buffs = Core.EntityManager.GetBuffer<BuffBuffer>(character);
        for (int i = 0; i < buffs.Length; i++)
        {
            if (!IsSupportedForm(buffs[i].PrefabGuid._Value, buffs[i].PrefabGuid.GetPrefabName())) continue;
            Entity fb = buffs[i].Entity;
            if (!fb.Exists()) continue;
            try
            {
                DestroyUtility.Destroy(Core.EntityManager, fb, DestroyDebugReason.TryRemoveBuff);
                if (Beelzebub.Config.Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz FORM] {steamId} cast non-form ability {castName} → exiting {form} form.");
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz FORM] selective-exit destroy failed for {steamId}: {ex.Message}"); }
            break;
        }
    }

    /// <summary>
    /// Called when a tracked vanilla form buff spawns on a player. Strips the break-on-cast
    /// trigger and injects the player's loadout abilities onto the form bar. Fully guarded.
    /// </summary>
    public static void ApplyFormLoadout(Entity buffEntity, Entity character, int formBuffGuid, bool triggerUpdate = true)
    {
        if (!Beelzebub.Config.Settings.Forms_CustomAbilities_Enabled.Value) return;
        if (!buffEntity.Exists() || !character.Exists()) return;
        ulong steamId = character.GetSteamId();
        if (steamId == 0) return;

        // v0.59.0: build the form bar from the player's PER-FORM bucket (slot→ability), keyed by
        // the form they just entered. Slot-accurate (a form-grant to slot 5 lands on slot 5). If the
        // player hasn't built a set for this form, fall back to their UNIVERSAL binds, then first
        // captures (the v0.48.0 behavior) so the feature is still useful before per-form curation.
        var form = FormForBuff(formBuffGuid, new PrefabGUID(formBuffGuid).GetPrefabName());   // v0.80.0: name-aware (skins)
        var perSlot = new Dictionary<int, int>();
        if (form != ShapeshiftForm.None)
            foreach (var (slot, ability) in Core.AbilityRegistry.GetFormSlots(steamId, form))
                if (ability != 0) perSlot[slot] = ability;

        bool fromFormBucket = perSlot.Count > 0;
        if (!fromFormBucket)
        {
            // Fallback: universal binds (slot-accurate), else first captures packed from slot 1.
            foreach (var (slot, ability) in Core.AbilityRegistry.GetSlots(steamId))
                if (ability != 0) perSlot[slot] = ability;
            if (perSlot.Count == 0)
            {
                int s = 1;
                foreach (var c in Core.AbilityRegistry.ListFor(steamId))
                {
                    perSlot[s++] = c.AbilityPrefabGuid;
                    if (perSlot.Count >= 6) break;
                }
            }
        }
        if (perSlot.Count == 0)
        {
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz FORM] {steamId} entered {new PrefabGUID(formBuffGuid).GetPrefabName()} but has no loadout — nothing injected (set up .beelz form-grant {form} first).");
            return;
        }

        try
        {
            // 1. Stop the form from auto-exiting when the player casts an injected (non-form) ability.
            //    v0.89.1 component dump proved the vanilla Wolf/Bear form buff has NO
            //    RemoveBuffOnGameplayEvent — its exit-on-cast is driven by DestroyOnGameplayEvent (a buffer
            //    that destroys the buff when a cast-triggered gameplay event fires). So the old strips were
            //    silent no-ops. Removing DestroyOnGameplayEvent makes the form HOLD through casting; the
            //    legit wheel-exit / death / disconnect use other paths (and our custom-form-exit handler
            //    fires on the buff's eventual destroy regardless), so exiting still works.
            if (buffEntity.Has<DestroyOnGameplayEvent>()) Core.EntityManager.RemoveComponent<DestroyOnGameplayEvent>(buffEntity);
            if (buffEntity.Has<RemoveBuffOnGameplayEvent>()) Core.EntityManager.RemoveComponent<RemoveBuffOnGameplayEvent>(buffEntity);
            if (buffEntity.Has<RemoveBuffOnGameplayEventEntry>()) Core.EntityManager.RemoveComponent<RemoveBuffOnGameplayEventEntry>(buffEntity);

            // 2. Ensure the buff is allowed to replace ability slots.
            if (!buffEntity.Has<ReplaceAbilityOnSlotData>()) Core.EntityManager.AddComponent<ReplaceAbilityOnSlotData>(buffEntity);

            // 3. v0.95.0 + v0.97.0 — OVERRIDE-GRANTED, KEEP-NATIVE-FALLBACK, EXACT-SLOT injection. Earlier
            //    versions discovered the form's pre-populated slots and mapped abilities onto them IN ORDER,
            //    capped at that count — so wolf (declares only slots 2 & 7) showed 2, bear showed 3 (its
            //    empty-but-declared slots 2/4/5/6 were skipped because they had no NewGroupId), spider showed
            //    1, and a grant to slot 5 landed on whatever native slot came next. Bloodcraft's ExoForms
            //    prove a ReplaceAbilityOnSlotBuff entry renders on ANY slot 0-7, so we place each granted
            //    ability on the EXACT slot it was granted to and ADD entries for slots the vanilla form
            //    doesn't natively declare (extends sparse creature bars like wolf/spider). v0.97.0: a slot the
            //    player did NOT grant is LEFT ALONE so the form's own natural ability stays as a fallback
            //    (KDPen: an ungranted slot — e.g. the wolf's space-bar leap — should keep its native move, not
            //    be blanked). So: grant overrides the slot; no grant = the form's vanilla ability shows.
            DynamicBuffer<ReplaceAbilityOnSlotBuff> buffer = Core.EntityManager.HasBuffer<ReplaceAbilityOnSlotBuff>(buffEntity)
                ? Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(buffEntity)
                : Core.EntityManager.AddBuffer<ReplaceAbilityOnSlotBuff>(buffEntity);

            var sortedSlots = new List<int>(perSlot.Keys);
            sortedSlots.Sort();

            // 3a. Override ONLY the slots the player granted; leave every other declared slot on its native
            //     ability (natural fallback). Idempotent: a re-apply re-derives granted slots from perSlot.
            //     NOTE: this means a re-grant that MOVES an ability off a slot won't auto-restore that slot's
            //     native move until the form is re-entered — acceptable (forms are re-entered constantly).
            for (int b = 0; b < buffer.Length; b++)
            {
                if (!perSlot.TryGetValue(buffer[b].Slot, out int ab) || ab == 0) continue; // keep native fallback
                var e = buffer[b];
                e.Target = ReplaceAbilityTarget.BuffTarget;
                e.NewGroupId = new PrefabGUID(ab);
                e.Priority = 99;
                e.CopyCooldown = true;
                e.CastBlockType = GroupSlotModificationCastBlockType.WholeCast;
                buffer[b] = e;
            }

            // 3b. Add entries for granted slots the form doesn't already declare (so wolf/spider can show a
            //     full bar). Existence-checked, so a repeat apply never stacks duplicates.
            int injected = 0;
            var renderedSlots = new List<int>();
            foreach (var slot in sortedSlots)
            {
                if (slot < 0 || slot > 7 || perSlot[slot] == 0) continue;
                injected++;
                renderedSlots.Add(slot);
                bool exists = false;
                for (int b = 0; b < buffer.Length; b++) if (buffer[b].Slot == slot) { exists = true; break; }
                if (exists) continue;
                buffer.Add(new ReplaceAbilityOnSlotBuff
                {
                    Target = ReplaceAbilityTarget.BuffTarget,
                    Slot = slot,
                    NewGroupId = new PrefabGUID(perSlot[slot]),
                    Priority = 99,
                    CopyCooldown = true,
                    CastBlockType = GroupSlotModificationCastBlockType.WholeCast,
                });
            }

            // v0.89.0: when called from INSIDE ReplaceAbilityOnSlotSystem.OnUpdate (the form buff is
            // resolving its bar this frame), DON'T re-enter OnUpdate — the system resolves our edit
            // immediately, which is the correct client-visible timing. The heartbeat path passes
            // triggerUpdate=true (post-resolution) as a fallback.
            if (triggerUpdate && Core.ReplaceAbilityOnSlotSystem != null) Core.ReplaceAbilityOnSlotSystem.OnUpdate();
            Core.Log.LogInfo($"[Beelz FORM] {steamId} → {new PrefabGUID(formBuffGuid).GetPrefabName()} ({form}): injected {injected} ability(ies) on slot(s) [{string.Join(",", renderedSlots)}] [{(fromFormBucket ? "per-form set" : "universal fallback")}{(triggerUpdate ? "" : ", in-resolve")}], replaced native bar.");
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz FORM] ApplyFormLoadout failed for {steamId} ({new PrefabGUID(formBuffGuid).GetPrefabName()}): {ex.Message}");
        }
    }
}
