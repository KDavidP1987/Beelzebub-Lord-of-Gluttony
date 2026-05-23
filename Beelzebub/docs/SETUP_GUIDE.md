# Beelzebub setup guide (IN5)

End-to-end install + first-run checklist + config tour. Targets the **V Rising
dedicated server** (Steam Tool AppID 1829350) at Beelzebub v0.15.0.

Beelzebub is **server-side only**. Players don't install anything — they just
connect normally and use `.beelz` chat commands once you've enabled it.

---

## What you need

| Component | Version | Where |
|---|---|---|
| V Rising Dedicated Server | latest | Steam → Tools → "V Rising Dedicated Server" |
| BepInEx (IL2CPP build) | 6.0.0-be.733 or newer | Thunderstore: `BepInEx-BepInExPack_V_Rising` |
| VampireCommandFramework (VCF) | 0.10.4+ | Thunderstore: `deca-VampireCommandFramework` |
| Beelzebub | 0.15.0 | Thunderstore: `kdpen-Beelzebub` |

That's it. **You don't need Bloodcraft** to use Beelzebub. The two coexist
cleanly if you do run both — see `INTEROP_BLOODCRAFT.md` for details.

---

## Install order

1. **Stop the dedicated server** if it's running. BepInEx file-locks plugins
   at runtime; replacing the DLL while it's locked silently fails on Windows.
2. Install BepInEx using a Thunderstore mod manager (r2modman / Thunderstore
   Mod Manager / Gale) — point it at your dedicated-server install
   (typically `C:\Program Files (x86)\Steam\steamapps\common\VRisingDedicatedServer\`).
3. Install VCF the same way.
4. Install Beelzebub. The mod manager drops `Beelzebub.dll` into
   `<server>/BepInEx/plugins/`.
5. Start the server.
6. Watch `BepInEx/LogOutput.log` for these lines (in order):
   ```
   Plugin kdpen.Beelzebub vX.Y.Z loading...
   Harmony patches applied: N method(s) patched.
   Beelzebub initialized via … (attempt #1). Registry size: 0 player(s). …
   ```
   If the third line doesn't appear within ~5 seconds of "Loading World",
   the prefab map didn't populate — see Troubleshooting.

---

## First-run verification

Once a player is online:

1. They type `.beelz` (no args) — should see the five-line overview.
2. `.beelz help` — full walkthrough.
3. They kill any low-level mob.
4. Server log should show `[Beelz] <steamId> kill batch: captured N ability(ies), …`.
5. `.beelz list` — captured abilities appear, grouped by source.

If steps 1–2 work but step 4 doesn't, check that `CaptureOnKill = true` in
the config and look for `skip` lines in the log — the ability filter may be
filtering everything out (rare on default settings).

---

## Config tour — `BepInEx/config/kdpen.Beelzebub.cfg`

The config file is generated on first run. Hot-reload most values with
`.beelz admin reload` — restart the server only for `Hotkeys_Enabled`
(which affects command registration).

### Capture rates (the dials admins tune most)

```ini
[Capture.DropChance]
DropChance_Ability_Regular   = 0.05   # 5% chance to capture each eligible ability from a regular mob
DropChance_Ability_VBlood    = 0.05   # same for V-Blood kills
DropChance_Transform_Regular = 0.01   # 1% chance to unlock the transform-into-unit form from a regular mob
DropChance_Transform_VBlood  = 0.01   # same for V-Bloods
```

Higher = more captures per kill. Set `1.0` for "always" (legacy behavior).
Per-ability overrides live in `ability_rules.json` (see `ABILITY_MAP_FORMAT.md`).

### Server difficulty mode (TX4)

```ini
[Server]
Server_DifficultyMode = Basic   # or Brutal
```

Set this to match the V Rising difficulty preset you've configured the server
for. `Brutal` mode allows capturing `_Hard_*` ability variants and unlocking
`Difficulty=Brutal` transforms. `Basic` strips them.

Z3 (v0.14.0) added a carve-out: if a Hard-only ability has no Basic sibling
on the same prefab (Bishop of Death's `ChainBolt_Hard` is the canonical
example), Beelzebub lets it through on Basic mode anyway. So you usually
don't need to flip this just for collection completeness — set it to match
your actual server difficulty.

### Transformation behavior

```ini
[Transformation]
Transform_Mode_Regular           = Toggle   # Toggle | Timed | Disabled
Transform_Mode_VBlood            = Toggle
Transform_DurationSeconds_Regular = 60
Transform_DurationSeconds_VBlood  = 60
Transform_CooldownSeconds_Regular = 0
Transform_CooldownSeconds_VBlood  = 0
Transform_NativeShapeshift_Enabled = false   # Z2 (v0.14.0): native wolf/bear/etc visual on transform; default off
```

- `Toggle`: player toggles on/off manually with `.beelz transform` / `.beelz revert`.
- `Timed`: auto-reverts after `Transform_DurationSeconds_*`. Use for short-lived "ultimate" use case.
- `Disabled`: no transforms for that source class. Useful for transform-free
  Beelzebub installs (capture abilities, skip the boss-impersonation feature).

`Transform_NativeShapeshift_Enabled` defaults `false` because native shapeshift
forms drop on first off-form cast (see `INTEROP_BLOODCRAFT.md` and the v0.14.0
changelog). Leave it off unless you've reviewed the trade-off.

### Hotkeys (W4 — BCH integration prep)

```ini
[Hotkeys]
Hotkeys_Enabled       = true   # allow `.beelz hotkey set` / `clear` / `list`
Hotkeys_MaxPerPlayer  = 5      # cap to prevent storage abuse
```

Hotkey bindings are storage-only on the server — the cast trigger is
BloodCraftHub-side (when BCH ships the UI). Safe to leave enabled even if you
don't run BCH; players just won't have a way to fire the bindings yet.

### Logging

```ini
[Diagnostics]
VerboseLogging = false   # per-capture log lines, per-filter-reject reason. Noisy. Use for debugging the matrix.
```

---

## `ability_rules.json` — the curation file

Lives at `BepInEx/config/kdpen.Beelzebub/ability_rules.json`. **Auto-created
on first run from defaults**, then admin-curated.

For full schema docs see `ABILITY_MAP_FORMAT.md`. Quick orientation:

- `DenyPatterns` / `DenyGuids` — global filter. Defaults strip filler
  (`_Idle_`, `_MeleeAttack_`, `_Spawn_`, etc.) and brutal-only duplicates (`_Hard_`).
- `AbilityMap` — per-ability matrix. Weapons, forms, transform-only, Enabled
  kill-switch, difficulty, phase, damage/cooldown scaling, admin notes.
- `TransformMap` — per-V-Blood matrix. Enabled, difficulty, tier, **TX6 scaling
  fields** (DamageScale / CooldownScale / HealthScale / MovementSpeedScale),
  admin notes.
- `TransformOnlyPatterns` / `TransformOnlyGuids` — bulk transform-only rules.

A comprehensively-curated starter file ships with the mod at
`docs/ability_map.curated.json`. To use it: copy that file into
`BepInEx/config/kdpen.Beelzebub/ability_rules.json` (overwriting the auto-generated
one), then `.beelz admin reload` in-game.

---

## Common admin recipes

### Recipe: low-drop-rate "rare collection" server

Captures feel rare; transforms feel like real achievements.

```ini
[Capture.DropChance]
DropChance_Ability_Regular   = 0.02
DropChance_Ability_VBlood    = 0.10
DropChance_Transform_Regular = 0.003
DropChance_Transform_VBlood  = 0.05
```

### Recipe: PvE social server, generous rates

Everyone gets a kit fast.

```ini
[Capture.DropChance]
DropChance_Ability_Regular   = 0.20
DropChance_Ability_VBlood    = 0.50
DropChance_Transform_Regular = 0.05
DropChance_Transform_VBlood  = 0.25
```

### Recipe: Brutal-difficulty server with strict balance

Tune TX6 scales on each V-Blood transform to keep the boss-on-boss fights
sane. In `ability_rules.json`:

```json
"TransformMap": {
  "CHAR_Vampire_Dracula_VBlood": {
    "Enabled": true, "Difficulty": "Brutal", "Tier": 5,
    "DamageScale": 0.5, "CooldownScale": 1.8, "HealthScale": 0.6
  },
  "CHAR_Cursed_ToadKing_VBlood": {
    "Enabled": true, "Difficulty": "Basic", "Tier": 3,
    "DamageScale": 0.85, "MovementSpeedScale": 0.9
  }
}
```

`.beelz admin reload` to apply without restart.

### Recipe: transform-free install

Captures only; no transformation system at all.

```ini
[Transformation]
Transform_Mode_Regular = Disabled
Transform_Mode_VBlood  = Disabled
[Capture.DropChance]
DropChance_Transform_Regular = 0
DropChance_Transform_VBlood  = 0
```

Players who already unlocked transforms see the error
`Transformations are disabled for V-Bloods by the server admin` when they try
to activate. Their unlock list stays intact in case you re-enable later.

---

## Optional: paired with Bloodcraft

If you run Bloodcraft (`io.zfolmt.Bloodcraft`) on the same server, both mods
coexist out of the box. There's one config-flag flip recommended in the
Bloodcraft cfg to keep them out of each other's slot-3 bind:

```ini
# In BepInEx/config/Bloodcraft.cfg
[Classes]
ShiftSlot    = false   # if you intend to use .beelz grant 3 …
UnarmedSlots = false   # if you intend to use .beelz unarmed binds
```

Full audit + reasoning: `INTEROP_BLOODCRAFT.md`.

---

## Optional: paired with BloodCraftHub (BCH)

BCH is a separate client-side companion mod (same author) that renders a
"Bloodbook" UI on the player's screen — captured abilities, transformation
catalog, progress %, and (future) hotkey buttons.

BCH consumes Beelzebub via the parseable `.beelz api *` chat surface
(documented in `Reference Data/` / `MEMORY.md` under "BCH API contract v1").
There is **nothing to configure server-side** to enable BCH — BCH just reads
the chat-API replies that Beelzebub already emits. Each player flips a per-
player `EmitApiEvents` flag (see the admin commands) to start receiving the
parseable lines.

If BCH isn't installed, Beelzebub still works fully — players just don't get
the on-screen UI.

---

## Troubleshooting

### "Beelzebub initialized via …" never appears

The prefab map didn't populate. Causes:

- VCF wasn't loaded (check `LogOutput.log` for `gg.deca.VampireCommandFramework`).
- BepInEx version mismatch (`be.733` is the target; older builds may not
  expose the IL2CPP types Beelzebub uses).
- Plugin loaded but `Application.productName != "VRisingServer"` — confirm
  you're loading on the dedicated server, not the client.

### `.beelz` commands say "Beelzebub not yet initialized"

Plugin loaded but init deferred. Wait for the world to fully load
(`InitializationPatch` fires on `GameDataInitialized`). If it persists past
60s, check `LogOutput.log` for a failed init exception.

### Server log floods with `Clearing entity X which is a modification source`

You're on v0.13.0 or earlier. Update to v0.14.0+ — the Z1 carrier-buff
refactor eliminated this warning class. (Some lingering W2-path warnings on
weapon swap are normal and harmless if they appear once or twice; flood-level
volume indicates the old transform pipeline.)

### `.beelz transform` works but spell-book spells don't return on revert

You're on v0.13.0 or earlier. Z1 in v0.14.0 fixed this. Update.

### `.beelz grant <slot> <i>` says the ability is reserved for transform

That ability's `TransformOnly` flag is true in `ability_rules.json`. Either
edit the entry to set `TransformOnly: false` or pick a different ability.

### Captures stopped working after editing `ability_rules.json`

JSON parse failure. Check `BepInEx/LogOutput.log` for
`AbilityRules.Load failed: …`. Common culprits: trailing comma, missing
quote, bad escape. The mod falls back to defaults when the file is
malformed.

### Config edits don't take effect

For most settings, `.beelz admin reload` applies hot. A few values
(Hotkeys_Enabled flag, anything that changes command registration) require
a server restart.

---

## Where things live

| Thing | Path |
|---|---|
| Plugin DLL | `<server>/BepInEx/plugins/Beelzebub.dll` |
| BepInEx config | `<server>/BepInEx/config/kdpen.Beelzebub.cfg` |
| Curated rules | `<server>/BepInEx/config/kdpen.Beelzebub/ability_rules.json` |
| Player state (captures, unlocks, slots, hotkeys) | `<server>/BepInEx/config/kdpen.Beelzebub/state.json` |
| Audit log (admin grants/revokes) | `<server>/BepInEx/LogOutput.log`, lines prefixed `[Beelz AUDIT]` |
| Plugin general log | `<server>/BepInEx/LogOutput.log`, lines prefixed `[Beelzebub]` or `[Beelz]` |

Backing up `state.json` periodically is wise on long-running servers — that's
the file that holds every player's captures.
