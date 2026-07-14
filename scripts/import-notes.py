"""Bulk-import a note-vault export into a Nornis world.

Walks an exported vault (the folder layout produced by wiki-style note tools):

    <root>/
      Wiki/          -> world lore sources, no campaign
      Characters/    -> character writeups, no campaign
      Campaigns/
        <n> - <Campaign Name>/
          <Campaign Name>.md            -> becomes the campaign Description
          Arc <n> - <Arc Name>/
            <YYYY-MM-DD>[ - label]/     -> session folder (date = OccurredAt)
              <note>.md                 -> ImportedNote source(s)

Every note becomes an ImportedNote source. Order matters for extraction quality:
Wiki -> Characters -> campaign overviews -> all sessions globally chronological,
and each source's proposals are BATCH-ACCEPTED before the next source is sent, so
extraction context sees the accumulated record and dedupes across sessions. Without
accept-as-you-go every note extracts against an empty world and duplicates pile up.

Behavior learned from the Ruins of Symbaroum import (2026-07):
  - stubs (< ~40 chars after frontmatter) are skipped
  - re-exported duplicate notes in a session folder (same content, different
    heading) collapse to one source; genuinely different player notes are kept
  - bodies over the API limit are split into "(part n/m)" sources
  - failed extractions are retried once; Azure OpenAI content-filter rejections
    are permanent (soften the note text and re-run to import those)
  - the run is resumable: sources whose titles already exist are skipped
  - a final sweep re-attempts deferred proposals (forward references between
    notes resolve once the referenced artifact exists)

Watch the world's daily AI budget (AiBudget:DailyWorldBudgetUsd): a large vault
can exceed it in one day. The Symbaroum import (83 sources, ~2 MB) cost ~$5.50.

Usage:
    python scripts/import-notes.py --root "C:/path/to/Export/VaultName" \
        --world-id <guid> [--base-url https://...] [--dry-run]
"""
import argparse
import json
import re
import sys
import time
import urllib.request
from pathlib import Path

MAX_BODY = 95_000          # under the API's 100k limit, leaving room for part headers
STUB_THRESHOLD = 40        # chars of real content after frontmatter
POLL_SECONDS = 6
EXTRACT_TIMEOUT = 600
BATCH_ACCEPT_LIMIT = 50    # API caps batch-accept at 50 proposal ids
DATE_RE = re.compile(r"^(\d{4}-\d{2}-\d{2})")

BASE = ""
WORLD_ID = ""


def api(method, path, body=None, retries=3):
    for attempt in range(retries):
        req = urllib.request.Request(
            BASE + path,
            data=json.dumps(body).encode() if body is not None else None,
            headers={"Content-Type": "application/json"},
            method=method)
        try:
            with urllib.request.urlopen(req, timeout=90) as resp:
                data = resp.read()
                return resp.status, json.loads(data) if data else None
        except urllib.error.HTTPError as e:
            return e.code, json.loads(e.read() or b"{}")
        except Exception as e:
            if attempt == retries - 1:
                raise
            print(f"  transient ({e}); retrying", flush=True)
            time.sleep(10)


def strip_frontmatter(text):
    return re.sub(r"\A﻿?---\s*\r?\n.*?\r?\n---\s*\r?\n", "", text, flags=re.S)


def content_key(text):
    """Content minus headings/whitespace, for duplicate detection."""
    return re.sub(r"\s+", " ", re.sub(r"^#.*$", "", strip_frontmatter(text), flags=re.M)).strip()


def read(path):
    return path.read_text(encoding="utf-8-sig")


# ---------------------------------------------------------------- discovery --

def discover(root):
    """Returns (campaign_defs, work); work is ordered (title, body, occurred_at, campaign_folder)."""
    wiki, characters, overviews, sessions = [], [], [], []

    for top, bucket in (("Wiki", wiki), ("Characters", characters)):
        folder = root / top
        if not folder.is_dir():
            continue
        for f in sorted(folder.rglob("*.md")):
            body = read(f)
            if len(content_key(body)) >= STUB_THRESHOLD:
                bucket.append((f.stem, body, None, None))

    campaign_defs = {}
    campaigns_root = root / "Campaigns"
    for camp_dir in sorted(campaigns_root.iterdir()) if campaigns_root.is_dir() else []:
        if not camp_dir.is_dir():
            continue
        display = re.sub(r"^\d+\s*-\s*", "", camp_dir.name)
        campaign_defs[camp_dir.name] = {"name": display, "description": None, "dates": []}

        for f in sorted(camp_dir.rglob("*.md")):
            body = read(f)
            date_m = DATE_RE.match(f.parent.name)
            if f.stem == camp_dir.name:
                desc = content_key(body)
                if desc:
                    campaign_defs[camp_dir.name]["description"] = desc[:2000]
                continue
            if len(content_key(body)) < STUB_THRESHOLD:
                continue
            arc = next((p.name for p in f.parents if p.name.lower().startswith("arc")), None)
            if date_m:
                date = date_m.group(1)
                campaign_defs[camp_dir.name]["dates"].append(date)
                label = f.parent.name[len(date):].strip(" -")
                sessions.append((date, f.parent, f, arc, camp_dir.name, label))
            else:
                overviews.append((f"{f.stem} ({display})", body, None, camp_dir.name))

    # One primary note per session folder; extras only when content differs.
    session_work, by_folder = [], {}
    for item in sessions:
        by_folder.setdefault(item[1], []).append(item)
    for folder, items in by_folder.items():
        date, _, _, arc, camp, label = items[0]
        files = [it[2] for it in items]
        primary = next((x for x in files if x.stem == folder.name or DATE_RE.match(x.stem)), files[0])
        keys = {content_key(read(primary))}
        chosen = [primary]
        for f in files:
            if f is not primary and (k := content_key(read(f))) not in keys:
                keys.add(k)
                chosen.append(f)
        for f in chosen:
            suffix = f" — {label}" if label else ""
            extra = "" if f is primary else f" — {f.stem}"
            arctag = f" ({arc})" if arc else ""
            session_work.append((date, f"{date}{suffix}{extra}{arctag}", read(f), camp))

    session_work.sort(key=lambda x: (x[0], x[1]))
    work = wiki + characters + overviews + [(t, b, d, c) for d, t, b, c in session_work]
    return campaign_defs, work


# ------------------------------------------------------------------- import --

def ensure_campaigns(defs):
    _, existing = api("GET", f"/api/worlds/{WORLD_ID}/campaigns")
    by_name = {c["name"]: c["id"] for c in existing}
    ids = {}
    all_dates = {k: sorted(v["dates"]) for k, v in defs.items()}
    latest_start = max((d[0] for d in all_dates.values() if d), default=None)
    for folder, meta in defs.items():
        if meta["name"] in by_name:
            ids[folder] = by_name[meta["name"]]
            continue
        dates = all_dates[folder]
        is_latest = bool(dates) and dates[0] == latest_start
        status, c = api("POST", f"/api/worlds/{WORLD_ID}/campaigns", {
            "name": meta["name"],
            "description": meta["description"],
            "status": "Active" if is_latest else "Completed",
            "startedAt": f"{dates[0]}T00:00:00Z" if dates else None,
            "endedAt": None if is_latest or not dates else f"{dates[-1]}T00:00:00Z",
        })
        assert status == 201, c
        ids[folder] = c["id"]
        print(f"campaign created: {meta['name']}", flush=True)
    return ids


def split_body(title, body):
    if len(body) <= MAX_BODY:
        return [(title, body)]
    parts, chunk, size, n = [], [], 0, 1
    for line in body.splitlines(keepends=True):
        if size + len(line) > MAX_BODY and chunk:
            parts.append(("".join(chunk), n))
            chunk, size, n = [], 0, n + 1
        chunk.append(line)
        size += len(line)
    if chunk:
        parts.append(("".join(chunk), n))
    total = len(parts)
    return [(f"{title} (part {i}/{total})", text) for text, i in parts]


def batch_accept(ids):
    """Accept ids in API-sized chunks; returns (succeeded, failed_items)."""
    ok, failed = 0, []
    for i in range(0, len(ids), BATCH_ACCEPT_LIMIT):
        status, result = api("POST", f"/api/worlds/{WORLD_ID}/reviews/proposals/batch-accept",
                             {"proposalIds": ids[i:i + BATCH_ACCEPT_LIMIT]})
        if status != 200:
            print(f"  batch-accept error: {result}", flush=True)
            continue
        ok += len(result["succeeded"])
        failed.extend(result["failed"])
    return ok, failed


def accept_all_for(source_id):
    """Accept this source's proposals, re-running rounds until dependencies settle."""
    accepted = deferred = 0
    while True:
        status, queue = api("GET", f"/api/worlds/{WORLD_ID}/reviews/proposals")
        if status != 200:
            print(f"  queue fetch failed: {queue}", flush=True)
            return accepted, deferred
        mine = [p["id"] for p in queue["proposals"] if p.get("sourceId") == source_id]
        if not mine:
            return accepted, deferred
        ok, failed = batch_accept(mine)
        accepted += ok
        deferred = len(failed)
        if not ok:
            return accepted, deferred


def final_sweep():
    """Re-attempt deferred proposals now that the whole record exists."""
    swept = 0
    while True:
        _, queue = api("GET", f"/api/worlds/{WORLD_ID}/reviews/proposals")
        ids = [p["id"] for p in queue["proposals"]]
        if not ids:
            break
        ok, failed = batch_accept(ids)
        swept += ok
        if not ok:
            codes = {}
            for item in failed:
                codes[item["code"]] = codes.get(item["code"], 0) + 1
            print(f"still pending for manual review: {len(ids)} ({codes})", flush=True)
            break
    print(f"final sweep accepted {swept}", flush=True)


def process_one(title, body, occurred_at, campaign_id, existing_titles):
    if title in existing_titles:
        print(f"SKIP (exists): {title}", flush=True)
        return "skipped"
    status, src = api("POST", f"/api/worlds/{WORLD_ID}/sources", {
        "title": title[:200],
        "type": "ImportedNote",
        "visibility": "PartyVisible",
        "body": body,
        "occurredAt": f"{occurred_at}T00:00:00Z" if occurred_at else None,
        "campaignId": campaign_id,
    })
    if status != 201:
        print(f"CREATE FAILED {title}: {src}", flush=True)
        return "create_failed"

    for attempt in (1, 2):
        status, r = api("POST", f"/api/worlds/{WORLD_ID}/sources/{src['id']}/ready")
        if status != 200:
            print(f"READY FAILED {title}: {r}", flush=True)
            return "ready_failed"
        deadline = time.time() + EXTRACT_TIMEOUT
        state = "?"
        while time.time() < deadline:
            time.sleep(POLL_SECONDS)
            _, cur = api("GET", f"/api/worlds/{WORLD_ID}/sources/{src['id']}")
            state = cur["processingStatus"]
            if state in ("Processed", "Failed"):
                break
        if state == "Processed":
            acc, deferred = accept_all_for(src["id"])
            print(f"OK {title}: accepted {acc}" + (f", {deferred} deferred" if deferred else ""), flush=True)
            return "ok"
        if state == "Failed" and attempt == 1:
            print(f"RETRY {title}", flush=True)
            continue
        print(f"FAILED {title}: {state}", flush=True)
        return "failed"
    return "failed"


def main():
    global BASE, WORLD_ID
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", required=True, help="vault export root (the folder containing Wiki/Campaigns/...)")
    parser.add_argument("--world-id", required=True, help="target Nornis world id")
    parser.add_argument("--base-url", default="http://localhost:5000", help="Nornis API base URL")
    parser.add_argument("--dry-run", action="store_true", help="print the work plan without importing")
    args = parser.parse_args()

    BASE = args.base_url.rstrip("/")
    WORLD_ID = args.world_id
    root = Path(args.root)

    campaign_defs, work = discover(root)
    print(f"work items: {len(work)}", flush=True)
    if args.dry_run:
        for title, body, occurred_at, camp in work:
            print(f"  {occurred_at or '----------'}  {title}  [{camp or 'no campaign'}]  ({len(body)} chars)")
        return

    campaign_ids = ensure_campaigns(campaign_defs)
    _, existing = api("GET", f"/api/worlds/{WORLD_ID}/sources")
    existing_titles = {s["title"] for s in existing}

    tally = {}
    for i, (title, body, occurred_at, camp) in enumerate(work, 1):
        campaign_id = campaign_ids.get(camp) if camp else None
        for part_title, part_body in split_body(title, body):
            print(f"[{i}/{len(work)}] {part_title}", flush=True)
            outcome = process_one(part_title, part_body, occurred_at, campaign_id, existing_titles)
            tally[outcome] = tally.get(outcome, 0) + 1

    final_sweep()
    print(f"\nDONE: {tally}", flush=True)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", line_buffering=True)
    main()
