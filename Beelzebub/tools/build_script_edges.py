"""
v0.132.0 (P1 step 1) - build Resources/script_edges.tsv for AbilityChainGraph.

The chain graph decodes the COMMON spawn/apply edges live from the ECS prefabs
(StartAbilities, SpawnPrefabOnCast/StartCast, SpawnPrefabOnGameplayEvent,
SpawnPrefabOnDestroy, ApplyBuffOnGameplayEvent Buff0-3, projectile fan/multishot/
cluster, SpawnMinion death buff, knockback buff). V Rising also carries a long tail
(~150 types) of scripted components whose fields point at other prefabs
(Script_*_DataServer.NewThrowEntity / BuffToApply / SpawnEntity ...). Decoding each
in C# is brittle, so this script extracts those edges OFFLINE from the prefab dump
(`Reference Data/Prefabs`, read-only) into an embedded table the graph loads as an
extra edge source. Without it, a prefab reached only through a scripted edge would
look "unshared" and a baked edit could leak into another ability or boss.

Output line format:  <sourceGuid>\t<targetGuid>\t<Component.Field>
Idempotent; re-run after a game update / prefab re-dump, then rebuild the DLL.
The runtime logs how many table sources no longer resolve (drift check).
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DUMP = os.path.join(ROOT, "Reference Data", "Prefabs")
OUT = os.path.join(ROOT, "Beelzebub", "Beelzebub", "Resources", "script_edges.tsv")

# Components the C# graph already decodes live - skipped here so the table never
# disagrees with live data for the common edges.
LIVE_DECODED = {
    "ProjectM.AbilityGroupStartAbilitiesBuffer",
    "ProjectM.AbilitySpawnPrefabOnCast",
    "ProjectM.AbilitySpawnPrefabOnStartCast",
    "ProjectM.SpawnPrefabOnGameplayEvent",
    "ProjectM.SpawnPrefabOnDestroy",
    "ProjectM.ApplyBuffOnGameplayEvent",
    "ProjectM.ApplyKnockbackOnGameplayEvent",
    "ProjectM.SpawnMinionOnGameplayEvent",
    "ProjectM.Gameplay.Scripting.AbilityProjectileFanOnGameplayEvent_DataServer",
    "ProjectM.Gameplay.Scripting.AbilityProjectileFanOnTick_DataServer",
    "ProjectM.Gameplay.Scripting.Script_MultiShot_Cast_DataServer",
    "ProjectM.Gameplay.Scripting.EvenSpreadCluster_DataServer",
}

# Components whose prefab refs ARE spawn/apply/cast edges (beyond the scripting namespaces).
EXTRA_EDGE_COMPONENTS = {
    "ProjectM.ForceCastOnGameplayEvent",
}

# Fields that reference a prefab but do NOT spawn/apply it (conditions, lookups, settings).
NON_EDGE_FIELDS = {
    "AssetPrefabGuid", "Faction", "CastOptionsPrefab", "SCTType", "BuffRequired",
    "BuffStacksSource", "SpellSourceId", "SpellSourceId2", "SpellSourceId3",
    "AbilityGroupType", "TriggerOnSpecificPrefabType", "CorruptedBloodType", "BloodType",
    "MountGuid", "PlacementMatchPrefab", "ActivateScriptWhenPlayersHasBuff",
    "DestroyBlockingPassive", "RemoveCharmBuffType", "SpellMod", "DropTableGuid",
    "ImpactMappingGuid", "RagdollSetting", "SpellSchool", "LifeLeechSettingsGuid",
    "InitialSettingGuid", "SCTPrefab", "PassivePrefab",
}
# Component.Field pairs that are removals/conditions, not spawns.
NON_EDGE_PAIRS = {
    "ProjectM.Gameplay.Scripting.Script_RemoveBuffOnAbilityUseData.Buff",
    "ProjectM.Gameplay.Scripting.Script_DestroyBuffTypesOwnedByOwnerOnSpawnData.BuffType",
    "ProjectM.Gameplay.Scripting.Script_CreateGameplayEventIfKilledHasBuff_DataServer.BuffId",
}

HEADER = re.compile(r"^Prefab .* PrefabGuid\((-?\d+)\)")
COMP = re.compile(r"^  ([A-Za-z][\w.+]*)\s*$")
FIELD = re.compile(r"^\s+(\w+): .*PrefabGuid\((-?\d+)\)")


def is_edge_component(comp):
    if comp in LIVE_DECODED:
        return False
    if comp in EXTRA_EDGE_COMPONENTS:
        return True
    return comp.startswith("ProjectM.Gameplay.Scripting.") or comp.startswith("ProjectM.Shared.Script_")


def main():
    if not os.path.isdir(DUMP):
        sys.exit(f"prefab dump not found: {DUMP}")
    rows = set()
    for fn in os.listdir(DUMP):
        if not fn.endswith(".txt"):
            continue
        src = None
        comp = None
        with open(os.path.join(DUMP, fn), "r", encoding="utf-8", errors="replace") as f:
            for line in f:
                line = line.rstrip("\n")
                if src is None:
                    m = HEADER.match(line)
                    if m:
                        src = int(m.group(1))
                    continue
                m = COMP.match(line)
                if m:
                    comp = m.group(1)
                    continue
                if comp is None or not is_edge_component(comp):
                    continue
                m = FIELD.match(line)
                if not m:
                    continue
                field, tgt = m.group(1), int(m.group(2))
                if field == "PrefabGUID" or field in NON_EDGE_FIELDS:
                    continue
                pair = f"{comp}.{field}"
                if pair in NON_EDGE_PAIRS or tgt == src or tgt == 0:
                    continue
                short = comp.split(".")[-1] + "." + field
                rows.add((src, tgt, short))
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("# generated by tools/build_script_edges.py - do not hand-edit\n")
        # Integrity header: the mod fails closed if the loaded row count doesn't match.
        f.write(f"# edges={len(rows)}\n")
        for src, tgt, kind in sorted(rows):
            f.write(f"{src}\t{tgt}\t{kind}\n")
    kinds = len({r[2] for r in rows})
    print(f"wrote {len(rows)} edges ({kinds} component.field kinds) -> {OUT}")


if __name__ == "__main__":
    main()
