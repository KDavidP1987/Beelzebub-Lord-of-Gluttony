"""
Sanitize NON-ENGLISH text in the shipped ability_metadata.json.

The scrape/merge pipeline occasionally pulled a LOCALIZED (non-English) string for an ability's
`name` / `description` — Russian, Chinese, Japanese, Korean, Thai, Polish, German, French, Hungarian,
Turkish, Spanish, Portuguese, etc. These then flow over the API (`label=` / `desc=`) and show up as
"foreign text" in clients (BloodCraftHub's Scan All). The API was faithfully emitting bad data.

Fix: for any entry whose `name` or `description` contains foreign characters (any non-ASCII char that
isn't ordinary English typography like ’ — …), DELETE that field. Beelzebub's runtime name resolver
falls back to the humanized prefab name (its canonical English tier-3 fallback), so the API then emits
a clean English name instead of the localized string. Descriptions just become blank (desc coverage is
partial anyway) rather than garbled.

Idempotent + safe: only removes foreign fields, never English ones; re-run after any metadata regen.

DRY-RUN by default; `--write` commits. Run lint_ability_data.py + rebuild after.

Usage:
  python sanitize_metadata_language.py            # dry-run, list every entry it would clean
  python sanitize_metadata_language.py --write    # apply
"""
import os, sys, json, argparse

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(__file__)
META = os.path.normpath(os.path.join(HERE, '..', 'Beelzebub', 'Resources', 'ability_metadata.json'))

# Non-ASCII characters that are legitimate English typography — allowed, NOT treated as foreign.
ALLOWED = set("’‘“”—–…•°×→§ ")  # smart quotes, dashes, ellipsis, bullet, degree, times, arrow, nbsp

def foreign_chars(s):
    return [ch for ch in s if ord(ch) > 0x7F and ch not in ALLOWED]

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--write', action='store_true')
    args = ap.parse_args()

    doc = json.load(open(META, encoding='utf-8'))
    ab = doc['abilities']

    cleaned = 0
    name_drops = desc_drops = 0
    for guid, e in ab.items():
        hit = False
        for field in ('name', 'description'):
            s = e.get(field)
            if isinstance(s, str) and foreign_chars(s):
                hit = True
                print(f"  {guid:>12}  drop {field}: {s!r}")
                if args.write:
                    del e[field]
                if field == 'name': name_drops += 1
                else: desc_drops += 1
        if hit:
            cleaned += 1

    print(f"\n{cleaned} entr(y/ies) with foreign text  (name={name_drops}, description={desc_drops})")
    if not args.write:
        print("DRY-RUN — nothing written. Re-run with --write to apply, then lint + rebuild.")
        return
    json.dump(doc, open(META, 'w', encoding='utf-8'), indent=2, ensure_ascii=False)
    print(f"WROTE {META} — runtime humanizer now supplies English names for the cleaned entries. "
          f"Run: python lint_ability_data.py, then rebuild.")

if __name__ == '__main__':
    main()
