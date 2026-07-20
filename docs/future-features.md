# Future Features

## List of unprocessed features

* The map timeline isn't working right, I need to figure out why
* Add DataDog observability

---

## Open questions on storylines

**Problem.** The spec's storyline detail calls for "open questions," but no such concept exists anywhere in the pipeline.

**Design direction.** Convention over schema: facts with predicate `open question` get surfaced as their own section on storyline detail, and the extraction prompt is taught to emit them for unresolved tensions ("What is the Silver Key for?"). Resolving one is an `UpdateFact` proposal.

**Parked because:** needs the worker running end-to-end to be worth wiring.
