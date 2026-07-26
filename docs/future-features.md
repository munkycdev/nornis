## List of unprocessed features

- When removing a pin, remove the indicator on the map at the same time instead forcing a manual refresh
- Add the ability to add pins for existing known locations. A dropdown should show locations that don't have known pin artifacts
- World memory penalties are back, even though I clicked the X to resolve them.
- Remove "Verspergale Reach (template master)" from my list of worlds
	
- Tutorial: for the tutorial prompt panel, clicking on the header text minimizes and restores the tutorial panel, but clicking on the down/up arrow does nothing. Make that also trigger minimization/restoration.
- Tutorial: when clicking "Submit for Review", the user is taken to the Sources tab, which is good, but the "Add the Session 6 notes" doesn't mark checked unless I go back to the Capture page.

- Campaign backlog import: a review-paced onboarding flow for users bringing existing session notes
	- Problem: sources marked ready together all extract against the same (empty or stale) canon in parallel — each proposes its own copy of every entity, and the review queue becomes a dedup minefield. Most likely first-session disaster for a new user pasting their campaign backlog. (Discovered 2026-07-25 while authoring the demo template; recovered using the extraction replay.)
	- Fix: a first-class "import my campaign" flow — add all the notes up front, then walk them oldest-first, extract → review → advance, so each note extracts against the vetted canon of its predecessors. This is the review-paced extraction replay (feature: re-run extraction) pointed at new notes instead of existing ones; most of the machinery already exists.
	- Backstop (do regardless): apply-time dedup in ApplyCreateArtifact — match an existing artifact by type + normalized name and convert the create into an update/no-op instead of inserting a duplicate. Catches exact-name duplicates even outside the import flow; near-miss names ("Salt Factor" vs "The Salt Factor") remain the manual-merge feature's job.
- Review pipeline hardening (three small issues found vetting the demo template, 2026-07-25)
	- Batch-accept is order-dependent: facts/relationships submitted before the CreateArtifact they reference fail name resolution and stay pending. BatchAcceptAsync should order creates first (or retry failures once after the pass).
	- The extractor occasionally emits `"confidence":"0.99"` as a string; the applicator rejects the whole proposal. Normalize numeric-looking strings when storing or applying payloads.
	- An Edited-but-not-yet-accepted proposal disappears from the pending review queue but still blocks batch completion (and therefore a waiting replay). Either show Edited proposals in the queue or make edit+accept one action.
- 