# Future Features

## List of unprocessed features

* On the Session Wrap-Up card, when selecting a parent, make the dropdown an autocomplete search entry instead of a dumb dropdown
* On the Codex graph view, could we add a border to each dot indicating its status?
* I'd like to be able to change the visibility of Library entries
* Some storylines span multiple campaigns. We need to find a way to accommodate that
* Capture Session Audio type should accept a wav, mp3, or other audio file. It should transcribe the content and create a source transcript
* The map timeline isn't working right, I need to figure out why
* Create a features page
* Create a README.md
* Add DataDog observability

---

## Open questions on storylines

**Problem.** The spec's storyline detail calls for "open questions," but no such concept exists anywhere in the pipeline.

**Design direction.** Convention over schema: facts with predicate `open question` get surfaced as their own section on storyline detail, and the extraction prompt is taught to emit them for unresolved tensions ("What is the Silver Key for?"). Resolving one is an `UpdateFact` proposal.

**Parked because:** needs the worker running end-to-end to be worth wiring.
