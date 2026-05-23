"""Scrape ability metadata from vrising.gaming.tools.

Approach:
  1. Read the abilities index page (already saved as abilities_index.html).
  2. Extract every /abilities/<slug> link.
  3. For each slug, fetch the detail page with English Accept-Language header.
  4. Extract the embedded JSON body (the gaming.tools page server-renders the
     ability data as JSON inside the HTML, accessible via a regex).
  5. Save the FULL raw parsed JSON per ability into raw_abilities.json (a
     dict keyed by slug). This is the audit trail.
  6. A separate post-process step builds ability_metadata.json keyed by
     PrefabGUID with only the fields we ship.

Throttled to ~300ms between requests to be polite. Re-runnable: skips slugs
already present in raw_abilities.json. Logs progress every 50 fetches.
"""
import re
import sys
import json
import time
import urllib.request
import urllib.error
import os

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

BASE = 'https://vrising.gaming.tools'
INDEX_FILE = 'abilities_index.html'
RAW_FILE = 'raw_abilities.json'
SLEEP_BETWEEN = 0.3  # 300ms — polite but completes ~1500 in ~8 minutes

HEADERS = {
    'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
    'Accept-Language': 'en-US,en;q=0.9',
    'Accept': 'text/html,application/xhtml+xml',
}

def load_slugs():
    with open(INDEX_FILE, 'r', encoding='utf-8') as f:
        html = f.read()
    slugs = sorted(set(re.findall(r'/abilities/([a-z0-9_]+)', html)))
    return slugs

def load_existing_raw():
    if os.path.exists(RAW_FILE):
        with open(RAW_FILE, 'r', encoding='utf-8') as f:
            return json.load(f)
    return {}

def save_raw(data):
    tmp = RAW_FILE + '.tmp'
    with open(tmp, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=0)
    os.replace(tmp, RAW_FILE)

def fetch_html(url, retries=3):
    for attempt in range(retries):
        try:
            req = urllib.request.Request(url, headers=HEADERS)
            with urllib.request.urlopen(req, timeout=30) as resp:
                return resp.read().decode('utf-8', errors='replace')
        except urllib.error.HTTPError as e:
            if e.code == 404:
                return None
            print(f'  HTTP {e.code} on attempt {attempt+1}, retrying...', flush=True)
            time.sleep(2 ** attempt)
        except Exception as e:
            print(f'  Error on attempt {attempt+1}: {e}, retrying...', flush=True)
            time.sleep(2 ** attempt)
    return None

def extract_body(html):
    """Pull the embedded `"body":"..."` JSON-encoded string and return the
    parsed inner dict. Returns None if no body is found."""
    # The body field contains escaped JSON. Find it.
    # Pattern: "body":"{\"id\":...,\"...\":\"...\"}"
    # Use non-greedy with a lookahead for the closing brace+quote followed by }
    m = re.search(r'"body":"(\{.*?\})"\}', html)
    if not m:
        return None
    escaped = m.group(1)
    try:
        unescaped = json.loads('"' + escaped + '"')
        return json.loads(unescaped)
    except Exception as e:
        print(f'  Parse error: {e}', flush=True)
        return None

def main():
    slugs = load_slugs()
    raw = load_existing_raw()
    print(f'Index has {len(slugs)} slugs. Already scraped: {len(raw)}.', flush=True)

    total = len(slugs)
    new_count = 0
    fail_count = 0
    save_every = 25

    for i, slug in enumerate(slugs, 1):
        if slug in raw:
            continue

        url = f'{BASE}/abilities/{slug}'
        html = fetch_html(url)
        if html is None:
            print(f'[{i}/{total}] FAILED to fetch {slug}', flush=True)
            fail_count += 1
            time.sleep(SLEEP_BETWEEN)
            continue

        body = extract_body(html)
        if body is None:
            print(f'[{i}/{total}] NO BODY in {slug}', flush=True)
            fail_count += 1
            time.sleep(SLEEP_BETWEEN)
            continue

        raw[slug] = body
        new_count += 1

        if new_count % 50 == 0:
            print(f'[{i}/{total}] scraped {new_count} new (total: {len(raw)}, failed: {fail_count}) — sample: {body.get("name","?")}', flush=True)

        if new_count % save_every == 0:
            save_raw(raw)

        time.sleep(SLEEP_BETWEEN)

    save_raw(raw)
    print(f'\nDONE. new={new_count} failed={fail_count} total_in_db={len(raw)}', flush=True)

if __name__ == '__main__':
    main()
