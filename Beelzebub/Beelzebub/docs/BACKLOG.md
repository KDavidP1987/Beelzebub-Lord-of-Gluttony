# Backlog

Deferred work with the slug each item will be planned under (`dod plan <slug>`, store `docs/dod/`). An item leaves
this list when its plan is created.

The umbrella list for 1.0 (testing, ability reconfiguration, stability, planned features) is `ROADMAP_1.0.md`;
rows here are its engineering items. Add a row here when a roadmap item gets a slug.

| Slug | What | Deferred from |
|---|---|---|
| `clearbar-fullreset` | DONE v0.137.4 (clearbar-fullreset): clearbar runs the layered reset for one chosen set, keeps the other sets' binds (RestoreKept), dismounts / ends a transform and says so; ApiVersion 35. | bar-reset (v0.137), Out of scope |
| `transform-chain-guard` | DONE v0.137.5 (transform-chain-guard): one pure gate (`Logic/TransformGate.cs`) on every transform route — no re-apply over a still-spawning form, no transform-to-transform chain (testform too), refused phase switches change no state and a refused combat-end reset is retried; `.beelz phase` counts curated boss phases again (broken since v0.100); ApiVersion stays 35. | bar-reset (v0.137), Out of scope |
| `timed-revert-orphan` | The timed auto-revert (`TransformService.Tick`) clears the transform without `TransformBuffService.MarkReverted`, so an async form buff landing after a timed expiry is not destroyed by the revert-orphan guard (pre-existing; found by the transform-chain-guard audit, Codex round 1 F2). Route timed expiry through `Revert`'s cleanup. | transform-chain-guard (v0.137.5) audit |
| `vrclient-close-server-quit` | 2026-10-04 the dev server quit gracefully (`OnApplicationQuit`, world saved) at 12:08:04, the second `vrclient.py close` ran; the client had already disconnected about 11:53 (`LeftGame`). Cause unproven: `taskkill /IM VRising.exe` should not match `VRisingServer.exe`. Diagnostic first: start the server, run `close` with no client, and see whether the server survives; logs in `%TEMP%\beelz-logs-2026-10-04-close-stopped-server\`. | session 2026-10-04 |
| `docs-consolidation` | Merge the two docs folders (`Beelzebub/docs/` and `Beelzebub/Beelzebub/docs/`) and split the oversized docs. | process adoption, 2026-09-30 |
| `dev-snapshot` | Port `dev-snapshot.ps1` (copy both server logs + state before a restart) from Nyarlathotep. | process adoption, 2026-09-30 |
| mounted-bar-reset | DONE v0.137.3 (mounted-bar-reset): saddle slots are R/C/T (5/6/7), saved slot-3 binds move to 5, a reset while riding dismounts on purpose and says so. | bar-reset (v0.137), Business rules 4 |
| `modid-remap-errors` | DONE v0.137.6 (modid-remap-errors): a heartbeat sweep (`Logic/ModLeak.cs`, `Services/ModLeakService.cs`) clears and destroys slot-override holders whose every slot mod is gone, confirmed in two reads 2 s apart, after boot and every pop; first boot cleaned 7, restarts log 0 remap errors. Was: Each startup logs a few `Couldn't remap old Modification Id … AbilityGroupSlotModificationBuffer` errors (harmless; since the v0.137 test builds): popping a slot mod by id leaves its id listed on a persistent source entity. Diagnostic first, then clear that entry on pop + one-time cleanup of saved dangling ids. | grant-refresh (v0.137.1), owner report 2026-10-03 |
| `modleak-pass-cost` | One pop-armed ModLeak sweep pass that cleaned 1 holder took 268 ms in play (daa5b29 session; the same clean took 10 ms earlier, boot passes 16-33 ms). Diagnostic first: time the read (slot dumps) and the clean (`ClearLooseSourceModifications` + destroy) separately on the sweep line; only then decide on splitting the clean across frames (owner decision 9-A). | modid-remap-errors (v0.137.6) audit |
| `unslot-in-form` | DONE v0.137.2 (form-bar-edits): in-form unslot pushes the weapon skill when the slot is then empty; queued restore on exit. | grant-refresh audit |
| `grant-in-form` | DONE v0.137.2 (form-bar-edits): a grant made in a form is queued and pushed on exit (heartbeat retry). | grant-refresh audit |
