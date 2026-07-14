# Future Features

Shipped from this list so far: manual artifact merge, processing/review nav badges, heuristic health widget removal (AI assessment is now the only Continuity Health).

Stack-ranked by when to work on them. One line each; when an item gets picked up, it
gets a real spec under `docs/features/`.

## Now-ish — polish the record you're actively using

4. **Categorized tree projection of artifacts** — collapsible type → status → name grouping; pure client-side, cheap.
5. **Organize the Canon section** — 1,000+ facts post-import need grouping/filtering to be readable (pairs naturally with the tree work).
6. **Better icons** — replace placeholder Material icons where the vocabulary deserves its own marks.

## Next — improve the weekly capture loop

7. **Capture page à la Chronicis** — reference external information alongside the editor while taking session notes.
8. **Graph view of artifacts** — Cytoscape/vis-network over relationships; start from a selected artifact's neighborhood, not the 360-node hairball. Biggest UI lift, biggest wow.
9. **Access-token refresh** — the 24h token/cookie expiry currently ends in 401s/re-login; add `offline_access` + refresh in the bearer handler.
10. **Auto-accept for GM-authored sources** — opt-in per-world setting; the bulk importer already proved accept-as-you-go (extraction context only sees accepted knowledge).
11. **Character ↔ artifact linking** — connect a member's Character record to its AI-extracted Character artifact.

## When other players join

12. **Role-aware UI** — hide GM-only affordances (retrospective, member management, GMOnly options) from players; server already enforces.
13. **Source authority / role-clamped truth states** — player-authored facts cap at `Likely`, GM promote path to `Confirmed`.
14. **Server-side Ask conversations** — move Ask history out of localStorage; auth (the old blocker) is done.

## Watch / as-needed

15. **RAG for Ask (Azure AI Search)** — revisit when keyword retrieval visibly misses; not before.
16. **Retroactive excerpt capture** — bulk-imported references predate per-proposal quotes; a backfill pass costs re-extraction money for polish.
17. **Content-filtered sources** — two grisly Symbaroum sessions blocked by the Azure OpenAI filter; soften text, or apply for the modified-filter tier, or surface a distinct "filtered" status.
18. **Handwriting capture for iPad** — needs an ink/OCR pipeline; the `SourceExtraction` entity is the designated landing spot.
