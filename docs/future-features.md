# Future Features

## List of unprocessed features

* Capture
  * Perhaps have a tabbed interface instead of displaying all of the source options
* General
  * Access-token refresh — the 24h token/cookie expiry currently ends in 401s/re-login; add `offline_access` + refresh in the bearer handler.
  * When adding a user to a world, I'd like to have a dropdown with usernames instead of having to enter the user's guid
  * Let's make the "N" logo in the left bar clickable that takes the user to the root of the site
* Review
    * When I click Accept All or Reject All, it would be nice to have a spinner until processing is done.
---

## Open questions on storylines

**Problem.** The spec's storyline detail calls for "open questions," but no such concept exists anywhere in the pipeline.

**Design direction.** Convention over schema: facts with predicate `open question` get surfaced as their own section on storyline detail, and the extraction prompt is taught to emit them for unresolved tensions ("What is the Silver Key for?"). Resolving one is an `UpdateFact` proposal.

**Parked because:** needs the worker running end-to-end to be worth wiring.
