# DoD project profile — Beelzebub, Lord of Gluttony

## Project-wide notes
- Pure decision logic lives in `Beelzebub/Beelzebub/Logic/*.cs` and is unit-tested by `Beelzebub.Tests` (linked
  sources, xUnit, in `Beelzebub.sln`). IL2CPP/ECS calls stay in `Services/` and are verified by `manual` in-game
  evidence on the local dedicated server (127.0.0.1:9876); static rules by `cmd` checks in `Beelzebub/tools/preflight.ps1`.
- Layer 11 (Design & UX) is never N/A here: the `.beelz` chat command surface is the UX, and BCH consumes it.
- Probe 12.3 (carried from Nyarlathotep): every per-session check whose input the next boot overwrites
  (BepInEx/LogOutput.log, logs/NyarDev.log) is run and recorded before the restart (`preflight.ps1 -LogCheck`).
- Probe 6.1: V Rising slot resolution is layered (saved binds, equip-buff ReplaceAbilityOnSlotBuff rows, form/carrier
  buffs, ModificationsRegistry mods, auto re-inject on equip/login/form); a plan touching the bar names each layer.
- Probe 6.2 (clearbar-fullreset, 83 %): name how each collaborator reports failure and how a deferred engine effect is
  observed — reports: throws, returns false/0, or swallows and logs; deferred: an async form buff, a buff destroyed
  next frame, a queued ECS command. A call that swallows its error reads as success unless the plan names it.

## Audience
- who · project owner
- default · working
- asked · 2026-09-30
- BepInEx/Harmony · working
- C# · working
- IL2CPP interop · working
- PowerShell · working
- Python · working
- Thunderstore packaging · working
- Unity ECS · working
- VampireCommandFramework · new
- xUnit · new
