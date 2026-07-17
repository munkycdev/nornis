# Future Features

## List of unprocessed features

* Timeline
  * When mousing over a tree, give the name of the associated storyline in a tooltip. When hovering over a session, indicate the name and relevant detail about why it's marked with the storyline in the tooltip.
---

## Open questions on storylines

**Problem.** The spec's storyline detail calls for "open questions," but no such concept exists anywhere in the pipeline.

**Design direction.** Convention over schema: facts with predicate `open question` get surfaced as their own section on storyline detail, and the extraction prompt is taught to emit them for unresolved tensions ("What is the Silver Key for?"). Resolving one is an `UpdateFact` proposal.

**Parked because:** needs the worker running end-to-end to be worth wiring.
