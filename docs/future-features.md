# Future Features

## List of unprocessed features

* Capture
  * Add the ability to hand-write notes
  * Perhaps have a tabbed interface instead of displaying all of the source options
* General
  * Access-token refresh — the 24h token/cookie expiry currently ends in 401s/re-login; add `offline_access` + refresh in the bearer handler.

---

## Open questions on storylines

**Problem.** The spec's storyline detail calls for "open questions," but no such concept exists anywhere in the pipeline.

**Design direction.** Convention over schema: facts with predicate `open question` get surfaced as their own section on storyline detail, and the extraction prompt is taught to emit them for unresolved tensions ("What is the Silver Key for?"). Resolving one is an `UpdateFact` proposal.

**Parked because:** needs the worker running end-to-end to be worth wiring.
