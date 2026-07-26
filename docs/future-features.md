## List of unprocessed features

- Reorder the left panels pages:
	- Ask, Capture, Review, Codex, Timeline, Locations, Library, Sources, Admin
- I'd like a way to remove pin locations from a map without deleting the associated location artifact.
- Add a link on the Journey and Timeline maps pages to the original map source so that pins can be adjusted
- When displaying pins on a world map, don't add place names. The pins have been extracted based on existing place names on the map, so there's a major visual duplication.
- When a new world is set up and there's no extracted world knowledge/history, display "N/A" on the World Memory rating instead of "-"
- When deleting a world, display a progress spinner and don't allow the user to dismiss the modal popup until it's done.
- When loading a world, it flashes content that I suspect is locally cached. It would be best to start with an empty screen and then decide what to do - load locally cached data or perform the calls to get info from the api.

- World memory penalties are back, even though I clicked the X to resolve them.

- Sources page: the "All campaigns" button is taller than the "+ Capture Source" button right next to it.
- Authenticated Ask page: The -> button in the ask card is taller than the ask box. Could you match the size as on the public overview page's ask button?
- Review all pages at a mobile device aspect ratio and make adjustments to make the site more usable at the narrower width. Specific observations:
	- Reduce the size of the public site's navigation font so that it fits comfortably instead of making the page wider than the viewport.
	- When asking a question on a mobile device, the answer overlays the world description for some reason.
	
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