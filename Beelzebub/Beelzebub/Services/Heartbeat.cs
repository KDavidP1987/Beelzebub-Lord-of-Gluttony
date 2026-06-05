using System;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace Beelzebub.Services;

/// <summary>
/// v0.81.0 — a reliable periodic "pulse" for time-based work that must run during normal play even when
/// no unit is dying.
///
/// WHY: the summon-lifespan sweep, the granted-ability cooldown enforcer, and the debounced state save
/// were all driven from <c>DeathEventListenerSystem.OnUpdate</c>, which on the V Rising server only runs
/// when there are DEATH EVENTS. So while a player summoned/cast/idled without killing anything, none of
/// it ticked — summons outlived their timeout (despawning all-at-once on the next kill) and per-ability
/// cooldown/config changes didn't apply live. (Confirmed in-game: groups expired at age 25/53/190s for a
/// 5s timeout.)
///
/// HOW: no single patched server system is a clean every-frame heartbeat, so we <see cref="Pulse"/> from
/// several HIGH-FREQUENCY gameplay systems (ability cast-start, buff spawn, damage, death). Any active
/// player triggers one of these many times a second. A self-throttle (<see cref="MinInterval"/>) collapses
/// the burst to one real tick per interval, so calling it from N hooks per frame stays cheap. The
/// underlying Tick work keeps its own coarser sub-throttles (e.g. the 5s lifespan check), so the pulse
/// just needs to be frequent ENOUGH, not every frame.
/// </summary>
internal static class Heartbeat
{
    static DateTime _last = DateTime.MinValue;
    static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    static bool _timerStarted;

    /// <summary>
    /// v0.81.0: install a real per-frame driver so the heartbeat fires even when the player is idle (the
    /// event pulses from cast/buff/damage/death can't cover "I set a 30s cooldown, can't re-cast, and just
    /// stand here"). A registered IL2CPP MonoBehaviour's <c>Update()</c> is called by Unity every frame —
    /// the dedicated server still runs the Unity update loop. Self-throttle keeps real work to 1/sec. The
    /// event pulses remain as a backup if registration fails. Called once at init.
    /// </summary>
    public static void StartTimer()
    {
        if (_timerStarted) return;
        _timerStarted = true;
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<HeartbeatBehaviour>();
            var go = new GameObject("Beelzebub_Heartbeat");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<HeartbeatBehaviour>();
            Core.Log.LogInfo("[Beelz] heartbeat driver started (per-frame).");
        }
        catch (Exception ex)
        {
            _timerStarted = false;
            Core.Log.LogWarning($"[Beelz] heartbeat per-frame driver failed to start (event-driven pulses still active): {ex.Message}");
        }
    }

    /// <summary>Run the periodic ticks at most once per <see cref="MinInterval"/>. Safe to call from any
    /// frequently-firing patch; fully guarded.</summary>
    public static void Pulse()
    {
        if (!Core.IsReady) return;
        var now = DateTime.UtcNow;
        if (now - _last < MinInterval) return;
        _last = now;

        // Auto-revert of Timed transforms, summon leash/lifespan sweep, phase checks (each self-throttled).
        try { Core.Transforms.Tick(); }
        catch (Exception ex) { Core.Log.LogError($"[Beelz] Heartbeat Transforms.Tick failed: {ex}"); }

        // Re-anchor pending granted-ability cooldown overrides set on cast.
        try { AbilityCooldownEnforcer.Tick(); }
        catch (Exception ex) { Core.Log.LogError($"[Beelz] Heartbeat AbilityCooldownEnforcer.Tick failed: {ex}"); }

        // Drain any debounced state save.
        try { Core.Persistence.MaybeSave(); }
        catch (Exception ex) { Core.Log.LogError($"[Beelz] Heartbeat Persistence.MaybeSave failed: {ex}"); }

        // v0.88.0: periodic server-wide leaderboard broadcast (no-op unless enabled + interval elapsed).
        try { BroadcastService.Tick(); }
        catch (Exception ex) { Core.Log.LogError($"[Beelz] Heartbeat BroadcastService.Tick failed: {ex}"); }

        // v0.101.0: inject the Mounted saddle loadout for players who just got on a horse (mounting fires
        // no EnterShapeshiftEvent, so this scan is how the Mounted form is detected). No-op when disabled.
        try { ShapeshiftAbilityService.TickMountedForms(); }
        catch (Exception ex) { Core.Log.LogError($"[Beelz] Heartbeat TickMountedForms failed: {ex}"); }
    }
}

/// <summary>
/// v0.81.0: host MonoBehaviour for the per-frame heartbeat. Unity calls <see cref="Update"/> every frame
/// on the dedicated server; we forward to the throttled <see cref="Heartbeat.Pulse"/>. The IntPtr ctor is
/// required for an Il2Cpp-injected MonoBehaviour.
/// </summary>
public class HeartbeatBehaviour : MonoBehaviour
{
    public HeartbeatBehaviour(IntPtr ptr) : base(ptr) { }
    void Update()
    {
        // v0.86.0 (Bug A): drive the pending shapeshift-form loadout apply EVERY frame (not via the
        // 1s-throttled Pulse) so a freshly-entered form gets its abilities injected the instant the
        // form buff appears — a 1s lag would show the native form bar first. Cheap no-op when nothing
        // is pending (a single dictionary-count check).
        if (Core.IsReady)
        {
            try { Services.ShapeshiftAbilityService.TickPendingForms(); }
            catch { /* never let a form-apply hiccup kill the heartbeat */ }
        }
        Heartbeat.Pulse();
    }
}
