#!/usr/bin/env python3
"""
Beelzebub — ability description / school data-pass (localization extraction).

Extends Resources/ability_metadata.json with authoritative English ability
descriptions + magic-school tags, derived from V Rising's own localization
string table (the same origin as the original 238 curated descriptions, per
docs/ABILITY_AUDIT.md §3).

Sources (read-only reference data, NOT shipped):
  - English.json            : V Rising localization Nodes (loc-UUID -> text).
  - PrefabNames.cs          : prefab GUID -> NAME loc-UUID map (Bloodcraft).
  - prefab_names.tsv        : prefab GUID -> prefab name (already shipped).
  - ability_metadata.json   : existing curated metadata (preserved + enriched).

Method (see the session investigation): a prefab's name node is locatable via
the name-key map; its DESCRIPTION is the longest description-like localization
node within the gap from the name node up to the next name-like node. Validated
against the existing English descriptions at ~97% precision when restricted to
HIGH confidence (exactly one description-like node in the gap) plus a denylist
for generic/utility names (Primary Attack, Cancel *, ...). Medium confidence
(multiple candidates) is a coin-flip and is NOT shipped.

Output: writes <metadata>.NEW.json next to the input for review; does not
overwrite. Prints a coverage report.
"""
import json, re, bisect, io, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))   # workspace root

ENGLISH_JSON = os.path.join(ROOT, "Learning Mods", "Bloodcraft-main",
                            "Resources", "Localization", "English.json")
PREFABNAMES_CS = os.path.join(ROOT, "Learning Mods", "Bloodcraft-main",
                              "Resources", "PrefabNames.cs")
RES = os.path.join(HERE, "..", "Resources")
METADATA = os.path.join(RES, "ability_metadata.json")
TSV = os.path.join(RES, "prefab_names.tsv")
OUT = os.path.join(RES, "ability_metadata.NEW.json")

# Generic / utility ability names whose loc layout collides or carries no real
# description — extraction is unreliable, so keep whatever legacy text exists.
DENY_EXACT = {"Primary Attack", "Auto Attack", "Basic Attack", "Mounted Attack",
              "Attack", "Travel"}
DENY_PREFIX = ("Cancel ", "Toggle ", "Stop ", "End ", "Activate ", "Deactivate ")

SCHOOLS = ["Blood", "Chaos", "Frost", "Illusion", "Storm", "Unholy"]


def load_english():
    d = json.load(io.open(ENGLISH_JSON, encoding="utf-8"))
    nodes = d["Nodes"]
    return nodes, {n["Guid"]: i for i, n in enumerate(nodes)}, [n["Text"] for n in nodes]


def load_namekeys():
    nk = {}
    txt = io.open(PREFABNAMES_CS, encoding="utf-8").read()
    for m in re.finditer(r'new\((-?\d+)\),\s*"([0-9a-f-]+)"', txt):
        nk[int(m.group(1))] = m.group(2)
    return nk


def load_tsv():
    pn = {}
    for line in io.open(TSV, encoding="utf-8"):
        p = line.rstrip("\n").split("\t")
        if len(p) >= 2:
            try:
                pn[int(p[0])] = p[1]
            except ValueError:
                pass
    return pn


def strip_tags(s):
    s = re.sub(r"<[^>]+>", "", s)            # <skillcolor> </c> <lsg> ...
    s = s.replace("\\n", " ").replace("\n", " ").replace("\\", "")
    return re.sub(r"\s+", " ", s).strip()


def is_english(s):
    return sum(1 for c in s if ord(c) > 127) < max(1, len(s)) * 0.15


def words(s):
    return set(re.findall(r"[a-z]{4,}", re.sub(r"<[^>]+>", "", s).lower()))


def overlap(a, b):
    wa = words(a)
    return len(wa & words(b)) / max(1, len(wa))


def school_for(prefab_name):
    # AB_<...>_<School>_<...> — match a school token anywhere in the prefab name.
    for sc in SCHOOLS:
        if re.search(r"(?<![A-Za-z])" + sc + r"(?![a-z])", prefab_name):
            return sc
    return None


def main():
    nodes, gidx, txt = load_english()
    namekeys = load_namekeys()
    pname = load_tsv()
    allnk = set(namekeys.values())
    nk_idx = sorted(gidx[g] for g in allnk if g in gidx)

    def next_namekey(ni):
        p = bisect.bisect_right(nk_idx, ni)
        return nk_idx[p] if p < len(nk_idx) else len(nodes)

    def is_namelike(i):
        if nodes[i]["Guid"] in allnk:
            return True
        s = strip_tags(txt[i])
        if not s or len(s) > 34:
            return False
        if "{" in txt[i] or "." in s:
            return False
        return len(s.split()) <= 5

    def looks_desc(t):
        s = strip_tags(t)
        return len(s) >= 22 and ("{" in t or len(s.split()) >= 5)

    def extract(ni):
        """Return (description_text, confidence) for the ability whose name node is at ni."""
        end = min(next_namekey(ni), ni + 8)
        cands, j = [], ni + 1
        while j < end:
            if is_namelike(j):
                break
            if looks_desc(txt[j]):
                cands.append(j)
            j += 1
        if not cands:
            return None, "none"
        best = max(cands, key=lambda k: len(strip_tags(txt[k])))
        return strip_tags(txt[best]), ("high" if len(cands) == 1 else "med")

    def denied(name):
        if name in DENY_EXACT:
            return True
        return any(name.startswith(p) for p in DENY_PREFIX)

    meta = json.load(io.open(METADATA, encoding="utf-8"))
    abilities = meta["abilities"]

    # The universe to enrich: existing entries + every AB_*_(AbilityGroup|Group)
    # prefab that has a name-key (so we can locate it in the loc table).
    ag_prefabs = [g for g, n in pname.items()
                  if n.startswith("AB_") and re.search(r"_(AbilityGroup|Group)$", n)]
    universe = set(int(k) for k in abilities) | set(ag_prefabs)

    stats = dict(desc_before=0, desc_after=0, desc_new=0, desc_replaced_foreign=0,
                 desc_kept_legacy=0, school_before=0, school_after=0,
                 high=0, denied_skips=0, new_entries=0, guarded_keeps=0,
                 foreign_cleared=0)

    for k, v in abilities.items():
        if v.get("description"):
            stats["desc_before"] += 1
        if v.get("school"):
            stats["school_before"] += 1

    for guid in universe:
        key = str(guid)
        entry = abilities.get(key)
        if entry is None:
            entry = {"name": None, "categories": ["Other"]}
            abilities[key] = entry
            stats["new_entries"] += 1

        pn = pname.get(guid, "")
        friendly = entry.get("name")

        # --- description ---
        legacy = entry.get("description")
        legacy_english = legacy if (legacy and is_english(legacy)) else None
        name_for_deny = friendly or strip_tags(pn)
        game_desc, conf = (None, "none")
        nk = namekeys.get(guid)
        if nk and nk in gidx and not denied(name_for_deny or ""):
            game_desc, conf = extract(gidx[nk])

        chosen = None
        used_game = False
        if conf == "high" and game_desc:
            if not legacy:
                # blank -> fill with authoritative game text (bulk of new coverage)
                chosen, used_game = game_desc, True
                stats["high"] += 1
            elif not legacy_english:
                # foreign legacy -> always replace with English game text
                chosen, used_game = game_desc, True
                stats["high"] += 1
                stats["desc_replaced_foreign"] += 1
            elif overlap(legacy_english, game_desc) >= 0.3:
                # English legacy AND game text agree on the ability -> cleaner game text
                chosen, used_game = game_desc, True
                stats["high"] += 1
            else:
                # English legacy but game text disagrees (rare wrong-match, e.g.
                # Veil-of-Illusion picking a sibling Veil) -> keep the legacy text.
                chosen = legacy_english
                stats["desc_kept_legacy"] += 1
                stats["guarded_keeps"] += 1
        elif legacy_english:
            chosen = legacy_english
            stats["desc_kept_legacy"] += 1
        # else: leave blank (med-conf excluded; foreign-without-game dropped)

        if denied(name_for_deny or "") and legacy_english:
            stats["denied_skips"] += 1

        if chosen:
            if not legacy:
                stats["desc_new"] += 1
            # Normalize every shipped description (game text is already clean;
            # this also scrubs stray markup / double-spaces from kept legacy).
            chosen = strip_tags(chosen)
            entry["description"] = chosen
            # The new game text uses {param} placeholders, not the legacy %param%
            # form — the stale parameters map (often garbage values) no longer
            # applies, so drop it when we install game text.
            if used_game and "parameters" in entry:
                del entry["parameters"]
        elif legacy and not legacy_english:
            # Foreign legacy with no English game match — drop it so the resolver
            # falls back to the friendly name instead of showing non-English text.
            del entry["description"]
            entry.pop("parameters", None)
            stats["foreign_cleared"] += 1

        # --- name (fill only if missing; authoritative loc name) ---
        if not entry.get("name") and nk and nk in gidx:
            nm = strip_tags(txt[gidx[nk]])
            if nm and is_english(nm) and len(nm) <= 40:
                entry["name"] = nm

        # --- school (fill only if missing) ---
        if not entry.get("school"):
            sc = school_for(pn)
            if sc:
                entry["school"] = sc

    for v in abilities.values():
        if v.get("description"):
            stats["desc_after"] += 1
        if v.get("school"):
            stats["school_after"] += 1

    meta["version"] = meta.get("version", 1)
    json.dump(meta, io.open(OUT, "w", encoding="utf-8"),
              ensure_ascii=False, indent=2)

    n = len(abilities)
    print(f"entries: {n}  (new this pass: {stats['new_entries']})")
    print(f"description: {stats['desc_before']} -> {stats['desc_after']} "
          f"({100*stats['desc_after']//n}%)  [new+filled: {stats['desc_new']}, "
          f"foreign repaired: {stats['desc_replaced_foreign']}, "
          f"high-conf game: {stats['high']}, kept legacy: {stats['desc_kept_legacy']}]")
    print(f"school:      {stats['school_before']} -> {stats['school_after']} "
          f"({100*stats['school_after']//n}%)")
    print(f"denylist skips (kept legacy for generic names): {stats['denied_skips']}")
    print(f"guarded keeps (English legacy preserved over disagreeing game text): {stats['guarded_keeps']}")
    print(f"foreign legacy cleared (no English match): {stats['foreign_cleared']}")
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
