# Execution order

- ~~The campaign management page feels like it breaks our ux patterns. Could we move all of the edits that can be done on the campaigns listing page to the campaign detail page with the exception of campaign sorting, which makes sense to have on the overall listing.~~ Done 2026-09-10: name, introduction, status, dates, cast and deletion live on the campaign page; the list keeps ordering and creation. Dates gained an explicit clear (`ClearStartedAt`/`ClearEndedAt`) since null had meant "leave it".
- On the campaign detail page, instead of displaying character guids, I'd like to display chips with the name of the characters in the campaign
- Ask the Loremaster doesn't bring in knowledge from Library items. Could we change that?
- It would be great in an article to be able to link to a topic from the library so that the article could extract information from the library. How might we do that?
- At narrow widths, things start to get jumbled together. Could you do a pass-through and fix spacing issues?
- I'd like to do a full sweep through all pages and clean up URL paths. Instead of displaying the guid ID for a given detail page, I'd like to find a way to use a friendly slug instead.
- Handwritten notes aren't very usable right now, let's work together to figure out how we might fix that.
## The spec files

Each plan lives whole in its own file. They are specs, not authorization — a session
implements only what this file's sequence assigns it, and does not browse sibling
plans for inspiration:

- [plans/scrub-plan.md](plans/scrub-plan.md) — the nine-reviewer audit sweep (tiers 1-2 done; 3-5 open)
- [plans/test-quality.md](plans/test-quality.md) — coverage, CRAP, floors, authorization suite, audits
- [plans/system-status.md](plans/system-status.md) — /status endpoint, checks, worker heartbeat, status page
- [plans/operational-hardening.md](plans/operational-hardening.md) — O1-O6
- [plans/defect-remediation.md](plans/defect-remediation.md) — D1-D4 and the verified-sound record
- [plans/loremaster-wiki.md](plans/loremaster-wiki.md) — W1-W4 (all currently Fable-held)
