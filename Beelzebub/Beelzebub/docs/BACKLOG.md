# Backlog

Deferred work with the slug each item will be planned under (`dod plan <slug>`, store `docs/dod/`). An item leaves
this list when its plan is created.

| Slug | What | Deferred from |
|---|---|---|
| `clearbar-fullreset` | DONE v0.137.4 (clearbar-fullreset): clearbar runs the layered reset for one chosen set, keeps the other sets' binds (RestoreKept), dismounts / ends a transform and says so; ApiVersion 35. | bar-reset (v0.137), Out of scope |
| `transform-chain-guard` | A recurrence guard in `TransformService.TryActivate` against chaining transforms (the pattern that left creature kits on the bar). | bar-reset (v0.137), Out of scope |
| `docs-consolidation` | Merge the two docs folders (`Beelzebub/docs/` and `Beelzebub/Beelzebub/docs/`) and split the oversized docs. | process adoption, 2026-09-30 |
| `dev-snapshot` | Port `dev-snapshot.ps1` (copy both server logs + state before a restart) from Nyarlathotep. | process adoption, 2026-09-30 |
| mounted-bar-reset | DONE v0.137.3 (mounted-bar-reset): saddle slots are R/C/T (5/6/7), saved slot-3 binds move to 5, a reset while riding dismounts on purpose and says so. | bar-reset (v0.137), Business rules 4 |
| `modid-remap-errors` | Each startup logs a few `Couldn't remap old Modification Id … AbilityGroupSlotModificationBuffer` errors (harmless; since the v0.137 test builds): popping a slot mod by id leaves its id listed on a persistent source entity. Diagnostic first, then clear that entry on pop + one-time cleanup of saved dangling ids. | grant-refresh (v0.137.1), owner report 2026-10-03 |
| `unslot-in-form` | DONE v0.137.2 (form-bar-edits): in-form unslot pushes the weapon skill when the slot is then empty; queued restore on exit. | grant-refresh audit |
| `grant-in-form` | DONE v0.137.2 (form-bar-edits): a grant made in a form is queued and pushed on exit (heartbeat retry). | grant-refresh audit |
