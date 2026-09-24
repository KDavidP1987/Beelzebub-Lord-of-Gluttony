"""
Generate the GitHub landing page (repo-root README.md) from the Thunderstore README
(Beelzebub/Beelzebub/README.md) so the two never drift.

The Thunderstore README stays the single hand-edited source. This script copies it, rewrites its links for the
repo root, and adds the GitHub-only sections (download links, building from source, repo layout, docs index).
Run it whenever the Thunderstore README changes (every release): python tools/sync_github_readme.py
"""
import os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
SRC = os.path.join(ROOT, 'Beelzebub', 'Beelzebub', 'README.md')
OUT = os.path.join(ROOT, 'README.md')
REPO = 'https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony'
STORE = 'https://thunderstore.io/c/v-rising/p/kdpen/Beelzebub/'

BADGES = f"""<p align="center">
  <a href="{STORE}"><img alt="Thunderstore" src="https://img.shields.io/badge/Thunderstore-kdpen%2FBeelzebub-2a6fdb"></a>
  <a href="{REPO}/releases"><img alt="Release" src="https://img.shields.io/github/v/release/KDavidP1987/Beelzebub-Lord-of-Gluttony?include_prereleases&label=release"></a>
  <img alt="Status" src="https://img.shields.io/badge/status-early%20access-orange">
  <img alt="Side" src="https://img.shields.io/badge/V%20Rising-server--side-8b1e3f">
  <a href="LICENSE"><img alt="License" src="https://img.shields.io/badge/license-MIT-green"></a>
</p>
"""

GET_IT = f"""## 📦 Get it

- **Thunderstore (recommended):** [{STORE}]({STORE}) — install with r2modman / Thunderstore Mod Manager.
- **GitHub Releases:** [{REPO}/releases]({REPO}/releases) — the same zip; drop `BepInEx/plugins/Beelzebub.dll`
  into your dedicated server's `BepInEx\\plugins\\` folder.
- **What changed:** [player changelog](Beelzebub/Beelzebub/CHANGELOG.md) · full technical history in the git log

---

"""

DEV = """## 🛠️ For developers & server admins

### Documentation
| Doc | What it covers |
|---|---|
| [Setup guide](Beelzebub/docs/SETUP_GUIDE.md) | Installing the dedicated server, BepInEx and Beelzebub; first-run config |
| [Commands](Beelzebub/Beelzebub/docs/COMMANDS.md) | Every player and admin command |
| [Ability config](Beelzebub/Beelzebub/docs/ABILITY_CONFIG.md) | Per-ability shaping knobs, cooldowns, damage modes, locks, reseed |
| [Recovery guide](Beelzebub/Beelzebub/docs/RECOVERY_GUIDE.md) | Fixing a stuck character or ability bar without a server wipe |
| [In-game test checklist](Beelzebub/Beelzebub/docs/INGAME_TEST_CHECKLIST.md) · [ability test plan (xlsx)](Beelzebub/Beelzebub/docs/V0136_ABILITY_TEST_PLAN.xlsx) | What to verify on a test server, row by row |
| [Summons as allies](Beelzebub/docs/SUMMON_AS_ALLY.md) | How captured summons become player allies |
| [Bloodcraft interop](Beelzebub/docs/INTEROP_BLOODCRAFT.md) | Running alongside Bloodcraft |
| [Ability change impact](Beelzebub/Beelzebub/docs/ABILITY_CHANGE_IMPACT.md) | Checklist for changing how an ability behaves without breaking its neighbours |
| [BloodCraftHub integration](Beelzebub/Beelzebub/docs/BCH_INTEGRATION_HANDOFF.md) | The `[BEELZ:*]` chat API contract for client UIs |

### Building from source
Requirements: the .NET 6 SDK. The V Rising, BepInEx and VCF references come from NuGet.

```powershell
cd Beelzebub
dotnet restore Beelzebub.sln
dotnet build Beelzebub.sln -c Release
dotnet test Beelzebub.Tests
```

If a V Rising Dedicated Server is installed at the default Steam path, the build also copies the DLL into its
`BepInEx\\plugins` folder (override with `-p:VRisingServerPath="<path>"`; stop the server first — it locks the DLL).
The Thunderstore package is built with `tcli build` from `Beelzebub/Beelzebub/` (output in `build/`).

### Repository layout
```
Beelzebub/
├── Beelzebub/              the plugin (net6.0, BepInEx IL2CPP)
│   ├── Plugin.cs, Core.cs  entry point + service wiring
│   ├── Patches/            Harmony patches (death events, ability casts, buffs, summons, …)
│   ├── Services/           capture, slotting, transforms, tuning, cooldowns, summons, …
│   ├── Commands/           VCF chat commands (.beelz …, .beelz admin …, .beelz api …)
│   ├── Config/Settings.cs  BepInEx config bindings
│   ├── Resources/          shipped ability data, embedded in the DLL
│   │                       (ability_rules.default.json, ability_metadata.json, prefab names)
│   └── docs/               player/admin docs
├── Beelzebub.Tests/        unit tests for the pure logic
├── tools/                  Python data pipeline (metadata mining, audits, tester baseline, test sheet)
└── docs/                   setup + architecture docs
```

Contributions, bug reports and ability-viability notes are welcome — see [Feedback](#feedback).

---

"""


def main():
    s = open(SRC, encoding='utf-8').read()
    # the splash image is served from the repo itself on GitHub
    s = re.sub(r'https://raw\.githubusercontent\.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/main/', '', s)
    # relative doc links in the Thunderstore README are relative to Beelzebub/Beelzebub/
    s = re.sub(r'\]\((docs/[^)]+)\)', r'](Beelzebub/Beelzebub/\1)', s)
    s = s.replace(f'{REPO}/blob/main/LICENSE', 'LICENSE')
    # badges under the splash, before the title
    i = s.index('# Beelzebub, Lord of Gluttony')
    s = s[:i] + BADGES + '\n' + s[i:]
    # "Get it" before Screenshots, developer section before Credits
    for anchor, block in (('## 📸 Screenshots', GET_IT), ('## 🙏 Credits', DEV)):
        if anchor not in s: sys.exit(f'anchor not found: {anchor}')
        s = s.replace(anchor, block + anchor, 1)
    header = ('<!-- GENERATED from Beelzebub/Beelzebub/README.md by Beelzebub/tools/sync_github_readme.py '
              '— edit the Thunderstore README, then re-run the script. -->\n')
    out = header + s
    old = open(OUT, encoding='utf-8').read() if os.path.exists(OUT) else None
    if out == old: print('README.md already in sync'); return
    open(OUT, 'w', encoding='utf-8').write(out)
    print('WROTE', OUT)


if __name__ == '__main__':
    main()
