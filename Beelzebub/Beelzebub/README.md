# Beelzebub, Lord of Gluttony

A server-side V Rising mod: **defeat a unit, acquire its ability.** Earned abilities can be assigned to your spell slots via chat commands.

> **Status:** early development (v0.1.0). Not yet recommended for production servers.

## Features (planned)

- Defeating a unit (V-Blood or otherwise) grants the killer a chance to acquire one of that unit's abilities.
- Earned abilities can be assigned to any of the player's six spell slots via chat command.
- Admin configuration:
  - Allow/deny lists for which units and abilities are capturable.
  - Per-ability range/magnitude scaling (where the underlying ECS components allow).
  - Configurable drop chance per kill, with rare "transform-into-unit" unlocks that let players assume an enemy unit's full ability set (EXO-style).

## Requirements

- A V Rising **Dedicated Server** (Steam Tool AppID 1829350) — Beelzebub does not run on the client / "Host & Play" private game mode.
- [BepInExPack_V_Rising](https://thunderstore.io/c/v-rising/p/BepInEx/BepInExPack_V_Rising/) 1.733.2 or compatible.
- [VampireCommandFramework](https://thunderstore.io/c/v-rising/p/deca/VampireCommandFramework/) 0.10.4 or compatible.

## Installation

Install via [r2modman](https://thunderstore.io/package/ebkr/r2modman/) (recommended) or manually drop `Beelzebub.dll` into:

```
<VRisingDedicatedServer>\BepInEx\plugins\
```

Stop the dedicated server process before replacing the DLL — it is file-locked while the server runs.

## Configuration

Configuration documentation will be added as the system stabilizes. See `BepInEx\config\kdpen.Beelzebub.cfg` after first run.

## Commands

VCF chat commands. Documentation will be auto-generated from the command registry as commands are added.

## License

See `LICENSE` (TBD).
